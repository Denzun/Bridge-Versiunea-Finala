using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Forms;
using System.Management;
using Microsoft.Win32;
using POSBridge.Core;
using POSBridge.Core.Models;
using POSBridge.Core.Services;
using POSBridge.Devices.Datecs;
using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using MessageBox = System.Windows.MessageBox;
using Application = System.Windows.Application;

// Force load device assemblies for reflection (Multi-Vendor Architecture)
// This ensures assemblies are loaded before FiscalDeviceFactory.CreateDevice() uses reflection
#pragma warning disable CS8019 // Unnecessary using directive (needed for assembly loading)
using POSBridge.Devices.Incotex;
#pragma warning restore CS8019

namespace POSBridge.WPF;

public partial class MainWindow : Window
{
    private NotifyIcon? _trayIcon;
    private bool _isRealExit;
    private FolderWatcherService? _watcher;
    private readonly FiscalEngine _fiscalEngine; // Legacy Datecs engine (kept for backward compatibility)
    private IFiscalDevice? _currentDevice; // Multi-vendor device instance
    private string _bonFolder;
    private readonly string _settingsPath;
    private int _operatorCode = 1;
    private string _operatorPassword = "0000";
    private string _comPort = "COM7";
    private int _baudRate = 115200;
    private bool _useEthernet = false;
    private string _ipAddress = "192.168.1.219";
    private int _tcpPort = 9100;
    private DeviceType _selectedDeviceType = DeviceType.Incotex; // Default to Incotex
    private string _connectionType = "USB"; // Default to USB Direct for Incotex
    private const string DebugLogPath = @"d:\Proiecte Cursor\POS Bridge\.cursor\debug.log";
    private static readonly string LogFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
    private int _totalProcessed;
    private int _successCount;
    private int _errorCount;
    private bool _isWatching;
    private bool? _allowPrinting;
    private bool _runAtStartup;
    private string _dudePath = @"C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe";
    private string _deviceSerialNumber = string.Empty;
    private string _tenantCode = "demo";
    private DeviceActivationService? _activationService;
    private CancellationTokenSource? _connectCts;
    private DateTime? _firstAuthDate; // Data instalării licenței (de la server)

    public MainWindow()
    {
        try
        {
            InitializeComponent();
            
            // Force load device assemblies for Multi-Vendor Architecture
            // This ensures assemblies are loaded before FiscalDeviceFactory uses reflection
            ForceLoadDeviceAssemblies();
            
            _fiscalEngine = FiscalEngine.Instance;
            _settingsPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "settings.txt");
            var settings = LoadSettings();
            _bonFolder = settings.BonFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Bon");
            _operatorCode = settings.OperatorCode;
            _operatorPassword = settings.OperatorPassword;
            _runAtStartup = settings.RunAtStartup;
            _comPort = settings.ComPort;
            _baudRate = settings.BaudRate;
            _dudePath = settings.DudePath;
            _useEthernet = settings.UseEthernet;
            _ipAddress = settings.IpAddress;
            _tcpPort = settings.TcpPort;
            _selectedDeviceType = settings.DeviceType;
            _connectionType = settings.ConnectionType;
            // DeviceSerialNumber and TenantCode are handled in App.xaml.cs for activation
            FolderPathBox.Text = Path.GetFullPath(_bonFolder);
            OperatorCodeBox.Text = _operatorCode.ToString();
            OperatorPasswordBox.Text = _operatorPassword;
            StartupWithWindowsCheckBox.IsChecked = _runAtStartup;
            
            Loaded += MainWindow_Loaded;
            InitTrayIcon();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Eroare la construcție:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                "Eroare constructor", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
            throw;
        }
    }

    /// <summary>
    /// Forces loading of device assemblies to support reflection-based factory.
    /// Without this, FiscalDeviceFactory.CreateDevice() will fail to find assemblies.
    /// </summary>
    private void ForceLoadDeviceAssemblies()
    {
        try
        {
            // Force load Incotex assembly by referencing a type from it
            // This ensures the assembly is loaded into AppDomain
            var incotexType = typeof(POSBridge.Devices.Incotex.IncotexDevice);
            Log($"✓ Loaded assembly: {incotexType.Assembly.GetName().Name}");
        }
        catch (Exception ex)
        {
            Log($"⚠ Warning: Could not pre-load Incotex assembly: {ex.Message}");
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Create Bon folder structure if it doesn't exist
            EnsureBonFolders(_bonFolder);

            LoadBaudRates();
            LoadComPorts();
            LoadDeviceTypes();
            LoadConnectionType();
            LoadActivationInfo();
            UpdateDemoFooter();

            Log("Aplicație pornită.");
            Log($"Folder monitorizat: {_bonFolder}");

            if (_runAtStartup)
            {
                ApplyRunAtStartup();
                SaveSettings(); // Persistă pornirea automată
            }

            // Auto-connect enabled with auto-start watcher
            Log("Conectare automată la imprimanta fiscală...");
            _connectCts = new CancellationTokenSource();
            ConnectButton.Visibility = Visibility.Collapsed;
            CancelConnectionButton.Visibility = Visibility.Visible;
            try
            {
                await ConnectAndMaybeStartWatcherAsync(autoStartWatcher: true);
            }
            finally
            {
                ConnectButton.Visibility = Visibility.Visible;
                CancelConnectionButton.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Eroare la inițializare:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}", 
                "Eroare inițializare", 
                MessageBoxButton.OK, 
                MessageBoxImage.Error);
        }
    }

    // ==================== CONNECTION ====================

    private async Task ConnectAndMaybeStartWatcherAsync(bool autoStartWatcher)
    {
        bool connectionFailed = false;
        Exception? connectionError = null;
        
        try
        {
            // Disable button immediately on UI thread
            ConnectButton.IsEnabled = false;
            
            // Check if we're using multi-vendor architecture (Incotex, Tremol, Elcom)
            if (_selectedDeviceType != DeviceType.Datecs)
            {
                await ConnectMultiVendorDeviceAsync(autoStartWatcher);
                return;
            }
            
            // Legacy Datecs connection using FiscalEngine
            Log("Inițializare DUDE COM Server...");
            UpdateStatus("Se conectează...", Colors.Orange);

            ConnectionLogger.WriteSection($"ÎNCEPUT CONEXIUNE - Datecs {(_useEthernet ? $"Ethernet {_ipAddress}:{_tcpPort}" : $"Serial {_comPort}@{_baudRate}")}");

            var (opCode, opPwd) = GetOperatorCredentials();
            _fiscalEngine.OperatorCode = opCode;
            _fiscalEngine.OperatorPassword = opPwd;

            Task<bool> connectTask;
            string connectionInfo;

            if (_useEthernet)
            {
                _ipAddress = IpAddressBox.Text.Trim();
                if (int.TryParse(TcpPortBox.Text, out int manualPort) && manualPort > 0 && manualPort <= 65535)
                    _tcpPort = manualPort;

                // TCP/IP: try manual port first; if it fails, fall back to auto-port scan
                Log($"Conectare prin Ethernet la {_ipAddress}:{_tcpPort}...");
                connectionInfo = $"{_ipAddress}:{_tcpPort}";
                
                connectTask = Task.Run(() =>
                {
                    try
                    {
                        bool connected = false;
                        int? successfulPort = null;
                        int? portToTry = _tcpPort > 0 ? _tcpPort : null;

                        // 1. Try manual port first (if user specified one)
                        if (portToTry.HasValue)
                        {
                            Dispatcher.Invoke(() => Log($"🔌 Încercare port manual: {portToTry.Value}"));
                            try
                            {
                                _fiscalEngine.InitializeForTCP(_ipAddress, portToTry.Value);
                                connected = _fiscalEngine.TestConnection();
                                if (connected)
                                    successfulPort = portToTry.Value;
                            }
                            catch
                            {
                                Dispatcher.Invoke(() => Log($"Port {portToTry.Value} a eșuat, scanare automată..."));
                            }
                        }

                        // 2. If manual port failed, try auto-port detection
                        if (!connected)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Log($"🔍 Scanare automată: 3999, 9100, 4999, 9999, 8000, 4000, 5000...");
                                Log($"⏱️ Durată estimată: ~15 secunde");
                            });
                            successfulPort = _fiscalEngine.InitializeForTCPAutoPort(_ipAddress);
                            if (successfulPort.HasValue)
                                connected = _fiscalEngine.TestConnection();
                        }

                        if (connected && successfulPort.HasValue)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                _tcpPort = successfulPort.Value;
                                TcpPortBox.Text = successfulPort.Value.ToString();
                                SaveSettings();
                                Log($"✅ Conectat pe port {successfulPort.Value}");
                            });
                            return true;
                        }
                        Dispatcher.Invoke(() => Log($"❌ Niciun port funcțional (încercați 9100 sau 3999 manual în câmpul Port)"));
                        return false;
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => Log($"✗ Eroare în conectare TCP: {ex.Message}"));
                        throw;
                    }
                });
            }
            else
            {
                // RS232 Serial Connection
                var selectedPort = PortComboBox.SelectedItem?.ToString();
                var selectedBaud = BaudComboBox.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(selectedPort))
                    throw new InvalidOperationException("Selectați un port COM.");
                if (!int.TryParse(selectedBaud, out int baudRate))
                    throw new InvalidOperationException("Selectați o viteză de transmisie validă.");

                _comPort = selectedPort!;
                _baudRate = baudRate;
                connectionInfo = $"{selectedPort} @ {baudRate}";
                
                connectTask = Task.Run(() =>
                {
                    try
                    {
                        _fiscalEngine.Initialize(selectedPort!, baudRate);
                        return _fiscalEngine.TestConnection();
                    }
                    catch (UnauthorizedAccessException)
                    {
                        throw new InvalidOperationException($"Portul {selectedPort} este ocupat de altă aplicație. Închideți aplicația care folosește portul și încercați din nou.");
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            Log($"✗ Eroare în conectare Serial: {ex.Message}");
                        });
                        throw;
                    }
                });
            }

            SaveSettings();

            int timeoutMs = _useEthernet ? 12000 : 8000;
            var ct = _connectCts?.Token ?? CancellationToken.None;
            var cancelTask = Task.Delay(Timeout.Infinite, ct).ContinueWith(_ => false, TaskContinuationOptions.OnlyOnCanceled);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs), cancelTask);
            if (ct.IsCancellationRequested)
                throw new OperationCanceledException("Conectarea a fost anulată.");
            if (completed != connectTask)
                throw new TimeoutException($"Conexiunea a expirat ({timeoutMs/1000}s). Verificați conexiunea și alimentarea dispozitivului.");

            if (await connectTask)
            {
                ConnectionLogger.Write($"CONEXIUNE REUȘITĂ: {connectionInfo}");
                Log($"✓ Conectat la imprimanta fiscală Datecs pe {connectionInfo}");
                UpdateStatus("Conectat", Colors.Green);

                // Enable controls
                DisconnectButton.IsEnabled = true;
                StartWatcherButton.IsEnabled = true;
                TestConnectionButton.IsEnabled = true;
                FeedPaperButton.IsEnabled = true;
                FeedLinesBox.IsEnabled = true;
                CancelReceiptButton.IsEnabled = true;
                CashInButton.IsEnabled = true;
                CashOutButton.IsEnabled = true;
                CashAmountBox.IsEnabled = true;
                XReportButton.IsEnabled = true;
                ZReportButton.IsEnabled = true;
                EnableReportButtons(true);

                StatusBarText.Text = "Dispozitiv conectat. Apasă 'Pornește monitorizarea' pentru a procesa fișiere.";

                if (autoStartWatcher && !_isWatching)
                {
                    StartWatcher();
                }

                RefreshDeviceInfoDisplay();
            }
            else
            {
                connectionFailed = true;
                throw new Exception("Dispozitivul nu răspunde");
            }
        }
        catch (Exception ex)
        {
            connectionFailed = true;
            connectionError = ex;
            
            ConnectionLogger.Write($"CONEXIUNE EȘUATĂ: {ex.Message}");
            Log($"✗ Conexiune eșuată: {ex.Message}");
            UpdateStatus("Conexiune eșuată", Colors.Red);
            ShowTrayNotification("POS Bridge - Conexiune eșuată", ex.Message);

            string connectionType = _useEthernet ? $"Ethernet ({_ipAddress}:{_tcpPort})" : "Serial (COM)";
            
            // Enhanced error message for common issues
            string errorDetail = ex.Message;
            if (ex is UnauthorizedAccessException || ex.Message.Contains("ocupat") || ex.Message.Contains("in use"))
            {
                errorDetail = $"Portul este ocupat de altă aplicație.\n\nSoluții:\n• Închideți alte aplicații care ar putea folosi portul (FiscalNet, DUDE, etc.)\n• Verificați în Task Manager dacă există procese blocate\n• Deconectați și reconectați dispozitivul USB\n\nEroare tehnică: {ex.Message}";
            }
            
            MessageBox.Show(
                $"Nu s-a putut conecta la imprimanta fiscală prin {connectionType}:\n\n{errorDetail}\n\nVerificați:\n- DUDE COM Server este instalat\n- Aplicația este compilată ca x86\n- Dispozitivul este accesibil\n- Portul nu este folosit de altă aplicație",
                "Eroare conexiune",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            // IMPORTANT: Do NOT call _fiscalEngine.IsConnected when connectionFailed - the COM STA thread
            // may still be blocked on open_Connection() TCP, causing a deadlock via tcs.Task.Wait().
            bool anyConnected = connectionFailed
                ? false
                : (_fiscalEngine.IsConnectedCached || _currentDevice?.IsConnected == true);
            if (connectionFailed || !anyConnected)
            {
                ConnectButton.IsEnabled = true;
                
                // Disable buttons that require active connection
                DisconnectButton.IsEnabled = false;
                StartWatcherButton.IsEnabled = false;
                TestConnectionButton.IsEnabled = true;
                FeedPaperButton.IsEnabled = false;
                CancelReceiptButton.IsEnabled = false;
                // CashIn, CashOut, XReport, ZReport stay enabled - handlers show error or create file (Datecs X/Z)
                CashAmountBox.IsEnabled = true;
                EnableReportButtons(false);
                
                // Update status bar
                StatusBarText.Text = connectionError != null 
                    ? $"Conexiune eșuată: {connectionError.Message}" 
                    : "Deconectat. Apasă 'Conectare dispozitiv' pentru a încerca din nou.";
            }
        }
    }

    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        _connectCts?.Cancel();
        _connectCts = new CancellationTokenSource();
        ConnectButton.Visibility = Visibility.Collapsed;
        CancelConnectionButton.Visibility = Visibility.Visible;
        try
        {
            await ConnectAndMaybeStartWatcherAsync(autoStartWatcher: false);
        }
        finally
        {
            ConnectButton.Visibility = Visibility.Visible;
            CancelConnectionButton.Visibility = Visibility.Collapsed;
        }
    }

    private void CancelConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        _connectCts?.Cancel();
        Log("⏸️ Conectare anulată de utilizator.");
        UpdateStatus("Anulat", Colors.Gray);
        ConnectButton.Visibility = Visibility.Visible;
        CancelConnectionButton.Visibility = Visibility.Collapsed;
        ConnectButton.IsEnabled = true;
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Stop watcher first
            if (_isWatching)
            {
                StopWatcher();
            }

            try
            {
                _fiscalEngine.Disconnect();
            }
            catch
            {
                // Ignore disconnect errors
            }
            
            Log("Deconectat de la imprimanta fiscală.");
            UpdateStatus("Deconectat", Colors.Red);
            
            // Disable controls
                ConnectButton.IsEnabled = true;
                DisconnectButton.IsEnabled = false;
                StartWatcherButton.IsEnabled = false;
                StopWatcherButton.IsEnabled = false;
                TestConnectionButton.IsEnabled = true;
                FeedPaperButton.IsEnabled = false;
            FeedLinesBox.IsEnabled = false;
            CancelReceiptButton.IsEnabled = false;
            CashInButton.IsEnabled = false;
            CashOutButton.IsEnabled = false;
            CashAmountBox.IsEnabled = false;
                XReportButton.IsEnabled = false;
                ZReportButton.IsEnabled = false;
                EnableReportButtons(false);

                SerieFabricatieText.Text = "—";
                NumeDispozitivText.Text = "—";
                VendorNameText.Text = "—";
                ModelNameText.Text = "—";
                SerieFiscalaText.Text = "—";
                CodFiscalText.Text = "—";
            
            StatusBarText.Text = "Deconectat. Apasă 'Conectare dispozitiv' pentru a reconecta.";
        }
        catch (Exception ex)
        {
            Log($"Eroare la deconectare: {ex.Message}");
        }
    }

    // ==================== FOLDER WATCHER ====================

    private void StartWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        StartWatcher();
    }

    private void StopWatcherButton_Click(object sender, RoutedEventArgs e)
    {
        StopWatcher();
    }

    private void StartWatcher()
    {
        if (_isWatching)
            return;

        try
        {
            Log("═══════════════════════════════════════");
            Log("Pornire serviciu monitorizare folder...");
            
            // Create watcher service
            _watcher = new FolderWatcherService(_bonFolder);
            _watcher.LogMessage += OnWatcherLog;
            _watcher.BonProcessed += OnBonProcessed;
            _watcher.ProcessCommandFileAsync = ProcessCommandFileAsync;
            _watcher.ProcessSpecialCommandAsync = ProcessSpecialCommandAsync;
            // _watcher.ConfirmFileAsync = ConfirmFileAsync; // Dezactivat: tipareste automat fara confirmare
            
            // Start monitoring
            _watcher.Start();
            
            _isWatching = true;
            
            Log("✓ Monitorizarea folderului este activă");
            Log($"Adaugă fișiere .txt în: {_bonFolder}");
            Log("Format: S^DENUMIRE^PRET^CANTITATE^UM^GRTVA^GRDEP (plus comenzi DP/DV/MP/MV/ST/TL/P etc.)");
            Log("Comenzi speciale: X^ (raport X), Z^ (raport Z), I^VALOARE^ (introducere numerar), O^VALOARE^ (scoatere numerar)");
            Log("═══════════════════════════════════════");
            
            UpdateStatus("Monitorizare", Colors.Green);
            StartWatcherButton.IsEnabled = false;
            StopWatcherButton.IsEnabled = true;
            StatusBarText.Text = "Adaugă fișiere .txt în folderul Bon pentru procesare.";
        }
        catch (Exception ex)
        {
            Log($"✗ Nu s-a putut porni monitorizarea: {ex.Message}");
            MessageBox.Show(
                $"Nu s-a putut porni monitorizarea folderului:\n\n{ex.Message}",
                "Eroare monitorizare",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StopWatcher()
    {
        if (!_isWatching)
            return;

        try
        {
            Log("Stopping folder watcher...");
            
            _watcher?.Stop();
            _watcher?.Dispose();
            _watcher = null;
            
            _isWatching = false;
            
            Log("✓ Monitorizarea folderului s-a oprit");
            Log("═══════════════════════════════════════");
            
            UpdateStatus("Conectat", Colors.Orange);
            StartWatcherButton.IsEnabled = true;
            StopWatcherButton.IsEnabled = false;
            StatusBarText.Text = "Monitorizare oprită. Apasă 'Pornește monitorizarea' pentru a relua.";
        }
        catch (Exception ex)
        {
            Log($"Eroare la oprirea monitorizării: {ex.Message}");
        }
    }

    // ==================== BON PROCESSING ====================

    private async Task<BonProcessingResult> ProcessCommandFileAsync(ReceiptCommandFile commandFile)
    {
        var (opCode, opPwd) = GetOperatorCredentials();
        if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null)
        {
            return await IncotexBonProcessor.ProcessCommandFileAsync(commandFile, _currentDevice, opCode, opPwd);
        }
        _fiscalEngine.OperatorCode = opCode;
        _fiscalEngine.OperatorPassword = opPwd;
        return await Task.Run(() => _fiscalEngine.ProcessCommandFile(commandFile));
    }

    private async Task<BonProcessingResult> ProcessSpecialCommandAsync(string command)
    {
        var result = new BonProcessingResult
        {
            Success = true,
            FileName = command,
            ProcessedAt = DateTime.Now
        };

        try
        {
            if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null)
            {
                return await ProcessSpecialCommandIncotexAsync(command, result);
            }
            return await Task.Run(() =>
            {
                try
                {
                    if (command.Equals("X^", StringComparison.OrdinalIgnoreCase))
                    {
                        _fiscalEngine.PrintDailyReport("X");
                        result.ReceiptNumber = "X Report";
                    }
                    else if (command.Equals("Z^", StringComparison.OrdinalIgnoreCase))
                    {
                        _fiscalEngine.PrintDailyReport("Z");
                        result.ReceiptNumber = "Z Report";
                    }
                    else if (command.StartsWith("I^", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = command.Split('^');
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                            throw new InvalidOperationException("Cash In command requires amount: I^VALOARE^");
                        if (!decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                            throw new InvalidOperationException($"Invalid amount for Cash In: {parts[1]}");
                        if (amount <= 0)
                            throw new InvalidOperationException("Cash In amount must be greater than 0");
                        _fiscalEngine.CashIn(amount);
                        result.ReceiptNumber = $"Cash In: {amount:F2} lei";
                    }
                    else if (command.StartsWith("O^", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = command.Split('^');
                        if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                            throw new InvalidOperationException("Cash Out command requires amount: O^VALOARE^");
                        if (!decimal.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out decimal amount))
                            throw new InvalidOperationException($"Invalid amount for Cash Out: {parts[1]}");
                        if (amount <= 0)
                            throw new InvalidOperationException("Cash Out amount must be greater than 0");
                        _fiscalEngine.CashOut(amount);
                        result.ReceiptNumber = $"Cash Out: {amount:F2} lei";
                    }
                    else
                    {
                        throw new InvalidOperationException($"Unknown special command: {command}");
                    }
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                }
                result.ProcessingDuration = DateTime.Now - result.ProcessedAt;
                return result;
            });
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        result.ProcessingDuration = DateTime.Now - result.ProcessedAt;
        return result;
    }

    private async Task<BonProcessingResult> ProcessSpecialCommandIncotexAsync(string command, BonProcessingResult result)
    {
        var (opCode, opPwd) = GetOperatorCredentials();
        var cmdFile = new ReceiptCommandFile { FileName = command, Commands = new List<ReceiptCommand>() };
        if (command.Equals("X^", StringComparison.OrdinalIgnoreCase))
            cmdFile.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.XReport });
        else if (command.Equals("Z^", StringComparison.OrdinalIgnoreCase))
            cmdFile.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.ZReport });
        else if (command.StartsWith("I^", StringComparison.OrdinalIgnoreCase))
        {
            var parts = command.Split('^');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidOperationException("Cash In requires amount: I^VALOARE^");
            string rawAmtI = parts[1].Trim();
            if (!decimal.TryParse(rawAmtI.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal rawValI))
                throw new InvalidOperationException($"Invalid amount: {parts[1]}");
            // Convenție fișier: integer = bani (ex: 30000 = 300.00 lei); decimal = lei (ex: 300.00)
            decimal amountLeiI = (rawAmtI.Contains('.') || rawAmtI.Contains(',')) ? rawValI : rawValI / 100m;
            if (amountLeiI <= 0) throw new InvalidOperationException("Amount must be > 0");
            cmdFile.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.CashIn, Value = amountLeiI });
        }
        else if (command.StartsWith("O^", StringComparison.OrdinalIgnoreCase))
        {
            var parts = command.Split('^');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                throw new InvalidOperationException("Cash Out requires amount: O^VALOARE^");
            string rawAmtO = parts[1].Trim();
            if (!decimal.TryParse(rawAmtO.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal rawValO))
                throw new InvalidOperationException($"Invalid amount: {parts[1]}");
            decimal amountLeiO = (rawAmtO.Contains('.') || rawAmtO.Contains(',')) ? rawValO : rawValO / 100m;
            if (amountLeiO <= 0) throw new InvalidOperationException("Amount must be > 0");
            cmdFile.Commands.Add(new ReceiptCommand { Type = ReceiptCommandType.CashOut, Value = amountLeiO });
        }
        else
            throw new InvalidOperationException($"Unknown special command: {command}");

        return await IncotexBonProcessor.ProcessCommandFileAsync(cmdFile, _currentDevice!, opCode, opPwd);
    }

    private Task<bool> ConfirmFileAsync(string filePath)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (_allowPrinting.HasValue)
                return _allowPrinting.Value;

            var result = MessageBox.Show(
                "Vrei să tipărești bonurile detectate în această sesiune?",
                "Confirmare Tipărire Bonuri",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            _allowPrinting = result == MessageBoxResult.Yes;
            return _allowPrinting.Value;
        }).Task;
    }

    private void OnBonProcessed(object? sender, BonProcessingResult result)
    {
        // Update statistics on UI thread
        Dispatcher.Invoke(() =>
        {
            _totalProcessed++;
            
            if (result.Success)
            {
                _successCount++;
            }
            else
            {
                _errorCount++;
            }
            
            TotalProcessedText.Text = _totalProcessed.ToString();
            SuccessCountText.Text = _successCount.ToString();
            ErrorCountText.Text = _errorCount.ToString();
            
            StatusBarText.Text = result.Success
                ? $"Ultimul procesat: {result.FileName} → Bon #{result.ReceiptNumber} ({result.ProcessingDuration.TotalSeconds:F1}s)"
                : $"Ultima eroare: {result.FileName} → {result.ErrorMessage}";

            if (!result.Success)
            {
                ShowTrayNotification("POS Bridge - Eroare bon", $"{result.FileName}: {result.ErrorMessage}");
            }
        });
    }

    private void OnWatcherLog(object? sender, string message)
    {
        Log(message);
    }

    // ==================== UTILITIES ====================

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        TestConnectionButton.IsEnabled = false;
        try
        {
            Log("Testare conexiune...");
            UpdateStatus("Se verifică conexiunea...", Colors.Orange);
            
            // Dacă nu ești conectat, conectează-te mai întâi (Datecs)
            if (_selectedDeviceType == DeviceType.Datecs && !_fiscalEngine.IsConnected)
            {
                await ConnectAndMaybeStartWatcherAsync(autoStartWatcher: false);
                if (_fiscalEngine.IsConnected)
                {
                    Log("✓ Test conexiune reușit");
                    MessageBox.Show(
                        "Test conexiune reușit!\nImprimanta fiscală Datecs răspunde.",
                        "Test conexiune",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return; // ConnectAndMaybeStartWatcherAsync deja afișează eroarea dacă a eșuat
            }
            
            // Incotex / alte dispozitive: folosește fluxul multi-vendor
            if (_selectedDeviceType != DeviceType.Datecs)
            {
                if (_selectedDeviceType == DeviceType.Incotex)
                {
                    var probe = ProbeIncotexWindowsDevice();
                    Log($"🔍 Probe Windows Incotex: present={probe.IsPresent}, service={probe.ServiceName ?? "N/A"}, com={probe.ComPort ?? "N/A"}");
                    if (!string.IsNullOrWhiteSpace(probe.DeviceName))
                        Log($"   Device: {probe.DeviceName}");
                    if (!string.IsNullOrWhiteSpace(probe.InstanceId))
                        Log($"   InstanceId: {probe.InstanceId}");

                    if (!probe.IsPresent)
                    {
                        MessageBox.Show(
                            "Incotex nu este detectat în Windows (VID_0483&PID_5740).\n\nVerificați:\n• Cablul USB\n• Alimentarea casei\n• Device Manager",
                            "Test conexiune Incotex",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    if (_connectionType == "USB" && !probe.IsWinUsb)
                    {
                        Log("⚠ Driverul detectat nu este WinUSB. USB Direct poate eșua.");
                    }
                }

                // Deja conectat: verifică cu GetStatusAsync fără reconectare
                if (_currentDevice != null && _currentDevice.IsConnected)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        string status = await _currentDevice.GetStatusAsync().WaitAsync(cts.Token);
                        Log("✓ Test conexiune reușit");
                        MessageBox.Show(
                            $"Test conexiune reușit!\nDispozitivul răspunde.\nStatus: {status}",
                            "Test conexiune",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    catch (OperationCanceledException)
                    {
                        Log("✗ Test conexiune eșuat: timeout (15 secunde)");
                        // Disconnect since device is not responding
                        try
                        {
                            await _currentDevice.DisconnectAsync();
                            Log("⚠ Dispozitiv deconectat (nu răspunde)");
                        }
                        catch { }
                        UpdateStatus("Deconectat", Colors.Red);
                        MessageBox.Show(
                            "Test conexiune eșuat – timeout!\n\nDispozitivul nu a răspuns în 15 secunde.\nVerificați cablul și dacă casa este pornită.",
                            "Test conexiune",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    catch (Exception ex)
                    {
                        Log($"✗ Test conexiune eșuat: {ex.Message}");
                        // Disconnect since device is not responding
                        try
                        {
                            await _currentDevice.DisconnectAsync();
                            Log("⚠ Dispozitiv deconectat (eroare comunicare)");
                        }
                        catch { }
                        UpdateStatus("Deconectat", Colors.Red);
                        MessageBox.Show(
                            $"Test conexiune eșuat!\nDispozitivul nu răspunde.\n\n{ex.Message}",
                            "Test conexiune",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return;
                }

                // Incotex: auto-scan pe toate COM-urile + viteze uzuale
                if (_selectedDeviceType == DeviceType.Incotex)
                {
                    bool autoDetected = await TryAutoDetectIncotexSerialAsync();
                    if (autoDetected)
                    {
                        Log("✓ Test conexiune reușit (auto-scan Incotex)");
                        MessageBox.Show(
                            $"Test conexiune reușit!\nIncotex detectat pe {_comPort} @ {_baudRate}.",
                            "Test conexiune",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    Log("ℹ Auto-scan Incotex nu a găsit un răspuns valid. Încerc setările curente...");
                }

                // Nu e conectat: conectează cu timeout 20 secunde
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await ConnectAndMaybeStartWatcherAsync(autoStartWatcher: false).WaitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    Log("✗ Test conexiune eșuat: timeout (20 secunde) – dispozitivul nu răspunde");
                    MessageBox.Show(
                        "Test conexiune eșuat – timeout!\n\nNu s-a putut conecta în 20 secunde.\n\nVerificați:\n• Casa Incotex este pornită\n• Cablul USB/COM este conectat\n• Portul nu este folosit de altă aplicație",
                        "Test conexiune",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
                if (_currentDevice != null && _currentDevice.IsConnected)
                {
                    Log("✓ Test conexiune reușit");
                    MessageBox.Show(
                        "Test conexiune reușit!\nDispozitivul răspunde.",
                        "Test conexiune",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                return;
            }
            
            // Deja conectat (Datecs): doar verifică
            if (_fiscalEngine.TestConnection())
            {
                Log("✓ Test conexiune reușit");
                MessageBox.Show(
                    "Test conexiune reușit!\nImprimanta fiscală răspunde.",
                    "Test conexiune",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                Log("✗ Test conexiune eșuat");
                MessageBox.Show(
                    "Test conexiune eșuat!\nDispozitivul nu răspunde.",
                    "Test conexiune",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare test conexiune: {ex.Message}");
            MessageBox.Show(
                $"Eroare la testul conexiunii:\n\n{ex.Message}",
                "Test conexiune",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
            if (_currentDevice?.IsConnected == true)
                UpdateStatus("Conectat", Colors.Green);
            else if (_fiscalEngine?.IsConnected == true)
                UpdateStatus("Conectat", Colors.Green);
            else
                UpdateStatus("Deconectat", Colors.Red);
        }
    }

    private sealed class IncotexWindowsProbe
    {
        public bool IsPresent { get; init; }
        public bool IsWinUsb { get; init; }
        public string? ServiceName { get; init; }
        public string? DeviceName { get; init; }
        public string? InstanceId { get; init; }
        public string? ComPort { get; init; }
    }

    /// <summary>
    /// Reads Windows PnP info for Incotex VID/PID and any mapped COM port.
    /// </summary>
    private IncotexWindowsProbe ProbeIncotexWindowsDevice()
    {
        const string vidPidFilter = "VID_0483&PID_5740";
        string? service = null;
        string? deviceName = null;
        string? instanceId = null;
        string? comPort = null;
        bool present = false;

        try
        {
            using var pnpSearcher = new ManagementObjectSearcher(
                "SELECT Name,PNPDeviceID,Service FROM Win32_PnPEntity " +
                $"WHERE PNPDeviceID LIKE '%{vidPidFilter}%'");
            using var pnpResults = pnpSearcher.Get();

            foreach (ManagementBaseObject entry in pnpResults)
            {
                present = true;
                deviceName = entry["Name"] as string;
                instanceId = entry["PNPDeviceID"] as string;
                service = entry["Service"] as string;
                break;
            }
        }
        catch (Exception ex)
        {
            Log($"⚠ Probe Incotex PnP eșuată: {ex.Message}");
        }

        try
        {
            using var serialSearcher = new ManagementObjectSearcher(
                "SELECT DeviceID,PNPDeviceID FROM Win32_SerialPort " +
                $"WHERE PNPDeviceID LIKE '%{vidPidFilter}%'");
            using var serialResults = serialSearcher.Get();
            foreach (ManagementBaseObject port in serialResults)
            {
                var deviceId = port["DeviceID"] as string;
                if (!string.IsNullOrWhiteSpace(deviceId))
                {
                    comPort = deviceId.Trim().ToUpperInvariant();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Log($"⚠ Probe Incotex COM eșuată: {ex.Message}");
        }

        bool winUsb = string.Equals(service, "WinUSB", StringComparison.OrdinalIgnoreCase);
        return new IncotexWindowsProbe
        {
            IsPresent = present,
            IsWinUsb = winUsb,
            ServiceName = service,
            DeviceName = deviceName,
            InstanceId = instanceId,
            ComPort = comPort
        };
    }

    /// <summary>
    /// Tries all COM ports and standard Incotex baud rates until a valid response is received.
    /// On success, updates UI + settings with the detected port and speed and keeps the device connected.
    /// </summary>
    private async Task<bool> TryAutoDetectIncotexSerialAsync()
    {
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        var baudRates = new[] { 115200, 57600, 38400, 19200, 9600 };

        if (ports.Length == 0)
        {
            Log("✗ Auto-scan Incotex: nu există porturi COM disponibile.");
            return false;
        }

        Log($"🔎 Auto-scan Incotex: {ports.Length} port(uri) × {baudRates.Length} viteze...");

        foreach (var port in ports)
        {
            foreach (var baud in baudRates)
            {
                IFiscalDevice? candidate = null;
                try
                {
                    Log($"↪ Test Incotex: {port} @ {baud}...");
                    candidate = FiscalDeviceFactory.CreateDevice(DeviceType.Incotex);

                    var settings = new POSBridge.Abstractions.Models.ConnectionSettings
                    {
                        Type = ConnectionType.Serial,
                        Port = port,
                        BaudRate = baud,
                        TimeoutSeconds = 2
                    };

                    using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    bool connected = await candidate.ConnectAsync(settings).WaitAsync(connectCts.Token);
                    if (!connected || !candidate.IsConnected)
                    {
                        continue;
                    }

                    // Extra verification that communication is stable.
                    string status = "OK";
                    try
                    {
                        using var statusCts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                        status = await candidate.GetStatusAsync().WaitAsync(statusCts.Token);
                    }
                    catch
                    {
                        // Keep default status text if explicit status read fails after connect.
                    }

                    if (_currentDevice is IDisposable previousDisposable)
                    {
                        try
                        {
                            if (_currentDevice.IsConnected)
                                await _currentDevice.DisconnectAsync();
                        }
                        catch { }
                        try { previousDisposable.Dispose(); } catch { }
                    }

                    _currentDevice = candidate;
                    candidate = null; // Ownership transferred to _currentDevice

                    ApplyDetectedIncotexSerialSettings(port, baud);
                    Log($"✅ Incotex detectat: {port} @ {baud} (Status: {status})");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"✗ {port} @ {baud}: {ex.Message}");
                }
                finally
                {
                    if (candidate != null)
                    {
                        try
                        {
                            if (candidate.IsConnected)
                                await candidate.DisconnectAsync();
                        }
                        catch { }
                        try
                        {
                            if (candidate is IDisposable disposable)
                                disposable.Dispose();
                        }
                        catch { }
                    }
                }
            }
        }

        Log("✗ Auto-scan Incotex: nicio combinație COM/viteză nu a răspuns.");
        return false;
    }

    private void ApplyDetectedIncotexSerialSettings(string comPort, int baudRate)
    {
        _connectionType = "Serial";
        _useEthernet = false;
        _comPort = comPort;
        _baudRate = baudRate;

        ConnectionTypeComboBox.SelectedIndex = 0; // Serial
        SerialSettingsPanel.Visibility = Visibility.Visible;
        EthernetSettingsPanel.Visibility = Visibility.Collapsed;

        if (!PortComboBox.Items.Cast<object>().Any(i => string.Equals(i?.ToString(), comPort, StringComparison.OrdinalIgnoreCase)))
            PortComboBox.Items.Add(comPort);
        PortComboBox.SelectedItem = PortComboBox.Items
            .Cast<object>()
            .FirstOrDefault(i => string.Equals(i?.ToString(), comPort, StringComparison.OrdinalIgnoreCase));

        string baudText = baudRate.ToString();
        if (!BaudComboBox.Items.Cast<object>().Any(i => string.Equals(i?.ToString(), baudText, StringComparison.OrdinalIgnoreCase)))
            BaudComboBox.Items.Add(baudText);
        BaudComboBox.SelectedItem = baudText;

        SaveSettings();
    }

    private async void FeedPaperButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_fiscalEngine.IsConnected)
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            if (!int.TryParse(FeedLinesBox.Text?.Trim(), out int lines) || lines <= 0)
                throw new InvalidOperationException("Număr invalid de linii. Folosiți un număr întreg pozitiv.");

            FeedPaperButton.IsEnabled = false;
            Log($"Alimentare hârtie: {lines} linii...");

            await Task.Run(() => _fiscalEngine.FeedPaper(lines));

            Log("✓ Alimentare hârtie executată.");
        }
        catch (Exception ex)
        {
            Log($"✗ Alimentare hârtie eșuată: {ex.Message}");
            MessageBox.Show(
                $"Alimentarea hârtiei a eșuat:\n\n{ex.Message}",
                "Eroare alimentare hârtie",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            FeedPaperButton.IsEnabled = true;
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Open folder in Windows Explorer
            Process.Start("explorer.exe", _bonFolder);
            Log($"Folder deschis: {_bonFolder}");
        }
        catch (Exception ex)
        {
            Log($"Eroare la deschiderea folderului: {ex.Message}");
        }
    }

    private void BrowseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Selectați folderul de monitorizat pentru fișiere BON",
            SelectedPath = _bonFolder,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FolderPathBox.Text = dialog.SelectedPath;
        }
    }

    private void ApplyFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string newPath = FolderPathBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(newPath))
                throw new InvalidOperationException("Calea folderului este goală.");

            if (!Directory.Exists(newPath))
                Directory.CreateDirectory(newPath);

            bool wasWatching = _isWatching;
            if (_isWatching)
            {
                StopWatcher();
            }

            _bonFolder = Path.GetFullPath(newPath);
            EnsureBonFolders(_bonFolder);
            GetOperatorCredentials();
            SaveSettings();
            Log($"Folder monitorizat setat la: {_bonFolder}");
            StatusBarText.Text = $"Monitoring folder: {_bonFolder}";

            if (wasWatching)
            {
                StartWatcher();
            }
        }
        catch (Exception ex)
        {
            Log($"Nu s-a putut seta folderul: {ex.Message}");
            MessageBox.Show(
                $"Nu s-a putut seta folderul:\n\n{ex.Message}",
                "Eroare folder",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
        Log("Jurnal șters.");
    }

    private void PortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = PortComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            _comPort = selected;
            SaveSettings();
        }
    }

    private void BaudComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var selected = BaudComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(selected) && int.TryParse(selected, out int baud))
        {
            _baudRate = baud;
            SaveSettings();
        }
    }

    private void OperatorCodeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = OperatorCodeBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text) && int.TryParse(text, out int code))
        {
            _operatorCode = code;
            SaveSettings();
        }
    }

    private void OperatorPasswordBox_LostFocus(object sender, RoutedEventArgs e)
    {
        var text = OperatorPasswordBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            _operatorPassword = text;
            SaveSettings();
        }
    }

    private void StartupWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        _runAtStartup = StartupWithWindowsCheckBox.IsChecked == true;
        SaveSettings();
        ApplyRunAtStartup();
    }

    private void ApplyRunAtStartup()
    {
        try
        {
            const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string valueName = "POS Bridge";

            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key == null)
            {
                Log("Nu s-a putut accesa cheia de registru pentru pornire automată.");
                return;
            }

            if (_runAtStartup)
            {
                var exePath = GetExePath();
                if (string.IsNullOrEmpty(exePath))
                {
                    Log("Nu s-a putut determina calea aplicației pentru pornire automată.");
                    return;
                }
                key.SetValue(valueName, $"\"{exePath}\"");
                Log("✓ Pornire automată la startup activată.");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
                Log("Pornire automată la startup dezactivată.");
            }
        }
        catch (Exception ex)
        {
            Log($"Eroare la setarea pornirii automate: {ex.Message}");
            if (StartupWithWindowsCheckBox.IsChecked == true)
            {
                MessageBox.Show(
                    $"Nu s-a putut seta pornirea automată:\n\n{ex.Message}",
                    "Eroare",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private static string? GetExePath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(processPath) &&
            !processPath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFullPath(processPath);
        }

        var baseDir = AppContext.BaseDirectory;
        var exePath = Path.Combine(baseDir, "POSBridge.WPF.exe");
        if (File.Exists(exePath))
            return Path.GetFullPath(exePath);

        var dllPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
        exePath = Path.ChangeExtension(dllPath, ".exe");
        return File.Exists(exePath) ? Path.GetFullPath(exePath) : null;
    }

    private void OpenDeviceInfoWindow_Click(object sender, RoutedEventArgs e)
    {
        var w = new DeviceInfoWindow(_selectedDeviceType, _currentDevice);
        w.Owner = this;
        w.Show();
    }

    // ==================== RAPOARTE AVANSATE ====================

    private void EnableReportButtons(bool enabled)
    {
        EcrReportButton.IsEnabled = enabled;
        DepartmentsReportButton.IsEnabled = enabled;
        ItemGroupsReportButton.IsEnabled = enabled;
        FmDateShortButton.IsEnabled = enabled;
        FmDateDetailButton.IsEnabled = enabled;
        FmZShortButton.IsEnabled = enabled;
        FmZDetailButton.IsEnabled = enabled;
        OperatorsReportButton.IsEnabled = enabled;
        PluReportButton.IsEnabled = enabled;
        DiagnosticButton.IsEnabled = enabled;
        DailyInfoButton.IsEnabled = enabled;
        LastFiscalButton.IsEnabled = enabled;
        DailyTaxButton.IsEnabled = enabled;
        ExtDeviceInfoButton.IsEnabled = enabled;
        RemainingZButton.IsEnabled = enabled;
    }

    private async void EcrReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice?.IsConnected == true)
            await RunReportTaskAsync("Raport ECR (X)", EcrReportButton,
                () => _currentDevice.PrintDailyReportAsync("X"));
        else
            await RunReportAsync("Raport ECR", EcrReportButton, () => _fiscalEngine.PrintEcrReport());
    }

    private async void DepartmentsReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice?.IsConnected == true)
            await RunReportTaskAsync("Raport Departamente", DepartmentsReportButton,
                () => _currentDevice.PrintDepartmentsReportAsync());
        else
            await RunReportAsync("Raport Departamente", DepartmentsReportButton, () => _fiscalEngine.PrintDepartmentsReport());
    }

    private async void ItemGroupsReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexItemGroups)
            await RunReportTaskAsync("Raport PLU programat", ItemGroupsReportButton,
                () => incotexItemGroups.PrintDailyExtendedReportAsync("X"));
        else if (_currentDevice?.IsConnected == true)
            await RunReportTaskAsync("Raport Grupe articole", ItemGroupsReportButton,
                () => _currentDevice.PrintDepartmentsReportAsync());
        else
            await RunReportAsync("Raport Grupe articole", ItemGroupsReportButton, () => _fiscalEngine.PrintItemGroupsReport());
    }

    private string FormatDateForFiscal(DateTime? date)
    {
        return date?.ToString("dd-MM-yy") ?? "";
    }

    private async void FmDateShortButton_Click(object sender, RoutedEventArgs e)
    {
        string startStr = FormatDateForFiscal(FmDateStartPicker.SelectedDate);
        string endStr = FormatDateForFiscal(FmDateEndPicker.SelectedDate);
        if (_currentDevice is IncotexDevice incotexFmDateShort)
        {
            DateTime start = FmDateStartPicker.SelectedDate ?? DateTime.Today.AddDays(-30);
            DateTime end = FmDateEndPicker.SelectedDate ?? DateTime.Today;
            await RunReportTaskAsync($"Raport MF date sumar ({startStr} - {endStr})", FmDateShortButton,
                () => incotexFmDateShort.PrintFiscalMemorySummaryByDateAsync(start, end));
        }
        else
        {
            await RunReportAsync($"Raport MF date scurt ({startStr} - {endStr})", FmDateShortButton,
                () => _fiscalEngine.PrintFiscalMemoryByDate("0", startStr, endStr));
        }
    }

    private async void FmDateDetailButton_Click(object sender, RoutedEventArgs e)
    {
        string startStr = FormatDateForFiscal(FmDateStartPicker.SelectedDate);
        string endStr = FormatDateForFiscal(FmDateEndPicker.SelectedDate);
        if (_currentDevice?.IsConnected == true)
        {
            DateTime start = FmDateStartPicker.SelectedDate ?? DateTime.Today.AddDays(-30);
            DateTime end = FmDateEndPicker.SelectedDate ?? DateTime.Today;
            await RunReportTaskAsync($"Raport MF date detaliat ({startStr} - {endStr})", FmDateDetailButton,
                () => _currentDevice.PrintFiscalMemoryByDateAsync(start, end));
        }
        else
        {
            await RunReportAsync($"Raport MF date detaliat ({startStr} - {endStr})", FmDateDetailButton,
                () => _fiscalEngine.PrintFiscalMemoryByDate("1", startStr, endStr));
        }
    }

    private async void FmZShortButton_Click(object sender, RoutedEventArgs e)
    {
        string startTxt = FmZStartBox.Text?.Trim() ?? "";
        string endTxt = FmZEndBox.Text?.Trim() ?? "";
        if (_currentDevice is IncotexDevice incotexFmZShort &&
            int.TryParse(startTxt, out int zStart) && int.TryParse(endTxt, out int zEnd))
        {
            await RunReportTaskAsync($"Raport MF Z sumar (Z{startTxt} - Z{endTxt})", FmZShortButton,
                () => incotexFmZShort.PrintFiscalMemorySummaryByZAsync(zStart, zEnd));
        }
        else
        {
            await RunReportAsync($"Raport MF Z scurt (Z{startTxt} - Z{endTxt})", FmZShortButton,
                () => _fiscalEngine.PrintFiscalMemoryByZRange("0", startTxt, endTxt));
        }
    }

    private async void FmZDetailButton_Click(object sender, RoutedEventArgs e)
    {
        string startTxt = FmZStartBox.Text?.Trim() ?? "";
        string endTxt = FmZEndBox.Text?.Trim() ?? "";
        if (_currentDevice is IncotexDevice incotexFmZDetail &&
            int.TryParse(startTxt, out int zStart) && int.TryParse(endTxt, out int zEnd))
        {
            await RunReportTaskAsync($"Raport MF Z detaliat (Z{startTxt} - Z{endTxt})", FmZDetailButton,
                () => incotexFmZDetail.PrintFiscalMemoryDetailByZAsync(zStart, zEnd));
        }
        else
        {
            await RunReportAsync($"Raport MF Z detaliat (Z{startTxt} - Z{endTxt})", FmZDetailButton,
                () => _fiscalEngine.PrintFiscalMemoryByZRange("1", startTxt, endTxt));
        }
    }

    private async void OperatorsReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice?.IsConnected == true)
            await RunReportTaskAsync("Raport operatori", OperatorsReportButton,
                () => _currentDevice.PrintOperatorsReportAsync());
        else
            await RunReportAsync("Raport operatori", OperatorsReportButton,
                () => _fiscalEngine.PrintOperatorsReport());
    }

    private async void PluReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexPlu)
            await RunReportTaskAsync("Raport PLU extins (Z+PLU)", PluReportButton,
                () => incotexPlu.PrintDailyExtendedReportAsync("Z"));
        else
            await RunReportAsync("Raport PLU", PluReportButton,
                () => _fiscalEngine.PrintPluReport());
    }

    private async void DiagnosticButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice?.IsConnected == true)
        {
            await RunQueryTaskAsync("Diagnostic AMEF", DiagnosticButton, async () =>
            {
                var info = await _currentDevice.GetDeviceInfoAsync();
                string status = await _currentDevice.GetStatusAsync();
                return $"Vendor: {info.VendorName} | Model: {info.ModelName} | FW: {info.FirmwareVersion} | Status: {status}";
            });
        }
        else
        {
            await RunReportAsync("Raport diagnostic", DiagnosticButton,
                () => _fiscalEngine.PrintDiagnosticReport());
        }
    }

    private async void DailyInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexDaily)
        {
            await RunQueryTaskAsync("Info zilnice", DailyInfoButton, async () =>
            {
                var (total, neg, refund, cash, fiscReceipts, allReceipts) = await incotexDaily.ReadDailyTotalsAsync();
                return $"Total vânzări: {total:F2} lei | Reduceri: {neg:F2} | Retur: {refund:F2} | Numerar: {cash:F2} | Bonuri fiscale: {fiscReceipts} | Total bonuri: {allReceipts}";
            });
        }
        else
        {
            await RunQueryAsync("Info zilnice", DailyInfoButton,
                () => _fiscalEngine.GetAdditionalDailyInfo("0"));
        }
    }

    private async void LastFiscalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexLast)
        {
            await RunQueryTaskAsync("Ultimul document tipărit", LastFiscalButton, async () =>
            {
                string lastDoc = await incotexLast.ReadLastDocumentNumberAsync();
                return $"Număr ultimul document: {lastDoc}";
            });
        }
        else
        {
            await RunQueryAsync("Ultima inregistrare fiscala", LastFiscalButton, () =>
            {
                string gross = _fiscalEngine.GetLastFiscalEntryInfo("0");
                string vat = _fiscalEngine.GetLastFiscalEntryInfo("1");
                return $"Sume brute: {gross} | Sume TVA: {vat}";
            });
        }
    }

    private async void DailyTaxButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexTax)
        {
            await RunQueryTaskAsync("Info TVA zilnice", DailyTaxButton, async () =>
            {
                var (total, neg, refund, cash, fiscReceipts, _) = await incotexTax.ReadDailyTotalsAsync();
                return $"Total net: {total:F2} lei | Reduceri/Storni: {neg:F2} | Retur: {refund:F2} | Numerar sertar: {cash:F2} | Bonuri fiscale azi: {fiscReceipts}";
            });
        }
        else
        {
            await RunQueryAsync("Info TVA zilnice", DailyTaxButton,
                () => _fiscalEngine.GetDailyTaxInfo("0"));
        }
    }

    private async void ExtDeviceInfoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice?.IsConnected == true)
        {
            await RunQueryTaskAsync("Info extinse AMEF", ExtDeviceInfoButton, async () =>
            {
                var info = await _currentDevice.GetDeviceInfoAsync();
                return $"Vendor: {info.VendorName} | Model: {info.ModelName} | FW: {info.FirmwareVersion} | Serie: {info.SerialNumber} | Serie Fiscală: {info.FiscalNumber} | CUI: {info.FiscalCode}";
            });
        }
        else
        {
            await RunQueryAsync("Info extinse AMEF", ExtDeviceInfoButton, () =>
            {
                string info1 = _fiscalEngine.GetExtendedDeviceInfo("1");
                string info3 = _fiscalEngine.GetExtendedDeviceInfo("3");
                return $"Info generala: {info1} | Ultimul bon: {info3}";
            });
        }
    }

    private async void RemainingZButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDevice is IncotexDevice incotexZ)
        {
            await RunQueryTaskAsync("Z-uri ramase in MF", RemainingZButton, async () =>
            {
                var (logical, physical) = await incotexZ.ReadFreeZRecordsAsync();
                return $"Înregistrări libere: {logical} logice / {physical} fizice";
            });
        }
        else
        {
            await RunQueryAsync("Z-uri ramase in MF", RemainingZButton,
                () => _fiscalEngine.GetRemainingZReports());
        }
    }

    /// <summary>
    /// Returns true if any fiscal device (Datecs or multi-vendor) is connected.
    /// </summary>
    private bool IsAnyDeviceConnected() =>
        (_currentDevice?.IsConnected == true) || _fiscalEngine.IsConnected;

    /// <summary>
    /// Executes a synchronous print report action and logs result (used for Datecs/FiscalEngine).
    /// </summary>
    private async Task RunReportAsync(string name, System.Windows.Controls.Button button, Action action)
    {
        try
        {
            if (!IsAnyDeviceConnected())
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            button.IsEnabled = false;
            Log($"⏳ Se tipareste {name}...");
            StatusBarText.Text = $"Se tipareste {name}...";

            await Task.Run(action);

            Log($"✓ {name} tiparit cu succes!");
            StatusBarText.Text = $"{name} tiparit cu succes.";
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare {name}: {ex.Message}");
            StatusBarText.Text = $"Eroare la {name}.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Executes an async print report task and logs result (used for Incotex/multi-vendor).
    /// </summary>
    private async Task RunReportTaskAsync(string name, System.Windows.Controls.Button button, Func<Task> actionAsync)
    {
        try
        {
            if (!IsAnyDeviceConnected())
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            button.IsEnabled = false;
            Log($"⏳ Se tipareste {name}...");
            StatusBarText.Text = $"Se tipareste {name}...";

            await actionAsync();

            Log($"✓ {name} tiparit cu succes!");
            StatusBarText.Text = $"{name} tiparit cu succes.";
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare {name}: {ex.Message}");
            StatusBarText.Text = $"Eroare la {name}.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Executes a query and logs result to journal.
    /// </summary>
    private async Task RunQueryAsync(string name, System.Windows.Controls.Button button, Func<string> query)
    {
        try
        {
            if (!IsAnyDeviceConnected())
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            button.IsEnabled = false;
            Log($"⏳ Se citesc {name}...");
            StatusBarText.Text = $"Se citesc {name}...";

            string result = await Task.Run(query);

            Log($"✓ {name}: {result}");
            StatusBarText.Text = $"{name} citite cu succes.";
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare {name}: {ex.Message}");
            StatusBarText.Text = $"Eroare la {name}.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// Executes an async query and logs result to journal (used for Incotex/multi-vendor).
    /// </summary>
    private async Task RunQueryTaskAsync(string name, System.Windows.Controls.Button button, Func<Task<string>> queryAsync)
    {
        try
        {
            if (!IsAnyDeviceConnected())
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            button.IsEnabled = false;
            Log($"⏳ Se citesc {name}...");
            StatusBarText.Text = $"Se citesc {name}...";

            string result = await queryAsync();

            Log($"✓ {name}: {result}");
            StatusBarText.Text = $"{name} citite cu succes.";
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare {name}: {ex.Message}");
            StatusBarText.Text = $"Eroare la {name}.";
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    // ==================== UI HELPERS ====================

    /// <summary>
    /// Afiseaza notificare balloon in tray (langa ceas) cand fereastra este ascunsa.
    /// </summary>
    private void ShowTrayNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Error)
    {
        try
        {
            bool visible = IsVisible;
            var state = WindowState;
            Log($"[TRAY DEBUG] IsVisible={visible}, WindowState={state}, _trayIcon={((_trayIcon != null) ? "OK" : "NULL")}");

            if (_trayIcon != null)
            {
                _trayIcon.ShowBalloonTip(5000, title, message, icon);
                Log($"[TRAY DEBUG] ShowBalloonTip apelat cu succes");
            }
        }
        catch (Exception ex)
        {
            Log($"[TRAY DEBUG] Eroare: {ex.Message}");
        }
    }

    private void Log(string message)
    {
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogScrollViewer.ScrollToEnd();
        });

        // Salvare in fisier zilnic (Logs/log_2026-02-13.txt)
        try
        {
            Directory.CreateDirectory(LogFolder);
            string logFile = Path.Combine(LogFolder, $"log_{DateTime.Now:yyyy-MM-dd}.txt");
            File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch
        {
            // Ignora erorile de scriere log
        }
    }

    private void UpdateStatus(string status, System.Windows.Media.Color color)
    {
        StatusText.Text = status;
        StatusLed.Fill = new SolidColorBrush(color);
    }

    private void RefreshDeviceInfoDisplay()
    {
        if (!_fiscalEngine.IsConnected)
            return;

        try
        {
            var info = _fiscalEngine.GetDeviceInfo();
            var model = string.IsNullOrWhiteSpace(info.DeviceName) || info.DeviceName == "N/A" ? null : info.DeviceName;
            var serie = string.IsNullOrWhiteSpace(info.SerialNumber) || info.SerialNumber == "N/A" ? null : info.SerialNumber;

            SerieFabricatieText.Text = serie ?? "—";
            NumeDispozitivText.Text = model ?? "—";

            VendorNameText.Text = "Datecs";
            ModelNameText.Text = model ?? "FP-2000";
            SerieFiscalaText.Text = !string.IsNullOrWhiteSpace(info.FiscalNumber) && info.FiscalNumber != "N/A" ? info.FiscalNumber : "—";
            CodFiscalText.Text = !string.IsNullOrWhiteSpace(info.TAXnumber) && info.TAXnumber != "N/A" ? info.TAXnumber : "—";

            if (!string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(serie))
            {
                SaveDeviceInfoToSettings(model ?? "N/A", serie ?? "N/A");
                _ = SendDeviceInfoToServerAsync(model ?? "N/A", serie ?? "N/A");
            }
        }
        catch
        {
            SerieFabricatieText.Text = "—";
            NumeDispozitivText.Text = "—";
            VendorNameText.Text = "—";
            ModelNameText.Text = "—";
            SerieFiscalaText.Text = "—";
            CodFiscalText.Text = "—";
        }
    }

    /// <summary>
    /// Saves device model and fiscal printer series to settings file (merges with existing).
    /// </summary>
    private void SaveDeviceInfoToSettings(string model, string fiscalPrinterSeries)
    {
        try
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_settingsPath))
            {
                foreach (var line in File.ReadAllLines(_settingsPath))
                {
                    var idx = line.IndexOf('=');
                    if (idx > 0)
                    {
                        var key = line.Substring(0, idx).Trim();
                        var val = line.Substring(idx + 1).Trim();
                        dict[key] = val;
                    }
                }
            }
            dict["DeviceModel"] = model;
            dict["FiscalPrinterSeries"] = fiscalPrinterSeries;
            // Preserve standard keys if missing
            if (!dict.ContainsKey("BonFolder")) dict["BonFolder"] = _bonFolder;
            if (!dict.ContainsKey("OperatorCode")) dict["OperatorCode"] = _operatorCode.ToString();
            if (!dict.ContainsKey("OperatorPassword")) dict["OperatorPassword"] = _operatorPassword;
            if (!dict.ContainsKey("ComPort")) dict["ComPort"] = _comPort;
            if (!dict.ContainsKey("BaudRate")) dict["BaudRate"] = _baudRate.ToString();
            if (!dict.ContainsKey("DeviceSerialNumber")) dict["DeviceSerialNumber"] = "AUTO";
            if (!dict.ContainsKey("TenantCode")) dict["TenantCode"] = _tenantCode;
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(_settingsPath, dict.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Sends device model and serial to the connection server.
    /// </summary>
    private async Task SendDeviceInfoToServerAsync(string model, string fiscalPrinterSeries)
    {
        try
        {
            _activationService ??= new DeviceActivationService();
            var serial = GetDeviceSerialNumberFromApp();
            await _activationService.SendDeviceInfoAsync(serial, model, fiscalPrinterSeries);
            Log($"✓ Model și serie transmise la server: {model} / {fiscalPrinterSeries}");
        }
        catch (Exception ex)
        {
            Log($"Transmitere model/serie la server: {ex.Message}");
        }
    }

    private void LoadComPorts()
    {
        PortComboBox.Items.Clear();
        var ports = SerialPort.GetPortNames().OrderBy(p => p).ToArray();
        foreach (var port in ports)
        {
            PortComboBox.Items.Add(port);
        }

        if (ports.Length > 0)
        {
            var preferred = ports.FirstOrDefault(p => p.Equals(_comPort, StringComparison.OrdinalIgnoreCase));
            PortComboBox.SelectedItem = preferred ?? ports[0];
            Log($"Porturi COM detectate: {string.Join(", ", ports)}");
            Log($"Port COM selectat din setări: {_comPort}");
        }
        else
        {
            Log("Niciun port COM detectat.");
        }
    }

    private void LoadDeviceTypes()
    {
        DeviceTypeComboBox.Items.Clear();
        
        // Add all device types with descriptive names
        foreach (DeviceType deviceType in Enum.GetValues(typeof(DeviceType)))
        {
            var displayName = FiscalDeviceFactory.GetDeviceTypeName(deviceType);
            var item = new ComboBoxItem
            {
                Content = displayName,
                Tag = deviceType,
                IsEnabled = FiscalDeviceFactory.IsDeviceTypeSupported(deviceType)
            };
            DeviceTypeComboBox.Items.Add(item);
            
            if (deviceType == _selectedDeviceType)
            {
                DeviceTypeComboBox.SelectedItem = item;
            }
        }
        
        Log($"✨ Multi-Vendor: {FiscalDeviceFactory.GetSupportedDeviceTypes().Length} device type(s) supported");
    }

    private void LoadConnectionType()
    {
        UpdateConnectionTypeOptions();
    }

    /// <summary>
    /// Populate ConnectionTypeComboBox based on the selected device type.
    /// Incotex only supports USB Direct; Datecs supports Serial, USB, and Ethernet.
    /// </summary>
    private void UpdateConnectionTypeOptions()
    {
        ConnectionTypeComboBox.SelectionChanged -= ConnectionTypeComboBox_SelectionChanged;
        try
        {
            ConnectionTypeComboBox.Items.Clear();

            if (_selectedDeviceType == DeviceType.Incotex)
            {
                ConnectionTypeComboBox.Items.Add(new ComboBoxItem { Content = "🔌 USB Direct", Tag = "USB" });
                ConnectionTypeComboBox.SelectedIndex = 0;
                _connectionType = "USB";
                SerialSettingsPanel.Visibility = Visibility.Collapsed;
                EthernetSettingsPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                ConnectionTypeComboBox.Items.Add(new ComboBoxItem { Content = "📡 Serial (COM Port)", Tag = "Serial" });
                ConnectionTypeComboBox.Items.Add(new ComboBoxItem { Content = "🔌 USB Direct", Tag = "USB" });
                ConnectionTypeComboBox.Items.Add(new ComboBoxItem { Content = "🌐 Ethernet (TCP/IP)", Tag = "Ethernet" });

                int idx = _connectionType switch { "USB" => 1, "Ethernet" => 2, _ => 0 };
                ConnectionTypeComboBox.SelectedIndex = idx;

                if (_connectionType == "Ethernet")
                {
                    IpAddressBox.Text = _ipAddress;
                    TcpPortBox.Text = _tcpPort.ToString();
                    SerialSettingsPanel.Visibility = Visibility.Collapsed;
                    EthernetSettingsPanel.Visibility = Visibility.Visible;
                }
                else if (_connectionType == "USB")
                {
                    SerialSettingsPanel.Visibility = Visibility.Collapsed;
                    EthernetSettingsPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    SerialSettingsPanel.Visibility = Visibility.Visible;
                    EthernetSettingsPanel.Visibility = Visibility.Collapsed;
                }
            }
        }
        finally
        {
            ConnectionTypeComboBox.SelectionChanged += ConnectionTypeComboBox_SelectionChanged;
        }
    }

    private void DeviceTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is DeviceType deviceType)
        {
            if (_selectedDeviceType != deviceType)
            {
                _selectedDeviceType = deviceType;

                // Set default operator password and TCP port per device type (only if still at a default value)
                string defaultPassword = deviceType == DeviceType.Datecs ? "0001" : "0000";
                if (OperatorPasswordBox.Text == "0000" || OperatorPasswordBox.Text == "0001")
                {
                    OperatorPasswordBox.Text = defaultPassword;
                    _operatorPassword = defaultPassword;
                }

                int defaultPort = deviceType == DeviceType.Datecs ? 3999 : 9100;
                if (_tcpPort == 9100 || _tcpPort == 3999)
                {
                    _tcpPort = defaultPort;
                    if (TcpPortBox != null)
                        TcpPortBox.Text = defaultPort.ToString();
                }

                SaveSettings();
                Log($"🏭 Device type changed to: {FiscalDeviceFactory.GetDeviceTypeName(deviceType)}");
                
                // Update vendor name in UI
                VendorNameText.Text = FiscalDeviceFactory.GetDeviceTypeName(deviceType).Split(' ')[0];

                // Refresh connection type options (Incotex = USB only, Datecs = Serial/USB/Ethernet)
                UpdateConnectionTypeOptions();
                
                if (!FiscalDeviceFactory.IsDeviceTypeSupported(deviceType))
                {
                    MessageBox.Show(
                        $"Support pentru {deviceType} va fi adăugat în viitor!\n\n" +
                        "Multi-Vendor Architecture este pregătită.\n" +
                        "Implementare planificată pentru următoarele faze.",
                        "Coming Soon",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
        }
    }

    private void ConnectionTypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ConnectionTypeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag)
        {
            bool isEthernet = tag == "Ethernet";
            bool isUsb = tag == "USB";
            _useEthernet = isEthernet;
            _connectionType = tag;
            
            // Show/hide appropriate panels
            if (isUsb)
            {
                // USB Direct - hide both Serial and Ethernet settings
                SerialSettingsPanel.Visibility = Visibility.Collapsed;
                EthernetSettingsPanel.Visibility = Visibility.Collapsed;
                Log($"🔌 Conexiune schimbată la: USB Direct (pentru Incotex, Tremol, etc.)");
            }
            else if (isEthernet)
            {
                // Ethernet - hide Serial, show Ethernet
                SerialSettingsPanel.Visibility = Visibility.Collapsed;
                EthernetSettingsPanel.Visibility = Visibility.Visible;
                
                _ipAddress = IpAddressBox.Text;
                if (int.TryParse(TcpPortBox.Text, out int port))
                    _tcpPort = port;
                    
                Log($"🌐 Conexiune schimbată la: Ethernet ({_ipAddress}:{_tcpPort})");
            }
            else
            {
                // Serial - show Serial, hide Ethernet
                SerialSettingsPanel.Visibility = Visibility.Visible;
                EthernetSettingsPanel.Visibility = Visibility.Collapsed;
                Log($"📡 Conexiune schimbată la: Serial (COM)");
            }
            
            SaveSettings();
        }
    }

    private static void EnsureBonFolders(string basePath)
    {
        Directory.CreateDirectory(basePath);
        Directory.CreateDirectory(Path.Combine(basePath, "Procesate"));
        Directory.CreateDirectory(Path.Combine(basePath, "Erori"));
        Directory.CreateDirectory(Path.Combine(basePath, "Istoric"));

        // Raspuns folder is at the same level as the Bon folder
        string? parentFolder = Path.GetDirectoryName(basePath);
        if (!string.IsNullOrEmpty(parentFolder))
            Directory.CreateDirectory(Path.Combine(parentFolder, "Raspuns"));
    }

    private (string? BonFolder, int OperatorCode, string OperatorPassword, bool RunAtStartup, string ComPort, int BaudRate, string DudePath, bool UseEthernet, string IpAddress, int TcpPort, string DeviceSerialNumber, string TenantCode, DeviceType DeviceType, string ConnectionType) LoadSettings()
    {
        string? bonFolder = null;
        int operatorCode = 1;
        string operatorPassword = "0000";
        bool runAtStartup = true; // Implicit: pornește la pornirea Windows
        string comPort = "COM7";
        int baudRate = 115200;
        string dudePath = @"C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe";
        bool useEthernet = false;
        string ipAddress = "192.168.1.219";
        int tcpPort = 9100;
        string deviceSerialNumber = "AUTO";
        string tenantCode = "demo";
        DeviceType deviceType = DeviceType.Incotex;
        string connectionType = "USB";

        try
        {
            if (File.Exists(_settingsPath))
            {
                var lines = File.ReadAllLines(_settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    if (!trimmed.Contains('='))
                    {
                        if (bonFolder == null)
                            bonFolder = trimmed;
                        continue;
                    }

                    var parts = trimmed.Split('=', 2);
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key.Equals("BonFolder", StringComparison.OrdinalIgnoreCase))
                        bonFolder = value;
                    else if (key.Equals("OperatorCode", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int code))
                        operatorCode = code;
                    else if (key.Equals("OperatorPassword", StringComparison.OrdinalIgnoreCase))
                        operatorPassword = value;
                    else if (key.Equals("RunAtStartup", StringComparison.OrdinalIgnoreCase))
                        runAtStartup = value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key.Equals("ComPort", StringComparison.OrdinalIgnoreCase))
                        comPort = value;
                    else if (key.Equals("BaudRate", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int baud))
                        baudRate = baud;
                    else if (key.Equals("DudePath", StringComparison.OrdinalIgnoreCase))
                        dudePath = value;
                    else if (key.Equals("UseEthernet", StringComparison.OrdinalIgnoreCase))
                        useEthernet = value.Equals("1", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else if (key.Equals("IpAddress", StringComparison.OrdinalIgnoreCase))
                        ipAddress = value;
                    else if (key.Equals("TcpPort", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int port))
                        tcpPort = port;
                    else if (key.Equals("DeviceSerialNumber", StringComparison.OrdinalIgnoreCase))
                        deviceSerialNumber = value;
                    else if (key.Equals("TenantCode", StringComparison.OrdinalIgnoreCase))
                        tenantCode = value;
                    else if (key.Equals("DeviceType", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<DeviceType>(value, ignoreCase: true, out var dt))
                        deviceType = dt;
                    else if (key.Equals("ConnectionType", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                        connectionType = value;
                }
            }
        }
        catch
        {
            // Ignore read errors
        }

        return (bonFolder, operatorCode, operatorPassword, runAtStartup, comPort, baudRate, dudePath, useEthernet, ipAddress, tcpPort, deviceSerialNumber, tenantCode, deviceType, connectionType);
    }

    private void SaveSettings()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var lines = new[]
            {
                $"BonFolder={_bonFolder}",
                $"OperatorCode={_operatorCode}",
                $"OperatorPassword={_operatorPassword}",
                $"RunAtStartup={(_runAtStartup ? "1" : "0")}",
                $"ComPort={_comPort}",
                $"BaudRate={_baudRate}",
                $"DudePath={_dudePath}",
                $"UseEthernet={(_useEthernet ? "1" : "0")}",
                $"IpAddress={_ipAddress}",
                $"TcpPort={_tcpPort}",
                $"DeviceType={_selectedDeviceType}",
                $"ConnectionType={_connectionType}",
                $"DeviceSerialNumber=AUTO",
                $"TenantCode=demo"
            };
            File.WriteAllLines(_settingsPath, lines);
        }
        catch
        {
            // Ignore write errors
        }
    }

    private void LoadBaudRates()
    {
        BaudComboBox.Items.Clear();
        var baudRates = new[] { "115200", "57600", "38400", "19200", "9600" };
        foreach (var baud in baudRates)
        {
            BaudComboBox.Items.Add(baud);
        }

        var savedBaud = _baudRate.ToString();
        var preferred = baudRates.FirstOrDefault(b => b == savedBaud);
        BaudComboBox.SelectedItem = preferred ?? "115200";
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static System.Drawing.Icon? LoadTrayIconFromPng()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/POSBridge.WPF;component/Assets/pos-bridge.png", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream == null) return null;

            using var stream = streamInfo.Stream;
            using var bitmap = new System.Drawing.Bitmap(stream);
            var hIcon = bitmap.GetHicon();
            var icon = System.Drawing.Icon.FromHandle(hIcon);
            var cloned = (System.Drawing.Icon)icon.Clone();
            DestroyIcon(hIcon);
            return cloned;
        }
        catch
        {
            return null;
        }
    }

    private void InitTrayIcon()
    {
        try
        {
            var icon = LoadTrayIconFromPng();
            if (icon == null)
            {
                icon = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location)
                    ?? System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new NotifyIcon
            {
                Icon = icon,
                Text = "POS Bridge - rulează în fundal",
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

            var menu = new ContextMenuStrip();
            var openItem = new ToolStripMenuItem("Deschide / Restore");
            openItem.Click += (_, _) => RestoreFromTray();
            var exitItem = new ToolStripMenuItem("Ieșire");
            exitItem.Click += (_, _) => ExitApplication();

            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = menu;
        }
        catch (Exception ex)
        {
            Log($"Avertisment: iconița tray nu s-a putut crea: {ex.Message}");
        }
    }

    private void RestoreFromTray()
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(RestoreFromTray);
                return;
            }
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
        catch (Exception ex)
        {
            Log($"Eroare la restaurarea ferestrei: {ex.Message}");
            try
            {
                MessageBox.Show(
                    $"Nu s-a putut restaura fereastra:\n\n{ex.Message}\n\nÎnchide aplicația din iconiță (Ieșire) și repornește-o.",
                    "Eroare restaurare",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // Ignore if MessageBox also fails (e.g. during shutdown)
            }
        }
    }

    private void ExitApplication()
    {
        try
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(ExitApplication);
                return;
            }
            _isRealExit = true;
            Close();
        }
        catch (Exception ex)
        {
            Log($"Eroare la închidere: {ex.Message}");
            try { Application.Current.Shutdown(); } catch { }
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isRealExit)
        {
            // Salvează setările înainte de minimizare în tray
            try { SyncUiToFields(); SaveSettings(); } catch { }
            e.Cancel = true;
            Hide();
            Log("Aplicația rulează în fundal. Click pe iconiță pentru a o deschide.");
            return;
        }

        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;

            Log("═══════════════════════════════════════");
            Log("Aplicație în închidere - curățare...");
            
            // Stop folder watcher
            if (_isWatching)
            {
                Log("Oprire monitorizare folder...");
                _watcher?.Stop();
                _watcher?.Dispose();
                Log("✓ Monitorizarea folderului s-a oprit");
            }

            // Disconnect from fiscal device and release COM port
            try
            {
                Log("Deconectare de la dispozitivul fiscal...");
                _fiscalEngine.Disconnect();
                Log("✓ Dispozitiv fiscal deconectat");
            }
            catch (Exception ex)
            {
                Log($"⚠ Avertisment deconectare: {ex.Message}");
                // Continue with shutdown even if disconnect fails
            }

            // Force close COM transport to prevent locked port on next app start
            try
            {
                Log("Eliberare forțată port COM...");
                _fiscalEngine.ForceCloseComPort();
                Log("✓ Port COM eliberat");
            }
            catch (Exception ex)
            {
                Log($"⚠ Avertisment eliberare COM: {ex.Message}");
            }

            // Close DUDE process if we launched it
            try
            {
                if (App.DudeProcess != null && !App.DudeProcess.HasExited)
                {
                    Log($"Închidere proces DUDE (PID: {App.DudeProcess.Id})...");
                    
                    // Try graceful close first
                    App.DudeProcess.CloseMainWindow();
                    
                    // Wait up to 5 seconds for graceful exit
                    if (!App.DudeProcess.WaitForExit(5000))
                    {
                        Log("⚠ DUDE nu s-a închis grațios, forțare închidere...");
                        App.DudeProcess.Kill();
                        App.DudeProcess.WaitForExit();
                    }
                    
                    Log("✓ Proces DUDE închis cu succes");
                }
            }
            catch (Exception ex)
            {
                Log($"⚠ Avertisment închidere DUDE: {ex.Message}");
                // Continue with shutdown even if DUDE close fails
            }

            // Save settings
            GetOperatorCredentials();
            SaveSettings();
            Log("✓ Setări salvate");
            
            Log("Curățare aplicație finalizată cu succes");
            Log("═══════════════════════════════════════");
        }
        catch (Exception ex)
        {
            // Log error but don't prevent application from closing
            try
            {
                Log($"✗ Eroare curățare: {ex.Message}");
            }
            catch
            {
                // Ignore logging errors during shutdown
            }
        }
        
        base.OnClosing(e);
    }

    /// <summary>
    /// Sincronizează valorile din UI în câmpurile interne înainte de salvare.
    /// </summary>
    private void SyncUiToFields()
    {
        var port = PortComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(port))
            _comPort = port;

        var baud = BaudComboBox.SelectedItem?.ToString();
        if (!string.IsNullOrWhiteSpace(baud) && int.TryParse(baud, out int b))
            _baudRate = b;

        if (ConnectionTypeComboBox.SelectedItem is ComboBoxItem ctItem && ctItem.Tag is string ctTag)
        {
            _connectionType = ctTag;
            _useEthernet = ctTag == "Ethernet";
        }

        var ip = IpAddressBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(ip))
            _ipAddress = ip;

        if (TcpPortBox != null && int.TryParse(TcpPortBox.Text, out int tp))
            _tcpPort = tp;

        GetOperatorCredentials();
    }

    private (int OperatorCode, string OperatorPassword) GetOperatorCredentials()
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.Invoke(GetOperatorCredentials);

        int opCode = 1;
        string opCodeText = OperatorCodeBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(opCodeText) && int.TryParse(opCodeText, out int parsed))
            opCode = parsed;

        string opPwd = OperatorPasswordBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(opPwd))
            opPwd = "0000";

        _operatorCode = opCode;
        _operatorPassword = opPwd;
        DebugLog("MainWindow.xaml.cs:640", "GetOperatorCredentials", new
        {
            operatorCode = opCode,
            operatorPwdLen = opPwd.Length
        }, "H1", "pre-fix");
        return (opCode, opPwd);
    }

    private static void DebugLog(string location, string message, object data, string hypothesisId, string runId)
    {
        try
        {
            var payload = new
            {
                id = Guid.NewGuid().ToString("n"),
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                location,
                message,
                data,
                runId,
                hypothesisId
            };
            File.AppendAllText(DebugLogPath, JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
            // Ignore logging failures
        }
    }

    /// <summary>
    /// Cancel blocked fiscal receipt (Command 60 - 3Ch)
    /// </summary>
    private async void CancelReceiptButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool connected = _selectedDeviceType == DeviceType.Incotex 
                ? (_currentDevice?.IsConnected ?? false) 
                : _fiscalEngine.IsConnected;
            if (!connected)
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            var result = MessageBox.Show(
                "Această funcție va ANULA bonul fiscal deschis curent.\n\n" +
                "⚠️ ATENȚIE:\n" +
                "• Folosește această funcție DOAR dacă ai un bon blocat/deschis\n" +
                "• Bonul va fi anulat complet (nu se va tipări)\n" +
                "• Operațiunea este IREVERSIBILĂ\n" +
                "• Dacă nu există bon deschis, va apărea o eroare\n\n" +
                "Continuați cu anularea?",
                "Anulare Bon Blocat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                Log("Operațiune anulare bon anulată de utilizator.");
                return;
            }

            CancelReceiptButton.IsEnabled = false;
            Log("═══════════════════════════════════════");
            Log("❌ Încercare anulare bon blocat...");

            if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null)
            {
                await _currentDevice.CancelReceiptAsync();
            }
            else
            {
                await Task.Run(() => _fiscalEngine.CancelReceipt());
            }
            Log("✓ Bon anulat cu succes!");
            Log("   Bonul deschis a fost anulat.");
            Log("═══════════════════════════════════════");

            MessageBox.Show(
                "Bonul fiscal deschis a fost anulat cu succes!\n\n" +
                "Casa de marcat este acum liberă pentru un nou bon.",
                "Bon Anulat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"✗ Anulare bon eșuată: {ex.Message}");
            Log("═══════════════════════════════════════");

            string errorMessage = ex.Message.Contains("not permitted") || ex.Message.Contains("Command not permitted")
                ? "Nu există bon deschis de anulat!\n\nCasa de marcat raportează că nu există nicio operațiune în curs."
                : $"Eroare la anularea bonului:\n\n{ex.Message}";

            MessageBox.Show(
                errorMessage,
                "Eroare Anulare Bon",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CancelReceiptButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Cash In - Introduce cash into the register
    /// </summary>
    private async void CashInButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool connected = _selectedDeviceType == DeviceType.Incotex 
                ? (_currentDevice?.IsConnected ?? false) 
                : _fiscalEngine.IsConnected;
            if (!connected)
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            if (!decimal.TryParse(CashAmountBox.Text, out decimal amount) || amount <= 0)
            {
                MessageBox.Show("Introduceți o sumă validă mai mare decât 0!", "Sumă Invalidă", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Introduceți {amount:F2} lei în casa de marcat?\n\n" +
                "Această operațiune va fi înregistrată în rapoartele fiscale.",
                "Introducere Numerar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                Log("Operațiune introducere numerar anulată de utilizator.");
                return;
            }

            CashInButton.IsEnabled = false;
            Log($"💰 Introducere {amount:F2} lei în casă...");

            if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null)
            {
                await IncotexOperations.CashInAsync(_currentDevice, amount);
            }
            else
            {
                await Task.Run(() => _fiscalEngine.CashIn(amount));
            }
            Log($"✓ Introducere numerar reușită: {amount:F2} lei");
        }
        catch (Exception ex)
        {
            Log($"✗ Introducere numerar eșuată: {ex.Message}");
            MessageBox.Show(
                $"Eroare la introducerea numerar:\n\n{ex.Message}",
                "Eroare",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CashInButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Cash Out - Remove cash from the register
    /// </summary>
    private async void CashOutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool connected = _selectedDeviceType == DeviceType.Incotex 
                ? (_currentDevice?.IsConnected ?? false) 
                : _fiscalEngine.IsConnected;
            if (!connected)
                throw new InvalidOperationException("Dispozitivul nu este conectat.");

            if (!decimal.TryParse(CashAmountBox.Text, out decimal amount) || amount <= 0)
            {
                MessageBox.Show("Introduceți o sumă validă mai mare decât 0!", "Sumă Invalidă", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Scoateți {amount:F2} lei din casa de marcat?\n\n" +
                "⚠️ ATENȚIE: Această operațiune va fi înregistrată în rapoartele fiscale.",
                "Scoatere Numerar",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                Log("Operațiune scoatere numerar anulată de utilizator.");
                return;
            }

            CashOutButton.IsEnabled = false;
            Log($"💸 Scoatere {amount:F2} lei din casă...");

            if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null)
            {
                await IncotexOperations.CashOutAsync(_currentDevice, amount);
            }
            else
            {
                await Task.Run(() => _fiscalEngine.CashOut(amount));
            }
            Log($"✓ Scoatere numerar reușită: {amount:F2} lei");
        }
        catch (Exception ex)
        {
            Log($"✗ Scoatere numerar eșuată: {ex.Message}");
            MessageBox.Show(
                $"Eroare la scoaterea numerar:\n\n{ex.Message}",
                "Eroare",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            CashOutButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// X Report - Intermediate daily report (does NOT reset counters)
    /// </summary>
    private async void XReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = MessageBox.Show(
                "Creați un Raport X (Intermediar)?\n\n" +
                "ℹ️ INFO:\n" +
                "• Raport intermediar al vânzărilor curente\n" +
                "• NU resetează contoarele zilnice\n" +
                "• Poate fi tipărit oricând în timpul zilei\n" +
                "• Util pentru verificări intermediare\n\n" +
                (_selectedDeviceType == DeviceType.Incotex && _currentDevice?.IsConnected == true
                    ? "Se va tipări direct pe casa Incotex conectată."
                    : "Se va crea un fișier care va fi procesat automat."),
                "Raport X",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                Log("Raport X anulat de utilizator.");
                return;
            }

            XReportButton.IsEnabled = false;
            Log("═══════════════════════════════════════");

            if (_selectedDeviceType == DeviceType.Incotex && _currentDevice != null && _currentDevice.IsConnected)
            {
                Log("📊 Tipărire Raport X pe Incotex...");
                await IncotexOperations.PrintXReportAsync(_currentDevice);
                Log("✓ Raport X tipărit cu succes.");
                Log("═══════════════════════════════════════");
                MessageBox.Show("Raport X a fost tipărit cu succes pe casa Incotex.", "Raport X", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Log("📊 Creare fișier Raport X...");
                await Task.Run(() =>
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"raportX_{timestamp}.txt";
                    string filePath = Path.Combine(_bonFolder, fileName);
                    File.WriteAllText(filePath, "X^");
                    Log($"✓ Fișier Raport X creat: {fileName}");
                    Log("   Fișierul va fi procesat automat de monitorizarea folderului.");
                });
                Log("═══════════════════════════════════════");
                MessageBox.Show("Fișierul raport X a fost creat cu succes!\n\nVa fi procesat automat de monitorizarea folderului.", "Raport X Creat", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Log($"✗ Raport X eșuat: {ex.Message}");
            Log("═══════════════════════════════════════");
            MessageBox.Show($"Eroare Raport X:\n\n{ex.Message}", "Eroare Raport X", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            XReportButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Z Report - Daily report with zeroing (RESETS counters)
    /// </summary>
    private async void ZReportButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            bool directIncotex = _selectedDeviceType == DeviceType.Incotex && _currentDevice?.IsConnected == true;

            var result = MessageBox.Show(
                "⚠️ ATENȚIE: Creați Raportul Z (Zilnic cu Zerare)?\n\n" +
                "🔴 IMPORTANT:\n" +
                "• Raport zilnic FINAL\n" +
                "• RESETEAZĂ toate contoarele zilnice\n" +
                "• Operațiune IREVERSIBILĂ\n" +
                "• Se face DOAR la închiderea zilei\n" +
                "• După Z, începe o nouă zi fiscală\n\n" +
                (directIncotex ? "Se va tipări direct pe casa Incotex conectată." : "Se va crea un fișier care va fi procesat automat.") + "\n\n" +
                "Sigur doriți să continuați?",
                "⚠️ Raport Z - CONFIRMARE",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                Log("Raport Z anulat de utilizator.");
                return;
            }

            var doubleConfirm = MessageBox.Show(
                "🔴 ULTIMA CONFIRMARE!\n\n" +
                "Această operațiune va RESETA contoarele zilnice!\n\n" +
                "Confirmați crearea Raportului Z?",
                "⚠️ Confirmare Finală",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);

            if (doubleConfirm != MessageBoxResult.Yes)
            {
                Log("Raport Z anulat de utilizator (confirmare finală).");
                return;
            }

            ZReportButton.IsEnabled = false;
            Log("═══════════════════════════════════════");

            if (directIncotex && _currentDevice != null)
            {
                Log("📋 Tipărire Raport Z pe Incotex...");
                Log("⚠️  ATENȚIE: Contoarele zilnice vor fi resetate!");
                await IncotexOperations.PrintZReportAsync(_currentDevice);
                Log("✓ Raport Z tipărit cu succes.");
                Log("═══════════════════════════════════════");
                MessageBox.Show("Raport Z a fost tipărit cu succes pe casa Incotex.\n\nContoarele zilnice au fost resetate.", "Raport Z", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                Log("📋 Creare fișier Raport Z...");
                Log("⚠️  ATENȚIE: Contoarele zilnice vor fi resetate la procesare!");
                await Task.Run(() =>
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string fileName = $"raportZ_{timestamp}.txt";
                    string filePath = Path.Combine(_bonFolder, fileName);
                    File.WriteAllText(filePath, "Z^");
                    Log($"✓ Fișier Raport Z creat: {fileName}");
                    Log("   Fișierul va fi procesat automat de monitorizarea folderului.");
                });
                Log("═══════════════════════════════════════");
                MessageBox.Show("Fișierul raport Z a fost creat cu succes!\n\nVa fi procesat automat de monitorizarea folderului.\n\n⚠️ ATENȚIE: Contoarele zilnice vor fi resetate la procesare!", "Raport Z Creat", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            Log($"✗ Raport Z eșuat: {ex.Message}");
            Log("═══════════════════════════════════════");
            MessageBox.Show($"Eroare Raport Z:\n\n{ex.Message}", "Eroare Raport Z", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ZReportButton.IsEnabled = true;
        }
    }

    #region Device Activation

    /// <summary>
    /// Loads activation information into the UI.
    /// </summary>
    private void LoadActivationInfo()
    {
        try
        {
            var app = Application.Current as App;
            if (app != null)
            {
                _deviceSerialNumber = GetDeviceSerialNumberFromApp();
                _tenantCode = GetTenantCodeFromApp();
            }

            DeviceSerialTextBox.Text = _deviceSerialNumber;
            DeviceTenantTextBox.Text = _tenantCode;
            FiscalPrinterSeriesTextBox.Text = "—";
            DeviceModelTextBox.Text = "—";
            DeviceStatusTextBox.Text = App.IsDeviceActivated ? "Activated" : "Deactivated";
            FirstAuthDateTextBox.Text = "—";

            if (App.IsDeviceActivated)
                UpdateActivationStatus(true, "Dispozitiv activat", "Aplicația funcționează normal.");
            else
                UpdateActivationStatus(false, "Dispozitiv neactivat", "Mod demo - 30 zile.");

            // Fetch full activation info from server in background
            _ = RefreshActivationInfoFromServerAsync();
        }
        catch (Exception ex)
        {
            Log($"Eroare la încărcarea info activare: {ex.Message}");
        }
    }

    /// <summary>
    /// Fetches full activation info from the server and updates the UI fields.
    /// </summary>
    private async Task RefreshActivationInfoFromServerAsync()
    {
        try
        {
            _activationService ??= new DeviceActivationService();
            var result = await _activationService.CheckActivationAsync(_deviceSerialNumber);

            Dispatcher.Invoke(() =>
            {
                _firstAuthDate = result.FirstAuthenticationDate;
                FiscalPrinterSeriesTextBox.Text = string.IsNullOrWhiteSpace(result.FiscalPrinterSeries) ? "—" : result.FiscalPrinterSeries;
                DeviceModelTextBox.Text = string.IsNullOrWhiteSpace(result.Model) ? "—" : result.Model;
                DeviceStatusTextBox.Text = string.IsNullOrWhiteSpace(result.Status) ? (result.IsActivated ? "Activated" : "Deactivated") : result.Status;
                FirstAuthDateTextBox.Text = result.FirstAuthenticationDate?.ToString("dd.MM.yyyy HH:mm") ?? "—";

                if (result.IsActivated)
                {
                    App.IsDeviceActivated = true;
                    UpdateActivationStatus(true, "Dispozitiv activat", result.Message);
                }
                else
                {
                    App.IsDeviceActivated = false;
                    UpdateActivationStatus(false, "Dispozitiv neactivat", result.Message);
                }
                UpdateDemoFooter();
            });
        }
        catch (Exception ex)
        {
            Log($"Nu s-au putut încărca detaliile de activare: {ex.Message}");
        }
    }

    /// <summary>
    /// Afișează în footer: status licență (activă/demo) și data instalării (când e disponibilă).
    /// </summary>
    private void UpdateDemoFooter()
    {
        bool isActivated = App.IsDeviceActivated;

        if (FooterDemoText == null) return;

        if (!isActivated)
        {
            var firstRun = GetDemoFirstRunDate();
            int remaining = 30 - (DateTime.Today - firstRun).Days;
            string daysText = remaining <= 0 ? "Demo expirat" : remaining == 1 ? "Demo - 1 zi rămasă" : $"Demo - {remaining} zile rămase";
            FooterDemoText.Inlines.Clear();
            FooterDemoText.Inlines.Add(new Run(daysText) { Foreground = System.Windows.Media.Brushes.Red, FontWeight = FontWeights.Bold });
            FooterDemoText.Visibility = Visibility.Visible;
        }
        else
        {
            FooterDemoText.Inlines.Clear();
            var run = new Run("Licență activă");
            run.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60)); // Verde
            run.FontWeight = FontWeights.Bold;
            FooterDemoText.Inlines.Add(run);
            if (_firstAuthDate.HasValue)
            {
                FooterDemoText.Inlines.Add(new Run($" | Instalată: {_firstAuthDate.Value:dd.MM.yyyy}") { Foreground = System.Windows.Media.Brushes.Gray });
            }
            FooterDemoText.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Returnează data primei porniri în mod demo. Salvează data curentă dacă nu există.
    /// </summary>
    private DateTime GetDemoFirstRunDate()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                foreach (var line in File.ReadAllLines(_settingsPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DemoFirstRunDate=", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring("DemoFirstRunDate=".Length).Trim();
                        if (DateTime.TryParse(val, out var dt))
                            return dt.Date;
                        break;
                    }
                }
            }
        }
        catch { }

        var today = DateTime.Today;
        SaveDemoFirstRunDate(today);
        return today;
    }

    private void SaveDemoFirstRunDate(DateTime date)
    {
        try
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(_settingsPath))
            {
                foreach (var line in File.ReadAllLines(_settingsPath))
                {
                    var idx = line.IndexOf('=');
                    if (idx > 0) dict[line.Substring(0, idx).Trim()] = line.Substring(idx + 1).Trim();
                }
            }
            dict["DemoFirstRunDate"] = date.ToString("yyyy-MM-dd");
            var dir = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            File.WriteAllLines(_settingsPath, dict.Select(kv => $"{kv.Key}={kv.Value}"));
        }
        catch { }
    }

    /// <summary>
    /// Updates the activation status UI.
    /// </summary>
    private void UpdateActivationStatus(bool isActivated, string statusText, string message)
    {
        Dispatcher.Invoke(() =>
        {
            if (isActivated)
            {
                ActivationStatusBorder.Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#E8F5E9");
                ActivationStatusBorder.BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#4CAF50");
                ActivationStatusText.Text = statusText;
                ActivationStatusText.Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#2E7D32");
                ActivationMessageText.Text = message;
                ActivationMessageText.Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#558B2F");
            }
            else
            {
                ActivationStatusBorder.Background = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#FFEBEE");
                ActivationStatusBorder.BorderBrush = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#E74C3C");
                ActivationStatusText.Text = statusText;
                ActivationStatusText.Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#C62828");
                ActivationMessageText.Text = message;
                ActivationMessageText.Foreground = (System.Windows.Media.Brush)new BrushConverter().ConvertFrom("#D32F2F");
            }
        });
    }

    /// <summary>
    /// Gets the device serial number from settings or hardware.
    /// </summary>
    private string GetDeviceSerialNumberFromApp()
    {
        try
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DeviceSerialNumber=", StringComparison.OrdinalIgnoreCase))
                    {
                        var serial = trimmed.Substring("DeviceSerialNumber=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(serial) && serial != "AUTO")
                        {
                            return serial;
                        }
                    }
                }
            }

            // Generate from hardware
            var machineName = Environment.MachineName;
            var processorId = GetProcessorIdForDisplay();
            return $"POS_{machineName}_{processorId}".ToUpperInvariant();
        }
        catch
        {
            return $"POS_{Environment.MachineName}".ToUpperInvariant();
        }
    }

    /// <summary>
    /// Gets the tenant code from settings.
    /// </summary>
    private string GetTenantCodeFromApp()
    {
        try
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                var lines = File.ReadAllLines(settingsPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("TenantCode=", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenant = trimmed.Substring("TenantCode=".Length).Trim();
                        if (!string.IsNullOrWhiteSpace(tenant))
                        {
                            return tenant;
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return "demo";
    }

    /// <summary>
    /// Gets processor ID for display.
    /// </summary>
    private string GetProcessorIdForDisplay()
    {
        try
        {
            var mc = new System.Management.ManagementClass("win32_processor");
            var moc = mc.GetInstances();
            foreach (var mo in moc)
            {
                var processorId = mo.Properties["processorID"].Value?.ToString();
                if (!string.IsNullOrEmpty(processorId))
                {
                    return processorId.Substring(0, Math.Min(8, processorId.Length));
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return Environment.MachineName.GetHashCode().ToString("X8");
    }

    /// <summary>
    /// Checks device activation status.
    /// </summary>
    private async void CheckActivationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CheckActivationButton.IsEnabled = false;
            CheckActivationButton.Content = "⏳ Se verifică...";

            _activationService = new DeviceActivationService();
            var result = await _activationService.CheckActivationAsync(_deviceSerialNumber);

            // Update all UI fields with server response
            _firstAuthDate = result.FirstAuthenticationDate;
            FiscalPrinterSeriesTextBox.Text = string.IsNullOrWhiteSpace(result.FiscalPrinterSeries) ? "—" : result.FiscalPrinterSeries;
            DeviceModelTextBox.Text = string.IsNullOrWhiteSpace(result.Model) ? "—" : result.Model;
            DeviceStatusTextBox.Text = string.IsNullOrWhiteSpace(result.Status) ? (result.IsActivated ? "Activated" : "Deactivated") : result.Status;
            FirstAuthDateTextBox.Text = result.FirstAuthenticationDate?.ToString("dd.MM.yyyy HH:mm") ?? "—";

            if (result.IsActivated)
            {
                UpdateActivationStatus(true, "Dispozitiv activat", $"{result.Message}\nModel: {result.Model}");
                Log($"✅ Verificare activare: {result.Message} | Status: {result.Status} | Model: {result.Model} | Serie fiscală: {result.FiscalPrinterSeries}");
                MessageBox.Show(
                    $"Dispozitivul este activat și funcționează normal.\n\n" +
                    $"Status: {result.Status}\nModel: {result.Model}\nSerie fiscală: {result.FiscalPrinterSeries}",
                    "Activare", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                UpdateActivationStatus(false, "Dispozitiv neactivat", result.Message);
                Log($"⚠️ Verificare activare: {result.Message} | Status: {result.Status}");
                MessageBox.Show($"Dispozitivul nu este activat.\n\nStatus: {result.Status}\n{result.Message}\n\nContactați administratorul.", 
                    "Activare Necesară", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            UpdateDemoFooter();
        }
        catch (Exception ex)
        {
            Log($"Eroare verificare activare: {ex.Message}");
            MessageBox.Show($"Eroare la verificare: {ex.Message}", 
                "Eroare", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            CheckActivationButton.IsEnabled = true;
            CheckActivationButton.Content = "🔄 Verifică activarea";
        }
    }

    /// <summary>
    /// Tests the connection to the activation/licensing server and logs all details.
    /// </summary>
    private async void TestActivationServerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            TestActivationServerButton.IsEnabled = false;
            TestActivationServerButton.Content = "⏳ Se testează...";

            var svc = new DeviceActivationService();
            var apiBaseUrl = svc.BaseUrl;
            var checkUrl = svc.FullCheckUrl(_deviceSerialNumber);

            Log("═══════════════════════════════════════");
            Log("🌐 TEST CONEXIUNE SERVER ACTIVARE (API v13.0.0)");
            Log($"   URL bază:          {apiBaseUrl}");
            Log($"   Endpoint check:    {checkUrl}");
            Log($"   Endpoint register: {svc.FullRegisterUrl}");
            Log($"   Serial Number:     {_deviceSerialNumber}");
            Log($"   Tenant Code:       {_tenantCode}");
            Log("───────────────────────────────────────");

            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var stopwatch = Stopwatch.StartNew();

            // Step 1: DNS resolution test
            Log("   [1/3] Rezolvare DNS...");
            try
            {
                var uri = new Uri(apiBaseUrl);
                var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.Host);
                Log($"   ✓ DNS rezolvat: {uri.Host} → {string.Join(", ", addresses.Select(a => a.ToString()))}");
            }
            catch (Exception dnsEx)
            {
                Log($"   ✗ DNS eșuat: {dnsEx.Message}");
            }

            // Step 2: HTTP GET to /check endpoint
            Log("   [2/3] HTTP GET /check ...");
            try
            {
                var response = await httpClient.GetAsync(checkUrl);
                var body = await response.Content.ReadAsStringAsync();
                stopwatch.Stop();

                Log($"   ✓ HTTP Status:  {(int)response.StatusCode} {response.StatusCode}");
                Log($"   ✓ Timp răspuns:  {stopwatch.ElapsedMilliseconds} ms");
                Log($"   ✓ Content-Type:  {response.Content.Headers.ContentType}");
                Log($"   ✓ Răspuns body:  {body}");

                // Parse JSON response
                try
                {
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(body);
                    Log("   ✓ JSON valid. Câmpuri:");
                    foreach (var prop in jsonDoc.RootElement.EnumerateObject())
                    {
                        Log($"     • {prop.Name}: {prop.Value}");
                    }
                }
                catch
                {
                    Log("   ⚠ Răspunsul nu este JSON valid");
                }
            }
            catch (TaskCanceledException)
            {
                stopwatch.Stop();
                Log($"   ✗ TIMEOUT după {stopwatch.ElapsedMilliseconds} ms (limita: 10s)");
            }
            catch (System.Net.Http.HttpRequestException httpEx)
            {
                stopwatch.Stop();
                Log($"   ✗ HTTP eroare: {httpEx.Message}");
                if (httpEx.InnerException != null)
                    Log($"   ✗ Detalii:     {httpEx.InnerException.Message}");
            }

            // Step 3: HTTP GET to /register endpoint (just test reachability, no actual POST)
            Log("   [3/3] HTTP HEAD / (test server reachability)...");
            try
            {
                var sw2 = Stopwatch.StartNew();
                var baseResponse = await httpClient.GetAsync(apiBaseUrl);
                sw2.Stop();
                Log($"   ✓ Server accesibil: {(int)baseResponse.StatusCode} {baseResponse.StatusCode} ({sw2.ElapsedMilliseconds} ms)");
            }
            catch (Exception baseEx)
            {
                Log($"   ✗ Server inaccesibil: {baseEx.Message}");
            }

            Log("───────────────────────────────────────");
            Log("🌐 TEST FINALIZAT");
            Log("═══════════════════════════════════════");
        }
        catch (Exception ex)
        {
            Log($"✗ Eroare test server: {ex.Message}");
        }
        finally
        {
            TestActivationServerButton.IsEnabled = true;
            TestActivationServerButton.Content = "🌐 Test conexiune server activare";
        }
    }

    /// <summary>
    /// Copies the serial number to clipboard.
    /// </summary>
    private void CopySerialButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(_deviceSerialNumber);
            Log("📋 Serial Number copiat în clipboard");
            MessageBox.Show("Serial Number a fost copiat în clipboard!", 
                "Copiat", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"Eroare copiere: {ex.Message}");
        }
    }

    #endregion
}
