# Changelog

All notable changes to RT950Update will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Graphical progress bar for firmware updates
- Visual progress indicator (0-100%) during download
- macOS DMG packaging script (`create-dmg.sh`)
- Universal binary support (Apple Silicon + Intel)
- Custom application icon (icon.icns)
- Icon conversion utility in build script (WebP to ICNS)
- Automatic DMG creation with professional layout
- Build scripts for arm64, x64, and universal macOS binaries

### Changed
- Progress display now uses visual progress bar instead of text percentages
- Status messages no longer filled with repetitive percentage updates
- Improved UI layout with dedicated progress indicator

### Fixed
- hdiutil detach errors during DMG creation
- lipo errors when combining universal binaries with identical architectures
- Progress bar now properly updates on UI thread

## [0.2.0] - 2025-01-26

### Added
- Cross-platform serial communication using System.IO.Ports
- Support for both Upgrade Mode and Flashing Mode
- BTF firmware file selection via file browser
- COM port auto-detection and selection
- Real-time status messages during firmware update
- Serial port mirroring support for debugging (/tmp/vmodem0)
- macOS-specific COM port preferences (cu vs tty)
- Color-coded instruction panels (green for Upgrade, red for Flashing)
- Proper serial port cleanup on completion or error

### Technical Details
- Built with Avalonia UI 11.3.6
- Uses .NET 9.0
- ReactiveUI for MVVM pattern
- CommunityToolkit.MVVM for commands
- Serial communication at 115200 baud rate
- Packet-based firmware transfer protocol
- Support for 512-byte and 1024-byte packet sizes

## [0.1.0] - Initial Release

### Added
- Initial RT-950 firmware update application
- Basic firmware download functionality
- Serial port communication
- BTF file format support
- Boot loader protocol implementation
- State machine for firmware update process
- Error handling and timeout management
- Cross-platform support (macOS, Windows, Linux)

---

## Project Information

**Application**: RT950Update
**Description**: Firmware update utility for RT-950 radio devices
**Platform**: Cross-platform (macOS, Windows, Linux)
**Framework**: Avalonia UI / .NET 9.0
**License**: TBD

## Installation

### macOS
1. Download the latest DMG file from releases
2. Open the DMG
3. Drag RT950Update.app to Applications folder
4. Launch from Applications

### Building from Source
```bash
# Clone the repository
git clone [repository-url]
cd RT950Update

# Build for current platform
dotnet build -c Release

# Create macOS DMG (Universal)
./create-dmg.sh universal

# Create macOS DMG (Apple Silicon only)
./create-dmg.sh arm64

# Create macOS DMG (Intel only)
./create-dmg.sh x64
```

## Usage

### Upgrade Mode (Normal Update)
1. Select BTF firmware file
2. Choose COM port
3. Turn on the radio
4. Click "Upgrade Mode"
5. Wait for completion

### Flashing Mode (Recovery Update)
1. Select BTF firmware file
2. Choose COM port
3. Turn off the radio
4. Hold side keys 3 and 4
5. Turn on the radio while holding keys
6. Click "Flashing Mode"
7. Wait for completion

## Known Issues
- None currently reported

## Roadmap
- [ ] Firmware file verification
- [ ] Backup functionality
- [ ] Multi-device support
- [ ] Update history logging
- [ ] Automatic firmware download
- [ ] Device information display
