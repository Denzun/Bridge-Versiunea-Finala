using System.IO.Ports;
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

        private CancellationTokenSource? _connectCts;

    /// <summary>
    /// Direct diagnostic test - bypasses all wrapper code
    /// </summary>
    public async Task<bool> DiagnoseAsync(string portName)
    {
        return await Task.Run(() =>
        {
            var log = new System.Text.StringBuilder();
            SerialPort? port = null;
            
            try
            {
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Opening {portName}...");
                port = new SerialPort(portName, 115200)
                {
                    DataBits = 8, Parity = Parity.None, StopBits = StopBits.One,
                    Handshake = Handshake.None, DtrEnable = false, RtsEnable = true,
                    ReadTimeout = 3000, WriteTimeout = 3000
                };
                port.Open();
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Port opened OK");
                
                Thread.Sleep(100);
                port.DiscardInBuffer();
                port.DiscardOutBuffer();

                // ENQ
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Sending ENQ...");
                port.Write(new byte[] { 0x05 }, 0, 1);
                int enq = port.ReadByte();
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] ENQ response: 0x{enq:X2}");

                if (enq != 0x06)
                    throw new Exception($"ENQ got 0x{enq:X2}, expected ACK");

                Thread.Sleep(50);
                port.DiscardInBuffer();

                // Packet: 02 00 04 A0 00 01 01 03 06 35
                byte[] packet = { 0x02, 0x00, 0x04, 0xA0, 0x00, 0x01, 0x01, 0x03, 0x06, 0x35 };
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] BytesToWrite before: {port.BytesToWrite}");
                port.Write(packet, 0, packet.Length);
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] BytesToWrite after:  {port.BytesToWrite}");
                
                int resp = port.ReadByte();
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] Packet response: 0x{resp:X2} → {(resp == 0x06 ? "ACK ✓" : resp == 0x15 ? "NAK ✗" : "???")}");

                // Write result to a file we CAN see
                Directory.CreateDirectory(@"C:\Temp");
                System.IO.File.WriteAllText(@"C:\Temp\smartpay_diag.txt", log.ToString());
                return resp == 0x06;
            }
            catch (Exception ex)
            {
                log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] EXCEPTION: {ex.Message}");
                Directory.CreateDirectory(@"C:\Temp");
                System.IO.File.WriteAllText(@"C:\Temp\smartpay_diag.txt", log.ToString());
                return false;
            }
            finally
            {
                try { port?.Close(); port?.Dispose(); } catch { }
            }
        });
    }

    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        // IMMEDIATE FILE WRITE - before anything else
        try
        {
            Directory.CreateDirectory(@"C:\Temp");
            File.WriteAllText(@"C:\Temp\smartpay_entry.txt", 
                $"SmartPay ConnectAsync ENTERED at {DateTime.Now:HH:mm:ss.fff}\nPort: {settings.Port}\nBaud: {settings.BaudRate}");
        }
        catch (Exception ex)
        {
            // Ignore file write errors
        }

        // Cancel any previous in-flight connect
        _connectCts?.Cancel();
        _connectCts?.Dispose();
        _connectCts = new CancellationTokenSource();
        var ct = _connectCts.Token;

        return await Task.Run(() =>
        {
            string port = settings.Port ?? "COM1";
            int[] baudRates;
            
            // If specific baud rate provided, try only that one
            if (settings.BaudRate > 0)
            {
                baudRates = new[] { settings.BaudRate };
            }
            else
            {
                // Auto-detect: try common baud rates
                baudRates = new[] { 115200, 9600, 19200, 38400 };
            }

            Exception? lastError = null;
            
            foreach (int baud in baudRates)
            {
                // Check cancellation before each attempt
                ct.ThrowIfCancellationRequested();
                
                try
                {
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Trying {port} @ {baud} baud...");
                    _serial.Connect(port, baud);
                    _connectionSettings = settings;
                    
                    // Try to communicate
                    SmartPayDebug.Log("[ConnectAsync] About to call GetDeviceInfo for verification...");
                    var info = GetDeviceInfoAsync().GetAwaiter().GetResult();
                    SmartPayDebug.Log($"[ConnectAsync] GetDeviceInfo returned: Success={!string.IsNullOrEmpty(info.FirmwareVersion)}, Model={info.ModelName}");
                    
                    if (!string.IsNullOrEmpty(info.ModelName))
                    {
                        ModelName = info.ModelName;
                    }
                    
                    // Success! Remember the working baud rate
                    if (settings.BaudRate == 0)
                    {
                        settings.BaudRate = baud;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Connected at {baud} baud!");
                    return true;
                }
                catch (FiscalDeviceException ex) when (ex.Message.Contains("NAK") || ex.Message.Contains("Timeout"))
                {
                    // Wrong baud rate or terminal not responding - try next
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Failed at {baud}: {ex.Message}");
                    lastError = ex;
                    _serial.Disconnect(); // Proper cleanup
                    Thread.Sleep(500); // Wait for port release
                    continue;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    _serial.Disconnect(); // Proper cleanup
                    Thread.Sleep(500);
                    if (baudRates.Length == 1) throw;
                }
            }
            
            // All baud rates failed - include debug log
            string debugLog = SmartPayDebug.GetLogContents();
            SmartPayDebug.FlushToFile();
            
            throw new FiscalDeviceException(
                $"Failed to connect to SmartPay.\n\n" +
                $"Last error: {lastError?.Message}\n\n" +
                $"DEBUG LOG:\n{debugLog}\n\n" +
                $"Check:\n" +
                $"1. Terminal is powered on and in ECR mode\n" +
                $"2. Correct COM port selected ({port})\n" +
                $"3. USB cable is connected properly\n" +
                $"4. Ingenico USB drivers are installed");
        }, ct);
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
                SmartPayDebug.Log("[GetDeviceInfo] Building GetInfo packet...");
                
                // Build Get Info command
                var packet = BuildPacket(CommandCode.GetInfo);
                SmartPayDebug.Log($"[GetDeviceInfo] Packet built, calling SendAndReceive...");
                
                var response = _serial.SendAndReceive(packet);
                SmartPayDebug.Log($"[GetDeviceInfo] SendAndReceive returned {response.Length} bytes");
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
