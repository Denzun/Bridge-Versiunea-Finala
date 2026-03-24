using System.Collections.Generic;
using System.Windows;
using MessageBox = System.Windows.MessageBox;
using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Core.Models;
using POSBridge.Devices.Datecs;
using POSBridge.Devices.Incotex;

namespace POSBridge.WPF;

public partial class DeviceInfoWindow : Window
{
    private readonly FiscalEngine _engine;
    private readonly DeviceType _deviceType;
    private readonly IFiscalDevice? _multiVendorDevice;

    private static readonly List<AllowedFunction> AllowedFunctions = new()
    {
        new("Bonuri fiscale", "Deschidere, vânzare, subtotal, plată, închidere, anulare"),
        new("Bonuri non-fiscale", "Deschidere, text liber, închidere"),
        new("Facturi fiscale", "Deschidere cu CIF client, vânzare, plată"),
        new("Numerar", "Cash In, Cash Out (introduce/scoate numerar)"),
        new("Rapoarte", "Raport X (intermediar), Raport Z (zilnic cu zerare)"),
        new("Imprimantă", "Paper Feed, Paper Cut, print text fiscal/non-fiscal"),
        new("Afișaj client", "Linie superioară, linie inferioară (max 20 caractere)"),
        new("Barcode/QR", "Print barcode EAN8/EAN13/Code128/QR, Interleave 2of5"),
        new("Subtotal", "Subtotal cu discount/surcharge procent/sumă"),
        new("Plăți", "Numerar, Card, Credit, Tichete masă, Voucher, Alte metode"),
        new("Citire date", "Status, dată/oră, CIF, TVA active, diagnostic"),
        new("Header/Footer", "Programare linii header (1-10), footer"),
        new("Articole PLU", "Programare, citire, raport articole"),
        new("Departamente/Grupuri", "Rapoarte pe departamente și grupuri articole"),
        new("Operatori", "30 operatori, parolă, raport operatori"),
    };

    /// <param name="deviceType">Tip dispozitiv (Datecs / Incotex / etc.). Implicit Datecs pentru compatibilitate.</param>
    /// <param name="multiVendorDevice">Dispozitiv conectat pentru Incotex etc.; null pentru Datecs</param>
    public DeviceInfoWindow(DeviceType deviceType = DeviceType.Datecs, IFiscalDevice? multiVendorDevice = null)
    {
        InitializeComponent();
        _engine = FiscalEngine.Instance;
        _deviceType = deviceType;
        _multiVendorDevice = multiVendorDevice;
        FunctionsList.ItemsSource = AllowedFunctions;
        Loaded += async (_, _) =>
        {
            try
            {
                await RefreshInfoAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Eroare la încărcarea informațiilor:\n\n{ex.Message}", "Eroare", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        try
        {
            await RefreshInfoAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async System.Threading.Tasks.Task RefreshInfoAsync()
    {
        bool connected;
        DeviceInfo info;

        if (_deviceType == DeviceType.Datecs)
        {
            connected = _engine.IsConnected;
            info = await System.Threading.Tasks.Task.Run(() => _engine.GetDeviceInfo());
        }
        else if (_deviceType == DeviceType.Incotex && _multiVendorDevice is IncotexDevice incotex && incotex.IsConnected)
        {
            connected = true;
            info = await incotex.GetDeviceInfoForDisplayAsync();
        }
        else
        {
            connected = false;
            info = new DeviceInfo();
        }

        Dispatcher.Invoke(() =>
        {
            NotConnectedText.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
            InfoGrid.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;

            if (connected)
            {
                SerialNumberText.Text = info.SerialNumber;
                FiscalNumberText.Text = info.FiscalNumber;
                TAXnumberText.Text = info.TAXnumber;
                DeviceNameText.Text = info.DeviceName;
                FirmwareText.Text = string.IsNullOrEmpty(info.FirmwareDate)
                    ? info.FirmwareVersion
                    : $"{info.FirmwareVersion} ({info.FirmwareDate})";
                DateTimeText.Text = info.DateTime;
                ReceiptOpenText.Text = info.ReceiptOpen;
                ReceiptNumberText.Text = info.ReceiptNumber;
                Header1Text.Text = info.Headerline1;
                Header2Text.Text = info.Headerline2;
                VATText.Text = $"{info.TaxA} / {info.TaxB} / {info.TaxC} / {info.TaxD} / {info.TaxE}";
                CashText.Text = $"{info.CashSum} (In: {info.CashIn}, Out: {info.CashOut})";
            }
        });
    }

    private record AllowedFunction(string Name, string Description);
}
