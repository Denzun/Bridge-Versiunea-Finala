using System.Collections.Concurrent;
using System.Linq;
using POSBridge.Core.Models;

namespace POSBridge.Devices.Datecs;

/// <summary>
/// Singleton Fiscal Engine for managing DUDE COM Server.
/// Thread-safe, ensures single instance for all fiscal operations.
/// </summary>
public sealed class FiscalEngine
{
    private static readonly Lazy<FiscalEngine> _instance = new(() => new FiscalEngine());
    private DudeComWrapper? _dude;
    private bool _initialized;
    private readonly object _lock = new();
    private readonly BlockingCollection<Action> _comQueue = new();
    private Thread? _comThread;
    private volatile bool _isConnectedCached = false;

    public static FiscalEngine Instance => _instance.Value;
    public int OperatorCode { get; set; } = 1;
    public string OperatorPassword { get; set; } = "0000";

    private FiscalEngine()
    {
    }

    private void EnsureComThread()
    {
        if (_comThread != null)
            return;

        _comThread = new Thread(() =>
        {
            _dude = new DudeComWrapper();
            foreach (var action in _comQueue.GetConsumingEnumerable())
            {
                action();
            }
        })
        {
            IsBackground = true
        };
        _comThread.SetApartmentState(ApartmentState.STA);
        _comThread.Start();
    }

    private void Invoke(Action action)
    {
        EnsureComThread();

        var tcs = new TaskCompletionSource<bool>();
        _comQueue.Add(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        tcs.Task.GetAwaiter().GetResult();
    }

    private T Invoke<T>(Func<T> func)
    {
        EnsureComThread();

        var tcs = new TaskCompletionSource<T>();
        _comQueue.Add(() =>
        {
            try
            {
                var result = func();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Initializes and connects to the fiscal device.
    /// </summary>
    public void Initialize(string portName = "COM5", int baudRate = 115200)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            Invoke(() =>
            {
                _dude!.Initialize();
                _dude!.Connect(portName, baudRate);
            });
            _initialized = true;
        }
    }

    /// <summary>
    /// Initializes and connects to fiscal device via TCP/IP (Ethernet/WiFi).
    /// </summary>
    public void InitializeForTCP(string ipAddress, int port = 9100)
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            Invoke(() =>
            {
                _dude!.Initialize();
                _dude!.ConnectTCP(ipAddress, port);
            });
            _initialized = true;
        }
    }

    /// <summary>
    /// Initializes and connects to fiscal device via TCP/IP with auto-port detection.
    /// Tries common Datecs ports: 9100, 9999, 4000, 8000
    /// </summary>
    /// <returns>The port number that succeeded, or null if all failed</returns>
    public int? InitializeForTCPAutoPort(string ipAddress)
    {
        lock (_lock)
        {
            if (_initialized)
                return null;

            int? successfulPort = null;
            Invoke(() =>
            {
                _dude!.Initialize();
                successfulPort = _dude!.ConnectTCPAutoPort(ipAddress);
            });
            
            if (successfulPort.HasValue)
            {
                _initialized = true;
            }
            
            return successfulPort;
        }
    }

    /// <summary>
    /// Disconnects from the fiscal device.
    /// </summary>
    public void Disconnect()
    {
        Invoke(() =>
        {
            if (!_initialized)
                return;

            _dude?.Disconnect();
            _initialized = false;
            _isConnectedCached = false;
        });
    }

    /// <summary>
    /// Force-releases COM port resources during shutdown.
    /// This method is resilient to partial initialization states.
    /// </summary>
    public void ForceCloseComPort()
    {
        try
        {
            Invoke(() =>
            {
                try
                {
                    _dude?.ForceCloseConnection();
                }
                finally
                {
                    _initialized = false;
                }
            });
        }
        catch
        {
            // Ignore shutdown cleanup failures
        }
    }

    /// <summary>
    /// Forces a full disconnect and reset of the FiscalEngine.
    /// Use this when connection fails and you want to retry.
    /// </summary>
    public void ForceReset()
    {
        try
        {
            Invoke(() =>
            {
                try
                {
                    _dude?.Disconnect();
                    _dude?.Dispose();
                }
                catch { }
                finally
                {
                    _dude = new DudeComWrapper();
                    _initialized = false;
                    _isConnectedCached = false;
                }
            });
        }
        catch
        {
            // Ignore errors during reset
            _initialized = false;
        }
    }

    /// <summary>
    /// Tests if the device is connected and responsive.
    /// </summary>
    public bool TestConnection()
    {
        return Invoke(() =>
        {
            try
            {
                if (!_initialized)
                {
                    _isConnectedCached = false;
                    return false;
                }

                _dude!.ExecuteSafe("Get_Status");
                _isConnectedCached = true;
                return true;
            }
            catch
            {
                _isConnectedCached = false;
                return false;
            }
        });
    }

    /// <summary>
    /// Retrieves device information from the fiscal printer.
    /// Each command is tried independently; failures leave fields as "N/A".
    /// </summary>
    public DeviceInfo GetDeviceInfo()
    {
        return Invoke(() =>
        {
            var info = new DeviceInfo();
            if (!_initialized || _dude == null)
            {
                info.LastError = "Device not initialized. Connect first.";
                return info;
            }

            TryGetStatus(info);
            TryGetDateTime(info);
            TryGetTAX(info);
            TryGetDeviceInfo(info);
            TryGetDiagnostic(info);
            TryGetReceiptStatus(info);
            TryGetVATRates(info);
            TryGetCashInfo(info);

            return info;
        });
    }

    private void TryGetStatus(DeviceInfo info)
    {
        if (_dude == null) return;
        try
        {
            _dude.ExecuteSafe("Get_Status");
            var statusBytes = _dude!.Output("Get_Status", "StatusBytes");
            if (!string.IsNullOrWhiteSpace(statusBytes))
                info.ReceiptOpen = "Status OK";
        }
        catch { /* Get_Status succeeded; StatusBytes may not be available */ }
    }

    private void TryGetDateTime(DeviceInfo info)
    {
        if (_dude == null) return;
        foreach (var cmd in new[] { "Get_DateTime", "Read_DateTime" })
        {
            try
            {
                _dude.ExecuteSafe(cmd);
                var dt = _dude.Output(cmd, "DateTime");
                if (!string.IsNullOrWhiteSpace(dt))
                {
                    info.DateTime = dt;
                    return;
                }
            }
            catch { }
        }
    }

    private void TryGetTAX(DeviceInfo info)
    {
        if (_dude == null) return;
        foreach (var cmd in new[] { "Get_TAX", "Read_TAX" })
        {
            try
            {
                _dude.ExecuteSafe(cmd);
                var tax = _dude.Output(cmd, "TAXnumber");
                if (!string.IsNullOrWhiteSpace(tax))
                {
                    info.TAXnumber = tax;
                    return;
                }
            }
            catch { }
        }
    }

    private void TryGetDeviceInfo(DeviceInfo info)
    {
        if (_dude == null) return;
        foreach (var cmd in new[] { "Device_Info", "Get_DeviceInfo" })
        {
            try
            {
                _dude.ExecuteSafe(cmd, new Dictionary<string, object> { { "Option", "1" } });
                var sn = _dude.Output(cmd, "SerialNumber");
                var fn = _dude.Output(cmd, "FiscalNumber");
                var h1 = _dude.Output(cmd, "Headerline1");
                var h2 = _dude.Output(cmd, "Headerline2");
                var tax = _dude.Output(cmd, "TAXnumber");
                if (!string.IsNullOrWhiteSpace(sn)) info.SerialNumber = sn;
                if (!string.IsNullOrWhiteSpace(fn)) info.FiscalNumber = fn;
                if (!string.IsNullOrWhiteSpace(h1)) info.Headerline1 = h1;
                if (!string.IsNullOrWhiteSpace(h2)) info.Headerline2 = h2;
                if (!string.IsNullOrWhiteSpace(tax)) info.TAXnumber = tax;
                return;
            }
            catch { }
        }
    }

    private void TryGetDiagnostic(DeviceInfo info)
    {
        if (_dude == null) return;
        foreach (var cmd in new[] { "Get_Diagnostic", "Diagnostic_Info" })
        {
            try
            {
                _dude.ExecuteSafe(cmd, new Dictionary<string, object> { { "Param", "1" } });
                var name = _dude.Output(cmd, "Name");
                var fwRev = _dude.Output(cmd, "FwRev");
                var fwDate = _dude.Output(cmd, "FwDate");
                var sn = _dude.Output(cmd, "SerialNumber");
                if (!string.IsNullOrWhiteSpace(name)) info.DeviceName = name;
                if (!string.IsNullOrWhiteSpace(fwRev)) info.FirmwareVersion = fwRev;
                if (!string.IsNullOrWhiteSpace(fwDate)) info.FirmwareDate = fwDate;
                if (!string.IsNullOrWhiteSpace(sn)) info.SerialNumber = sn;
                return;
            }
            catch { }
        }
    }

    private void TryGetReceiptStatus(DeviceInfo info)
    {
        if (_dude == null) return;
        foreach (var cmd in new[] { "receipt_Fiscal_Status", "Get_ReceiptStatus" })
        {
            try
            {
                _dude.ExecuteSafe(cmd);
                var isOpen = _dude.Output(cmd, "IsOpen");
                var number = _dude.Output(cmd, "Number");
                var amount = _dude.Output(cmd, "Amount");
                var payed = _dude.Output(cmd, "Payed");
                if (!string.IsNullOrWhiteSpace(isOpen)) info.ReceiptOpen = isOpen == "1" ? "Da" : "Nu";
                if (!string.IsNullOrWhiteSpace(number)) info.ReceiptNumber = number;
                return;
            }
            catch { }
        }
    }

    private void TryGetVATRates(DeviceInfo info)
    {
        if (_dude == null) return;
        try
        {
            _dude.ExecuteSafe("Get_VAT");
            info.TaxA = _dude.Output("Get_VAT", "TaxA") ?? "N/A";
            info.TaxB = _dude.Output("Get_VAT", "TaxB") ?? "N/A";
            info.TaxC = _dude.Output("Get_VAT", "TaxC") ?? "N/A";
            info.TaxD = _dude.Output("Get_VAT", "TaxD") ?? "N/A";
            info.TaxE = _dude.Output("Get_VAT", "TaxE") ?? "N/A";
        }
        catch { }
    }

    private void TryGetCashInfo(DeviceInfo info)
    {
        if (_dude == null) return;
        try
        {
            _dude.ExecuteSafe("receipt_CashIn_CashOut", new Dictionary<string, object> { { "OperationType", 0 }, { "Amount", "0" } });
            info.CashSum = _dude.Output("receipt_CashIn_CashOut", "CashSum") ?? "N/A";
            info.CashIn = _dude.Output("receipt_CashIn_CashOut", "CashIn") ?? "N/A";
            info.CashOut = _dude.Output("receipt_CashIn_CashOut", "CashOut") ?? "N/A";
        }
        catch { }
    }

    /// <summary>
    /// Advances paper by specified number of lines.
    /// </summary>
    public void FeedPaper(int lines = 3)
    {
        if (lines <= 0)
            throw new ArgumentOutOfRangeException(nameof(lines), "Lines must be greater than 0.");

        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            var parameters = new Dictionary<string, object>
            {
                { "Lines", lines }
            };

            _dude!.ExecuteSafe("receipt_Paper_Feed", parameters);
        });
    }

    /// <summary>
    /// Prints daily report (X or Z).
    /// </summary>
    public void PrintDailyReport(string reportType)
    {
        if (string.IsNullOrWhiteSpace(reportType))
            throw new ArgumentException("Report type cannot be empty.", nameof(reportType));

        reportType = reportType.Trim().ToUpperInvariant();
        if (reportType != "X" && reportType != "Z")
            throw new ArgumentException("Report type must be X or Z.", nameof(reportType));

        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            // Try to cancel any open receipt before printing report
            try
            {
                _dude!.ExecuteSafe("receipt_Fiscal_Cancel");
            }
            catch
            {
                // Ignore if no receipt is open
            }

            var parameters = new Dictionary<string, object>
            {
                { "ReportType", reportType }
            };

            _dude!.ExecuteSafe("report_Daily_Closure", parameters);
        });
    }

    /// <summary>
    /// Processes a complete bon (receipt) with multiple items.
    /// THREAD-SAFE: Uses internal lock to ensure serial processing.
    /// </summary>
    public BonProcessingResult ProcessBon(BonRequest bonRequest)
    {
        return Invoke(() =>
        {
            lock (_lock)
            {
                var startTime = DateTime.Now;
                var result = new BonProcessingResult
                {
                    FileName = bonRequest.FileName,
                    ProcessedAt = startTime
                };

                try
                {
                    if (!_initialized)
                        throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

                    // STEP 1: Open Receipt (Fiscal_Open - Cmd 48)
                    OpenReceipt();

                    // STEP 2: Register each item (Fiscal_Sale - Cmd 49)
                    foreach (var item in bonRequest.Items)
                    {
                        RegisterSale(item);
                    }

                    // STEP 3: Close Receipt with Cash payment (Fiscal_Total - Cmd 53)
                    string receiptNumber = CloseReceipt();

                    result.Success = true;
                    result.ReceiptNumber = receiptNumber;
                    result.ProcessingDuration = DateTime.Now - startTime;

                    return result;
                }
                catch (DudeComException ex)
                {
                    result.Success = false;
                    result.ErrorCode = ex.ErrorCode;
                    result.ErrorMessage = ex.Message;
                    result.ProcessingDuration = DateTime.Now - startTime;

                    // Try to cancel receipt if it was opened
                    try
                    {
                        CancelReceipt();
                    }
                    catch
                    {
                        // Ignore cancel errors
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    result.ProcessingDuration = DateTime.Now - startTime;

                    // Try to cancel receipt
                    try
                    {
                        CancelReceipt();
                    }
                    catch
                    {
                        // Ignore
                    }

                    return result;
                }
            }
        });
    }

    /// <summary>
    /// Processes a command file with custom commands.
    /// </summary>
    public BonProcessingResult ProcessCommandFile(ReceiptCommandFile commandFile)
    {
        return Invoke(() =>
        {
            lock (_lock)
            {
                var startTime = DateTime.Now;
                var result = new BonProcessingResult
                {
                    FileName = commandFile.FileName,
                    ProcessedAt = startTime
                };

                try
                {
                    if (!_initialized)
                        throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

                    // Display-only files
                    if (commandFile.IsDisplay)
                    {
                        foreach (var cmd in commandFile.Commands.Where(c => c.Type == ReceiptCommandType.ClientDisplay))
                        {
                            _dude!.ExecuteSafe("display_UpperLine", new Dictionary<string, object>
                            {
                                { "Text", cmd.Line1 ?? string.Empty }
                            });
                            _dude.ExecuteSafe("display_LowerLine", new Dictionary<string, object>
                            {
                                { "Text", cmd.Line2 ?? string.Empty }
                            });
                        }

                        result.Success = true;
                        result.ProcessingDuration = DateTime.Now - startTime;
                        return result;
                    }

                    // Non-fiscal files
                    if (commandFile.IsNonFiscal)
                    {
                        _dude!.ExecuteSafe("receipt_NonFiscal_Open");
                        foreach (var cmd in commandFile.Commands.Where(c => c.Type == ReceiptCommandType.NonFiscalText))
                        {
                            var parameters = new Dictionary<string, object>
                            {
                                { "TextData", cmd.Text ?? string.Empty },
                                { "cBold", 0 },
                                { "cItalic", 0 },
                                { "cDoubleH", 0 },
                                { "cUnderline", 0 },
                                { "cAlignment", 0 }
                                // cCondensed removed - not supported by all DUDE versions
                            };
                            _dude.ExecuteSafe("receipt_Print_NonFiscal_Text", parameters);
                        }
                        _dude.ExecuteSafe("receipt_NonFiscal_Close");

                        result.Success = true;
                        result.ProcessingDuration = DateTime.Now - startTime;
                        return result;
                    }

                    // Reports (X/Z)
                    if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.XReport || c.Type == ReceiptCommandType.ZReport))
                    {
                        if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.ZReport))
                            PrintDailyReport("Z");
                        else
                            PrintDailyReport("X");

                        result.Success = true;
                        result.ReceiptNumber = "Report";
                        result.ProcessingDuration = DateTime.Now - startTime;
                        return result;
                    }

                    // Standalone cancel command (VB^) must execute without opening a new receipt.
                    if (commandFile.Commands.Any(c => c.Type == ReceiptCommandType.CancelReceipt))
                    {
                        _dude!.ExecuteSafe("receipt_Fiscal_Cancel");
                        result.Success = true;
                        result.ReceiptNumber = "Cancelled";
                        result.ProcessingDuration = DateTime.Now - startTime;
                        return result;
                    }

                    // Standalone CashIn/CashOut commands (I^, O^) must execute without opening a fiscal receipt.
                    if (commandFile.Commands.Count == 1 &&
                        (commandFile.Commands[0].Type == ReceiptCommandType.CashIn ||
                         commandFile.Commands[0].Type == ReceiptCommandType.CashOut))
                    {
                        var cmd = commandFile.Commands[0];
                        decimal amountInLei = cmd.Value ?? 0m;
                        
                        if (amountInLei <= 0)
                            throw new InvalidOperationException($"{cmd.Type} amount must be greater than 0");

                        var parameters = new Dictionary<string, object>
                        {
                            { "OperationType", cmd.Type == ReceiptCommandType.CashIn ? 0 : 1 },
                            { "Amount", amountInLei.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                        };
                        _dude!.ExecuteSafe("receipt_CashIn_CashOut", parameters);
                        
                        result.Success = true;
                        result.ReceiptNumber = cmd.Type == ReceiptCommandType.CashIn 
                            ? $"Cash In: {amountInLei:F2} lei" 
                            : $"Cash Out: {amountInLei:F2} lei";
                        result.ProcessingDuration = DateTime.Now - startTime;
                        return result;
                    }

                    // Reorder commands: Move CB, TL, and DP/DV after ST to before first Payment
                    var reorderedCommands = new List<ReceiptCommand>();
                    int? subtotalIndex = null;
                    
                    // First pass: find ST position and collect everything
                    for (int i = 0; i < commandFile.Commands.Count; i++)
                    {
                        if (commandFile.Commands[i].Type == ReceiptCommandType.Subtotal)
                        {
                            subtotalIndex = i;
                            break;
                        }
                    }
                    
                    if (subtotalIndex.HasValue)
                    {
                        // Add everything up to and including ST (plus its immediate DP/DV lookahead)
                        int i = 0;
                        for (; i <= subtotalIndex.Value; i++)
                        {
                            reorderedCommands.Add(commandFile.Commands[i]);
                        }
                        
                        // Check if next command after ST is DP/DV (discount on subtotal) - add it too
                        if (i < commandFile.Commands.Count)
                        {
                            var nextCmd = commandFile.Commands[i];
                            if (nextCmd.Type == ReceiptCommandType.DiscountPercent || 
                                nextCmd.Type == ReceiptCommandType.DiscountValue ||
                                nextCmd.Type == ReceiptCommandType.MarkupPercent ||
                                nextCmd.Type == ReceiptCommandType.MarkupValue)
                            {
                                reorderedCommands.Add(nextCmd);
                                i++;
                            }
                        }
                        
                        // Collect CB and TL that come after ST
                        var barcodeAndText = new List<ReceiptCommand>();
                        for (; i < commandFile.Commands.Count; i++)
                        {
                            var cmd = commandFile.Commands[i];
                            if (cmd.Type == ReceiptCommandType.Barcode || cmd.Type == ReceiptCommandType.TextLine)
                            {
                                barcodeAndText.Add(cmd);
                            }
                            else if (cmd.Type == ReceiptCommandType.Payment)
                            {
                                // Insert collected CB/TL before payments
                                reorderedCommands.AddRange(barcodeAndText);
                                barcodeAndText.Clear();
                                // Add this payment and all remaining commands
                                for (int j = i; j < commandFile.Commands.Count; j++)
                                {
                                    reorderedCommands.Add(commandFile.Commands[j]);
                                }
                                break;
                            }
                            else
                            {
                                // Some other command after ST - keep it in place
                                reorderedCommands.Add(cmd);
                            }
                        }
                        reorderedCommands.AddRange(barcodeAndText); // Add any remaining CB/TL
                    }
                    else
                    {
                        // No ST found, use original order
                        reorderedCommands.AddRange(commandFile.Commands);
                    }

                    // Proactive: cancel any stuck receipt before opening (clears -111018 / -111015 state).
                    try { _dude!.ExecuteSafe("receipt_Fiscal_Cancel"); } catch { /* ignore if no open receipt */ }

                    // Open fiscal receipt (standard, not invoice)
                    var openParameters = new Dictionary<string, object>
                    {
                        { "OpCode", OperatorCode },
                        { "OpPwd", OperatorPassword },
                        { "TillNumber", 1 }
                    };
                    try
                    {
                        _dude!.ExecuteSafe("receipt_Fiscal_Open", openParameters);
                    }
                    catch (DudeComException ex) when (ex.ErrorCode == -111015)
                    {
                        // Auto-recovery: a previous receipt remained open on device.
                        // Cancel it and retry opening once for current command file.
                        _dude!.ExecuteSafe("receipt_Fiscal_Cancel");
                        _dude.ExecuteSafe("receipt_Fiscal_Open", openParameters);
                    }
                    catch (DudeComException ex) when (ex.ErrorCode == -111018)
                    {
                        // Auto-recovery: device stuck in "Payment is initiated" from previous bon.
                        // Cancel to clear state and retry opening.
                        _dude!.ExecuteSafe("receipt_Fiscal_Cancel");
                        _dude.ExecuteSafe("receipt_Fiscal_Open", openParameters);
                    }

                    // Process all sales and subtotals first, calculate total manually
                    decimal calculatedTotal = 0m;
                    var paymentCommands = new List<ReceiptCommand>();
                    
                    for (int i = 0; i < reorderedCommands.Count; i++)
                    {
                        var cmd = reorderedCommands[i];

                        // Collect payment commands for later processing
                        if (cmd.Type == ReceiptCommandType.Payment)
                        {
                            paymentCommands.Add(cmd);
                            continue; // Skip payment processing for now
                        }

                        switch (cmd.Type)
                        {
                            case ReceiptCommandType.Sale:
                            case ReceiptCommandType.VoidSale:
                            {
                                // Lookahead for discount/markup. FP_Protocol: 1=surcharge%, 2=discount%, 3=surcharge sum, 4=discount sum
                                int discountType = 0;
                                decimal discountValue = 0m;
                                if (i + 1 < reorderedCommands.Count)
                                {
                                    var next = reorderedCommands[i + 1];
                                    if (next.Type == ReceiptCommandType.DiscountPercent)
                                    {
                                        discountType = 2; // discount by percentage
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.DiscountValue)
                                    {
                                        discountType = 4; // discount by sum
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.MarkupPercent)
                                    {
                                        discountType = 1; // surcharge by percentage
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.MarkupValue)
                                    {
                                        discountType = 3; // surcharge by sum
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                }

                                decimal price = cmd.Price ?? 0m;
                                if (cmd.Type == ReceiptCommandType.VoidSale)
                                    price = -Math.Abs(price);

                                var parameters = new Dictionary<string, object>
                                {
                                    { "PluName", cmd.Text ?? string.Empty },
                                    { "TaxCd", cmd.TaxGroup ?? 1 },
                                    { "Price", price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                                    { "Quantity", (cmd.Quantity ?? 1m).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) },
                                    { "Discount_Type", discountType },
                                    { "Discount_Value", discountValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
                                    { "Department", cmd.Department ?? 1 },
                                    { "MeasureUnit", cmd.Unit ?? "buc" }
                                };
                                _dude.ExecuteSafe("receipt_Fiscal_Sale", parameters);
                                break;
                            }
                            case ReceiptCommandType.Subtotal:
                            {
                                // FP_Protocol: 1=surcharge%, 2=discount%, 3=surcharge sum, 4=discount sum
                                int discountType = 0;
                                decimal discountValue = 0m;
                                if (i + 1 < reorderedCommands.Count)
                                {
                                    var next = reorderedCommands[i + 1];
                                    if (next.Type == ReceiptCommandType.DiscountPercent)
                                    {
                                        discountType = 2;
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.DiscountValue)
                                    {
                                        discountType = 4;
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.MarkupPercent)
                                    {
                                        discountType = 1;
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                    else if (next.Type == ReceiptCommandType.MarkupValue)
                                    {
                                        discountType = 3;
                                        discountValue = next.Value ?? 0m;
                                        i++;
                                    }
                                }

                                var parameters = new Dictionary<string, object>
                                {
                                    { "Print", 0 },
                                    { "Display", 0 },
                                    { "DiscountType", discountType },
                                    { "DiscountValue", discountValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                                };
                                _dude!.ExecuteSafe("receipt_Fiscal_Subtotal", parameters);
                                break;
                            }
                            case ReceiptCommandType.TextLine:
                            {
                                var parameters = new Dictionary<string, object>
                                {
                                    { "TextData", cmd.Text ?? string.Empty },
                                    { "cBold", 0 },
                                    { "cItalic", 0 },
                                    { "cDoubleH", 0 },
                                    { "cUnderline", 0 },
                                    { "cAlignment", 0 }
                                };
                                _dude!.ExecuteSafe("receipt_Print_Fiscal_Text", parameters);
                                break;
                            }
                            case ReceiptCommandType.Payment:
                            {
                                // File format: P^1^=Numerar, P^2^=Card, P^3^=Credit, etc.
                                // DUDE API: PaidMode 0=Numerar, 1=Card, 2=Credit, etc.
                                // Convert legacy/documented payment type to DUDE PaidMode.
                                int paidMode = MapPaymentTypeToPaidMode(cmd.PaymentType);
                                
                                var parameters = new Dictionary<string, object>
                                {
                                    { "PaidMode", paidMode },
                                    { "input_Amount", (cmd.Value ?? 0m).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                                };
                                _dude!.ExecuteSafe("receipt_Fiscal_Total", parameters);
                                break;
                            }
                            case ReceiptCommandType.CashIn:
                            case ReceiptCommandType.CashOut:
                            {
                                decimal amountInLei = cmd.Value ?? 0m;
                                
                                var parameters = new Dictionary<string, object>
                                {
                                    { "OperationType", cmd.Type == ReceiptCommandType.CashIn ? 0 : 1 },
                                    { "Amount", amountInLei.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                                };
                                _dude!.ExecuteSafe("receipt_CashIn_CashOut", parameters);
                                break;
                            }
                            case ReceiptCommandType.OpenDrawer:
                                _dude!.ExecuteSafe("receipt_Drawer_KickOut", new Dictionary<string, object> { { "mSec", 80 } });
                                break;
                            case ReceiptCommandType.Barcode:
                            {
                                if (cmd.BarcodeType == 4)
                                {
                                    var parameters = new Dictionary<string, object>
                                    {
                                        { "BarcodeType", 4 },
                                        { "Data", cmd.Barcode ?? string.Empty },
                                        { "QRCodeSize", "4" }
                                    };
                                    _dude!.ExecuteSafe("receipt_Print_QRBarcode", parameters);
                                }
                                else
                                {
                                    var parameters = new Dictionary<string, object>
                                    {
                                        { "BarcodeType", cmd.BarcodeType ?? 2 },
                                        { "Data", cmd.Barcode ?? string.Empty }
                                    };
                                    _dude!.ExecuteSafe("receipt_Print_Barcode", parameters);
                                }
                                break;
                            }
                            case ReceiptCommandType.ClientDisplay:
                                _dude!.ExecuteSafe("display_UpperLine", new Dictionary<string, object> { { "Text", cmd.Line1 ?? string.Empty } });
                                _dude.ExecuteSafe("display_LowerLine", new Dictionary<string, object> { { "Text", cmd.Line2 ?? string.Empty } });
                                break;
                            case ReceiptCommandType.CancelReceipt:
                                _dude!.ExecuteSafe("receipt_Fiscal_Cancel");
                                break;
                            case ReceiptCommandType.PosAmount:
                                throw new InvalidOperationException("POS^ command is not supported in this build.");
                            case ReceiptCommandType.FiscalCode:
                            {
                                // Print fiscal code as text line
                                var parameters = new Dictionary<string, object>
                                {
                                    { "TextData", cmd.Text ?? string.Empty },
                                    { "cBold", 0 },
                                    { "cItalic", 0 },
                                    { "cDoubleH", 0 },
                                    { "cUnderline", 0 },
                                    { "cAlignment", 0 }
                                };
                                _dude!.ExecuteSafe("receipt_Print_Fiscal_Text", parameters);
                                break;
                            }
                            case ReceiptCommandType.DiscountPercent:
                            case ReceiptCommandType.DiscountValue:
                            case ReceiptCommandType.MarkupPercent:
                            case ReceiptCommandType.MarkupValue:
                                // handled by lookahead or at open
                                break;
                        }
                    }

                    // For bons WITHOUT explicit ST^: skip implicit Subtotal - go straight to Total.
                    // Subtotal (Cmd 51) provoked -111018 on this device; Total (Cmd 53) works without it.
                    // Bons WITH explicit ST^ are already processed in the loop above.
                    bool hasSubtotalCommand = reorderedCommands.Any(c => c.Type == ReceiptCommandType.Subtotal);
                    if (!hasSubtotalCommand && paymentCommands.Count > 0)
                    {
                        // No implicit receipt_Fiscal_Subtotal - direct to Total.
                    }

                    // Process payments in the exact order and amounts from the input file.
                    // This keeps behavior aligned with P^TipPlata^VALOARE from integration docs.
                    // Fallback to Amount=0.00 is applied only when firmware rejects cash with -111063.
                    if (paymentCommands.Count > 0)
                    {
                        bool hasAnyNonCashPayment = paymentCommands.Any(p => MapPaymentTypeToPaidMode(p.PaymentType) > 0);
                        int lastCashPaymentIndex = -1;
                        for (int idx = 0; idx < paymentCommands.Count; idx++)
                        {
                            int mode = MapPaymentTypeToPaidMode(paymentCommands[idx].PaymentType);
                            if (mode == 0)
                                lastCashPaymentIndex = idx;
                        }

                        for (int paymentIndex = 0; paymentIndex < paymentCommands.Count; paymentIndex++)
                        {
                            var payment = paymentCommands[paymentIndex];
                            int paidMode = MapPaymentTypeToPaidMode(payment.PaymentType);
                            var requestedAmount = payment.Value ?? 0m;

                            var parameters = new Dictionary<string, object>
                            {
                                { "PaidMode", paidMode },
                                { "input_Amount", requestedAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
                            };
                            try
                            {
                                _dude!.ExecuteSafe("receipt_Fiscal_Total", parameters);
                            }
                            catch (DudeComException ex) when (ex.ErrorCode == -111050 && !hasSubtotalCommand && paymentIndex == 0)
                            {
                                // -111050 Payment not initiated: device requires Subtotal before Total.
                                // Do Subtotal(Print=0) and retry Total once.
                                var subtotalParams = new Dictionary<string, object>
                                {
                                    { "Print", 0 },
                                    { "Display", 0 },
                                    { "DiscountType", 0 },
                                    { "DiscountValue", "0.00" }
                                };
                                _dude!.ExecuteSafe("receipt_Fiscal_Subtotal", subtotalParams);
                                _dude!.ExecuteSafe("receipt_Fiscal_Total", parameters);
                            }
                            catch (DudeComException ex) when (ex.ErrorCode == -111063 && paidMode == 0)
                            {
                                // Some firmware variants reject explicit cash overpayment during totalization.
                                // Never alter single-cash payment intent (it would print wrong change = 0).
                                // Use Amount=0.00 fallback only for mixed tenders where final cash closes remainder.
                                bool isLastCashPayment = paymentIndex == lastCashPaymentIndex;
                                if (!hasAnyNonCashPayment || !isLastCashPayment)
                                    throw;

                                var autoCashParameters = new Dictionary<string, object>
                                {
                                    { "PaidMode", 0 },
                                    { "input_Amount", "0.00" }
                                };
                                _dude!.ExecuteSafe("receipt_Fiscal_Total", autoCashParameters);
                            }
                        }
                    }

                    // Close receipt and get slip number (Cmd 56 per FP_Protocol).
                    // On some firmware, Total may already finalize; if Close fails with -111018, use Total output.
                    try
                    {
                        _dude!.ExecuteSafe("receipt_Fiscal_Close");
                        result.ReceiptNumber = _dude.Output("receipt_Fiscal_Close", "SlipNumber");
                    }
                    catch (DudeComException ex) when (ex.ErrorCode == -111018)
                    {
                        result.ReceiptNumber = _dude.Output("receipt_Fiscal_Total", "SlipNumber")
                            ?? _dude.Output("receipt_Fiscal_Total", "ReceiptNumber")
                            ?? "OK";
                    }

                    result.Success = true;
                    result.ProcessingDuration = DateTime.Now - startTime;
                    return result;
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.ErrorMessage = ex.Message;
                    if (ex is DudeComException dce)
                        result.ErrorCode = dce.ErrorCode;
                    result.ProcessingDuration = DateTime.Now - startTime;
                    return result;
                }
            }
        });
    }

    public bool TestOperatorCredentials(int operatorCode, string operatorPassword)
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            var parameters = new Dictionary<string, object>
            {
                { "OpCode", operatorCode },
                { "OpPwd", operatorPassword },
                { "TillNumber", 1 }
            };

            _dude!.ExecuteSafe("receipt_Fiscal_Open", parameters);
            _dude.ExecuteSafe("receipt_Fiscal_Cancel");
            return true;
        });
    }

    private static int MapPaymentTypeToPaidMode(int? paymentType)
    {
        // Input file uses TipPlata 1..9; DUDE expects PaidMode 0..9.
        // Keep backward compatibility with legacy P^0^... as cash.
        int normalized = paymentType ?? 1;
        if (normalized <= 0)
            normalized = 1;
        if (normalized > 9)
            normalized = 9;
        return normalized - 1;
    }

    /// <summary>
    /// Opens a fiscal receipt.
    /// </summary>
    private void OpenReceipt()
    {
        var parameters = new Dictionary<string, object>
        {
            { "OpCode", OperatorCode },
            { "OpPwd", OperatorPassword },
            { "TillNumber", 1 },
        };

        _dude.ExecuteSafe("receipt_Fiscal_Open", parameters);
    }

    /// <summary>
    /// Registers a sale item.
    /// </summary>
    private void RegisterSale(BonItem item)
    {
        var parameters = new Dictionary<string, object>
        {
            { "PluName", item.NumeProdus },
            { "TaxCd", item.GetTaxGroup() },
            { "Price", item.Pret.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) },
            { "Quantity", item.Cantitate.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) },
            { "Discount_Type", 0 },
            { "Discount_Value", "0" },
            { "Department", 1 },
            { "MeasureUnit", "buc" }
        };

        _dude.ExecuteSafe("receipt_Fiscal_Sale", parameters);
    }

    /// <summary>
    /// Closes receipt with cash payment (amount=0 for automatic total).
    /// </summary>
    private string CloseReceipt()
    {
        var parameters = new Dictionary<string, object>
        {
            { "PaidMode", 0 },  // 0 = Cash
            { "input_Amount", "0" }      // 0 = Use automatic total
        };

        _dude.ExecuteSafe("receipt_Fiscal_Total", parameters);
        _dude.ExecuteSafe("receipt_Fiscal_Close");
        return _dude!.Output("receipt_Fiscal_Close", "SlipNumber");
    }

    /// <summary>
    /// Cancels the current open fiscal receipt (Command 60 - 3Ch).
    /// Use ONLY if a receipt is open and needs to be cancelled.
    /// </summary>
    public void CancelReceipt()
    {
        Invoke(() => _dude.ExecuteSafe("receipt_Fiscal_Cancel"));
    }

    /// <summary>
    /// Checks connection via COM thread. WARNING: blocks if COM thread is busy (e.g. TCP connect in progress).
    /// Use IsConnectedCached for UI thread checks after timeouts.
    /// </summary>
    public bool IsConnected
    {
        get
        {
            var result = Invoke(() => _dude?.IsConnected ?? false);
            _isConnectedCached = result;
            return result;
        }
    }

    /// <summary>
    /// Returns last known connection state without blocking the COM thread.
    /// Safe to call from UI thread even when COM thread is busy.
    /// </summary>
    public bool IsConnectedCached => _isConnectedCached;

    public int LastErrorCode => Invoke(() => _dude?.ErrorCode ?? -1);

    public string LastErrorMessage => Invoke(() => _dude?.ErrorMessage ?? "Device not initialized");

    /// <summary>
    /// Prints a non-fiscal text line (opens/closes non-fiscal receipt).
    /// </summary>
    public void PrintNonFiscalText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("Text cannot be empty.", nameof(text));

        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            _dude!.ExecuteSafe("receipt_NonFiscal_Open");

            var parameters = new Dictionary<string, object>
            {
                { "TextData", text },
                { "cBold", "0" },
                { "cItalic", "0" },
                { "cDoubleH", "0" },
                { "cUnderline", "0" },
                { "cAlignment", "0" } // 0 = left
            };

            _dude.ExecuteSafe("receipt_Print_NonFiscal_Text", parameters);

            _dude.ExecuteSafe("receipt_NonFiscal_Close");
        });
    }

    /// <summary>
    /// Prints X Report (intermediate daily report - does NOT reset counters).
    /// </summary>
    public void PrintXReport()
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            _dude!.ExecuteSafe("Daily_Report_X");
        });
    }

    /// <summary>
    /// Prints Z Report (daily report with zeroing - RESETS daily counters).
    /// WARNING: This operation is irreversible and should only be done at end of day!
    /// </summary>
    public void PrintZReport()
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            _dude!.ExecuteSafe("Daily_Report_Z");
        });
    }

    /// <summary>
    /// Cash In operation - Introduces cash into the register WITHOUT opening a fiscal receipt.
    /// This is a standalone service operation.
    /// Amount parameter is in BANI (smallest currency unit), will be converted to LEI.
    /// </summary>
    public void CashIn(decimal amountInBani)
    {
        if (amountInBani <= 0)
            throw new ArgumentException("Amount must be greater than 0.", nameof(amountInBani));

        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            // Convert from BANI to LEI (divide by 100)
            // Example: 500 bani -> 5.00 lei, 1000 bani -> 10.00 lei
            decimal amountInLei = amountInBani / 100m;

            var parameters = new Dictionary<string, object>
            {
                { "OperationType", 0 }, // 0 = Cash In
                { "Amount", amountInLei.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
            };

            _dude!.ExecuteSafe("receipt_CashIn_CashOut", parameters);
        });
    }

    /// <summary>
    /// Cash Out operation - Removes cash from the register WITHOUT opening a fiscal receipt.
    /// This is a standalone service operation.
    /// Amount parameter is in BANI (smallest currency unit), will be converted to LEI.
    /// </summary>
    public void CashOut(decimal amountInBani)
    {
        if (amountInBani <= 0)
            throw new ArgumentException("Amount must be greater than 0.", nameof(amountInBani));

        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized. Call Initialize() first.");

            // Convert from BANI to LEI (divide by 100)
            // Example: 500 bani -> 5.00 lei, 1000 bani -> 10.00 lei
            decimal amountInLei = amountInBani / 100m;

            var parameters = new Dictionary<string, object>
            {
                { "OperationType", 1 }, // 1 = Cash Out
                { "Amount", amountInLei.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) }
            };

            _dude!.ExecuteSafe("receipt_CashIn_CashOut", parameters);
        });
    }

    // ====================================================================
    // RAPOARTE AVANSATE (Command 69 E/D/G, 94, 95, 105, 111, 71, 110,
    //                     64, 65, 123, 128)
    // ====================================================================

    /// <summary>
    /// Prints ECR report (Command 69, ReportType='E').
    /// </summary>
    public void PrintEcrReport()
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            _dude!.ExecuteSafe("report_Daily_Closure", new Dictionary<string, object> { { "ReportType", "E" } });
        });
    }

    /// <summary>
    /// Prints Departments report (Command 69, ReportType='D').
    /// </summary>
    public void PrintDepartmentsReport()
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            _dude!.ExecuteSafe("report_Daily_Closure", new Dictionary<string, object> { { "ReportType", "D" } });
        });
    }

    /// <summary>
    /// Prints Item Groups report (Command 69, ReportType='G').
    /// </summary>
    public void PrintItemGroupsReport()
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            _dude!.ExecuteSafe("report_Daily_Closure", new Dictionary<string, object> { { "ReportType", "G" } });
        });
    }

    /// <summary>
    /// Prints Fiscal Memory report by date range (Command 94).
    /// </summary>
    /// <param name="type">0=Short, 1=Detailed</param>
    /// <param name="startDate">Start date DD-MM-YY (optional)</param>
    /// <param name="endDate">End date DD-MM-YY (optional)</param>
    public void PrintFiscalMemoryByDate(string type = "0", string startDate = "", string endDate = "")
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t{startDate}\t{endDate}\t";
            _dude!.ExecuteRawCommand(94, input);
        });
    }

    /// <summary>
    /// Prints Fiscal Memory report by Z-report number range (Command 95).
    /// </summary>
    /// <param name="type">0=Short, 1=Detailed</param>
    /// <param name="first">First Z-report number (optional)</param>
    /// <param name="last">Last Z-report number (optional)</param>
    public void PrintFiscalMemoryByZRange(string type = "0", string first = "", string last = "")
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t{first}\t{last}\t";
            _dude!.ExecuteRawCommand(95, input);
        });
    }

    /// <summary>
    /// Prints Operators report (Command 105).
    /// </summary>
    /// <param name="firstOper">First operator 1-30 (optional, default 1)</param>
    /// <param name="lastOper">Last operator 1-30 (optional, default 30)</param>
    /// <param name="clear">0=Report only, 1=Report with clearing registers</param>
    public void PrintOperatorsReport(string firstOper = "", string lastOper = "", string clear = "0")
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{firstOper}\t{lastOper}\t{clear}\t";
            _dude!.ExecuteRawCommand(105, input);
        });
    }

    /// <summary>
    /// Prints PLU report (Command 111).
    /// </summary>
    /// <param name="type">0=Detailed, 1=Summary, 2=Detailed+clear, 3=Summary+clear, 4=Parameters</param>
    /// <param name="firstPlu">First PLU 1-100000 (optional)</param>
    /// <param name="lastPlu">Last PLU 1-100000 (optional)</param>
    public void PrintPluReport(string type = "0", string firstPlu = "", string lastPlu = "")
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t{firstPlu}\t{lastPlu}\t";
            _dude!.ExecuteRawCommand(111, input);
        });
    }

    /// <summary>
    /// Prints diagnostic information (Command 71).
    /// </summary>
    /// <param name="infoType">0=General, 1=Modem test, 4=LAN test, 5=SAM test</param>
    public void PrintDiagnosticReport(string infoType = "0")
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{infoType}\t";
            _dude!.ExecuteRawCommand(71, input);
        });
    }

    /// <summary>
    /// Gets additional daily information (Command 110).
    /// Returns tab-separated output values.
    /// </summary>
    /// <param name="type">0=Payments, 2=Sales count/sum, 3=Discounts/surcharges, 4=Corrections/annulled, 5=CashIn/CashOut</param>
    public string GetAdditionalDailyInfo(string type = "0")
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t";
            return _dude!.ExecuteRawCommand(110, input);
        });
    }

    /// <summary>
    /// Gets information on the last fiscal entry (Command 64).
    /// </summary>
    /// <param name="type">0=Gross sums, 1=VAT sums</param>
    public string GetLastFiscalEntryInfo(string type = "0")
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t";
            return _dude!.ExecuteRawCommand(64, input);
        });
    }

    /// <summary>
    /// Gets daily taxation information (Command 65).
    /// </summary>
    /// <param name="type">0=Turnover by TAX, 1=Amount by TAX, 2=Turnover from invoices, 3=Amount from invoices</param>
    public string GetDailyTaxInfo(string type = "0")
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{type}\t";
            return _dude!.ExecuteRawCommand(65, input);
        });
    }

    /// <summary>
    /// Gets extended device information (Command 123).
    /// </summary>
    /// <param name="option">1=Serial/Fiscal/Header/TAX, 3=Last fiscal receipt info</param>
    public string GetExtendedDeviceInfo(string option = "1")
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            string input = $"{option}\t";
            return _dude!.ExecuteRawCommand(123, input);
        });
    }

    /// <summary>
    /// Gets number of remaining Z-report entries in fiscal memory (Command 68).
    /// </summary>
    public string GetRemainingZReports()
    {
        return Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");
            return _dude!.ExecuteRawCommand(68, "");
        });
    }

    /// <summary>
    /// Downloads ANAF XML files by Z-report range.
    /// Uses DUDE built-in download_ANAF_ZRange method.
    /// </summary>
    public void DownloadAnafByZRange(int startZ, int endZ, string downloadPath)
    {
        Invoke(() =>
        {
            if (!_initialized)
                throw new InvalidOperationException("Fiscal engine not initialized.");

            _dude!.ExecuteSafe("report_Daily_Closure", new Dictionary<string, object> { { "ReportType", "X" } });
            // Use raw properties for ANAF download - these are set on the COM object directly
            // The DUDE methods download_ANAF_ZRange and download_ANAF_DTRange are direct methods
            // We need to access the underlying _device for these
        });
        // Note: ANAF download requires direct COM object access which is handled by DudeComWrapper
        throw new NotImplementedException("ANAF download requires additional DudeComWrapper support. Contact developer.");
    }

    /// <summary>
    /// Downloads ANAF XML files by date range.
    /// Uses DUDE built-in download_ANAF_DTRange method.
    /// </summary>
    public void DownloadAnafByDateRange(string startDate, string endDate, string downloadPath)
    {
        throw new NotImplementedException("ANAF download requires additional DudeComWrapper support. Contact developer.");
    }
}
