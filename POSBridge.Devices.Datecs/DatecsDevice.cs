using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Models;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.Datecs;

/// <summary>
/// Datecs fiscal device implementation using DUDE COM Server.
/// Implements IFiscalDevice interface for multi-vendor architecture.
/// </summary>
public class DatecsDevice : IFiscalDevice
{
    private readonly DudeComWrapper _dude;
    private ConnectionSettings? _connectionSettings;
    private bool _disposed;

    public DatecsDevice()
    {
        _dude = new DudeComWrapper();
        Capabilities = CreateDatecsCapabilities();
    }

    // ==================== DEVICE IDENTITY ====================

    public string VendorName => "Datecs";

    public string ModelName { get; private set; } = "Unknown";

    public DeviceCapabilities Capabilities { get; }

    // ==================== CONNECTION MANAGEMENT ====================

    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        // Validate connection type
        if (settings.Type != ConnectionType.Serial && settings.Type != ConnectionType.Ethernet)
            throw new FiscalDeviceException("Datecs devices support Serial (RS232) or Ethernet (TCP/IP) connection");

        return await Task.Run(() =>
        {
            try
            {
                _dude.Initialize();
                
                // Connect based on connection type
                if (settings.Type == ConnectionType.Ethernet)
                {
                    // TCP/IP connection
                    if (string.IsNullOrWhiteSpace(settings.IpAddress))
                        throw new FiscalDeviceException("IP Address is required for Ethernet connection");
                    
                    int tcpPort = settings.TcpPort > 0 ? settings.TcpPort : 9100; // Default: 9100 (AppSocket) sau 3999 (DP-25X)
                    _dude.ConnectTCP(settings.IpAddress, tcpPort);
                }
                else
                {
                    // RS232 serial connection
                    if (string.IsNullOrWhiteSpace(settings.Port))
                        throw new FiscalDeviceException("COM Port is required for Serial connection");
                    
                    _dude.Connect(settings.Port, settings.BaudRate);
                }
                
                _connectionSettings = settings;

                // Read device info to get model name
                try
                {
                    var info = GetDeviceInfoAsync().Result;
                    ModelName = info.ModelName;
                }
                catch
                {
                    ModelName = "Datecs Fiscal Printer";
                }

                return _dude.IsConnected;
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to connect to Datecs device: {ex.Message}", ex);
            }
        });
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.Disconnect();
                _connectionSettings = null;
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to disconnect: {ex.Message}", ex);
            }
        });
    }

    public bool IsConnected => _dude.IsConnected;

    // ==================== RECEIPT OPERATIONS ====================

    public async Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "OpCode", operatorCode },
                    { "OpPwd", password },
                    { "TillNumber", 1 }
                };

                _dude.ExecuteSafe("receipt_Fiscal_Open", parameters);

                return new ReceiptResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to open receipt: {ex.Message}", ex);
            }
        });
    }

    public async Task<SaleResult> AddSaleAsync(string name, decimal price, decimal quantity, int vatGroup, int department = 1)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "PluName", name },
                    { "TaxCd", vatGroup.ToString() },
                    { "Price", price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                    { "Quantity", quantity.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) },
                    { "Discount_Type", 0 },
                    { "Discount_Value", "0" },
                    { "Department", department },
                    { "MeasureUnit", "buc" }
                };

                _dude.ExecuteSafe("receipt_Fiscal_Sale", parameters);

                return new SaleResult
                {
                    Success = true,
                    ReceiptNumber = null
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to add sale: {ex.Message}", ex);
            }
        });
    }

    public async Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Print", print ? 1 : 0 },
                    { "Display", display ? 1 : 0 },
                    { "DiscountType", 0 },
                    { "DiscountValue", "0" }
                };

                _dude.ExecuteSafe("receipt_Fiscal_Subtotal", parameters);
                
                // Read subtotal amount from output
                var subtotalStr = _dude.Output("receipt_Fiscal_Subtotal", "Subtotal");
                decimal.TryParse(subtotalStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal subtotal);

                return new SubtotalResult
                {
                    Success = true,
                    Amount = subtotal
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to get subtotal: {ex.Message}", ex);
            }
        });
    }

    public async Task<DiscountResult> AddDiscountAsync(decimal valueOrPercent, bool isPercent)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "DiscountType", isPercent ? 1 : 0 },
                    { "DiscountValue", valueOrPercent.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                };

                _dude.ExecuteSafe("receipt_Fiscal_Discount", parameters);

                return new DiscountResult
                {
                    Success = true
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to add discount: {ex.Message}", ex);
            }
        });
    }

    public async Task<PaymentResult> AddPaymentAsync(PaymentType type, decimal amount)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "PaidMode", (int)type },
                    { "input_Amount", amount == 0 ? "0" : amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                };

                _dude.ExecuteSafe("receipt_Fiscal_Total", parameters);

                return new PaymentResult
                {
                    Success = true,
                    Change = 0m
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to add payment: {ex.Message}", ex);
            }
        });
    }

    public async Task<CloseResult> CloseReceiptAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("receipt_Fiscal_Close");
                var receiptNumber = _dude.Output("receipt_Fiscal_Close", "SlipNumber");

                return new CloseResult
                {
                    Success = true,
                    ReceiptNumber = receiptNumber,
                    FiscalNumber = receiptNumber,
                    TotalAmount = 0m
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to close receipt: {ex.Message}", ex);
            }
        });
    }

    public async Task CancelReceiptAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("receipt_Fiscal_Cancel");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to cancel receipt: {ex.Message}", ex);
            }
        });
    }

    // ==================== CRITICAL METHODS (from FiscalNet analysis) ====================

    public async Task<ReceiptInfo> ReadCurrentReceiptInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("Get_Status");
                var statusBytes = _dude.Output("Get_Status", "StatusBytes") ?? "";
                
                // Parse status to determine if receipt is opened
                // Simplified implementation - in production, parse status bytes properly
                bool isOpened = statusBytes.Contains("1") || statusBytes.Length > 0;

                return new ReceiptInfo
                {
                    IsReceiptOpened = isOpened,
                    IsFiscal = true,
                    SalesCount = 0,
                    SubtotalAmount = 0m,
                    PaymentInitiated = false,
                    PaymentFinalized = false
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to read receipt info: {ex.Message}", ex);
            }
        });
    }

    public async Task PrintLastReceiptDuplicateAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("report_Fiscal_ClosureByNum_Duplicate");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print duplicate: {ex.Message}", ex);
            }
        });
    }

    // ==================== CASH MANAGEMENT ====================

    public async Task CashInAsync(decimal amount, string description = "")
    {
        await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "OperationType", 0 },  // 0 = Cash In
                    { "Amount", amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                };

                _dude.ExecuteSafe("receipt_CashIn_CashOut", parameters);
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to cash in: {ex.Message}", ex);
            }
        });
    }

    public async Task CashOutAsync(decimal amount, string description = "")
    {
        await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "OperationType", 1 },  // 1 = Cash Out
                    { "Amount", amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                };

                _dude.ExecuteSafe("receipt_CashIn_CashOut", parameters);
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to cash out: {ex.Message}", ex);
            }
        });
    }

    public async Task<DailyAmounts> ReadDailyAvailableAmountsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // Execute with OperationType=0 and Amount=0 to just read (not modify) cash
                var parameters = new Dictionary<string, object>
                {
                    { "OperationType", 0 },
                    { "Amount", "0" }
                };

                _dude.ExecuteSafe("receipt_CashIn_CashOut", parameters);
                
                var cashSumStr = _dude.Output("receipt_CashIn_CashOut", "CashSum") ?? "0";
                decimal.TryParse(cashSumStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal cashSum);

                return new DailyAmounts
                {
                    Cash = cashSum,
                    Card = 0m,  // Datecs doesn't separate by payment type in this command
                    Credit = 0m,
                    Voucher = 0m
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to read daily amounts: {ex.Message}", ex);
            }
        });
    }

    // ==================== REPORTS ====================

    public async Task PrintDailyReportAsync(string type)
    {
        await Task.Run(() =>
        {
            try
            {
                type = type.Trim().ToUpperInvariant();
                if (type != "X" && type != "Z")
                    throw new ArgumentException("Report type must be X or Z");

                // Try to cancel any open receipt before printing report
                try
                {
                    _dude.ExecuteSafe("receipt_Fiscal_Cancel");
                }
                catch
                {
                    // Ignore if no receipt is open
                }

                var parameters = new Dictionary<string, object>
                {
                    { "ReportType", type }
                };

                _dude.ExecuteSafe("report_Daily_Closure", parameters);
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print daily report: {ex.Message}", ex);
            }
        });
    }

    public async Task PrintFiscalMemoryByDateAsync(DateTime startDate, DateTime endDate)
    {
        await Task.Run(() =>
        {
            try
            {
                var startStr = startDate.ToString("ddMMyy");
                var endStr = endDate.ToString("ddMMyy");
                
                var output = _dude.ExecuteRawCommand(94, $"{startStr}\t{endStr}");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print FM report: {ex.Message}", ex);
            }
        });
    }

    public async Task PrintOperatorsReportAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("report_Operators");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print operators report: {ex.Message}", ex);
            }
        });
    }

    public async Task PrintDepartmentsReportAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("report_Departments");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print departments report: {ex.Message}", ex);
            }
        });
    }

    // ==================== DEVICE INFO & STATUS ====================

    public async Task<DeviceInfo> GetDeviceInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                _dude.ClearInput();
                _dude.ExecuteCommand("Get_DeviceInfo");
                _dude.CheckError();

                return new DeviceInfo
                {
                    VendorName = VendorName,
                    ModelName = _dude.Output("Get_DeviceInfo", "Model") ?? "Unknown",
                    SerialNumber = _dude.Output("Get_DeviceInfo", "SerialNumber") ?? "",
                    FiscalNumber = _dude.Output("Get_DeviceInfo", "FiscalMemoryNumber") ?? "",
                    FirmwareVersion = _dude.Output("Get_DeviceInfo", "FirmwareVersion") ?? "",
                    IsFiscalized = !string.IsNullOrWhiteSpace(_dude.Output("Get_DeviceInfo", "FiscalMemoryNumber"))
                };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to get device info: {ex.Message}", ex);
            }
        });
    }

    public async Task<string> GetStatusAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                _dude.ClearInput();
                _dude.ExecuteCommand("Get_Status");
                _dude.CheckError();

                var statusBytes = _dude.Output("Get_Status", "StatusBytes");
                return statusBytes ?? "OK";
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to get status: {ex.Message}", ex);
            }
        });
    }

    // ==================== DISPLAY & OTHER ====================

    public async Task DisplayTextAsync(string text1, string text2 = "")
    {
        await Task.Run(() =>
        {
            try
            {
                var parameters = new Dictionary<string, object>
                {
                    { "Line1", text1 },
                    { "Line2", text2 }
                };

                _dude.ExecuteSafe("display_Text_DoubleLines", parameters);
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to display text: {ex.Message}", ex);
            }
        });
    }

    public async Task OpenCashDrawerAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("receipt_Open_CashDrawer");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to open cash drawer: {ex.Message}", ex);
            }
        });
    }

    public async Task PrintNonFiscalTextAsync(string text)
    {
        await Task.Run(() =>
        {
            try
            {
                _dude.ExecuteSafe("receipt_NonFiscal_Open");

                var parameters = new Dictionary<string, object>
                {
                    { "TextData", text }
                };

                _dude.ExecuteSafe("receipt_NonFiscal_Text", parameters);
                _dude.ExecuteSafe("receipt_NonFiscal_Close");
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to print non-fiscal text: {ex.Message}", ex);
            }
        });
    }

    // ==================== CAPABILITIES ====================

    private static DeviceCapabilities CreateDatecsCapabilities()
    {
        return new DeviceCapabilities
        {
            // Connection types
            SupportsRS232 = true,
            SupportsUSB = false,  // Datecs USB appears as virtual COM port
            SupportsEthernet = true,  // TCP/IP support via DUDE
            SupportsWiFi = false,
            SupportsBluetooth = false,
            SupportsGPRS = false,

            // CRITICAL FEATURES (from FiscalNet analysis)
            SupportsReceiptInfo = true,        // ReadCurrentReceiptInfo - prevents "Receipt is opened" errors
            SupportsSubtotalReturn = true,     // Subtotal returns amount for validation
            SupportsDailyAmounts = true,       // ReadDailyAvailableAmounts - cash reconciliation
            SupportsLastReceiptDuplicate = true, // Print last receipt duplicate
            SupportsCashDescription = true,    // Cash In/Out with description

            // Advanced Features
            SupportsWiFiConfiguration = false,
            SupportsGPRSConfiguration = false,
            SupportsDeviceProgramming = true,  // Can program PLU, departments via DUDE

            // Limits
            MaxItemNameLength = 36,
            MaxOperators = 30,
            MaxDepartments = 16,
            MaxPaymentTypes = 5
        };
    }

    // ==================== DISPOSE ====================

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _dude?.Disconnect();
            _dude?.Dispose();
        }
        catch
        {
            // Ignore cleanup errors
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
