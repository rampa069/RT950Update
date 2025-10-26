using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RT950Update.Core;

public class BootHelper
{
    private const byte PACKAGE_HEADER = 0xAA;
    private const byte PACKAGE_END = 0x55;
    private const byte CMD_HANDSHAKE = 0x0A;
    private const byte CMD_CHECKMODELTYPE = 0x02;
    private const byte CMD_UPDATE = 0x03;
    private const byte CMD_UPDATE_DATA_PACKAGES = 0x04;
    private const byte CMD_UPDATE_END = 0x45;
    private const byte CMD_INTO_BOOT = 0x42;
    private const byte CMD_INTO_ERASE_MODE = 0xEE;

    private SerialPort? _serialPort;
    private int _nativeSerialFd = -1; // Native file descriptor for RAW mode
    private bool _useNativeSerial = false;
    private STATE _bootProcessState = STATE.HandShakeStep0_0;
    private bool _flagTransmitting = false;
    private int _cntRetry = 5;
    private int _cntError = 3;
    private readonly byte[] _bufferTx = new byte[2048];
    private readonly byte[] _bufferRx = new byte[128];
    private int _addr = 0;
    private int _dataLen = 0;
    private int _seed = 0;
    private long _byteOfFile = 0L;
    private double _percent = 0.0;
    private bool _flagPTTPress = false;
    private int _totalPackage = 0;
    private byte _lastCmd = 0;
    private byte[]? _buffer = null;
    private bool _flagFileEnd = false;
    private readonly PackageFmt _packageHelper;

    public string StateMessage { get; private set; } = "Handshake...\r\n";

    public event EventHandler<string>? MessageUpdated;
    public event EventHandler<double>? ProgressUpdated;

    public BootHelper(bool flagPTTPress)
    {
        _flagPTTPress = flagPTTPress;
        _packageHelper = new PackageFmt();
        ErrorCntClr();
    }

    public async Task<bool> BootLoadingAsync(
        SerialPort serialPort,
        string filePath,
        CancellationToken cancellationToken)
    {
        _serialPort = serialPort;
        _useNativeSerial = false;
        _flagTransmitting = true;

        if (await HandShake0Async(cancellationToken))
        {
            try
            {
                await using var stream = new FileStream(filePath, FileMode.Open);
                var fileInfo = new FileInfo(filePath);
                _byteOfFile = fileInfo.Length;
                _addr = 0;
                return await BootLoadingAsync(stream, cancellationToken);
            }
            catch (Exception ex)
            {
                UpdateMessage($"Error: {ex.Message}\r\n");
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Boot loading using native RAW serial port (bypasses .NET SerialPort)
    /// </summary>
    public async Task<bool> BootLoadingNativeAsync(
        string portPath,
        string filePath,
        CancellationToken cancellationToken)
    {
        UpdateMessage($"[NATIVE] Opening port {portPath} in RAW mode...\r\n");
        _nativeSerialFd = NativeSerial.OpenRaw(portPath);

        if (_nativeSerialFd < 0)
        {
            UpdateMessage($"[NATIVE] ERROR: Failed to open port in RAW mode\r\n");
            return false;
        }

        UpdateMessage($"[NATIVE] Port opened successfully (fd={_nativeSerialFd})\r\n");
        _useNativeSerial = true;
        _flagTransmitting = true;

        try
        {
            if (await HandShake0Async(cancellationToken))
            {
                try
                {
                    await using var stream = new FileStream(filePath, FileMode.Open);
                    var fileInfo = new FileInfo(filePath);
                    _byteOfFile = fileInfo.Length;
                    _addr = 0;
                    return await BootLoadingAsync(stream, cancellationToken);
                }
                catch (Exception ex)
                {
                    UpdateMessage($"Error: {ex.Message}\r\n");
                    return false;
                }
            }

            return false;
        }
        finally
        {
            if (_nativeSerialFd >= 0)
            {
                UpdateMessage($"[NATIVE] Closing port (fd={_nativeSerialFd})\r\n");
                NativeSerial.Close(_nativeSerialFd);
                _nativeSerialFd = -1;
            }
        }
    }

    /// <summary>
    /// Write data to serial port (native or .NET)
    /// </summary>
    private void SerialWrite(byte[] buffer, int offset, int count)
    {
        if (_useNativeSerial)
        {
            // Extract the portion to write
            byte[] toWrite = new byte[count];
            Array.Copy(buffer, offset, toWrite, 0, count);

            int written = NativeSerial.write(_nativeSerialFd, toWrite, count);
            if (written != count)
            {
                throw new IOException($"Native write failed: expected {count}, wrote {written}");
            }
        }
        else
        {
            _serialPort!.Write(buffer, offset, count);
        }
    }

    /// <summary>
    /// Read data from serial port (native or .NET) - blocking with timeout
    /// </summary>
    private async Task<int> SerialReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (_useNativeSerial)
        {
            // Native read with retry and select() to detect data
            return await Task.Run(() =>
            {
                byte[] readBuffer = new byte[count];

                // Use ReadWithRetry which uses select() to poll for data
                int bytesRead = NativeSerial.ReadWithRetry(
                    _nativeSerialFd,
                    readBuffer,
                    count,
                    1000,
                    100,
                    msg => UpdateMessage(msg)); // Pass logging callback

                if (bytesRead > 0)
                {
                    Array.Copy(readBuffer, 0, buffer, offset, bytesRead);
                    UpdateMessage($"[NATIVE] Final result: Read {bytesRead} bytes: {BitConverter.ToString(readBuffer, 0, bytesRead)}\r\n");
                }
                else
                {
                    UpdateMessage($"[NATIVE] Final result: Read returned 0 bytes (timeout or no data)\r\n");
                }

                return bytesRead < 0 ? 0 : bytesRead;
            }, cancellationToken);
        }
        else
        {
            // .NET SerialPort read - use direct Read() like RT880-FlasherX
            // Use the port's configured ReadTimeout (5000ms), not a short 100ms
            try
            {
                return _serialPort.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Synchronous read (for compatibility)
    /// </summary>
    private int SerialRead(byte[] buffer, int offset, int count)
    {
        if (_useNativeSerial)
        {
            byte[] readBuffer = new byte[count];
            int bytesRead = NativeSerial.ReadWithRetry(
                _nativeSerialFd,
                readBuffer,
                count,
                500,
                50,
                msg => UpdateMessage(msg));

            if (bytesRead > 0)
            {
                Array.Copy(readBuffer, 0, buffer, offset, bytesRead);
            }

            return bytesRead < 0 ? 0 : bytesRead;
        }
        else
        {
            try
            {
                return _serialPort!.Read(buffer, offset, count);
            }
            catch (TimeoutException)
            {
                return 0;
            }
        }
    }

    private async Task<bool> HandShake0Async(CancellationToken cancellationToken)
    {
        byte[] lastSentData = Array.Empty<byte>();
        DateTime lastSendTime = DateTime.MinValue;
        const int retryTimeoutMs = 3000; // 3 seconds - increased timeout

        if (_flagPTTPress)
        {
            _bootProcessState = STATE.HandShake1;
            return true;
        }

        // Check if device sends anything spontaneously
        await Task.Delay(500, cancellationToken);
        try
        {
            byte[] spontaneousData = new byte[128];
            int bytesRead = await SerialReadAsync(spontaneousData, 0, spontaneousData.Length, cancellationToken);
        }
        catch (Exception)
        {
            // Ignore
        }

        // Flush input buffer before starting
        if (!_useNativeSerial)
        {
            _serialPort!.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
        }

        await Task.Delay(200, cancellationToken); // Longer initial delay

        while (_flagTransmitting && !cancellationToken.IsCancellationRequested)
        {
            // Check for timeout and retry
            if (_bootProcessState != STATE.HandShakeStep0_0 &&
                (DateTime.Now - lastSendTime).TotalMilliseconds > retryTimeoutMs)
            {
                if (_cntRetry <= 0)
                {
                    UpdateMessage("Handshake failed!\r\n");
                    _flagTransmitting = false;
                    return false;
                }

                _cntRetry--;

                // Resend last command
                if (lastSentData.Length > 0)
                {
                    SerialWrite(lastSentData, 0, lastSentData.Length);
                    lastSendTime = DateTime.Now;
                }
            }

            switch (_bootProcessState)
            {
                case STATE.HandShakeStep0_0:
                    lastSentData = Encoding.ASCII.GetBytes("PROGRAMBT9000U");
                    SerialWrite(lastSentData, 0, lastSentData.Length);
                    await Task.Delay(200, cancellationToken); // Give radio time to respond
                    lastSendTime = DateTime.Now;
                    ErrorCntClr();
                    _bootProcessState = STATE.HandShakeStep0_1;
                    // Don't break - immediately try to read the response
                    goto case STATE.HandShakeStep0_1;

                case STATE.HandShakeStep0_1:
                    try
                    {
                        int bytesRead = await SerialReadAsync(_bufferRx, 0, 1, cancellationToken);
                        if (bytesRead > 0)
                        {
                            if (_bufferRx[0] == 0x06) // ACK
                            {
                                ErrorCntClr();
                                lastSentData = Encoding.ASCII.GetBytes("UPDATE");
                                SerialWrite(lastSentData, 0, lastSentData.Length);
                                lastSendTime = DateTime.Now;
                                _bootProcessState = STATE.HandShakeStep0_2;
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Normal timeout, just continue
                    }
                    break;

                case STATE.HandShakeStep0_2:
                    try
                    {
                        int bytesRead = await SerialReadAsync(_bufferRx, 0, 1, cancellationToken);
                        if (bytesRead > 0)
                        {
                            if (_bufferRx[0] == 0x06) // ACK
                            {
                                ErrorCntClr();
                                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
                                _bootProcessState = STATE.Booting_IntoBootMode;
                                return true;
                            }
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Normal timeout, just continue
                    }
                    break;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<bool> BootLoadingAsync(Stream stream, CancellationToken cancellationToken)
    {
        ushort packageNum = 0;
        DateTime lastCommandTime = DateTime.Now;
        const int commandTimeoutMs = 2000; // 2 seconds like original code
        bool flagRetry = false;

        while (_flagTransmitting && !cancellationToken.IsCancellationRequested)
        {
            // Check for timeout and set retry flag (like original code's Timer)
            if (_bootProcessState == STATE.Booting_WaitResponse1 ||
                _bootProcessState == STATE.Booting_WaitResponse2 ||
                _bootProcessState == STATE.Booting_WaitResponse3)
            {
                if ((DateTime.Now - lastCommandTime).TotalMilliseconds > commandTimeoutMs)
                {
                    flagRetry = true;
                }
            }

            // Handle retry logic (like original code lines 390-406)
            if (flagRetry)
            {
                flagRetry = false;
                if (_cntRetry <= 0)
                {
                    UpdateMessage(" Failure!\r\n");
                    _flagTransmitting = false;
                    return false;
                }
                _cntRetry--;
                UpdateMessage($" {5 - _cntRetry}th Resend...\r\n");
                _bootProcessState = STATE.Booting_WaitResponse1;
                // Resend last command
                if (_buffer != null)
                {
                    SerialWrite(_buffer, 0, _buffer.Length);
                    lastCommandTime = DateTime.Now;
                }
                _seed = 0;
                _dataLen = 0;
            }

            switch (_bootProcessState)
            {
                case STATE.Booting_IntoBootMode:
                    _buffer = _packageHelper.Packing(CMD_INTO_BOOT, 0, 0, null);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_INTO_BOOT;
                    _seed = 0;
                    _dataLen = 0;
                    break;

                case STATE.HandShake1:
                    _buffer = Encoding.ASCII.GetBytes("BOOTLOADER_V3");
                    _buffer = _packageHelper.Packing(CMD_HANDSHAKE, 0, (ushort)_buffer.Length, _buffer);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_HANDSHAKE;
                    _seed = 0;
                    _dataLen = 0;
                    break;

                case STATE.Booting_CheckModelType:
                    byte[] modelData = new byte[32];
                    stream.Seek(992L, SeekOrigin.Begin);
                    await stream.ReadAsync(modelData, 0, 32, cancellationToken);
                    _buffer = _packageHelper.Packing(CMD_CHECKMODELTYPE, 0, (ushort)modelData.Length, modelData);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_CHECKMODELTYPE;
                    _seed = 0;
                    _dataLen = 0;
                    break;

                case STATE.Booting_SendPackages:
                    long length = stream.Length;
                    _totalPackage = (int)(length / 1024);
                    if (length % 1024 > 0)
                        _totalPackage++;

                    byte[] packageData = new byte[2];
                    if (_totalPackage > 1)
                    {
                        packageData[0] = (byte)((_totalPackage - 1) >> 8);
                        packageData[1] = (byte)(_totalPackage - 1);
                    }
                    else
                    {
                        packageData[0] = 0;
                        packageData[1] = (byte)_totalPackage;
                    }

                    _buffer = _packageHelper.Packing(CMD_UPDATE_DATA_PACKAGES, 0, 2, packageData);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_UPDATE_DATA_PACKAGES;
                    stream.Seek(0L, SeekOrigin.Begin);
                    _seed = 0;
                    _dataLen = 0;
                    break;

                case STATE.Booting_ReadFile:
                    int bytesRead = await stream.ReadAsync(_bufferTx, 0, 1024, cancellationToken);

                    if (bytesRead > 0 && bytesRead < 1024)
                    {
                        _flagFileEnd = true;
                        for (int i = bytesRead; i < 1024; i++)
                        {
                            _bufferTx[i] = 0;
                        }
                    }
                    else if (bytesRead == 0)
                    {
                        _bootProcessState = STATE.Booting_End;
                        break;
                    }

                    _buffer = _packageHelper.Packing(CMD_UPDATE, packageNum++, 1024, _bufferTx);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_UPDATE;
                    _seed = 0;
                    _dataLen = 0;
                    break;

                case STATE.Booting_End:
                    _buffer = _packageHelper.Packing(CMD_UPDATE_END, 0, 0, null);
                    SerialWrite(_buffer, 0, _buffer.Length);
                    _bootProcessState = STATE.Booting_WaitResponse1;
                    lastCommandTime = DateTime.Now; // Start timeout timer
                    _lastCmd = CMD_UPDATE_END;
                    UpdateMessage("\r\nDownload Completed!");
                    UpdateProgress(100.0);
                    return true;

                case STATE.Booting_WaitResponse1:
                    // NON-BLOCKING POLL like original code - check BytesToRead first
                    if (!_useNativeSerial && _serialPort!.BytesToRead >= 1)
                    {
                        _serialPort.Read(_bufferRx, 0, 1);
                        if (_bufferRx[0] == PACKAGE_HEADER)
                        {
                            _seed++;
                            _bootProcessState = STATE.Booting_WaitResponse2;
                            ErrorCntClr();
                            lastCommandTime = DateTime.Now; // Reset timeout timer (like original baseTimer.Stop/Start)
                        }
                    }
                    else if (_useNativeSerial)
                    {
                        // For native serial, try non-blocking read
                        byte[] tempBuf = new byte[1];
                        int rxBytes = NativeSerial.read(_nativeSerialFd, tempBuf, 1);
                        if (rxBytes > 0)
                        {
                            _bufferRx[0] = tempBuf[0];
                            if (_bufferRx[0] == PACKAGE_HEADER)
                            {
                                _seed++;
                                _bootProcessState = STATE.Booting_WaitResponse2;
                                ErrorCntClr();
                                lastCommandTime = DateTime.Now; // Reset timeout timer
                            }
                        }
                    }
                    break;

                case STATE.Booting_WaitResponse2:
                    // NON-BLOCKING POLL - check if 5 bytes available
                    if (!_useNativeSerial && _serialPort!.BytesToRead >= 5)
                    {
                        _serialPort.Read(_bufferRx, _seed, 5);
                        _seed += 5;
                        // Extract dataLen from bytes 4-5 (after header at position 0)
                        _dataLen = (_bufferRx[4] << 8) | _bufferRx[5];
                        _bootProcessState = STATE.Booting_WaitResponse3;
                    }
                    else if (_useNativeSerial)
                    {
                        // For native serial, try to read available data
                        byte[] tempBuf = new byte[5];
                        int rxBytes = NativeSerial.read(_nativeSerialFd, tempBuf, 5);
                        if (rxBytes == 5)
                        {
                            Array.Copy(tempBuf, 0, _bufferRx, _seed, 5);
                            _seed += 5;
                            // Extract dataLen from bytes 4-5
                            _dataLen = (_bufferRx[4] << 8) | _bufferRx[5];
                            _bootProcessState = STATE.Booting_WaitResponse3;
                        }
                    }
                    break;

                case STATE.Booting_WaitResponse3:
                {
                    // NON-BLOCKING POLL - check if enough bytes available
                    int bytesNeeded = _dataLen + 2 + 1; // data + CRC (2 bytes) + END (1 byte)

                    if (!_useNativeSerial)
                    {
                        int bytesAvailable = _serialPort!.BytesToRead;
                        if (bytesAvailable < bytesNeeded)
                        {
                            // Not enough bytes yet, continue loop
                            break;
                        }

                        // Read all available bytes (like original code does)
                        _serialPort.Read(_bufferRx, _seed, bytesAvailable);
                        _packageHelper.AnalysePackage(_bufferRx);

                        if (_packageHelper.Verify)
                        {
                            if (_packageHelper.CommandArgs == 0x06) // ACK
                            {
                                ErrorCntClr();
                                await HandleAckResponseAsync(_packageHelper.Command, packageNum).ConfigureAwait(false);
                            }
                            else
                            {
                                await HandleErrorResponseAsync(_packageHelper.CommandArgs, stream).ConfigureAwait(false);
                            }
                        }
                    }
                    else if (_useNativeSerial)
                    {
                        // For native serial, try to read the needed bytes
                        byte[] tempBuf = new byte[bytesNeeded];
                        int rxBytes = NativeSerial.read(_nativeSerialFd, tempBuf, bytesNeeded);
                        if (rxBytes >= bytesNeeded)
                        {
                            Array.Copy(tempBuf, 0, _bufferRx, _seed, rxBytes);
                            _packageHelper.AnalysePackage(_bufferRx);

                            if (_packageHelper.Verify)
                            {
                                if (_packageHelper.CommandArgs == 0x06) // ACK
                                {
                                    ErrorCntClr();
                                    await HandleAckResponseAsync(_packageHelper.Command, packageNum).ConfigureAwait(false);
                                }
                                else
                                {
                                    await HandleErrorResponseAsync(_packageHelper.CommandArgs, stream).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    break;
                }
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false); // Small delay to prevent CPU spinning
        }

        return false;
    }

    private async Task HandleAckResponseAsync(byte command, ushort packageNum)
    {
        switch (command)
        {
            case CMD_INTO_BOOT:
                UpdateMessage(" Entered Boot Mode Successfully!\r\n");
                _bootProcessState = STATE.HandShake1;
                break;

            case CMD_HANDSHAKE:
                UpdateMessage(" Handshake Successful!\r\n");
                _bootProcessState = STATE.Booting_CheckModelType;
                break;

            case CMD_CHECKMODELTYPE:
                UpdateMessage(" Model Validation Passed!\r\n");
                UpdateMessage(" Download Progress: 0%");
                _bootProcessState = STATE.Booting_SendPackages;
                break;

            case CMD_UPDATE_DATA_PACKAGES:
                _bootProcessState = STATE.Booting_ReadFile;
                break;

            case CMD_UPDATE:
                _percent = packageNum * 100.0 / _totalPackage;
                UpdateProgress(_percent);
                _bootProcessState = STATE.Booting_ReadFile;
                break;

            case CMD_UPDATE_END:
                UpdateMessage("\r\nDownload Completed!");
                UpdateProgress(100.0);
                return;
        }

        await Task.CompletedTask;
    }

    private async Task HandleErrorResponseAsync(byte errorCode, Stream stream)
    {
        switch (errorCode)
        {
            case 0xE1:
                UpdateMessage(" Handshake Code Error!");
                _flagTransmitting = false;
                break;

            case 0xE2:
                UpdateMessage(" Data Verification Error!");
                // Retry logic would go here
                break;

            case 0xE3:
                UpdateMessage(" Wrong Address!");
                _flagTransmitting = false;
                break;

            case 0xE4:
                UpdateMessage(" Flash Write Error!");
                _flagTransmitting = false;
                break;

            case 0xE5:
                UpdateMessage(" Command Error!");
                _flagTransmitting = false;
                break;

            case 0xE6:
                UpdateMessage(" Model Mismatch!");
                _flagTransmitting = false;
                break;
        }

        await Task.CompletedTask;
    }

    private void ErrorCntClr()
    {
        _cntError = 3;
        _cntRetry = 5;
    }

    private void UpdateMessage(string message)
    {
        StateMessage += message;
        MessageUpdated?.Invoke(this, message);
    }

    private void UpdateProgress(double progress)
    {
        _percent = progress;
        ProgressUpdated?.Invoke(this, progress);
    }
}
