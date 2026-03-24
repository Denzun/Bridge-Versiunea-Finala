using System.Windows;
using POSBridge.Devices.SmartPay;
using POSBridge.Abstractions.Enums;
using MessageBox = System.Windows.MessageBox;

namespace POSBridge.WPF;

/// <summary>
/// SmartPay/Ingenico device connection logic
/// Partial class extension for MainWindow
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Connect to SmartPay/Ingenico device with driver check
    /// </summary>
    private async Task ConnectSmartPayDeviceAsync(bool autoStartWatcher)
    {
        try
        {
            Log("🔍 Checking SmartPay driver status...");
            
            // Check driver status first
            var driverStatus = SmartPayDriverInstaller.CheckDriverStatus();
            
            if (!driverStatus.DeviceConnected)
            {
                var result = MessageBox.Show(
                    "Ingenico device not detected.\n\n" +
                    "Please:\n" +
                    "1. Connect the device via USB\n" +
                    "2. Power it on\n\n" +
                    "Do you want to see manual installation instructions?",
                    "SmartPay Device Not Found",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    ShowSmartPayDriverHelp();
                }
                return;
            }

            // Device connected but driver not installed
            if (!driverStatus.DriverInstalled || string.IsNullOrEmpty(driverStatus.ComPort))
            {
                Log("⚠ Driver not installed. Attempting automatic installation...");
                UpdateStatus("Installing driver...", System.Windows.Media.Colors.Orange);
                
                var progress = new Progress<string>(msg => Log($"  → {msg}"));
                var installResult = await SmartPayDriverInstaller.InstallDriverAsync(progress);
                
                if (!installResult.Success)
                {
                    var result = MessageBox.Show(
                        $"{installResult.Message}\n\n" +
                        "Do you want to see manual installation instructions?",
                        "Driver Installation Failed",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Error);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        ShowSmartPayDriverHelp();
                    }
                    return;
                }
                
                // Driver installed successfully
                Log($"✅ Driver installed! Device on {installResult.ComPort}");
                
                // Update COM port in settings
                if (!string.IsNullOrEmpty(installResult.ComPort))
                {
                    _comPort = installResult.ComPort;
                    PortComboBox.SelectedItem = _comPort;
                    SaveSettings();
                }
            }
            else
            {
                Log($"✅ Driver already installed. Device on {driverStatus.ComPort}");
                
                // Auto-select COM port if not already set
                if (!string.IsNullOrEmpty(driverStatus.ComPort) && 
                    (string.IsNullOrEmpty(_comPort) || _comPort == "AUTO"))
                {
                    _comPort = driverStatus.ComPort;
                    PortComboBox.SelectedItem = _comPort;
                }
            }

            // Now proceed with normal connection
            await ConnectMultiVendorDeviceAsync(autoStartWatcher);
        }
        catch (Exception ex)
        {
            Log($"❌ SmartPay connection error: {ex.Message}");
            MessageBox.Show(
                $"Error connecting to SmartPay: {ex.Message}",
                "Connection Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Shows driver help dialog
    /// </summary>
    private void ShowSmartPayDriverHelp()
    {
        var helpWindow = new Window
        {
            Title = "SmartPay Driver Installation Help",
            Width = 600,
            Height = 500,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.CanResize
        };

        var scrollViewer = new System.Windows.Controls.ScrollViewer
        {
            Margin = new Thickness(10),
            VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto
        };

        var textBlock = new System.Windows.Controls.TextBlock
        {
            Text = SmartPayDriverInstaller.GetManualInstallInstructions(),
            TextWrapping = System.Windows.TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12
        };

        scrollViewer.Content = textBlock;
        helpWindow.Content = scrollViewer;
        helpWindow.ShowDialog();
    }

    /// <summary>
    /// Button handler for manual driver install check
    /// </summary>
    private async void CheckSmartPayDriverButton_Click(object sender, RoutedEventArgs e)
    {
        Log("🔍 Checking SmartPay driver status...");
        var status = SmartPayDriverInstaller.CheckDriverStatus();
        
        if (!status.DeviceConnected)
        {
            MessageBox.Show(
                "Ingenico device not detected.\n\n" +
                "Please connect the device via USB and power it on.",
                "Device Not Found",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (status.DriverInstalled && !string.IsNullOrEmpty(status.ComPort))
        {
            MessageBox.Show(
                $"✅ Driver already installed!\n\n" +
                $"Model: {status.DetectedModel}\n" +
                $"COM Port: {status.ComPort}",
                "Driver Status",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        else
        {
            var result = MessageBox.Show(
                $"Device detected but driver not installed.\n\n" +
                $"Model: {status.DetectedModel}\n\n" +
                "Do you want to install the driver automatically?",
                "Install Driver?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                await InstallSmartPayDriverAsync();
            }
        }
    }

    /// <summary>
    /// Manual driver installation
    /// </summary>
    private async Task InstallSmartPayDriverAsync()
    {
        try
        {
            UpdateStatus("Installing driver...", System.Windows.Media.Colors.Orange);
            
            var progress = new Progress<string>(msg => 
            {
                Log($"  → {msg}");
                UpdateStatus(msg, System.Windows.Media.Colors.Orange);
            });
            
            var result = await SmartPayDriverInstaller.InstallDriverAsync(progress);
            
            if (result.Success)
            {
                MessageBox.Show(
                    result.Message,
                    "Installation Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                
                if (!string.IsNullOrEmpty(result.ComPort))
                {
                    _comPort = result.ComPort;
                    PortComboBox.SelectedItem = _comPort;
                    SaveSettings();
                }
            }
            else
            {
                var mbResult = MessageBox.Show(
                    $"{result.Message}\n\n" +
                    "Do you want to see manual installation instructions?",
                    "Installation Failed",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);
                
                if (mbResult == MessageBoxResult.Yes)
                {
                    ShowSmartPayDriverHelp();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Installation error: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            UpdateStatus("Ready", System.Windows.Media.Colors.Green);
        }
    }
}
