# SmartPay/Ingenico Driver Installation

## Automatic Driver Detection & Installation

POS Bridge now includes **automatic driver detection and installation** for SmartPay/Ingenico terminals!

## How It Works

### 1. Automatic Detection
When you select "SmartPay/Ingenico" as the device type and click **Connect**:
- POS Bridge automatically detects if the Ingenico terminal is connected via USB
- Checks if the Windows driver is installed
- Identifies the assigned COM port

### 2. Automatic Installation
If the driver is not installed:
- POS Bridge will attempt to **automatically install** the driver
- Uses Windows Update or bundled driver files
- No manual intervention required in most cases

### 3. Manual Installation (if automatic fails)
If automatic installation fails, you'll see instructions to:
1. Download the driver from Ingenico's website
2. Run the installer
3. Reconnect the device

## Supported Terminals

| Series | Models | Connection |
|--------|--------|------------|
| iCT | iCT220, iCT250 | USB Virtual COM |
| iPP | iPP320, iPP350 | USB Virtual COM |
| Desk | Desk/5000 | USB Virtual COM |
| Lane | Lane/7000, Lane/8000 | USB Virtual COM |
| iUN | iUN series | USB Virtual COM |
| Self | Self series | USB Virtual COM |

## Driver Sources

### Option 1: Ingenico Official Website
Download from: https://www.ingenico.com/support/download-center

### Option 2: Bundle with POS Bridge
To bundle the driver with POS Bridge for offline installation:

1. Download the Ingenico USB driver from the official website
2. Place the installer in this folder: `Drivere/SmartPay/`
3. Rename it to: `Ingenico_Driver.exe` or `setup.exe`

POS Bridge will automatically find and use it for installation.

## File Structure

```
Drivere/
└── SmartPay/
    ├── README_DRIVER.md (this file)
    ├── Ingenico_Driver.exe (optional - bundled driver)
    └── setup.exe (alternative name)
```

## Technical Details

### VID/PID Detection
POS Bridge recognizes these Ingenico USB devices:
- VID: `0x0B00` (Ingenico)
- PIDs: `0x0068-0x0073` (various models)

### Driver Types
- **Virtual COM Port**: Creates a COM port (e.g., COM3) for serial communication
- **WinUSB**: Alternative driver (not recommended for Ingenico)

### Protocol
- **Physical**: USB cable
- **Logical**: Serial over USB (Virtual COM Port)
- **Protocol**: SmartPay ECR Link v1.8 (TLV-based)

## Troubleshooting

### "Device not detected"
1. Check USB cable connection
2. Ensure terminal is powered on
3. Try different USB port
4. Check Windows Device Manager

### "Driver installation failed"
1. Run POS Bridge as Administrator
2. Download driver manually from Ingenico
3. Install driver before connecting POS Bridge

### "No COM port assigned"
1. Open Device Manager
2. Look under "Ports (COM & LPT)"
3. Check for "Ingenico USB Serial Port"
4. Note the COM port number (e.g., COM3)
5. Select this port in POS Bridge manually

## Windows Driver Installation Methods

### Method 1: Windows Update (Automatic)
Windows will automatically download and install the driver when you connect the device.

### Method 2: Ingenico Installer
1. Download driver from Ingenico website
2. Run the installer
3. Follow wizard instructions
4. Restart if prompted

### Method 3: Manual INF Install
1. Download driver package
2. Extract to a folder
3. Open Device Manager
4. Find "Unknown Device" or device with warning
5. Right-click → Update Driver
6. Choose "Browse my computer"
7. Select the extracted folder
8. Follow prompts

## Connection Settings

Once driver is installed:

| Setting | Value |
|---------|-------|
| Device Type | SmartPay/Ingenico (Serial) |
| Connection | Serial (COM Port) |
| Port | Auto-detected or COMx |
| Baud Rate | 115200 (default) |

## Testing Connection

1. Connect Ingenico terminal via USB
2. Power on the terminal
3. Open POS Bridge
4. Select "SmartPay/Ingenico (Serial)"
5. Click "Test Connection"
6. POS Bridge will detect, install driver if needed, and connect

## Support

For driver issues:
- Ingenico Support: https://www.ingenico.com/support
- POS Bridge documentation: See main README.md
