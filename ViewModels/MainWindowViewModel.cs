using System;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using ReactiveUI;
using RT950Update.Core;

namespace RT950Update.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private string _filePath = string.Empty;
    private string? _selectedComPort;
    private string _statusMessage = string.Empty;
    private bool _isDownloading = false;
    private double _progressValue = 0.0;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainWindowViewModel()
    {
        ComPorts = new ObservableCollection<string>();
        RefreshComPorts();

        OpenFileCommand = new AsyncRelayCommand(OpenFileAsync);
        FlashingModeCommand = new AsyncRelayCommand(StartFlashingModeAsync, () => !IsDownloading);
        UpgradeModeCommand = new AsyncRelayCommand(StartUpgradeModeAsync, () => !IsDownloading);
        RefreshPortsCommand = new RelayCommand(RefreshComPorts);
    }

    public string FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public string? SelectedComPort
    {
        get => _selectedComPort;
        set => this.RaiseAndSetIfChanged(ref _selectedComPort, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isDownloading, value))
            {
                ((AsyncRelayCommand)FlashingModeCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)UpgradeModeCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public double ProgressValue
    {
        get => _progressValue;
        set => this.RaiseAndSetIfChanged(ref _progressValue, value);
    }

    public ObservableCollection<string> ComPorts { get; }

    public ICommand OpenFileCommand { get; }
    public ICommand FlashingModeCommand { get; }
    public ICommand UpgradeModeCommand { get; }
    public ICommand RefreshPortsCommand { get; }

    public string FlashingModeInstructions =>
        "Flashing mode:\nTurn off the power, while holding down side keys 3 and 4, " +
        "turn on the power, enter the Update interface, click the [Flashing Mode] button, " +
        "and wait for completion.";

    public string UpgradeModeInstructions =>
        "Upgrade mode:\nTurn on the power, click the [Upgrade Mode] button, " +
        "and wait for completion.";

    private async Task OpenFileAsync()
    {
        // This will be called from the View using platform-specific file dialog
        await Task.CompletedTask;
    }

    private async Task StartFlashingModeAsync()
    {
        if (IsDownloading) return;
        await StartDownloadAsync(true);
    }

    private async Task StartUpgradeModeAsync()
    {
        if (IsDownloading) return;
        await StartDownloadAsync(false);
    }

    private async Task StartDownloadAsync(bool flashingMode)
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusMessage = "Please select a BTF file first.\n";
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedComPort))
        {
            StatusMessage = "Please select a COM port.\n";
            return;
        }

        SerialPort? serialPort = null;

        try
        {
            IsDownloading = true;
            ProgressValue = 0.0;
            StatusMessage = "Starting firmware update...\r\n";

            // Clean port name (remove suffixes)
            string portName = SelectedComPort
                .Replace(" (recommended)", "")
                .Replace(" (MIRROR - use this!)", "");

            // Open serial port EXACTLY like RT880-FlasherX
            serialPort = new SerialPort(portName, 115200, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 5000
            };

            serialPort.Open();

            // Flush buffers after opening (important for reliable communication)
            serialPort.DiscardInBuffer();
            serialPort.DiscardOutBuffer();

            _cancellationTokenSource = new CancellationTokenSource();
            var bootHelper = new BootHelper(flashingMode);

            bootHelper.MessageUpdated += (s, msg) =>
            {
                Dispatcher.UIThread.Post(() => StatusMessage += msg);
            };

            bootHelper.ProgressUpdated += (s, progress) =>
            {
                Dispatcher.UIThread.Post(() => ProgressValue = progress);
            };
            bool success = await bootHelper.BootLoadingAsync(
                serialPort,
                FilePath,
                _cancellationTokenSource.Token);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage += "\n✓ Update completed successfully!\n";
                });
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage += "\n✗ Update failed.\n";
                });
            }
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage += $"\nError: {ex.Message}\n";
            });
        }
        finally
        {
            // Close the serial port
            if (serialPort != null && serialPort.IsOpen)
            {
                try
                {
                    serialPort.Close();
                    serialPort.Dispose();
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            IsDownloading = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private void RefreshComPorts()
    {
        ComPorts.Clear();

        // Add virtual port if it exists (for serial mirroring)
        const string virtualPort = "/tmp/vmodem0";
        if (System.IO.File.Exists(virtualPort))
        {
            ComPorts.Add(virtualPort + " (MIRROR - use this!)");
        }

        var ports = SerialPort.GetPortNames();

        // On macOS, prefer /dev/cu.* over /dev/tty.* for raw serial communication
        var sortedPorts = ports.OrderBy(p => p.Contains("/tty.") ? 1 : 0).ThenBy(p => p);

        foreach (var port in sortedPorts)
        {
            // Add both cu and tty ports, but mark the preferred one
            if (port.Contains("/tty."))
            {
                string cuPort = port.Replace("/tty.", "/cu.");
                if (!ComPorts.Contains(cuPort))
                {
                    ComPorts.Add(cuPort + " (recommended)");
                }
            }
            ComPorts.Add(port);
        }

        if (ComPorts.Count > 0 && SelectedComPort == null)
        {
            SelectedComPort = ComPorts[0];
        }
    }
}
