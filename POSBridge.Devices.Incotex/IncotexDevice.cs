using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Models;
using POSBridge.Abstractions.Exceptions;
using System.IO.Ports;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// Incotex Succes M7 fiscal device implementation.
/// Protocol: Drivere/Incotex/Protocol Comunicatie Succes M7.pdf
/// </summary>
public class IncotexDevice : IFiscalDevice
{
    private readonly IncotexSerialWrapper _serial;
    private readonly IncotexUsbWrapper _usb;
    private ConnectionSettings? _connectionSettings;
    private bool _disposed;
    private byte _seq = 0x20;
    private readonly object _seqLock = new();

    public IncotexDevice()
    {
        _serial = new IncotexSerialWrapper();
        _usb = new IncotexUsbWrapper();
        Capabilities = CreateIncotexCapabilities();
    }

    public string VendorName => "Incotex";
    public string ModelName { get; private set; } = "Incotex Succes M7";
    public DeviceCapabilities Capabilities { get; }

    private byte NextSeq()
    {
        lock (_seqLock)
        {
            byte s = _seq;
            _seq = (byte)(_seq >= 0x7F ? 0x20 : _seq + 1);
            return s;
        }
    }

    /// <summary>
    /// Execute command, parse response, check status. Returns (statusBytes, dataBytes).
    /// </summary>
    private (byte[] Status, byte[] Data) ExecuteEx(byte[] packet, int expectedLen = 128)
    {
        byte cmdByte = packet.Length >= 4 ? packet[3] : (byte)0;
        string dataPreview = packet.Length > 4
            ? System.Text.Encoding.ASCII.GetString(packet, 4, Math.Min(packet.Length - 9, 60)).Replace('\t', '→')
            : "";
        System.Diagnostics.Debug.WriteLine($"[Incotex TX] CMD {cmdByte} (0x{cmdByte:X2}) data=\"{dataPreview}\"");

        var raw = Execute(packet, expectedLen);
        var (status, data) = IncotexProtocol.ParseResponse(raw);

        if (IncotexProtocol.IsStatusError(status))
        {
            string msg = IncotexProtocol.GetStatusErrorMessage(status);
            var flags = IncotexProtocol.GetStatusFlags(status);
            string flagStr = flags.Count > 0 ? string.Join(", ", flags) : "none";
            string fullMsg = $"CMD {cmdByte} (0x{cmdByte:X2}): {(string.IsNullOrEmpty(msg) ? "Incotex command failed" : msg)} [flags: {flagStr}] data=\"{dataPreview}\"";
            System.Diagnostics.Debug.WriteLine($"[Incotex ERR] {fullMsg}");
            throw new FiscalDeviceException(fullMsg);
        }
        return (status, data);
    }

    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        return await Task.Run(() =>
        {
            try
            {
                _connectionSettings = settings;
                var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 1, 120));
                string port = (settings.Port ?? "").Trim();
                bool wantsUsb = port.Equals("USB", StringComparison.OrdinalIgnoreCase);
                bool wantsAuto = string.IsNullOrWhiteSpace(port) || port.Equals("AUTO", StringComparison.OrdinalIgnoreCase);
                bool explicitCom = port.StartsWith("COM", StringComparison.OrdinalIgnoreCase);

                // ── Fast path: USB Direct (no WMI, no serial scan) ─────
                if (wantsUsb)
                {
                    System.Diagnostics.Debug.WriteLine("[Incotex] USB Direct mode -- connecting via WinUSB...");
                    return ConnectViaWinUsb();
                }

                // ── Explicit COM port ──────────────────────────────────
                if (explicitCom)
                {
                    int baud = settings.BaudRate > 0 ? settings.BaudRate : 115200;
                    System.Diagnostics.Debug.WriteLine($"[Incotex] Explicit COM: {port} @ {baud}");
                    if (TryConnectSerialAndVerify(port, baud, timeout, out string fwExplicit))
                    {
                        ModelName = $"Incotex Succes M7 (FW: {fwExplicit})";
                        return _serial.IsConnected;
                    }
                    throw new FiscalDeviceException(
                        $"Conexiune seriala esuata pe {port} @ {baud}.\n" +
                        "Dispozitivul Incotex nu raspunde pe acest port COM.");
                }

                // ── AUTO mode: try COM by VID/PID, then WinUSB ────────
                if (wantsAuto)
                {
                    string? comPort = IncotexComPortFinder.TryFindIncotexComPort();
                    if (!string.IsNullOrWhiteSpace(comPort))
                    {
                        int baud = settings.BaudRate > 0 ? settings.BaudRate : 115200;
                        System.Diagnostics.Debug.WriteLine($"[Incotex] WMI found COM port: {comPort} @ {baud}");
                        if (TryConnectSerialAndVerify(comPort, baud, timeout, out string fwSerial))
                        {
                            _connectionSettings.Port = comPort;
                            _connectionSettings.BaudRate = baud;
                            ModelName = $"Incotex Succes M7 (FW: {fwSerial})";
                            return _serial.IsConnected;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine("[Incotex] AUTO: no serial success, trying WinUSB...");
                    return ConnectViaWinUsb();
                }

                throw new FiscalDeviceException("Configuratie port invalida: " + port);
            }
            catch (Exception ex) when (ex is not FiscalDeviceException)
            {
                throw new FiscalDeviceException($"Eroare conectare Incotex: {ex.Message}", ex);
            }
        });
    }

    private bool ConnectViaWinUsb()
    {
        _usb.Connect();

        // Verify the device responds. Parse firmware even if the device reports
        // a status error (e.g. AMEF 128) -- those are device-state issues,
        // not connection failures.
        byte usbSeq = NextSeq();
        var usbCmd = IncotexProtocol.BuildDeviceInfo(usbSeq, true);
        var raw = Execute(usbCmd, 128);
        var (status, data) = IncotexProtocol.ParseResponse(raw);
        var (usbFw, _) = IncotexProtocol.ParseDeviceInfoFull(data);
        ModelName = $"Incotex Succes M7 (FW: {usbFw})";

        if (IncotexProtocol.IsStatusError(status))
        {
            string statusMsg = IncotexProtocol.GetStatusErrorMessage(status);
            System.Diagnostics.Debug.WriteLine($"[Incotex] Connected via WinUSB: {ModelName} (device warning: {statusMsg})");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] Connected via WinUSB: {ModelName}");
        }

        return _usb.IsConnected;
    }

    private bool TryConnectSerialAuto(ConnectionSettings settings, TimeSpan timeout, out string foundPort, out int foundBaud, out string fwVersion)
    {
        foundPort = string.Empty;
        foundBaud = 0;
        fwVersion = "Unknown";

        var candidatePorts = new List<string>();
        string? preferredPort = IncotexComPortFinder.TryFindIncotexComPort();
        if (!string.IsNullOrWhiteSpace(preferredPort))
            candidatePorts.Add(preferredPort);

        try
        {
            foreach (string p in SerialPort.GetPortNames().OrderBy(p => p))
            {
                if (!candidatePorts.Contains(p, StringComparer.OrdinalIgnoreCase))
                    candidatePorts.Add(p);
            }
        }
        catch
        {
            // ignore port enumeration errors
        }

        if (candidatePorts.Count == 0)
            return false;

        var baudCandidates = new List<int>();
        int configuredBaud = settings.BaudRate > 0 ? settings.BaudRate : 115200;
        baudCandidates.Add(configuredBaud);
        foreach (int b in new[] { 115200, 57600, 38400, 19200, 9600 })
        {
            if (!baudCandidates.Contains(b))
                baudCandidates.Add(b);
        }

        // Keep probing fast; long timeout per combination causes very slow auto-connect.
        TimeSpan probeTimeout = TimeSpan.FromSeconds(Math.Min(3, Math.Max(1, settings.TimeoutSeconds)));

        foreach (string comPort in candidatePorts)
        {
            foreach (int baud in baudCandidates)
            {
                if (TryConnectSerialAndVerify(comPort, baud, probeTimeout, out string fw))
                {
                    foundPort = comPort;
                    foundBaud = baud;
                    fwVersion = fw;
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryConnectSerialAndVerify(string comPort, int baud, TimeSpan timeout, out string fwVersion)
    {
        fwVersion = "Unknown";
        try
        {
            _serial.Connect(comPort, baud, timeout);
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildDeviceInfo(seq, true);
            // Use raw Execute (not ExecuteEx) - connection succeeds even if AMEF has state warnings
            // (e.g. wrong password lock, 24h Z needed). Errors are shown during actual operations.
            var raw = Execute(cmd, 128);
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            var (fw, _) = IncotexProtocol.ParseDeviceInfoFull(data);
            fwVersion = fw;
            if (IncotexProtocol.IsStatusError(status))
            {
                string warn = IncotexProtocol.GetStatusErrorMessage(status);
                System.Diagnostics.Debug.WriteLine($"[Incotex] Serial {comPort}: connected with AMEF warning: {warn}");
            }
            return true;
        }
        catch
        {
            try { _serial.Disconnect(); } catch { }
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        await Task.Run(() =>
        {
            _serial.Disconnect();
            _usb.Disconnect();
            _connectionSettings = null;
        });
    }

    public bool IsConnected => _serial.IsConnected || _usb.IsConnected;

    private byte[] Execute(byte[] command, int expectedLen)
    {
        var settings = _connectionSettings;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings?.TimeoutSeconds ?? 10, 1, 120));
        if (_serial.IsConnected)
            return _serial.ExecuteCommand(command, Math.Max(expectedLen, 128), timeout);
        if (_usb.IsConnected)
            return _usb.ExecuteCommand(command, Math.Max(expectedLen, 128));
        throw new FiscalDeviceException("Device not connected");
    }

    // ==================== RECEIPT OPERATIONS ====================

    /// <summary>
    /// CMD 57: Trimite informațiile clientului (CUI) înainte de deschiderea bonului fiscal cu Invoice.
    /// Trebuie apelat ÎNAINTE de OpenReceiptAsync cu invoice=true.
    /// </summary>
    public async Task PrintClientInfoAsync(string clientName, string cui, string address = "")
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildClientInfo(seq, clientName, cui, address);
            ExecuteEx(cmd);
        });
    }

    /// <summary>
    /// Deschide bon fiscal cu informații client (Invoice/Factură).
    /// Apelează automat CMD 57 urmat de CMD 48 cu opțiunea I.
    /// </summary>
    public async Task<ReceiptResult> OpenReceiptInvoiceAsync(int operatorCode, string password, string clientName, string cui, string address = "")
    {
        return await Task.Run(() =>
        {
            byte seq1 = NextSeq();
            var cmd1 = IncotexProtocol.BuildClientInfo(seq1, clientName, cui, address);
            ExecuteEx(cmd1);
            
            byte seq2 = NextSeq();
            var cmd2 = IncotexProtocol.BuildOpenReceiptInvoice(seq2, operatorCode, password ?? "0000", 1);
            var (_, data) = ExecuteEx(cmd2);
            var (all, fisc) = IncotexProtocol.ParseOpenReceiptResponse(data);
            return new ReceiptResult { Success = true };
        });
    }

    public async Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password)
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildOpenReceipt(seq, operatorCode, password ?? "0000", 1);
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"CMD48 full response hex: {IncotexProtocol.ToHexString(raw)}");
            var (status48, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status48))
            {
                string errMsg = IncotexProtocol.GetStatusErrorMessage(status48);
                LogPaymentDiagnostic($"CMD48 EROARE: {errMsg} status=[{IncotexProtocol.ToHexString(status48)}]");
                throw new FiscalDeviceException(string.IsNullOrEmpty(errMsg) ? "CMD48: Deschidere bon eșuată" : errMsg);
            }
            string openRaw = data != null && data.Length > 0 ? System.Text.Encoding.ASCII.GetString(data) : "";
            LogPaymentDiagnostic($"CMD48 data: \"{openRaw}\"");
            var (all, fisc) = IncotexProtocol.ParseOpenReceiptResponse(data);
            return new ReceiptResult { Success = true };
        });
    }

    /// <summary>
    /// Vânzare cu discount/adaos embedded în CMD 49.
    /// Pentru Incotex, discount-ul per articol TREBUIE inclus în CMD 49, nu separat.
    /// Percent negativ = reducere, pozitiv = adaos.
    /// </summary>
    public async Task<SaleResult> AddSaleWithDiscountAsync(string name, decimal price, decimal quantity, int vatGroup, int department = 1, string? unit = null, decimal? percentDiscount = null, decimal? absDiscount = null)
    {
        return await Task.Run(() =>
        {
            char vat = IncotexProtocol.VatGroupToChar(vatGroup);
            string unitStr = string.IsNullOrWhiteSpace(unit) ? "buc" : unit;
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildAddSale(seq, name ?? "", price, quantity, vat, unitStr, percentDiscount, absDiscount);
            string dataPreview = cmd.Length > 9 ? System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9) : "";
            LogPaymentDiagnostic($"Sale TX: \"{dataPreview.Replace("\t", "→")}\" (price={price}, qty={quantity}, vat={vat} dp={percentDiscount} dv={absDiscount})");
            LogPaymentDiagnostic($"Sale TX hex: {IncotexProtocol.ToHexString(cmd)}");
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"Sale RX hex: {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status))
            {
                string errMsg = IncotexProtocol.GetStatusErrorMessage(status);
                var flags = IncotexProtocol.GetStatusFlags(status);
                string flagStr = flags.Count > 0 ? string.Join(", ", flags) : "none";
                LogPaymentDiagnostic($"Sale ERR: {errMsg} [flags: {flagStr}] status={IncotexProtocol.ToHexString(status)}");
                throw new FiscalDeviceException($"CMD 49 (0x31): {(string.IsNullOrEmpty(errMsg) ? "Eroare vânzare" : errMsg)} [flags: {flagStr}] data=\"{dataPreview.Replace("\t", "→")}\"");
            }
            return new SaleResult { Success = true };
        });
    }

    public async Task<SaleResult> AddSaleAsync(string name, decimal price, decimal quantity, int vatGroup, int department = 1)
    {
        return await Task.Run(() =>
        {
            char vat = IncotexProtocol.VatGroupToChar(vatGroup);
            string unit = "buc";
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildAddSale(seq, name ?? "", price, quantity, vat, unit);
            string dataPreview = cmd.Length > 9 ? System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9) : "";
            LogPaymentDiagnostic($"Sale TX: \"{dataPreview.Replace("\t", "→")}\" (price={price}, qty={quantity}, vat={vat})");
            LogPaymentDiagnostic($"Sale TX hex: {IncotexProtocol.ToHexString(cmd)}");
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"Sale RX hex: {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status))
            {
                string msg = IncotexProtocol.GetStatusErrorMessage(status);
                var flags = IncotexProtocol.GetStatusFlags(status);
                string flagStr = flags.Count > 0 ? string.Join(", ", flags) : "none";
                LogPaymentDiagnostic($"Sale ERR: {msg} [flags: {flagStr}] status={IncotexProtocol.ToHexString(status)}");
                throw new FiscalDeviceException($"CMD 49 (0x31): {(string.IsNullOrEmpty(msg) ? "Eroare vânzare" : msg)} [flags: {flagStr}] data=\"{dataPreview.Replace("\t", "→")}\"");
            }
            return new SaleResult { Success = true };
        });
    }

    public async Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true)
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildSubtotal(seq, print, 0);
            var (_, data) = ExecuteEx(cmd);
            string rawStr = data != null && data.Length > 0 ? System.Text.Encoding.ASCII.GetString(data) : "";
            string hexStr = data != null && data.Length > 0 ? IncotexProtocol.ToHexString(data) : "empty";
            var (amount, _) = IncotexProtocol.ParseSubtotalResponse(data);
            LogPaymentDiagnostic($"Subtotal RAW: \"{rawStr}\" hex=[{hexStr}] -> parsed={amount:F2}");
            return new SubtotalResult { Success = true, Amount = amount };
        });
    }

    public async Task<DiscountResult> AddDiscountAsync(decimal valueOrPercent, bool isPercent)
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            // Protocol: negative = discount, positive = markup. Pass as-is.
            var cmd = isPercent
                ? IncotexProtocol.BuildSubtotal(seq, true, 0, percent: valueOrPercent)
                : IncotexProtocol.BuildSubtotal(seq, true, 0, abs: valueOrPercent);
            ExecuteEx(cmd);
            return new DiscountResult { Success = true };
        });
    }

    /// <summary>
    /// Plată cu sumă explicită (bon cu rest). Trimite suma achitată de client la AMEF.
    /// AMEF calculează restul dacă suma > total bon și returnează PCode='R' + Amount=rest.
    /// </summary>
    public async Task<PaymentResult> AddExplicitPaymentAsync(PaymentType type, decimal amount)
    {
        return await Task.Run(() =>
        {
            char pChar = IncotexProtocol.PaymentTypeToChar(type);
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPayment(seq, pChar, amount, explicitAmount: true);
            string dataSent = cmd.Length > 9 ? System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9).Replace("\t", "→") : "";
            LogPaymentDiagnostic($"Payment EXPLICIT TX: \"{dataSent}\" type={pChar} amount={amount:F2}");
            LogPaymentDiagnostic($"Payment EXPLICIT TX hex: {IncotexProtocol.ToHexString(cmd)}");
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"Payment EXPLICIT RX hex: {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status))
            {
                string msg = IncotexProtocol.GetStatusErrorMessage(status);
                var flags = IncotexProtocol.GetStatusFlags(status);
                string flagStr = flags.Count > 0 ? string.Join(", ", flags) : "none";
                LogPaymentDiagnostic($"Payment EXPLICIT ERR: {msg} [flags: {flagStr}] status={IncotexProtocol.ToHexString(status)}");
                throw new FiscalDeviceException($"CMD 53 explicit: {(string.IsNullOrEmpty(msg) ? "Eroare plată" : msg)} [flags: {flagStr}] data=\"{dataSent}\"");
            }
            var (pCode, amount1) = IncotexProtocol.ParsePaymentResponse(data);
            if (pCode == 'F')
            {
                string dataStr = data != null && data.Length > 0 ? System.Text.Encoding.ASCII.GetString(data) : "";
                LogPaymentDiagnostic($"Payment EXPLICIT F: response=\"{dataStr}\" hex={IncotexProtocol.ToHexString(data)}");
                throw new FiscalDeviceException($"Plată respinsă (PCode=F). Răspuns: \"{dataStr}\".");
            }
            // D + amount1 > 0 = plată parțială acceptată, amount1 = sumă rămasă de plată
            // D + amount1 = 0 = plată completă exactă
            // R + amount1 > 0 = plată completă cu rest (bon cu rest)
            decimal change = pCode == 'R' ? amount1 : 0;
            decimal remaining = (pCode == 'D' && amount1 > 0.001m) ? amount1 : 0;
            LogPaymentDiagnostic($"Payment EXPLICIT OK: pCode={pCode} change={change:F2} remaining={remaining:F2}");
            return new PaymentResult { Success = true, Change = change, Remaining = remaining };
        });
    }

    public async Task<PaymentResult> AddPaymentAsync(PaymentType type, decimal amount)
    {
        return await Task.Run(() =>
        {
            char pChar = IncotexProtocol.PaymentTypeToChar(type);
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPayment(seq, pChar, amount);
            string dataSent = cmd.Length > 9 ? System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9).Replace("\t", "→") : "";
            LogPaymentDiagnostic($"Payment TX: \"{dataSent}\" type={pChar} amount={amount:F2}");
            LogPaymentDiagnostic($"Payment TX hex: {IncotexProtocol.ToHexString(cmd)}");
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"Payment RX hex: {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status))
            {
                string msg = IncotexProtocol.GetStatusErrorMessage(status);
                var flags = IncotexProtocol.GetStatusFlags(status);
                string flagStr = flags.Count > 0 ? string.Join(", ", flags) : "none";
                LogPaymentDiagnostic($"Payment ERR: {msg} [flags: {flagStr}] status={IncotexProtocol.ToHexString(status)}");
                throw new FiscalDeviceException($"CMD 53 (0x35): {(string.IsNullOrEmpty(msg) ? "Eroare plată" : msg)} [flags: {flagStr}] data=\"{dataSent}\"");
            }
            var (pCode, amount1) = IncotexProtocol.ParsePaymentResponse(data);
            if (pCode == 'F')
            {
                string dataStr = data != null && data.Length > 0 ? System.Text.Encoding.ASCII.GetString(data) : "";
                string hex = data != null && data.Length > 0 ? IncotexProtocol.ToHexString(data) : "";
                LogPaymentError($"Plata F: amount={amount:F2} pChar={pChar} dataSent=\"{dataSent}\" response=\"{dataStr}\" hex={hex}");
                throw new FiscalDeviceException($"Plată respinsă (PCode=F). Răspuns: \"{dataStr}\". Verificați suma ({amount:F2}) și bonul deschis.");
            }
            // PCode='D' with amount1=0.00 means full payment accepted (0 remaining).
            // PCode='D' with amount1>0 means partial payment — remaining balance still due.
            if (pCode == 'D' && amount1 > 0.001m)
                throw new FiscalDeviceException($"Sumă insuficientă. Rămas: {amount1:F2}");
            LogPaymentDiagnostic($"Payment OK: pCode={pCode} change={amount1:F2}");
            return new PaymentResult { Success = true, Change = pCode == 'R' ? amount1 : 0 };
        });
    }

    public async Task<CloseResult> CloseReceiptAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildCloseReceipt(seq);
            var (_, data) = ExecuteEx(cmd);
            var (_, fiscReceipt) = IncotexProtocol.ParseCloseReceiptResponse(data);
            return new CloseResult { Success = true, ReceiptNumber = fiscReceipt };
        });
    }

    public async Task CancelReceiptAsync()
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildCancelReceipt(seq);
            ExecuteEx(cmd);
        });
    }

    public async Task<ReceiptInfo> ReadCurrentReceiptInfoAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReceiptInfo(seq, true);
            var raw = Execute(cmd, 128);
            LogPaymentDiagnostic($"CMD76 full response hex: {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (IncotexProtocol.IsStatusError(status))
                throw new FiscalDeviceException($"CMD 76 failed: {IncotexProtocol.GetStatusErrorMessage(status)}");
            string raw76 = data != null && data.Length > 0 ? System.Text.Encoding.ASCII.GetString(data) : "";
            LogPaymentDiagnostic($"CMD76 data extracted: \"{raw76}\"");
            var (open, items, amount, paid) = IncotexProtocol.ParseReceiptInfoResponse(data);
            return new ReceiptInfo
            {
                IsReceiptOpened = open,
                IsFiscal = true,
                SalesCount = items,
                SubtotalAmount = amount,
                PaymentInitiated = paid > 0,
                PaymentFinalized = paid >= amount && amount > 0
            };
        });
    }

    public async Task PrintLastReceiptDuplicateAsync()
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPrintDuplicate(seq, 1);
            ExecuteEx(cmd);
        });
    }

    public async Task CashInAsync(decimal amount, string description = "")
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            // OpNumber 1-10 conform protocolului. Format acceptat de AMEF M7: "Amount,OpNumber" (fără semn + pentru CashIn).
            var cmd = IncotexProtocol.BuildCash(seq, true, amount, 1, description);

            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "cash_debug.log");
            void Log(string msg) { try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { } }

            Log($"CashIn amount={amount}, data_string=\"{System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9)}\"");
            Log($"CashIn packet ({cmd.Length} bytes): {IncotexProtocol.ToHexString(cmd)}");

            var raw = Execute(cmd, 128);
            Log($"CashIn response ({raw.Length} bytes): {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            Log($"CashIn status ({status.Length} bytes): {IncotexProtocol.ToHexString(status)}");
            Log($"CashIn status flags: [{string.Join(", ", IncotexProtocol.GetStatusFlags(status))}]");
            if (data.Length > 0)
                Log($"CashIn response data ({data.Length} bytes): \"{System.Text.Encoding.ASCII.GetString(data)}\" hex={IncotexProtocol.ToHexString(data)}");

            if (IncotexProtocol.IsStatusError(status))
            {
                string msg = IncotexProtocol.GetStatusErrorMessage(status);
                Log($"CashIn ERROR: {msg}");
                throw new FiscalDeviceException(string.IsNullOrEmpty(msg) ? "CashIn failed" : msg);
            }
            Log("CashIn OK");
        });
    }

    public async Task CashOutAsync(decimal amount, string description = "")
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            // OpNumber 1-10 conform protocolului (0 = invalid → eroare sintaxă la AMEF).
            var cmd = IncotexProtocol.BuildCash(seq, false, amount, 1, description);
            
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(logDir);
            string logFile = Path.Combine(logDir, "cash_debug.log");
            void Log(string msg) { try { File.AppendAllText(logFile, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { } }
            
            Log($"CashOut amount={amount}, data_string=\"{System.Text.Encoding.ASCII.GetString(cmd, 4, cmd.Length - 9)}\"");
            Log($"CashOut packet ({cmd.Length} bytes): {IncotexProtocol.ToHexString(cmd)}");
            
            var raw = Execute(cmd, 128);
            Log($"CashOut response ({raw.Length} bytes): {IncotexProtocol.ToHexString(raw)}");
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            Log($"CashOut status ({status.Length} bytes): {IncotexProtocol.ToHexString(status)}");
            Log($"CashOut status flags: [{string.Join(", ", IncotexProtocol.GetStatusFlags(status))}]");
            if (data.Length > 0)
                Log($"CashOut response data ({data.Length} bytes): \"{System.Text.Encoding.ASCII.GetString(data)}\" hex={IncotexProtocol.ToHexString(data)}");
            
            if (IncotexProtocol.IsStatusError(status))
            {
                string msg = IncotexProtocol.GetStatusErrorMessage(status);
                Log($"CashOut ERROR: {msg}");
                throw new FiscalDeviceException(string.IsNullOrEmpty(msg) ? "CashOut failed" : msg);
            }
            Log("CashOut OK");
        });
    }

    public async Task<DailyAmounts> ReadDailyAvailableAmountsAsync()
    {
        return await Task.Run(() =>
        {
            decimal cash = 0, card = 0, credit = 0, voucher = 0;

            // CMD 173 -- all payment type totals (preferred, gives all 9 forms)
            try
            {
                byte seq = NextSeq();
                var cmd = IncotexProtocol.BuildReadAllPayments(seq);
                var (_, data) = ExecuteEx(cmd);
                var (cashVal, payments, _, _) = IncotexProtocol.ParseAllPayments(data);
                cash = cashVal;
                if (payments.Length > 0) card = payments[0];     // Pay1 = Card
                if (payments.Length > 4) credit = payments[4];   // Pay5 = Credit
                if (payments.Length > 2) voucher = payments[2];  // Pay3 = Voucher
            }
            catch
            {
                // Fallback: CMD 110 -- payment totals 1-3
                try
                {
                    byte seq2 = NextSeq();
                    var cmd2 = IncotexProtocol.BuildReadPayments(seq2);
                    var (_, data2) = ExecuteEx(cmd2);
                    var (c, p1, p2, p3, _, _) = IncotexProtocol.ParsePayments(data2);
                    cash = c;
                    card = p1;
                    voucher = p2;
                    credit = p3;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Incotex] ReadDailyAmounts fallback failed: {ex.Message}");
                }
            }

            return new DailyAmounts
            {
                Cash = cash,
                Card = card,
                Credit = credit,
                Voucher = voucher
            };
        });
    }

    public async Task PrintDailyReportAsync(string type)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildDailyReport(seq, type, true);
            ExecuteEx(cmd);
        });
    }

    /// <summary>
    /// CMD 94: Raport MF detaliat după dată. Comanda declanșează tipărirea pe AMEF.
    /// </summary>
    public async Task PrintFiscalMemoryByDateAsync(DateTime startDate, DateTime endDate)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildMfDetailByDate(seq, startDate, endDate);
            ExecuteEx(cmd);
        });
    }
    
    /// <summary>
    /// CMD 79: Raport MF sumar după dată.
    /// </summary>
    public async Task PrintFiscalMemorySummaryByDateAsync(DateTime startDate, DateTime endDate)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildMfSummaryByDate(seq, startDate, endDate);
            ExecuteEx(cmd);
        });
    }
    
    /// <summary>
    /// CMD 73: Raport MF detaliat după număr Z.
    /// </summary>
    public async Task PrintFiscalMemoryDetailByZAsync(int startZ, int endZ)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildMfDetailByZ(seq, startZ, endZ);
            ExecuteEx(cmd);
        });
    }
    
    /// <summary>
    /// CMD 95: Raport MF sumar după număr Z.
    /// </summary>
    public async Task PrintFiscalMemorySummaryByZAsync(int startZ, int endZ)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildMfSummaryByZ(seq, startZ, endZ);
            ExecuteEx(cmd);
        });
    }

    /// <summary>
    /// CMD 105: Tipărire raport detaliat operatori.
    /// </summary>
    public async Task PrintOperatorsReportAsync()
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildOperatorsReport(seq);
            ExecuteEx(cmd);
        });
    }

    /// <summary>
    /// CMD 111: Tipărire raport PLU (folosit ca substitut pentru raport departamente).
    /// RepType: 0 = raport vânzări PLU, 1 = date programate PLU.
    /// </summary>
    public async Task PrintDepartmentsReportAsync()
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPacket(seq, IncotexProtocol.CMD_PLU_REPORT,
                System.Text.Encoding.ASCII.GetBytes("0"));
            ExecuteEx(cmd);
        });
    }

    // ==================== PLU OPERATIONS ====================
    
    /// <summary>
    /// CMD 58: Vânzare PLU din baza de date AMEF.
    /// </summary>
    public async Task<SaleResult> AddPluSaleAsync(int pluCode, decimal? quantity = null, decimal? percentDiscount = null, decimal? price = null, bool isVoid = false)
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPluSale(seq, pluCode, quantity, percentDiscount, price, isVoid);
            ExecuteEx(cmd);
            return new SaleResult { Success = true };
        });
    }
    
    /// <summary>
    /// CMD 107 R: Citire PLU programat.
    /// </summary>
    public async Task<string> ReadPluAsync(int pluCode)
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadPlu(seq, pluCode);
            var (_, data) = ExecuteEx(cmd);
            return System.Text.Encoding.ASCII.GetString(data).Trim('\0', ' ');
        });
    }
    
    // ==================== EXTENDED REPORTS ====================
    
    /// <summary>
    /// CMD 108: Raport zilnic extins (Z + PLU).
    /// </summary>
    public async Task PrintDailyExtendedReportAsync(string type)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildDailyExtendedReport(seq, type, true);
            ExecuteEx(cmd);
        });
    }
    
    /// <summary>
    /// CMD 113: Citire număr ultimul document tipărit.
    /// </summary>
    public async Task<string> ReadLastDocumentNumberAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadLastDocNumber(seq);
            var (_, data) = ExecuteEx(cmd);
            return IncotexProtocol.ParseLastDocNumber(data);
        });
    }
    
    /// <summary>
    /// CMD 67: Citire totaluri zilnice curente.
    /// </summary>
    public async Task<(decimal TotalSales, decimal NegTotal, decimal Refund, decimal CashPaid, int FiscReceipts, int AllReceipts)> ReadDailyTotalsAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadDailyTotals(seq);
            var (_, data) = ExecuteEx(cmd);
            return IncotexProtocol.ParseDailyTotals(data);
        });
    }
    
    /// <summary>
    /// CMD 68: Citire număr rapoarte Z libere în MF.
    /// </summary>
    public async Task<(int Logical, int Physical)> ReadFreeZRecordsAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadFreeZRecords(seq);
            var (_, data) = ExecuteEx(cmd);
            return IncotexProtocol.ParseFreeZRecords(data);
        });
    }
    
    // ==================== DEVICE INFO & STATUS ====================

    public async Task<DeviceInfo> GetDeviceInfoAsync()
    {
        return await Task.Run(() =>
        {
            string fw = "N/A", serial = "";
            string fiscalSerial = "", fiscalCode = "";

            // CMD 90 - device info (tolerant of AMEF status errors)
            try
            {
                byte seq = NextSeq();
                var cmd = IncotexProtocol.BuildDeviceInfo(seq, true);
                var raw = Execute(cmd, 128);
                var (status, data) = IncotexProtocol.ParseResponse(raw);
                (fw, serial) = IncotexProtocol.ParseDeviceInfoFull(data);
                if (IncotexProtocol.IsStatusError(status))
                    System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfo CMD 90 warning: {IncotexProtocol.GetStatusErrorMessage(status)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfo CMD 90 failed: {ex.Message}");
            }

            // CMD 99 - fiscal serial & fiscal code (CUI)
            try
            {
                byte seq2 = NextSeq();
                var cmd2 = IncotexProtocol.BuildReadFiscalInfo(seq2);
                var raw2 = Execute(cmd2, 128);
                var (status2, data2) = IncotexProtocol.ParseResponse(raw2);
                (fiscalSerial, fiscalCode) = IncotexProtocol.ParseFiscalInfo(data2);
                if (IncotexProtocol.IsStatusError(status2))
                    System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfo CMD 99 warning: {IncotexProtocol.GetStatusErrorMessage(status2)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfo CMD 99 failed: {ex.Message}");
            }

            return new DeviceInfo
            {
                VendorName = VendorName,
                ModelName = ModelName,
                SerialNumber = serial,
                FirmwareVersion = fw,
                FiscalNumber = !string.IsNullOrWhiteSpace(fiscalSerial) ? fiscalSerial : serial,
                FiscalCode = fiscalCode
            };
        });
    }

    public async Task<string> GetStatusAsync()
    {
        return await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildStatus(seq, 'X');
            var (status, _) = ExecuteEx(cmd);
            return IncotexProtocol.IsStatusError(status) ? IncotexProtocol.GetStatusErrorMessage(status) : "OK";
        });
    }

    /// <summary>
    /// Preia informațiile complete despre casa de marcat Incotex pentru afișare în interfața
    /// "Informații dispozitiv". Returnează formatul Core.Models.DeviceInfo pentru compatibilitate cu UI-ul.
    /// </summary>
    public async Task<POSBridge.Core.Models.DeviceInfo> GetDeviceInfoForDisplayAsync()
    {
        var info = new POSBridge.Core.Models.DeviceInfo();

        // CMD 90 - Informații dispozitiv (Firmware, Serial)
        try
        {
            var abstrInfo = await GetDeviceInfoAsync();
            info.SerialNumber = abstrInfo.SerialNumber;
            info.FiscalNumber = !string.IsNullOrWhiteSpace(abstrInfo.FiscalNumber) ? abstrInfo.FiscalNumber : abstrInfo.SerialNumber;
            info.TAXnumber = abstrInfo.FiscalCode;
            info.DeviceName = abstrInfo.ModelName;
            info.FirmwareVersion = abstrInfo.FirmwareVersion;
            info.FirmwareDate = ""; // Nu e disponibil separat în CMD 90
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfoForDisplay CMD 90/99: {ex.Message}");
        }

        // CMD 62 - Dată și oră
        try
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadDateTime(seq);
            var raw = Execute(cmd, 64);
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (!IncotexProtocol.IsStatusError(status))
                info.DateTime = IncotexProtocol.ParseDateTime(data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfoForDisplay CMD 62: {ex.Message}");
        }

        // CMD 76 - Stare bon fiscal
        try
        {
            var receiptInfo = await ReadCurrentReceiptInfoAsync();
            info.ReceiptOpen = receiptInfo.IsReceiptOpened ? "Da" : "Nu";
            info.ReceiptNumber = receiptInfo.SalesCount.ToString();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfoForDisplay CMD 76: {ex.Message}");
        }

        // CMD 97 - Cote TVA
        try
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadVat(seq);
            var raw = Execute(cmd, 64);
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (!IncotexProtocol.IsStatusError(status))
            {
                var vat = IncotexProtocol.ParseVat(data);
                info.TaxA = vat.TaxA;
                info.TaxB = vat.TaxB;
                info.TaxC = vat.TaxC;
                info.TaxD = vat.TaxD;
                info.TaxE = vat.TaxE;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfoForDisplay CMD 97: {ex.Message}");
        }

        // CMD 70 - Numerar (citire cu +0)
        try
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildReadCash(seq);
            var raw = Execute(cmd, 64);
            var (status, data) = IncotexProtocol.ParseResponse(raw);
            if (!IncotexProtocol.IsStatusError(status))
            {
                var cash = IncotexProtocol.ParseCashInfo(data);
                info.CashSum = cash.CashSum;
                info.CashIn = cash.CashIn;
                info.CashOut = cash.CashOut;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Incotex] GetDeviceInfoForDisplay CMD 70: {ex.Message}");
        }

        return info;
    }

    public async Task DisplayTextAsync(string text1, string text2 = "")
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildDisplayLine1(seq, text1 ?? "");
            ExecuteEx(cmd);
            if (!string.IsNullOrWhiteSpace(text2))
            {
                seq = NextSeq();
                cmd = IncotexProtocol.BuildDisplayLine2(seq, text2);
                ExecuteEx(cmd);
            }
        });
    }

    /// <summary>
    /// Print text in the currently open fiscal receipt (CMD 54).
    /// </summary>
    public async Task PrintTextInFiscalReceiptAsync(string text)
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildPrintTextFiscal(seq, text ?? "");
            ExecuteEx(cmd);
        });
    }

    public async Task OpenCashDrawerAsync()
    {
        await Task.Run(() =>
        {
            byte seq = NextSeq();
            var cmd = IncotexProtocol.BuildOpenDrawer(seq, 80);
            ExecuteEx(cmd);
        });
    }

    public async Task PrintNonFiscalTextAsync(string text)
    {
        await Task.Run(() =>
        {
            var lines = (text ?? "").Split('\n', '\r');
            bool first = true;
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                string trimmed = line.Trim().Substring(0, Math.Min(38, line.Trim().Length));
                byte seq = NextSeq();
                if (first)
                {
                    var cmdOpen = IncotexProtocol.BuildOpenNonFiscal(seq);
                    ExecuteEx(cmdOpen);
                    first = false;
                }
                seq = NextSeq();
                var cmdPrint = IncotexProtocol.BuildPrintNonFiscal(seq, trimmed);
                ExecuteEx(cmdPrint);
            }
            if (!first)
            {
                byte seq = NextSeq();
                var cmdClose = IncotexProtocol.BuildCloseNonFiscal(seq);
                ExecuteEx(cmdClose);
            }
        });
    }

    private static DeviceCapabilities CreateIncotexCapabilities()
    {
        return new DeviceCapabilities
        {
            SupportsRS232 = true,
            SupportsUSB = true,
            SupportsEthernet = false,
            SupportsWiFi = false,
            SupportsReceiptInfo = true,
            SupportsSubtotalReturn = true,
            SupportsDailyAmounts = true,
            MaxItemNameLength = 38,
            MaxOperators = 10,
            MaxDepartments = 16,
            MaxPaymentTypes = 10
        };
    }

    private static void LogPaymentError(string message)
    {
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "payment_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] ERROR {message}\n");
        }
        catch { }
    }

    private static void LogPaymentDiagnostic(string message)
    {
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "payment_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _serial.Dispose();
            _usb.Dispose();
            _disposed = true;
        }
    }
}
