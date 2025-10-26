# RT-950 UPDATE - Cross-platform Version

Firmware update application for the RT-950 radio, ported from Windows Forms to Avalonia to run on macOS, Linux, and Windows.

## Features

- ✅ Cross-platform GUI with Avalonia
- ✅ Support for macOS (Apple Silicon and Intel)
- ✅ Support for Linux and Windows
- ✅ Modern async/await instead of Thread.Abort()
- ✅ Native serial port handling using POSIX termios on macOS/Linux
- ✅ Automatic serial port detection with smart filtering
- ✅ Virtual port support for serial debugging (/tmp/vmodem0)
- ✅ Optimized for macOS: prefers /dev/cu.* over /dev/tty.* ports
- ✅ Same communication protocol as the original version
- ✅ Built-in mode instructions in the UI
- ✅ Clean cancellation support with CancellationToken

## Requirements

- .NET 9.0 SDK
- USB serial port (RT-950 radio connected)

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run
```

## Create Standalone Executable

For macOS (Apple Silicon):
```bash
dotnet publish -c Release -r osx-arm64 --self-contained
```

For macOS (Intel):
```bash
dotnet publish -c Release -r osx-x64 --self-contained
```

For Linux x64:
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

For Windows x64:
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

The executable will be in `bin/Release/net9.0/{runtime}/publish/`

## Usage

1. Connect your RT-950 radio to a USB port
2. Run the application
3. Click "..." to select a .BTF firmware file
4. Select the COM port from the dropdown list
   - On macOS, ports marked "(recommended)" are preferred
   - If using serial mirroring, select the port marked "(MIRROR - use this!)"
5. Choose the update mode:
   - **Upgrade Mode** (green): Radio powered on normally
   - **Flashing Mode** (red): Radio in bootloader mode (power off, hold side keys 3+4, power on)
6. Wait for the update to complete

## Architecture

```
RT950Update/
├── Core/
│   ├── BootHelper.cs      - Communication and update logic
│   ├── PackageFmt.cs      - Protocol packaging
│   ├── NativeSerial.cs    - Native POSIX serial port handling
│   └── STATE.cs           - Process states
├── ViewModels/
│   ├── MainWindowViewModel.cs - UI logic
│   └── RelayCommand.cs    - Command implementation
└── Views/
    └── MainWindow.axaml   - Graphical interface
```

## Differences from Original Version

- Uses async/await instead of blocking threads
- CancellationToken for clean cancellation
- ReactiveUI for MVVM pattern
- Native serial port handling on macOS/Linux via P/Invoke
- Cross-platform: macOS, Linux, and Windows
- .NET 9.0 instead of .NET Framework 2.0
- Smart serial port detection and filtering
- Support for virtual serial ports (debugging)

## Technical Notes

The communication protocol is identical to the original:
- Baudrate: 115200
- Data bits: 8
- Stop bits: 1
- Parity: None
- Proprietary protocol with CRC-16/CCITT

### Serial Port Implementation

On macOS/Linux, the application uses native POSIX termios via P/Invoke for reliable serial communication in RAW mode. This bypasses .NET's SerialPort limitations on these platforms and ensures proper hardware flow control.

On macOS specifically:
- `/dev/cu.*` ports are preferred over `/dev/tty.*` for callout devices
- The application automatically marks recommended ports in the dropdown
- Virtual port `/tmp/vmodem0` is detected for serial port mirroring/debugging
