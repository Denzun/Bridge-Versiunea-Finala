using System.Windows;
using System.Windows.Media;
using POSBridge.Core;
using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Models;
using POSBridge.Devices.SmartPay;
using MessageBox = System.Windows.MessageBox;

namespace POSBridge.WPF;

/// <summary>
/// Multi-vendor device connection logic (Incotex, Tremol, Elcom)
/// Partial class extension for MainWindow
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Connect to a multi-vendor device (IFiscalDevice implementation)
    /// </summary>
    private async Task ConnectMultiVendorDeviceAsync(bool autoStartWatcher)
    {
        bool connectionFailed = false;
        Exception? connectionError = null;
        
        try
        {
            string deviceName = FiscalDeviceFactory.GetDeviceTypeName(_selectedDeviceType);
            string connDesc = _connectionType == "USB" ? "USB (auto)" : _connectionType == "Ethernet" ? $"Ethernet ({_ipAddress}:{_tcpPort})" : $"{_comPort} @ {_baudRate}";
            ConnectionLogger.WriteSection($"ÎNCEPUT CONEXIUNE - {deviceName} ({connDesc})");
            Log($"Conectare la {deviceName} prin {connDesc}...");
            UpdateStatus("Se conectează...", Colors.Orange);

            // SmartPay: Check driver before connecting
            if (_selectedDeviceType == Abstractions.Enums.DeviceType.SmartPay)
            {
                var driverStatus = SmartPayDriverInstaller.CheckDriverStatus();
                
                if (!driverStatus.DeviceConnected)
                {
                    Log("⚠ Ingenico device not detected via USB VID/PID scan.");
                    
                    // List available COM ports to help user
                    var comPorts = SmartPayDriverInstaller.ListAllComPorts();
                    if (comPorts.Count > 0)
                    {
                        Log("Available COM ports:");
                        foreach (var (comPort, desc) in comPorts)
                        {
                            string marker = (comPort == _comPort) ? " ← selected" : "";
                            Log($"  • {comPort}: {desc}{marker}");
                        }
                    }
                    
                    // If user has selected a COM port, try it anyway
                    if (!string.IsNullOrEmpty(_comPort) && _comPort.StartsWith("COM"))
                    {
                        Log($"⚠ Will try to connect to selected port {_comPort} anyway...");
                        
                        // Test if port is accessible
                        if (!SmartPayDriverInstaller.TestComPort(_comPort))
                        {
                            throw new Exception(
                                $"Cannot access {_comPort}.\n\n" +
                                "Please:\n" +
                                "1. Ensure the device is connected via USB\n" +
                                "2. Check Device Manager for the correct COM port\n" +
                                "3. Select the correct port from the dropdown\n\n" +
                                "Note: You may need to install the Ingenico USB driver from:\n" +
                                "https://www.ingenico.com/support/download-center");
                        }
                    }
                    else
                    {
                        throw new Exception(
                            "Ingenico device not detected.\n\n" +
                            "Please:\n" +
                            "1. Connect the device via USB and power it on\n" +
                            "2. Install the Ingenico USB driver\n" +
                            "3. Select the correct COM port from the dropdown\n\n" +
                            "Driver download: https://www.ingenico.com/support/download-center");
                    }
                }
                else if (!driverStatus.DriverInstalled || string.IsNullOrEmpty(driverStatus.ComPort))
                {
                    Log("⚠ SmartPay driver not installed. Attempting automatic installation...");
                    var progress = new Progress<string>(msg => Log($"  → {msg}"));
                    var installResult = await SmartPayDriverInstaller.InstallDriverAsync(progress);
                    
                    if (!installResult.Success)
                    {
                        // Allow manual connection attempt even if auto-install fails
                        if (!string.IsNullOrEmpty(_comPort) && _comPort.StartsWith("COM"))
                        {
                            Log($"⚠ Auto-install failed. Will try to connect to {_comPort} anyway...");
                        }
                        else
                        {
                            throw new Exception($"Driver installation failed: {installResult.Message}");
                        }
                    }
                    else
                    {
                        Log($"✅ Driver installed! Device on {installResult.ComPort}");
                        
                        // Update COM port in settings
                        if (!string.IsNullOrEmpty(installResult.ComPort))
                        {
                            _comPort = installResult.ComPort;
                            PortComboBox.SelectedItem = _comPort;
                            SaveSettings();
                        }
                    }
                }
                else
                {
                    Log($"✅ SmartPay driver OK. Device on {driverStatus.ComPort}");
                    
                    // Auto-select COM port if not set
                    if (!string.IsNullOrEmpty(driverStatus.ComPort) && 
                        (string.IsNullOrEmpty(_comPort) || _comPort == "AUTO"))
                    {
                        _comPort = driverStatus.ComPort;
                        PortComboBox.SelectedItem = _comPort;
                    }
                }
            }

            // Create device instance
            if (_currentDevice is IDisposable disposable)
            {
                try { disposable.Dispose(); } catch { }
            }
            _currentDevice = null;
            _currentDevice = FiscalDeviceFactory.CreateDevice(_selectedDeviceType);
            
            // Build connection settings from UI/saved settings
            var connType = Abstractions.Enums.ConnectionType.Serial;
            string port = _comPort;
            int baud = _baudRate;

            if (_connectionType == "USB")
            {
                port = "USB"; // AUTO COM detection (VID/PID) + WinUSB fallback
                baud = _baudRate > 0 ? _baudRate : 115200;
            }
            else if (_connectionType == "Ethernet")
            {
                connType = Abstractions.Enums.ConnectionType.Ethernet;
                port = _ipAddress;
                baud = 115200; // N/A for Ethernet
            }
            // Serial: use _comPort and _baudRate from settings

            var settings = new ConnectionSettings
            {
                Type = connType,
                Port = port,
                BaudRate = baud,
                IpAddress = _ipAddress,
                TcpPort = _tcpPort,
                TimeoutSeconds = 10
            };

            // Connect to device (with timeout + cancel support)
            int connectTimeoutMs = 15000;
            var ct = _connectCts?.Token ?? CancellationToken.None;
            var connectTask = _currentDevice.ConnectAsync(settings);
            var cancelTask = Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => false, TaskContinuationOptions.OnlyOnCanceled);
            var completed = await Task.WhenAny(connectTask, Task.Delay(connectTimeoutMs), cancelTask);
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("Conectarea a fost anulată.");
            if (completed != connectTask)
                throw new TimeoutException($"Conexiunea a expirat ({connectTimeoutMs / 1000}s). Verificați că dispozitivul este conectat și pornit.");
            bool connected = await connectTask;
            
            if (connected)
            {
                ConnectionLogger.Write($"CONEXIUNE REUȘITĂ: {deviceName} ({connDesc})");
                Log($"✓ Conectat la {deviceName} prin USB");
                UpdateStatus("Conectat", Colors.Green);

                // Try to get device info and transmit to server
                try
                {
                    var info = await _currentDevice.GetDeviceInfoAsync();
                    var serie = !string.IsNullOrWhiteSpace(info.SerialNumber)
                        ? info.SerialNumber
                        : (!string.IsNullOrWhiteSpace(info.FirmwareVersion) && info.FirmwareVersion != "N/A"
                            ? $"FW {info.FirmwareVersion}"
                            : "—");
                    var serieFiscala = !string.IsNullOrWhiteSpace(info.FiscalNumber) ? info.FiscalNumber : "—";
                    var codFiscal = !string.IsNullOrWhiteSpace(info.FiscalCode) ? info.FiscalCode : "—";
                    var model = string.IsNullOrWhiteSpace(info.ModelName) ? "N/A" : info.ModelName;

                    Log($"  Vendor: {info.VendorName}");
                    Log($"  Model: {model}");
                    Log($"  Firmware: {info.FirmwareVersion}");
                    Log($"  Serie: {serie}");
                    Log($"  Serie Fiscală: {serieFiscala}");
                    Log($"  Cod Fiscal: {codFiscal}");

                    VendorNameText.Text = info.VendorName;
                    ModelNameText.Text = model;
                    SerieFabricatieText.Text = serie;
                    NumeDispozitivText.Text = model;
                    SerieFiscalaText.Text = serieFiscala;
                    CodFiscalText.Text = codFiscal;

                    // Save and transmit model/serial to connection server
                    if (model != "N/A" || !string.IsNullOrWhiteSpace(info.SerialNumber))
                    {
                        SaveDeviceInfoToSettings(model, serie);
                        _ = SendDeviceInfoToServerAsync(model, serie);
                    }
                }
                catch (Exception ex)
                {
                    Log($"⚠ Avertisment: Nu s-au putut citi informațiile dispozitivului: {ex.Message}");
                }

                // Enable controls
                DisconnectButton.IsEnabled = true;
                StartWatcherButton.IsEnabled = true; // Incotex suportă monitorizare (IncotexBonProcessor)
                TestConnectionButton.IsEnabled = true;
                FeedPaperButton.IsEnabled = false;
                FeedLinesBox.IsEnabled = false;
                CancelReceiptButton.IsEnabled = true;
                CashInButton.IsEnabled = true;
                CashOutButton.IsEnabled = true;
                CashAmountBox.IsEnabled = true;
                XReportButton.IsEnabled = true;
                ZReportButton.IsEnabled = true;
                EnableReportButtons(true);

                StatusBarText.Text = $"{deviceName} conectat. Gata pentru bonuri.";

                if (autoStartWatcher)
                {
                    StartWatcher();
                }
            }
            else
            {
                connectionFailed = true;
                throw new Exception($"{deviceName} nu răspunde");
            }
        }
        catch (Exception ex)
        {
            connectionFailed = true;
            connectionError = ex;

            string deviceName = FiscalDeviceFactory.GetDeviceTypeName(_selectedDeviceType);
            ConnectionLogger.Write($"CONEXIUNE EȘUATĂ: {deviceName} - {ex.Message}");
            Log($"✗ Conexiune eșuată la {deviceName}: {ex.Message}");
            UpdateStatus("Conexiune eșuată", Colors.Red);
            ShowTrayNotification($"POS Bridge - Conexiune eșuată", ex.Message);

            // Enhanced error message
            string errorDetail = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetail += $"\n\nDetalii: {ex.InnerException.Message}";
            }
            
            if (ex.Message.Contains("ocupat") || ex.Message.Contains("in use") || ex.Message.Contains("Access denied"))
            {
                errorDetail = $"Portul este ocupat de altă aplicație.\n\nSoluții:\n• Închideți alte aplicații care ar putea folosi portul (FiscalNet, etc.)\n• Verificați în Task Manager dacă există procese blocate\n• Deconectați și reconectați dispozitivul USB\n\nEroare: {ex.Message}";
            }
            
            MessageBox.Show(
                $"Nu s-a putut conecta la {deviceName}:\n\n{errorDetail}\n\nVerificați:\n- Portul COM corect\n- Dispozitivul este pornit și conectat\n- Portul nu este folosit de altă aplicație\n- Driverele sunt instalate",
                "Eroare conexiune",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            // Always re-enable Connect button if connection failed
            if (connectionFailed || _currentDevice?.IsConnected != true)
            {
                ConnectButton.IsEnabled = true;
                
                // Disable buttons that require active connection
                DisconnectButton.IsEnabled = false;
                StartWatcherButton.IsEnabled = false;
                TestConnectionButton.IsEnabled = false;
                FeedPaperButton.IsEnabled = false;
                CancelReceiptButton.IsEnabled = false;
                // CashIn, CashOut, XReport, ZReport stay enabled - handlers show error if not connected
                CashAmountBox.IsEnabled = true;
                EnableReportButtons(false);
                
                // Update status bar
                StatusBarText.Text = connectionError != null 
                    ? $"Conexiune eșuată: {connectionError.Message}" 
                    : "Deconectat. Apasă 'Conectare dispozitiv' pentru a încerca din nou.";
            }
        }
    }
}
