using System.Text;
using POSBridge.Abstractions.Enums;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// Incotex Succes M7 protocol implementation.
/// Format: &lt;01&gt;&lt;LEN&gt;&lt;SEQ&gt;&lt;CMD&gt;&lt;DATA&gt;&lt;05&gt;&lt;BCC&gt;&lt;03&gt;
/// Response: &lt;01&gt;&lt;LEN&gt;&lt;SEQ&gt;&lt;CMD&gt;&lt;DATA&gt;&lt;04&gt;&lt;STATUS(6)&gt;&lt;05&gt;&lt;BCC&gt;&lt;03&gt;
/// See: Drivere/Incotex/Protocol Comunicatie Succes M7.pdf
/// </summary>
public static class IncotexProtocol
{
    // Frame constants
    private const byte STX = 0x01;
    private const byte ETX = 0x03;
    private const byte POSTAMBLE = 0x05;
    private const byte STATUS_DELIM = 0x04;
    
    // Control bytes
    public const byte NAK = 0x15;
    public const byte SYN = 0x16;
    
    // LEN base: count(LEN,SEQ,CMD,DATA,05) + 0x20
    private const byte LEN_OFFSET = 0x20;
    
    // Command codes (Succes M7)
    public const byte CMD_OPEN_RECEIPT = 0x30;      // 48
    public const byte CMD_ADD_SALE = 0x31;         // 49
    public const byte CMD_SUBTOTAL = 0x33;         // 51
    public const byte CMD_PAYMENT = 0x35;          // 53
    public const byte CMD_CLOSE_RECEIPT = 0x38;    // 56
    public const byte CMD_CANCEL = 0x82;           // 130
    public const byte CMD_PRINT_DUPLICATE = 0x6D;  // 109
    public const byte CMD_CASH = 0x46;             // 70 - CashIn/CashOut
    public const byte CMD_FEED_PAPER = 0x2C;       // 44
    public const byte CMD_OPEN_DRAWER = 0x6A;      // 106
    public const byte CMD_DAILY_REPORT = 0x45;     // 69 - X/Z Report
    public const byte CMD_DEVICE_INFO = 0x5A;      // 90
    public const byte CMD_STATUS = 0x4A;           // 74
    public const byte CMD_RECEIPT_INFO = 0x4C;     // 76
    public const byte CMD_DISPLAY_LINE1 = 0x2F;    // 47
    public const byte CMD_DISPLAY_LINE2 = 0x23;    // 35
    public const byte CMD_OPEN_NONFISCAL = 0x26;   // 38
    public const byte CMD_PRINT_NONFISCAL = 0x2A;  // 42
    public const byte CMD_CLOSE_NONFISCAL = 0x27;  // 39
    public const byte CMD_PRINT_TEXT_FISCAL = 0x36; // 54 - text în bon fiscal
    public const byte CMD_BARCODE = 0x54;          // 84
    public const byte CMD_READ_FISCAL = 0x63;      // 99 - Citire serie fiscală și cod fiscal
    public const byte CMD_READ_DATETIME = 0x3E;    // 62 - Citire dată și oră
    public const byte CMD_READ_VAT = 0x61;         // 97 - Citire cote TVA
    public const byte CMD_READ_CASH = 0x46;        // 70 - CashIn/CashOut (folosit cu +0 pentru citire)
    
    // New command codes
    public const byte CMD_CLIENT_INFO = 0x39;         // 57 - Informații client / CUI (Invoice)
    public const byte CMD_SALE_DISPLAY = 0x34;        // 52 - Vânzare cu afișare display extern
    public const byte CMD_PLU_SALE = 0x3A;            // 58 - Vânzare PLU din baza de date AMEF
    public const byte CMD_READ_DAILY_TOTALS = 0x43;   // 67 - Totaluri zilnice curente
    public const byte CMD_READ_FREE_Z = 0x44;         // 68 - Nr. înregistrări libere MF
    public const byte CMD_MF_DETAIL_BY_Z = 0x49;      // 73 - Raport MF detaliat după nr. Z
    public const byte CMD_MF_SUMMARY_BY_DATE = 0x4F;  // 79 - Raport MF sumar după dată
    public const byte CMD_MF_DETAIL_BY_DATE = 0x5E;   // 94 - Raport MF detaliat după dată
    public const byte CMD_MF_SUMMARY_BY_Z = 0x5F;     // 95 - Raport MF sumar după nr. Z
    public const byte CMD_RECEIPT_DATA = 0x67;        // 103 - Date bon fiscal
    public const byte CMD_OPERATORS_REPORT = 0x69;    // 105 - Raport operatori
    public const byte CMD_PLU_PROGRAM = 0x6B;         // 107 - Programare/Citire PLU
    public const byte CMD_DAILY_EXTENDED = 0x6C;      // 108 - Raport zilnic extins (Z + PLU)
    public const byte CMD_READ_PAYMENTS = 0x6E;       // 110 - Totaluri pe forme plată 1-3
    public const byte CMD_PLU_REPORT = 0x6F;          // 111 - Raport PLU
    public const byte CMD_OPERATOR_SALES = 0x70;      // 112 - Vânzări pe operator
    public const byte CMD_LAST_DOC_NUMBER = 0x71;     // 113 - Nr. ultimul document tipărit
    public const byte CMD_READ_ALL_PAYMENTS = 0xAD;   // 173 - Totaluri toate formele de plată
    public const byte CMD_DEPARTMENT_PROGRAM = 0x83;  // 131 - Programare/Citire departamente
    public const byte CMD_TIP = 0x8A;                 // 138 - Înregistrare bacșiș

    private const byte TAB = 0x09;
    private const byte LF = 0x0A;
    
    /// <summary>
    /// Build packet: 01 LEN SEQ CMD DATA 05 BCC 03
    /// LEN = count(LEN,SEQ,CMD,DATA,05) + 0x20
    /// BCC = decimal sum of bytes from LEN to 05 inclusive, encoded as 4 bytes in 0x30..0x3F domain
    /// (each nibble sent as 0x30 + value, where value is 0..15).
    /// </summary>
    public static byte[] BuildPacket(byte seq, byte cmd, byte[]? data = null)
    {
        var dataBytes = data ?? Array.Empty<byte>();
        // Bytes from LEN to 05: LEN(1) + SEQ(1) + CMD(1) + DATA(n) + 05(1) = 4 + n
        int segmentLen = 4 + dataBytes.Length;
        byte len = (byte)(segmentLen + LEN_OFFSET);
        
        var packet = new List<byte> { STX, len, seq, cmd };
        packet.AddRange(dataBytes);
        packet.Add(POSTAMBLE);
        
        // BCC = sum (decimal) of bytes from LEN to 05 inclusive
        int sum = len + seq + cmd;
        foreach (byte b in dataBytes) sum += b;
        sum += POSTAMBLE;
        
        // Protocol requires each nibble encoded as 0x30..0x3F (not ASCII 'A'..'F').
        // Example for nibble 10 => 0x3A (':'), 15 => 0x3F ('?').
        int bcc = sum & 0xFFFF;
        packet.Add((byte)(0x30 + ((bcc >> 12) & 0x0F)));
        packet.Add((byte)(0x30 + ((bcc >> 8) & 0x0F)));
        packet.Add((byte)(0x30 + ((bcc >> 4) & 0x0F)));
        packet.Add((byte)(0x30 + (bcc & 0x0F)));
        packet.Add(ETX);
        
        return packet.ToArray();
    }
    
    /// <summary>
    /// Parse response: 01 LEN SEQ CMD DATA 04 STATUS 05 BCC 03
    /// Returns (statusBytes, dataBytes) or throws on invalid format
    /// </summary>
    public static (byte[] Status, byte[] Data) ParseResponse(byte[] response)
    {
        if (response == null || response.Length < 14)
            throw new InvalidOperationException("Incotex response too short");
        
        if (response[0] != STX || response[^1] != ETX)
            throw new InvalidOperationException("Incotex response invalid frame");
        
        byte len = response[1];
        int segmentLen = len - LEN_OFFSET;
        if (segmentLen < 4)
            throw new InvalidOperationException("Incotex response invalid LEN");
        
        // Find 04 (STATUS_DELIM) - marks start of 6-byte status
        int statusIdx = -1;
        for (int i = 4; i < response.Length - 12; i++)
        {
            if (response[i] == STATUS_DELIM)
            {
                statusIdx = i;
                break;
            }
        }
        
        if (statusIdx < 0)
            throw new InvalidOperationException("Incotex response missing STATUS delimiter");
        
        byte[] data = response.Skip(4).Take(statusIdx - 4).ToArray();
        byte[] status = response.Skip(statusIdx + 1).Take(6).ToArray();
        
        return (status, data);
    }
    
    /// <summary>
    /// Check status bytes for error.
    /// Byte 0: bit 0 = syntax error, bit 1 = invalid cmd, bit 5 = general error (OR of all # errors).
    /// Byte 1: bit 1 = command not allowed (#), bit 2 = RAM reset (#), bit 4 = 24h/Z needed (#).
    /// Byte 2: bit 0 = paper out (#).
    /// Byte 3: bits 0-6 = AMEF error code (always #).
    /// Bit 0.5 (General Error) captures all # errors, so checking it is sufficient + byte 3 for safety.
    /// </summary>
    public static bool IsStatusError(byte[] status)
    {
        if (status == null || status.Length < 4) return true;
        // 0x23 = bits 0 (syntax), 1 (invalid cmd), 5 (general error = OR of all # errors)
        if ((status[0] & 0x23) != 0) return true;
        if ((status[3] & 0x7F) != 0) return true;
        return false;
    }
    
    /// <summary>
    /// Get error message from status bytes.
    /// Priority: syntax/cmd errors (byte 0), then AMEF code (byte 3), then paper/state (byte 1-2).
    /// </summary>
    public static string GetStatusErrorMessage(byte[] status)
    {
        if (status == null || status.Length < 4) return "Unknown status";
        
        if ((status[0] & 0x01) != 0) return "Eroare sintaxă";
        if ((status[0] & 0x02) != 0) return "Cod comandă invalid";
        
        int code = status[3] & 0x7F;
        if (code != 0)
        {
            string? desc = GetAmeErrorDescription(code);
            return string.IsNullOrEmpty(desc) ? $"Eroare AMEF cod {code}" : $"[AMEF {code}] {desc}";
        }
        
        if (status.Length >= 2 && (status[1] & 0x20) != 0) return "AMEF BLOCAT: 3 parole greșite consecutiv! Reporniți AMEF și verificați parola operatorului în setări.";
        if (status.Length >= 2 && (status[1] & 0x02) != 0) return "Execuția comenzii nu este permisă";
        if (status.Length >= 2 && (status[1] & 0x04) != 0) return "Resetare RAM - programați data/ora";
        if (status.Length >= 2 && (status[1] & 0x10) != 0) return "24 ore de la primul bon fiscal - emiteți raportul Z";
        if (status.Length >= 3 && (status[2] & 0x01) != 0) return "Lipsă hârtie";
        if ((status[0] & 0x04) != 0) return "Ora / data nu este setată";
        
        return string.Empty;
    }

    /// <summary>
    /// Mesaje explicative pentru coduri AMEF (conform Protocol Comunicatie.txt, pag. 4-5)
    /// </summary>
    private static string? GetAmeErrorDescription(int code) => code switch
    {
        1 => "Depășire multiplicare",
        2 => "Depășire raport zilnic",
        3 => "Operație nepermisă",
        4 => "Reducerea depășește valoarea înregistrării / subtotal bon",
        5 => "Suma totală este 0",
        6 => "Depășire valoare bon",
        7 => "Depășire cantitate",
        8 => "Valoarea introdusă depășește valoarea maximă pentru prețuri (9 999 999,99)",
        9 => "Valoarea este 0",
        10 => "Preț liber este dezactivat pentru acest PLU sau departament",
        11 => "Este necesară o cantitate întreagă (fără zecimale)",
        12 => "Numărul maxim de tranzacții (250) înregistrate în bon. Bonul trebuie închis.",
        13 => "Valoarea procentuală este în afara intervalului 0,00 - 99,99",
        14 => "Operația este dezactivată; rezultatul ar fi negativ",
        16 => "Cantitatea din stoc nu este suficientă pentru această vânzare",
        18 => "Nu există nicio înregistrare care să poată fi corectată",
        21 => "Numerarul din sertar este mai mic decât suma care trebuie extrasă",
        22 => "Depășire raport operatori",
        23 => "Operatorul nu are drepturi pentru execuția acestei comenzi",
        24 => "Memoria fiscală este plină",
        25 => "Intervale ilegale privind un raport",
        26 => "Depășire raport PLU",
        32 => "Acest cod de bare este deja programat pentru un alt PLU",
        34 => "Depășire rapoarte grupe",
        36 => "Operația STORNO nu este permisă - valoarea TVA nu poate fi negativă",
        37 => "Cota TVA dezactivată",
        38 => "Raport Z obligatoriu - au trecut mai mult de 24 ore de la primul bon fiscal",
        40 => "Data efectuare verificare tehnică a expirat",
        42 => "Suma introdusă mai mare decât suma datorată (nepermis pentru forma de plată aleasă)",
        43 => "Nu există denumire programată în câmpul obligatoriu",
        73 => "Baterie descărcată",
        74 => "Raportul Z nu se poate efectua (data curentă anterioară datei ultimei înregistrări MF)",
        75 => "Înregistrarea imposibilă - limite parametru 2 (programare PLU câmpul 9)",
        76 => "Eroare ceas",
        88 => "Eroare de memorie fiscală",
        _ => null
    };
    
    // --- Command builders ---
    
    public static byte[] BuildOpenReceipt(byte seq, int opNumber, string password, int posNumber = 1)
    {
        string data = $"{opNumber},{password.Trim()},{posNumber}";
        return BuildPacket(seq, CMD_OPEN_RECEIPT, EncodeData(data));
    }
    
    /// <summary>
    /// AddSale format: [Text1][LF Text2] TAB TaxGr [Sign]Price * [Sign2]QTY [,Percent][;Abs][@MUnit]
    /// TaxGr: N,T,A,B,C,D,E,F
    /// Sign: omitted for positive price (regular sale), '-' for negative (void/correction).
    /// Succes M7 rejects '+' sign for normal sales - protocol states Sign is optional.
    /// </summary>
    public static byte[] BuildAddSale(byte seq, string name, decimal price, decimal quantity, char vatChar, string? unit = null, decimal? percentDiscount = null, decimal? absDiscount = null)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        // Positive price: no sign (AMEF rejects '+' for regular sales).
        // Negative price: '-' sign (correction/void).
        string priceStr = price < 0
            ? price.ToString("F2", ic)       // e.g. "-5.00"
            : price.ToString("F2", ic);      // e.g. "5.00"  (no '+')
        string qtyStr = quantity.ToString("0.###", ic);
        string data = $"{name.Trim().Substring(0, Math.Min(38, name.Trim().Length))}\t{vatChar}{priceStr}*{qtyStr}";
        if (percentDiscount.HasValue) data += $",{percentDiscount.Value.ToString("F2", ic)}";
        if (absDiscount.HasValue) data += $";{absDiscount.Value.ToString("F2", ic)}";
        if (!string.IsNullOrWhiteSpace(unit)) data += $"@{unit.Trim()}";
        return BuildPacket(seq, CMD_ADD_SALE, EncodeData(data));
    }

    /// <summary>
    /// CMD 52 - Vânzare cu afișare display extern. Format identic cu CMD 49.
    /// Unele modele Succes M7 înregistrează doar cu CMD 52.
    /// </summary>
    public static byte[] BuildAddSaleDisplay(byte seq, string name, decimal price, decimal quantity, char vatChar, string? unit = null)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string priceStr = price >= 0 ? $"+{price.ToString("F2", ic)}" : price.ToString("F2", ic);
        string qtyStr = quantity.ToString("0.###", ic);
        string data = $"{name.Trim().Substring(0, Math.Min(38, name.Trim().Length))}\t{vatChar}{priceStr}*{qtyStr}";
        if (!string.IsNullOrWhiteSpace(unit)) data += $"@{unit.Trim()}";
        return BuildPacket(seq, CMD_SALE_DISPLAY, EncodeData(data));
    }
    
    /// <summary>
    /// Subtotal: Print,Option [,Percent|;Abs] [@VATnum]
    /// Print: 0=no print, 1=print. Option: 0=normal, 1=Reducere Specială 1, 2=Reducere Specială 2
    /// </summary>
    public static byte[] BuildSubtotal(byte seq, bool print, int option = 0, decimal? percent = null, decimal? abs = null, int? vatNum = null)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string data = $"{(print ? 1 : 0)},{option}";
        if (percent.HasValue) data += $",{percent.Value.ToString("F2", ic)}";
        if (abs.HasValue) data += $";{abs.Value.ToString("F2", ic)}";
        if (vatNum.HasValue) data += $"@{vatNum}";
        return BuildPacket(seq, CMD_SUBTOTAL, EncodeData(data));
    }
    
    /// <summary>
    /// Payment: [Text1][LF Text2] TAB [[Payment][Sign]Amount]
    /// TAB is mandatory. [[Payment][Amount]] is optional.
    /// Two modes:
    ///   - Exact/auto: "\t" (cash) or "\tN" (other) — AMEF pays full remaining balance.
    ///   - Explicit: "\tP200.00" — AMEF applies exact amount, calculates change if overpaid.
    /// Use explicit mode when customer pays more than total (bon cu rest).
    /// Protocol: Sign is optional; only '+' is allowed (positive amounts — no sign needed).
    /// </summary>
    public static byte[] BuildPayment(byte seq, char paymentType, decimal amount, bool explicitAmount = false)
    {
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        string data;
        if (explicitAmount && amount > 0)
            // Explicit amount: TAB + PaymentChar + Amount (no space, no '+' sign)
            data = $"\t{paymentType}{amount.ToString("F2", ic)}";
        else
            // Auto-pay remaining balance
            data = paymentType == 'P' ? "\t" : $"\t{paymentType}";
        return BuildPacket(seq, CMD_PAYMENT, EncodeData(data));
    }
    
    public static byte[] BuildCloseReceipt(byte seq)
        => BuildPacket(seq, CMD_CLOSE_RECEIPT, null);
    
    public static byte[] BuildCancelReceipt(byte seq)
        => BuildPacket(seq, CMD_CANCEL, null);
    
    public static byte[] BuildPrintDuplicate(byte seq, int count = 1)
        => BuildPacket(seq, CMD_PRINT_DUPLICATE, EncodeData(count.ToString()));
    
    /// <summary>
    /// CMD 70 (46h): Introducere/Scoatere sume din sertar.
    /// Date: &lt;[Sign]Amount&gt;[,OpNumber][,Text]
    /// Amount se trimite cu 2 zecimale și punct zecimal (ex: "50.00").
    /// </summary>
    public static byte[] BuildCash(byte seq, bool isCashIn, decimal amount, int opNumber = 1, string? text = null)
    {
        // Protocol: <[Sign]Amount>[,OpNumber][,Text]
        // amount este în lei (ex: 300.00). AMEF Succes M7 acceptă format decimal F2.
        var ic = System.Globalization.CultureInfo.InvariantCulture;
        // AMEF Succes M7: CashIn = "Amount,OpNumber" (fără semn!). CashOut = "-Amount,OpNumber".
        // Semnul '+' explicit pentru CashIn cauzează "Eroare sintaxă" pe AMEF-ul M7.
        string sign = isCashIn ? "" : "-";
        string amountStr = amount.ToString("F2", ic);
        string data = $"{sign}{amountStr},{opNumber}";
        if (!string.IsNullOrWhiteSpace(text)) data += $",{text.Trim()}";
        return BuildPacket(seq, CMD_CASH, EncodeData(data));
    }
    
    public static byte[] BuildFeedPaper(byte seq, int lines = 1)
        => BuildPacket(seq, CMD_FEED_PAPER, lines >= 1 && lines <= 20 ? EncodeData(lines.ToString()) : null);
    
    public static byte[] BuildOpenDrawer(byte seq, int mSec = 80)
        => BuildPacket(seq, CMD_OPEN_DRAWER, mSec >= 5 && mSec <= 150 ? EncodeData(mSec.ToString()) : null);
    
    /// <summary>
    /// Daily report: Option 0/1=Z, 2/3=X. [N] = don't clear operator registers
    /// </summary>
    public static byte[] BuildDailyReport(byte seq, string type, bool clearOperatorRegisters = true)
    {
        string opt = type.ToUpperInvariant() == "Z" ? "0" : "2";
        if (!clearOperatorRegisters) opt += "N";
        return BuildPacket(seq, CMD_DAILY_REPORT, EncodeData(opt));
    }
    
    /// <summary>
    /// Device info: Calculate=1 to compute FW checksum
    /// </summary>
    public static byte[] BuildDeviceInfo(byte seq, bool calculateChecksum = true)
        => BuildPacket(seq, CMD_DEVICE_INFO, EncodeData(calculateChecksum ? "1" : "0"));
    
    /// <summary>
    /// Status: W=after registers printed, X=no delay
    /// </summary>
    public static byte[] BuildStatus(byte seq, char mode = 'X')
        => BuildPacket(seq, CMD_STATUS, EncodeData(mode.ToString()));
    
    /// <summary>
    /// Receipt info: Option T = include total
    /// </summary>
    public static byte[] BuildReceiptInfo(byte seq, bool includeTotal = true)
        => BuildPacket(seq, CMD_RECEIPT_INFO, includeTotal ? EncodeData("T") : null);
    
    public static byte[] BuildDisplayLine1(byte seq, string text)
        => BuildPacket(seq, CMD_DISPLAY_LINE1, EncodeData(text.Trim().Substring(0, Math.Min(38, text.Trim().Length))));
    
    public static byte[] BuildDisplayLine2(byte seq, string text)
        => BuildPacket(seq, CMD_DISPLAY_LINE2, EncodeData(text.Trim().Substring(0, Math.Min(38, text.Trim().Length))));
    
    public static byte[] BuildOpenNonFiscal(byte seq) => BuildPacket(seq, CMD_OPEN_NONFISCAL, null);
    public static byte[] BuildPrintNonFiscal(byte seq, string text)
        => BuildPacket(seq, CMD_PRINT_NONFISCAL, EncodeData(text.Trim().Substring(0, Math.Min(38, text.Trim().Length))));
    public static byte[] BuildCloseNonFiscal(byte seq) => BuildPacket(seq, CMD_CLOSE_NONFISCAL, null);
    
    /// <summary>
    /// Print text in fiscal receipt (cmd 54)
    /// </summary>
    public static byte[] BuildPrintTextFiscal(byte seq, string text)
        => BuildPacket(seq, CMD_PRINT_TEXT_FISCAL, EncodeData(text.Trim().Substring(0, Math.Min(38, text.Trim().Length))));
    
    /// <summary>
    /// Map vatGroup (1-8) to Succes M7 VAT char: N,T,A,B,C,D,E,F
    /// </summary>
    public static char VatGroupToChar(int vatGroup)
    {
        return vatGroup switch
        {
            0 or 5 => 'N',  // Scutite
            6 => 'T',       // Alte taxe
            1 => 'A',
            2 => 'B',
            3 => 'C',
            4 => 'D',
            7 => 'E',
            8 => 'F',
            _ => 'A'
        };
    }
    
    /// <summary>
    /// Map PaymentType to Succes M7 payment char
    /// </summary>
    /// <summary>
    /// Map PaymentType to Succes M7 protocol char (Protocol p.12-13).
    /// Standard protocol: P=Numerar, N=Card, C=Tichet Masa, D=Bon Valoric, B=Voucher, I=Credit.
    /// ATENȚIE: Pe AMEF-ul Succes M7 utilizat, 'N' și 'C' sunt INVERSATE față de standard:
    ///   'C' = Card (nu Tichet masă), 'N' = Tichete masă (nu Card).
    /// </summary>
    public static char PaymentTypeToChar(PaymentType type)
    {
        return type switch
        {
            PaymentType.Cash        => 'P', // Numerar
            PaymentType.Card        => 'C', // Card (pe acest AMEF 'C'=Card, nu 'N')
            PaymentType.Credit      => 'I', // Credit
            PaymentType.Voucher     => 'B', // Voucher
            PaymentType.Check       => 'D', // Bon valoric
            PaymentType.TicketMeal  => 'N', // Tichete masă (pe acest AMEF 'N'=Tichet masă, nu 'C')
            PaymentType.TicketValue => 'D', // Bon valoric
            _ => 'P'
        };
    }
    
    public static byte[] EncodeData(string data)
    {
        if (string.IsNullOrEmpty(data)) return Array.Empty<byte>();
        return Encoding.ASCII.GetBytes(data);
    }
    
    // --- Response parsers ---
    
    public static string ParseDeviceInfoResponse(byte[] data)
    {
        if (data == null || data.Length < 6) return "Unknown";
        string s = Encoding.ASCII.GetString(data).Trim('\0');
        var parts = s.Split(' ');
        return parts.Length > 0 ? parts[0] : "Unknown";
    }
    
    public static (string AllReceipts, string FiscReceipt) ParseOpenReceiptResponse(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim();
        var parts = s.Split(',');
        return (parts.Length > 0 ? parts[0].Trim() : "", parts.Length > 1 ? parts[1].Trim() : "");
    }
    
    public static (decimal SubTotal, string TaxValues) ParseSubtotalResponse(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim();
        var parts = s.Split(',');
        string first = parts.Length > 0 ? parts[0].Trim() : "";
        decimal sub = 0m;
        if (!string.IsNullOrEmpty(first))
        {
            string normalized = first.Replace(',', '.');
            decimal.TryParse(normalized, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out sub);
        }
        return (sub, s);
    }
    
    public static (char PCode, decimal Amount1) ParsePaymentResponse(byte[] data)
    {
        // AMEF response format: <PCode><Amount1> — PCode is first byte, Amount1 is the rest (no comma separator).
        // Examples: "D0.00" = done/debit remaining=0.00, "R2.50" = rest/change=2.50, "F" = fault.
        if (data == null || data.Length == 0) return ('F', 0);
        char code = (char)data[0];
        decimal amt = 0;
        if (data.Length > 1)
        {
            string amtStr = Encoding.ASCII.GetString(data, 1, data.Length - 1).Trim();
            decimal.TryParse(amtStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out amt);
        }
        return (code, amt);
    }
    
    public static (string AllReceipts, string FiscReceipt) ParseCloseReceiptResponse(byte[] data)
        => ParseOpenReceiptResponse(data);
    
    public static (bool Open, int Items, decimal Amount, decimal Paid) ParseReceiptInfoResponse(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim();
        var parts = s.Split(',');
        bool open = parts.Length > 0 && parts[0].Trim() == "1";
        int items = parts.Length > 1 && int.TryParse(parts[1].Trim(), out int i) ? i : 0;
        decimal amount = parts.Length > 2 && decimal.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal a) ? a : 0;
        decimal paid = parts.Length > 3 && decimal.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal p) ? p : 0;
        return (open, items, amount, paid);
    }
    
    /// <summary>
    /// CMD 99 (63h): Read fiscal serial and fiscal code (CUI). No data needed.
    /// </summary>
    public static byte[] BuildReadFiscalInfo(byte seq)
        => BuildPacket(seq, CMD_READ_FISCAL, null);

    /// <summary>
    /// Parse CMD 99 response. Expected: FiscalSerial,FiscalCode (comma-separated).
    /// </summary>
    public static (string FiscalSerial, string FiscalCode) ParseFiscalInfo(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        string fiscalSerial = parts.Length > 0 ? parts[0].Trim() : "";
        string fiscalCode = parts.Length > 1 ? parts[1].Trim() : "";
        return (fiscalSerial, fiscalCode);
    }

    /// <summary>
    /// CMD 62 (3Eh): Citire dată și oră. Fără date.
    /// </summary>
    public static byte[] BuildReadDateTime(byte seq)
        => BuildPacket(seq, CMD_READ_DATETIME, null);

    /// <summary>
    /// CMD 97 (61h): Citire cote TVA. Fără date.
    /// </summary>
    public static byte[] BuildReadVat(byte seq)
        => BuildPacket(seq, CMD_READ_VAT, null);

    /// <summary>
    /// CMD 70 (46h): Citire numerar - trimite +0.00 pentru a obține CashSum,ServIt,ServOut fără modificare.
    /// </summary>
    public static byte[] BuildReadCash(byte seq)
        => BuildPacket(seq, CMD_READ_CASH, EncodeData("+0.00"));

    /// <summary>
    /// Parse CMD 62 response. Format: ZZ-LL-AA HH:MM:SS
    /// </summary>
    public static string ParseDateTime(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        return string.IsNullOrWhiteSpace(s) ? "N/A" : s;
    }

    /// <summary>
    /// Parse CMD 97 response. Format: TaxA,TaxB,TaxC,TaxD,TaxE,TaxF
    /// </summary>
    public static (string TaxA, string TaxB, string TaxC, string TaxD, string TaxE, string TaxF) ParseVat(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        string Get(int i) => parts.Length > i ? parts[i].Trim() : "N/A";
        return (Get(0), Get(1), Get(2), Get(3), Get(4), Get(5));
    }

    /// <summary>
    /// Parse CMD 70 response. Format: Code,CashSum,ServIt,ServOut
    /// </summary>
    public static (string CashSum, string CashIn, string CashOut) ParseCashInfo(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        string sum = parts.Length > 1 ? parts[1].Trim() : "N/A";
        string servIt = parts.Length > 2 ? parts[2].Trim() : "N/A";
        string servOut = parts.Length > 3 ? parts[3].Trim() : "N/A";
        return (sum, servIt, servOut);
    }

    /// <summary>
    /// Parse device info from CMD 90 response.
    /// Actual device format (verified): FwRev[Space]FwDate[Space]FwTime,ChSum,Sw,Country,ECRnum
    /// Note: trailing commas are NOT trimmed so empty ECRnum is preserved as empty string.
    /// </summary>
    public static (string FirmwareVersion, string SerialNumber) ParseDeviceInfoFull(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0');
        var parts = s.Split(',');
        string fw = parts.Length > 0 ? parts[0].Split(' ')[0].Trim() : "Unknown";
        string serial = parts.Length > 4 ? parts[4].Trim() : "";
        return (fw, serial);
    }
    
    // ============================================================
    // CMD 57 (39h) - Client Info / CUI (Invoice)
    // ============================================================
    
    /// <summary>
    /// CMD 57: Informații client - trebuie trimis ÎNAINTE de CMD 48 (deschidere bon fiscal cu opțiunea I).
    /// Format: TAB TAB TAB Client TAB CUI TAB Address
    /// </summary>
    public static byte[] BuildClientInfo(byte seq, string clientName, string cui, string address = "")
    {
        string name = (clientName ?? "").Trim();
        if (name.Length > 38) name = name.Substring(0, 38);
        string addr = (address ?? "").Trim();
        if (addr.Length > 38) addr = addr.Substring(0, 38);
        string data = $"\t\t\t{name}\t{(cui ?? "").Trim()}\t{addr}";
        return BuildPacket(seq, CMD_CLIENT_INFO, EncodeData(data));
    }
    
    /// <summary>
    /// CMD 48 with Invoice option: deschide bon fiscal cu informații client (CUI).
    /// </summary>
    public static byte[] BuildOpenReceiptInvoice(byte seq, int opNumber, string password, int posNumber = 1)
    {
        string data = $"{opNumber},{password.Trim()},{posNumber},I";
        return BuildPacket(seq, CMD_OPEN_RECEIPT, EncodeData(data));
    }
    
    // ============================================================
    // CMD 67 (43h) - Totaluri zilnice curente
    // ============================================================
    
    /// <summary>
    /// CMD 67: Citire totaluri zilnice.
    /// Răspuns: Zrate,NegTotal,Refund,Paid,FiscReceipt,AllReceipt
    /// </summary>
    public static byte[] BuildReadDailyTotals(byte seq)
        => BuildPacket(seq, CMD_READ_DAILY_TOTALS, null);
    
    public static (decimal TotalSales, decimal NegTotal, decimal Refund, decimal CashPaid, int FiscReceipts, int AllReceipts) ParseDailyTotals(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        decimal Parse(int i) => parts.Length > i && decimal.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal v) ? v : 0;
        int ParseInt(int i) => parts.Length > i && int.TryParse(parts[i].Trim(), out int v) ? v : 0;
        return (Parse(0), Parse(1), Parse(2), Parse(3), ParseInt(4), ParseInt(5));
    }
    
    // ============================================================
    // CMD 110 (6Eh) - Totaluri pe forme de plată 1-3
    // ============================================================
    
    /// <summary>
    /// CMD 110: Citire totaluri pe forme de plată.
    /// Răspuns: Cash, Pay1, Pay2, Pay3, Closure, Receipt
    /// </summary>
    public static byte[] BuildReadPayments(byte seq)
        => BuildPacket(seq, CMD_READ_PAYMENTS, null);
    
    public static (decimal Cash, decimal Pay1, decimal Pay2, decimal Pay3, int Closure, int Receipt) ParsePayments(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        decimal Parse(int i) => parts.Length > i && decimal.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal v) ? v : 0;
        int ParseInt(int i) => parts.Length > i && int.TryParse(parts[i].Trim(), out int v) ? v : 0;
        return (Parse(0), Parse(1), Parse(2), Parse(3), ParseInt(4), ParseInt(5));
    }
    
    // ============================================================
    // CMD 173 (ADh) - Totaluri toate formele de plată
    // ============================================================
    
    /// <summary>
    /// CMD 173: Sume corespunzătoare fiecărei forme de plată.
    /// Răspuns: Cash,Pay1..Pay9,Closure,Receipt
    /// </summary>
    public static byte[] BuildReadAllPayments(byte seq)
        => BuildPacket(seq, CMD_READ_ALL_PAYMENTS, null);
    
    public static (decimal Cash, decimal[] Payments, int Closure, int Receipt) ParseAllPayments(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        decimal Parse(int i) => parts.Length > i && decimal.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal v) ? v : 0;
        int ParseInt(int i) => parts.Length > i && int.TryParse(parts[i].Trim(), out int v) ? v : 0;
        
        decimal cash = Parse(0);
        var payments = new decimal[9];
        for (int i = 0; i < 9; i++) payments[i] = Parse(i + 1);
        int closure = ParseInt(10);
        int receipt = ParseInt(11);
        return (cash, payments, closure, receipt);
    }
    
    // ============================================================
    // CMD 94 (5Eh) - Raport MF detaliat după dată
    // ============================================================
    
    /// <summary>
    /// CMD 94: Raport MF detaliat după dată. Format date: DDMMYY,DDMMYY
    /// </summary>
    public static byte[] BuildMfDetailByDate(byte seq, DateTime startDate, DateTime endDate)
    {
        string data = $"{startDate:ddMMyy},{endDate:ddMMyy}";
        return BuildPacket(seq, CMD_MF_DETAIL_BY_DATE, EncodeData(data));
    }
    
    // ============================================================
    // CMD 95 (5Fh) - Raport MF sumar după nr. Z
    // ============================================================
    
    /// <summary>
    /// CMD 95: Raport MF sumar după număr Z.
    /// </summary>
    public static byte[] BuildMfSummaryByZ(byte seq, int startZ, int endZ)
    {
        string data = $"{startZ},{endZ}";
        return BuildPacket(seq, CMD_MF_SUMMARY_BY_Z, EncodeData(data));
    }
    
    // ============================================================
    // CMD 73 (49h) - Raport MF detaliat după nr. Z
    // ============================================================
    
    /// <summary>
    /// CMD 73: Raport MF detaliat după număr Z.
    /// </summary>
    public static byte[] BuildMfDetailByZ(byte seq, int startZ, int endZ)
    {
        string data = $"{startZ},{endZ}";
        return BuildPacket(seq, CMD_MF_DETAIL_BY_Z, EncodeData(data));
    }
    
    // ============================================================
    // CMD 79 (4Fh) - Raport MF sumar după dată
    // ============================================================
    
    /// <summary>
    /// CMD 79: Raport MF sumar după dată.
    /// </summary>
    public static byte[] BuildMfSummaryByDate(byte seq, DateTime startDate, DateTime endDate)
    {
        string data = $"{startDate:ddMMyy},{endDate:ddMMyy}";
        return BuildPacket(seq, CMD_MF_SUMMARY_BY_DATE, EncodeData(data));
    }
    
    // ============================================================
    // CMD 105 (69h) - Raport operatori
    // ============================================================
    
    /// <summary>
    /// CMD 105: Tipărire raport operatori. Fără date.
    /// </summary>
    public static byte[] BuildOperatorsReport(byte seq)
        => BuildPacket(seq, CMD_OPERATORS_REPORT, null);
    
    // ============================================================
    // CMD 108 (6Ch) - Raport zilnic extins (Z + PLU)
    // ============================================================
    
    /// <summary>
    /// CMD 108: Raport fiscal zilnic extins (Z + PLU). Identic cu CMD 69 dar include raport PLU.
    /// </summary>
    public static byte[] BuildDailyExtendedReport(byte seq, string type, bool clearOperatorRegisters = true)
    {
        string opt = type.ToUpperInvariant() == "Z" ? "0" : "2";
        if (!clearOperatorRegisters) opt += "N";
        return BuildPacket(seq, CMD_DAILY_EXTENDED, EncodeData(opt));
    }
    
    // ============================================================
    // CMD 58 (3Ah) - Vânzare PLU din baza de date AMEF
    // ============================================================
    
    /// <summary>
    /// CMD 58: Vânzare PLU programat. Format: [Sign]PLU [*[Sign2]QTY] [,Percent] [@Price]
    /// </summary>
    public static byte[] BuildPluSale(byte seq, int pluCode, decimal? quantity = null, decimal? percentDiscount = null, decimal? price = null, bool isVoid = false)
    {
        string sign = isVoid ? "-" : "";
        string data = $"{sign}{pluCode}";
        if (quantity.HasValue)
            data += $"*{quantity.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}";
        if (percentDiscount.HasValue)
            data += $",{percentDiscount.Value:F2}";
        if (price.HasValue)
            data += $"@{price.Value:F2}";
        return BuildPacket(seq, CMD_PLU_SALE, EncodeData(data));
    }
    
    // ============================================================
    // CMD 107 (6Bh) - Programare/Citire PLU
    // ============================================================
    
    /// <summary>
    /// CMD 107 Read: Citire PLU programat.
    /// </summary>
    public static byte[] BuildReadPlu(byte seq, int pluCode)
        => BuildPacket(seq, CMD_PLU_PROGRAM, EncodeData($"R{pluCode}"));
    
    /// <summary>
    /// CMD 107 First: Cerere primul PLU programat.
    /// </summary>
    public static byte[] BuildFirstPlu(byte seq)
        => BuildPacket(seq, CMD_PLU_PROGRAM, EncodeData("F"));
    
    /// <summary>
    /// CMD 107 Next: Cerere următorul PLU programat.
    /// </summary>
    public static byte[] BuildNextPlu(byte seq)
        => BuildPacket(seq, CMD_PLU_PROGRAM, EncodeData("N"));
    
    // ============================================================
    // CMD 113 (71h) - Numărul ultimului document tipărit
    // ============================================================
    
    /// <summary>
    /// CMD 113: Citire număr ultimul document tipărit.
    /// </summary>
    public static byte[] BuildReadLastDocNumber(byte seq)
        => BuildPacket(seq, CMD_LAST_DOC_NUMBER, null);
    
    public static string ParseLastDocNumber(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        return string.IsNullOrWhiteSpace(s) ? "0" : s.Trim();
    }
    
    // ============================================================
    // CMD 112 (70h) - Vânzări pe operator
    // ============================================================
    
    /// <summary>
    /// CMD 112: Informații vânzări pe operator.
    /// </summary>
    public static byte[] BuildOperatorSales(byte seq, int opNumber)
        => BuildPacket(seq, CMD_OPERATOR_SALES, EncodeData(opNumber.ToString()));
    
    // ============================================================
    // CMD 103 (67h) - Date bon fiscal
    // ============================================================
    
    /// <summary>
    /// CMD 103: Informații bon fiscal curent. Fără date.
    /// Răspuns: CanVoid,TaxN,TaxT,TaxA,TaxB,TaxC,TaxD,TaxE,TaxF,Invoice,InvoiceNum
    /// </summary>
    public static byte[] BuildReceiptData(byte seq)
        => BuildPacket(seq, CMD_RECEIPT_DATA, null);
    
    // ============================================================
    // CMD 68 (44h) - Nr. înregistrări libere MF
    // ============================================================
    
    /// <summary>
    /// CMD 68: Citire număr rapoarte Z libere în MF.
    /// Răspuns: Logical, Physical
    /// </summary>
    public static byte[] BuildReadFreeZRecords(byte seq)
        => BuildPacket(seq, CMD_READ_FREE_Z, null);
    
    public static (int Logical, int Physical) ParseFreeZRecords(byte[] data)
    {
        string s = Encoding.ASCII.GetString(data ?? Array.Empty<byte>()).Trim('\0', ' ');
        var parts = s.Split(',');
        int ParseInt(int i) => parts.Length > i && int.TryParse(parts[i].Trim(), out int v) ? v : 0;
        return (ParseInt(0), ParseInt(1));
    }
    
    /// <summary>
    /// Detailed status interpretation for diagnostics. Returns all active flags.
    /// </summary>
    public static List<string> GetStatusFlags(byte[] status)
    {
        var flags = new List<string>();
        if (status == null || status.Length < 6) return flags;
        
        // Byte 0
        if ((status[0] & 0x01) != 0) flags.Add("Eroare sintaxă");
        if ((status[0] & 0x02) != 0) flags.Add("Cod comandă invalid");
        if ((status[0] & 0x04) != 0) flags.Add("Ora/data nesetată");
        if ((status[0] & 0x20) != 0) flags.Add("Eroare generală");
        
        // Byte 1
        if ((status[1] & 0x02) != 0) flags.Add("Execuție nepermisă");
        if ((status[1] & 0x04) != 0) flags.Add("Reset RAM");
        if ((status[1] & 0x10) != 0) flags.Add("24h de la primul bon");
        if ((status[1] & 0x20) != 0) flags.Add("3 parole greșite consecutive");
        if ((status[1] & 0x40) != 0) flags.Add("Afișaj extern neconectat");
        
        // Byte 2
        if ((status[2] & 0x01) != 0) flags.Add("Lipsă hârtie");
        if ((status[2] & 0x02) != 0) flags.Add("Așteptare confirmare schimbare hârtie");
        if ((status[2] & 0x08) != 0) flags.Add("Bon fiscal deschis");
        if ((status[2] & 0x20) != 0) flags.Add("Bon nefiscal deschis");
        
        // Byte 3 - AMEF error code
        int amefCode = status[3] & 0x7F;
        if (amefCode != 0)
        {
            string? desc = GetAmeErrorDescription(amefCode);
            flags.Add(desc ?? $"Eroare AMEF cod {amefCode}");
        }
        
        // Byte 4 - Fiscal memory
        if ((status[4] & 0x01) != 0) flags.Add("Eroare înregistrare MF");
        if ((status[4] & 0x04) != 0) flags.Add("Eroare MF");
        if ((status[4] & 0x08) != 0) flags.Add("Sub 60 înregistrări libere MF");
        if ((status[4] & 0x10) != 0) flags.Add("MF plină");
        if ((status[4] & 0x20) != 0) flags.Add("Eroare fatală MF");
        
        // Byte 5 - Fiscal memory info
        if ((status[5] & 0x01) != 0) flags.Add("MF readonly");
        if ((status[5] & 0x08) != 0) flags.Add("ECR fiscalizată");
        if ((status[5] & 0x10) != 0) flags.Add("Cel puțin o cotă TVA programată");
        if ((status[5] & 0x20) != 0) flags.Add("Serie fiscală înregistrată");
        
        return flags;
    }
    
    /// <summary>
    /// Check if a receipt (fiscal or non-fiscal) is currently open based on status bytes.
    /// </summary>
    public static bool IsReceiptOpen(byte[] status)
    {
        if (status == null || status.Length < 3) return false;
        return (status[2] & 0x08) != 0 || (status[2] & 0x20) != 0;
    }
    
    /// <summary>
    /// Check if paper is out based on status bytes.
    /// </summary>
    public static bool IsPaperOut(byte[] status)
    {
        if (status == null || status.Length < 3) return false;
        return (status[2] & 0x01) != 0;
    }
    
    public static string ToHexString(byte[]? data)
    {
        if (data == null || data.Length == 0) return "";
        return BitConverter.ToString(data).Replace("-", " ");
    }
}
