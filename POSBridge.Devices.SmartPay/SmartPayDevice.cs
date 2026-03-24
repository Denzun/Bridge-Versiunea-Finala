using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Exceptions;
using POSBridge.Abstractions.Models;
using static POSBridge.Devices.SmartPay.SmartPayProtocol;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// SmartPay/Ingenico fiscal device implementation.
/// Supports Ingenico iCT, iPP, Desk, Lane, iUN, and Self series.
/// </summary>
public class SmartPayDevice : IFiscalDevice
{
    private readonly SmartPaySerialWrapper _serial;
    private ConnectionSettings? _connectionSettings;
    private bool _disposed;

    public string VendorName => "SmartPay";
    public string ModelName { get; private set; } = "Ingenico Terminal";
    public DeviceCapabilities Capabilities { get; }

    public SmartPayDevice()
    {
        _serial = new SmartPaySerialWrapper();
        Capabilities = CreateCapabilities();
    }

    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        return await Task.Run(() =>
        {
            try
            {
                string port = settings.Port ?? "COM1";
                int baud = settings.BaudRate > 0 ? settings.BaudRate : 115200;

                _serial.Connect(port, baud);
                _connectionSettings = settings;

                // Get device info to verify connection and get model
                var info = GetDeviceInfoAsync().Result;
                if (!string.IsNullOrEmpty(info.ModelName))
                {
                    ModelName = info.ModelName;
                }

                return _serial.IsConnected;
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to connect to SmartPay: {ex.Message}", ex);
            }
        });
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                _serial.Disconnect();
                _connectionSettings = null;
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Failed to disconnect: {ex.Message}", ex);
            }
        });
    }

    public bool IsConnected => _serial.IsConnected;

    // ==================== DEVICE INFO ====================

    public async Task<DeviceInfo> GetDeviceInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // Build Get Info command
                var packet = BuildPacket(CommandCode.GetInfo);
                var response = _serial.SendAndReceive(packet);
                var (success, respCode, tags) = ParseResponse(response);

                var info = new DeviceInfo
                {
                    VendorName = VendorName,
                    ModelName = ModelName,
                    IsFiscalized = true
                };

                if (success && tags.Count > 0)
                {
                    if (tags.TryGetValue(TAG_SOFTWARE_VERSION, out var sw))
                        info.FirmwareVersion = System.Text.Encoding.ASCII.GetString(sw);

                    if (tags.TryGetValue(TAG_HARDWARE_VERSION, out var hw))
                    {
                        string hwStr = System.Text.Encoding.ASCII.GetString(hw);
                        info.SerialNumber = hwStr;
                        ModelName = $"Ingenico ({hwStr})";
                    }

                    if (tags.TryGetValue(TAG_TERMINAL_ID, out var tid))
                        info.FiscalNumber = System.Text.Encoding.ASCII.GetString(tid);
                }

                return info;
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
                var packet = BuildPacket(CommandCode.GetInfo);
                var response = _serial.SendAndReceive(packet);
                var (success, _, _) = ParseResponse(response);
                return success ? "OK" : "Error";
            }
            catch
            {
                return "Not Connected";
            }
        });
    }

    // ==================== RECEIPT OPERATIONS ====================

    public async Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password)
    {
        // SmartPay doesn't have explicit receipt open - transactions are standalone
        return await Task.FromResult(new ReceiptResult { Success = true });
    }

    public async Task<SaleResult> AddSaleAsync(string name, decimal price, decimal quantity, int vatGroup, int department = 1)
    {
        // SmartPay doesn't support itemized sales like fiscal printers
        // Sales are card transactions only
        return await Task.FromResult(new SaleResult { Success = true });
    }

    public async Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true)
    {
        return await Task.FromResult(new SubtotalResult { Success = true, Amount = 0m });
    }

    public async Task<DiscountResult> AddDiscountAsync(decimal valueOrPercent, bool isPercent)
    {
        return await Task.FromResult(new DiscountResult { Success = true });
    }

    public async Task<PaymentResult> AddPaymentAsync(PaymentType type, decimal amount)
    {
        // This is the main payment operation for SmartPay
        return await Task.Run(() =>
        {
            try
            {
                // Map PaymentType to SmartPay logic
                // SmartPay primarily handles card payments
                if (type != PaymentType.Card)
                {
                    return new PaymentResult 
                    { 
                        Success = true, 
                        Change = 0m // SmartPay handles exact card amounts
                    };
                }

                // For card payments, we'd call PerformSaleAsync
                // But this is called from the receipt flow, so we just acknowledge
                return new PaymentResult { Success = true, Change = 0m };
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Payment failed: {ex.Message}", ex);
            }
        });
    }

    public async Task<CloseResult> CloseReceiptAsync()
    {
        return await Task.FromResult(new CloseResult 
        { 
            Success = true, 
            ReceiptNumber = "0",
            TotalAmount = 0m
        });
    }

    public async Task CancelReceiptAsync()
    {
        // Nothing to cancel - transactions are atomic
        await Task.CompletedTask;
    }

    public async Task<ReceiptInfo> ReadCurrentReceiptInfoAsync()
    {
        return await Task.FromResult(new ReceiptInfo
        {
            IsReceiptOpened = false,
            IsFiscal = true,
            SalesCount = 0,
            SubtotalAmount = 0m
        });
    }

    public async Task PrintLastReceiptDuplicateAsync()
    {
        // SmartPay doesn't support this via ECR protocol
        await Task.CompletedTask;
    }

    // ==================== SMARTPAY SPECIFIC OPERATIONS ====================

    /// <summary>
    /// Performs a card sale transaction (SmartPay specific)
    /// </summary>
    public async Task<SmartPaySaleResult> PerformSaleAsync(decimal amount, string currencyCode = "946", string uniqueId = "")
    {
        return await Task.Run(() =>
        {
            try
            {
                var tags = new Dictionary<ushort, byte[]>
                {
                    [TAG_AMOUNT] = System.Text.Encoding.ASCII.GetBytes(FormatAmount(amount)),
                    [TAG_CURRENCY_CODE] = System.Text.Encoding.ASCII.GetBytes(currencyCode.PadLeft(3, '0')),
                };

                if (!string.IsNullOrEmpty(uniqueId))
                {
                    tags[TAG_UNIQUE_ID] = System.Text.Encoding.ASCII.GetBytes(uniqueId.PadRight(25).Substring(0, 25));
                }

                var packet = BuildPacket(CommandCode.Sale, tags);
                var response = _serial.SendAndReceive(packet);
                var (success, respCode, respTags) = ParseResponse(response);

                var result = new SmartPaySaleResult
                {
                    Success = success && respCode == ResponseCode.Success,
                    ResponseCode = (byte)respCode
                };

                if (result.Success)
                {
                    ExtractSaleResponse(respTags, result);
                }
                else
                {
                    result.ErrorMessage = GetResponseCodeDescription(respCode);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SmartPaySaleResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                };
            }
        });
    }

    /// <summary>
    /// Performs settlement (end of day)
    /// </summary>
    public async Task<SettlementResult> PerformSettlementAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var packet = BuildPacket(CommandCode.Settlement);
                var response = _serial.SendAndReceive(packet);
                var (success, respCode, tags) = ParseResponse(response);

                var result = new SettlementResult { Success = success };

                if (success && tags.TryGetValue(TAG_BATCH_NUMBER, out var batch))
                {
                    result.BatchNumber = System.Text.Encoding.ASCII.GetString(batch);
                }

                return result;
            }
            catch (Exception ex)
            {
                return new SettlementResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                };
            }
        });
    }

    /// <summary>
    /// Voids a previous transaction
    /// </summary>
    public async Task<VoidResult> PerformVoidAsync(string stan, string receiptNumber)
    {
        return await Task.Run(() =>
        {
            try
            {
                var tags = new Dictionary<ushort, byte[]>
                {
                    [TAG_STAN] = System.Text.Encoding.ASCII.GetBytes(stan.PadLeft(6, '0'))
                };

                var packet = BuildPacket(CommandCode.Void, tags);
                var response = _serial.SendAndReceive(packet);
                var (success, respCode, _) = ParseResponse(response);

                return new VoidResult
                {
                    Success = success && respCode == ResponseCode.Success,
                    ReceiptNumber = receiptNumber
                };
            }
            catch (Exception ex)
            {
                return new VoidResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                };
            }
        });
    }

    // ==================== CASH MANAGEMENT ====================

    public async Task CashInAsync(decimal amount, string description = "")
    {
        // Not supported by SmartPay ECR protocol
        await Task.CompletedTask;
    }

    public async Task CashOutAsync(decimal amount, string description = "")
    {
        // Not supported by SmartPay ECR protocol
        await Task.CompletedTask;
    }

    public async Task<DailyAmounts> ReadDailyAvailableAmountsAsync()
    {
        return await Task.FromResult(new DailyAmounts
        {
            Cash = 0m,
            Card = 0m,
            Credit = 0m,
            Voucher = 0m
        });
    }

    // ==================== REPORTS ====================

    public async Task PrintDailyReportAsync(string type)
    {
        if (type.ToUpper() == "Z")
        {
            await PerformSettlementAsync();
        }
        await Task.CompletedTask;
    }

    public async Task PrintFiscalMemoryByDateAsync(DateTime startDate, DateTime endDate)
    {
        await Task.CompletedTask;
    }

    public async Task PrintOperatorsReportAsync()
    {
        await Task.CompletedTask;
    }

    public async Task PrintDepartmentsReportAsync()
    {
        await Task.CompletedTask;
    }

    // ==================== DISPLAY & OTHER ====================

    public async Task DisplayTextAsync(string text1, string text2 = "")
    {
        await Task.CompletedTask;
    }

    public async Task OpenCashDrawerAsync()
    {
        await Task.CompletedTask;
    }

    public async Task PrintNonFiscalTextAsync(string text)
    {
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _serial.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    // ==================== PRIVATE HELPERS ====================

    private void ExtractSaleResponse(Dictionary<ushort, byte[]> tags, SmartPaySaleResult result)
    {
        if (tags.TryGetValue(TAG_APPROVED_AMOUNT, out var amt))
            result.ApprovedAmount = ParseAmount(amt);

        if (tags.TryGetValue(TAG_STAN, out var stan))
            result.Stan = System.Text.Encoding.ASCII.GetString(stan);

        if (tags.TryGetValue(TAG_RRN, out var rrn))
            result.Rrn = System.Text.Encoding.ASCII.GetString(rrn);

        if (tags.TryGetValue(TAG_AUTH_CODE, out var auth))
            result.AuthorizationCode = System.Text.Encoding.ASCII.GetString(auth);

        if (tags.TryGetValue(TAG_PAN, out var pan))
            result.CardNumber = System.Text.Encoding.ASCII.GetString(pan);

        if (tags.TryGetValue(TAG_CARDHOLDER_NAME, out var name))
            result.CardholderName = System.Text.Encoding.ASCII.GetString(name);

        if (tags.TryGetValue(TAG_RESPONSE_CODE, out var resp))
            result.HostResponseCode = System.Text.Encoding.ASCII.GetString(resp);

        if (tags.TryGetValue(TAG_RESPONSE_TEXT, out var respText))
            result.HostResponseText = System.Text.Encoding.ASCII.GetString(respText);

        if (tags.TryGetValue(TAG_EMV_APP_LABEL, out var appLabel))
            result.EmvApplicationLabel = System.Text.Encoding.ASCII.GetString(appLabel);

        if (tags.TryGetValue(TAG_TRANSACTION_FLAGS, out var flags) && flags.Length > 0)
            result.TransactionFlags = flags[0];
    }

    private static string GetResponseCodeDescription(ResponseCode code)
    {
        return code switch
        {
            ResponseCode.Success => "Success",
            ResponseCode.GenericError => "Generic Error",
            ResponseCode.HostCommunicationError => "Host Communication Error",
            ResponseCode.OutOfRange => "Out of Range",
            ResponseCode.InvalidInput => "Invalid Input Parameters",
            ResponseCode.InvalidTerminalParams => "Invalid Terminal Parameters",
            ResponseCode.MissingPinKey => "Missing PIN Encryption Key",
            ResponseCode.CancelByUser => "Cancelled by User",
            _ => $"Unknown Error (0x{(byte)code:X2})"
        };
    }

    private static DeviceCapabilities CreateCapabilities()
    {
        return new DeviceCapabilities
        {
            SupportsRS232 = true,
            SupportsUSB = true,
            SupportsEthernet = false,
            SupportsWiFi = false,
            SupportsBluetooth = false,
            SupportsReceiptInfo = false,
            SupportsSubtotalReturn = false,
            SupportsDailyAmounts = false,
            SupportsLastReceiptDuplicate = false,
            SupportsCashDescription = false,
            MaxItemNameLength = 0, // Not applicable
            MaxOperators = 1,
            MaxDepartments = 0,
            MaxPaymentTypes = 1 // Card only
        };
    }
}

// ==================== SMARTPAY SPECIFIC RESULTS ====================

public class SmartPaySaleResult
{
    public bool Success { get; set; }
    public byte ResponseCode { get; set; }
    public string? ErrorMessage { get; set; }
    public decimal ApprovedAmount { get; set; }
    public string? Stan { get; set; }
    public string? Rrn { get; set; }
    public string? AuthorizationCode { get; set; }
    public string? CardNumber { get; set; }
    public string? CardholderName { get; set; }
    public string? HostResponseCode { get; set; }
    public string? HostResponseText { get; set; }
    public string? EmvApplicationLabel { get; set; }
    public byte TransactionFlags { get; set; }
}

public class SettlementResult
{
    public bool Success { get; set; }
    public string? BatchNumber { get; set; }
    public string? ErrorMessage { get; set; }
}

public class VoidResult
{
    public bool Success { get; set; }
    public string? ReceiptNumber { get; set; }
    public string? ErrorMessage { get; set; }
}
