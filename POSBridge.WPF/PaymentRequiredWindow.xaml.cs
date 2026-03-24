using System.Diagnostics;
using System.IO;
using System.Windows;
using POSBridge.Core.Services;

namespace POSBridge.WPF;

/// <summary>
/// Window displayed when the POS device is not activated.
/// Shows payment required message and allows retry or exit.
/// </summary>
public partial class PaymentRequiredWindow : Window
{
    private readonly string _serialNumber;
    private readonly string _tenantCode;
    private readonly DeviceActivationService _activationService;
    
    public PaymentRequiredWindow(ActivationResult result, string serialNumber, string tenantCode)
    {
        InitializeComponent();
        
        _serialNumber = serialNumber;
        _tenantCode = tenantCode;
        _activationService = new DeviceActivationService();
        
        // Set initial message
        UpdateMessage(result.Message);
        
        // Set device info
        SerialNumberText.Text = $"Serial: {serialNumber}";
        TenantText.Text = $"Tenant: {tenantCode}";
        
        // Center on screen
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }
    
    /// <summary>
    /// Updates the status message displayed in the UI.
    /// </summary>
    public void UpdateMessage(string message)
    {
        StatusMessage.Text = message;
    }
    
    /// <summary>
    /// Handles retry button click - attempts to check activation again.
    /// </summary>
    private async void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        RetryButton.IsEnabled = false;
        UpdateMessage("Se verifică activarea...");
        
        try
        {
            var result = await _activationService.CheckActivationAsync(_serialNumber);
            
            if (result.IsActivated)
            {
                // Device is now activated - restart application
                UpdateMessage("Dispozitiv activat! Se repornește aplicația...");
                
                await Task.Delay(1500);
                
                // Restart application
                RestartApplication();
            }
            else
            {
                // Still not activated - try to register if not found
                if (result.Message.Contains("not registered", StringComparison.OrdinalIgnoreCase) ||
                    result.Message.Contains("nu este înregistrat", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateMessage("Dispozitiv necunoscut. Se înregistrează...");
                    
                    var (model, fiscalSeries) = GetDeviceModelAndSeriesFromSettings();
                    var registerResult = await _activationService.RegisterDeviceAsync(
                        _serialNumber, 
                        _tenantCode, 
                        fiscalPrinterSeries: fiscalSeries,
                        model: model
                    );
                    
                    UpdateMessage(registerResult.Message);
                }
                else
                {
                    UpdateMessage(result.Message);
                }
            }
        }
        catch (Exception ex)
        {
            UpdateMessage($"Eroare: {ex.Message}");
        }
        finally
        {
            RetryButton.IsEnabled = true;
        }
    }
    
    /// <summary>
    /// Handles exit button click - closes the application.
    /// </summary>
    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }
    
    /// <summary>
    /// Restarts the application to load with activated status.
    /// </summary>
    private void RestartApplication()
    {
        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var exeFile = Path.ChangeExtension(exePath, ".exe");
            
            if (File.Exists(exeFile))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exeFile,
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Nu s-a putut reporni aplicația:\n{ex.Message}\n\nVă rugăm reporniți manual.",
                "Eroare",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
    
    /// <summary>
    /// Gets device model and fiscal printer series from settings (saved when device connects).
    /// </summary>
    private static (string model, string fiscalPrinterSeries) GetDeviceModelAndSeriesFromSettings()
    {
        string model = "N/A";
        string fiscalSeries = "N/A";
        try
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
            if (File.Exists(settingsPath))
            {
                foreach (var line in File.ReadAllLines(settingsPath))
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("DeviceModel=", StringComparison.OrdinalIgnoreCase))
                    {
                        model = trimmed.Substring("DeviceModel=".Length).Trim();
                        if (string.IsNullOrWhiteSpace(model)) model = "N/A";
                    }
                    else if (trimmed.StartsWith("FiscalPrinterSeries=", StringComparison.OrdinalIgnoreCase))
                    {
                        fiscalSeries = trimmed.Substring("FiscalPrinterSeries=".Length).Trim();
                        if (string.IsNullOrWhiteSpace(fiscalSeries)) fiscalSeries = "N/A";
                    }
                }
            }
        }
        catch { }
        return (model, fiscalSeries);
    }

    /// <summary>
    /// Prevents closing the window without explicit action.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Allow closing only via Exit button or Retry success
        // User cannot close via Alt+F4 or X button
        if (RetryButton.IsEnabled && !e.Cancel)
        {
            // Show confirmation dialog
            var result = System.Windows.MessageBox.Show(
                "Doriți să închideți aplicația?\n\nDispozitivul nu este activat și nu poate fi utilizat.",
                "Confirmare ieșire",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        }
        
        base.OnClosing(e);
    }
}
