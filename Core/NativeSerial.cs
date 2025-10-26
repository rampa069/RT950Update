using System;
using System.Runtime.InteropServices;

namespace RT950Update.Core;

/// <summary>
/// Native P/Invoke wrapper for POSIX termios to configure serial port in RAW mode
/// </summary>
public static class NativeSerial
{
    // termios flags
    private const uint IGNBRK = 0x00000001;
    private const uint BRKINT = 0x00000002;
    private const uint IGNPAR = 0x00000004;
    private const uint PARMRK = 0x00000008;
    private const uint INPCK = 0x00000010;
    private const uint ISTRIP = 0x00000020;
    private const uint INLCR = 0x00000040;
    private const uint IGNCR = 0x00000080;
    private const uint ICRNL = 0x00000100;
    private const uint IXON = 0x00000200;
    private const uint IXOFF = 0x00000400;
    private const uint IXANY = 0x00000800;

    private const uint OPOST = 0x00000001;
    private const uint ONLCR = 0x00000002;

    private const uint CSIZE = 0x00000300;
    private const uint CS8 = 0x00000300;
    private const uint CSTOPB = 0x00000400;
    private const uint CREAD = 0x00000800;
    private const uint PARENB = 0x00001000;
    private const uint PARODD = 0x00002000;
    private const uint HUPCL = 0x00004000;
    private const uint CLOCAL = 0x00008000;

    private const uint ECHO = 0x00000008;
    private const uint ECHOE = 0x00000002;
    private const uint ECHOK = 0x00000004;
    private const uint ECHONL = 0x00000010;
    private const uint ICANON = 0x00000100;
    private const uint ISIG = 0x00000080;
    private const uint IEXTEN = 0x00000400;

    private const int TCSANOW = 0;
    private const int VMIN = 16;
    private const int VTIME = 17;

    private const int O_RDWR = 0x0002;
    private const int O_NOCTTY = 0x20000;
    private const int O_NONBLOCK = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Termios
    {
        public ulong c_iflag;    // input flags
        public ulong c_oflag;    // output flags
        public ulong c_cflag;    // control flags
        public ulong c_lflag;    // local flags
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] c_cc;      // control chars
        public ulong c_ispeed;   // input speed
        public ulong c_ospeed;   // output speed
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPStr)] string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, ref Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optional_actions, ref Termios termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int cfsetispeed(ref Termios termios, ulong speed);

    [DllImport("libc", SetLastError = true)]
    private static extern int cfsetospeed(ref Termios termios, ulong speed);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcflush(int fd, int queue_selector);

    private const ulong B115200 = 115200;

    /// <summary>
    /// Configure a serial port file descriptor for RAW mode
    /// This bypasses .NET's SerialPort and uses native POSIX termios
    /// </summary>
    public static bool ConfigureRawMode(int fd)
    {
        var termios = new Termios
        {
            c_cc = new byte[20]
        };

        if (tcgetattr(fd, ref termios) != 0)
        {
            return false;
        }

        // Set baud rate to 115200
        cfsetispeed(ref termios, B115200);
        cfsetospeed(ref termios, B115200);

        // Configure for RAW mode (equivalent to cfmakeraw())
        termios.c_iflag &= ~(IGNBRK | BRKINT | PARMRK | ISTRIP | INLCR | IGNCR | ICRNL | IXON);
        termios.c_oflag &= ~OPOST;
        termios.c_lflag &= ~(ECHO | ECHONL | ICANON | ISIG | IEXTEN);
        termios.c_cflag &= ~(CSIZE | PARENB);
        termios.c_cflag |= CS8 | CREAD | CLOCAL;

        // Set timeout: VMIN = 0 (non-blocking reads), VTIME = 1 (0.1 second timeout)
        termios.c_cc[VMIN] = 0;
        termios.c_cc[VTIME] = 1;

        if (tcsetattr(fd, TCSANOW, ref termios) != 0)
        {
            return false;
        }

        // Flush any pending data
        tcflush(fd, 2); // TCIOFLUSH = 2

        return true;
    }

    /// <summary>
    /// Open a serial port in RAW mode and return the file descriptor
    /// </summary>
    public static int OpenRaw(string portPath)
    {
        int fd = open(portPath, O_RDWR | O_NOCTTY);
        if (fd < 0)
        {
            return -1;
        }

        if (!ConfigureRawMode(fd))
        {
            close(fd);
            return -1;
        }

        return fd;
    }

    /// <summary>
    /// Close a file descriptor
    /// </summary>
    public static void Close(int fd)
    {
        if (fd >= 0)
        {
            close(fd);
        }
    }

    /// <summary>
    /// Read from file descriptor with timeout
    /// </summary>
    [DllImport("libc", SetLastError = true)]
    public static extern int read(int fd, byte[] buffer, int count);

    /// <summary>
    /// Write to file descriptor
    /// </summary>
    [DllImport("libc", SetLastError = true)]
    public static extern int write(int fd, byte[] buffer, int count);

    // For select() to check if data is available
    [StructLayout(LayoutKind.Sequential)]
    private struct timeval
    {
        public long tv_sec;
        public long tv_usec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct fd_set
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] fds_bits;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int select(int nfds, ref fd_set readfds, IntPtr writefds, IntPtr exceptfds, ref timeval timeout);

    private static void FD_ZERO(ref fd_set set)
    {
        set.fds_bits = new int[32];
    }

    private static void FD_SET(int fd, ref fd_set set)
    {
        set.fds_bits[fd / 32] |= (1 << (fd % 32));
    }

    /// <summary>
    /// Check if data is available to read (with timeout in milliseconds)
    /// </summary>
    public static bool DataAvailable(int fd, int timeoutMs)
    {
        fd_set readfds = new fd_set();
        FD_ZERO(ref readfds);
        FD_SET(fd, ref readfds);

        timeval timeout = new timeval
        {
            tv_sec = timeoutMs / 1000,
            tv_usec = (timeoutMs % 1000) * 1000
        };

        int result = select(fd + 1, ref readfds, IntPtr.Zero, IntPtr.Zero, ref timeout);
        return result > 0;
    }

    /// <summary>
    /// Read with retry loop - tries multiple times to read data
    /// </summary>
    public static int ReadWithRetry(int fd, byte[] buffer, int count, int timeoutMs, int maxRetries, Action<string>? log = null)
    {
        int totalRead = 0;
        int retries = 0;
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        log?.Invoke($"[NATIVE ReadWithRetry] Starting: want {count} bytes, timeout={timeoutMs}ms, maxRetries={maxRetries}\r\n");

        while (totalRead < count && retries < maxRetries)
        {
            long elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
            if (elapsed >= timeoutMs)
            {
                log?.Invoke($"[NATIVE ReadWithRetry] Total timeout reached after {elapsed}ms\r\n");
                break;
            }

            int remaining = timeoutMs - (int)elapsed;
            bool dataAvailable = DataAvailable(fd, Math.Min(remaining, 100));

            if (retries % 10 == 0) // Log every 10 retries
            {
                log?.Invoke($"[NATIVE ReadWithRetry] Retry {retries}/{maxRetries}, elapsed={elapsed}ms, dataAvailable={dataAvailable}\r\n");
            }

            if (dataAvailable)
            {
                int bytesRead = read(fd, buffer, count - totalRead);
                log?.Invoke($"[NATIVE ReadWithRetry] read() returned {bytesRead} bytes\r\n");

                if (bytesRead > 0)
                {
                    totalRead += bytesRead;
                    if (totalRead >= count)
                    {
                        log?.Invoke($"[NATIVE ReadWithRetry] SUCCESS: Read total of {totalRead} bytes\r\n");
                        break;
                    }
                }
                else if (bytesRead < 0)
                {
                    log?.Invoke($"[NATIVE ReadWithRetry] ERROR: read() returned {bytesRead}\r\n");
                    return -1; // Error
                }
            }

            retries++;
            System.Threading.Thread.Sleep(10); // Small delay between retries
        }

        log?.Invoke($"[NATIVE ReadWithRetry] Finished: totalRead={totalRead}, retries={retries}\r\n");
        return totalRead;
    }
}
