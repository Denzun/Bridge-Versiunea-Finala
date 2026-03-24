using System.Text;
using POSBridge.Abstractions.Enums;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// SmartPay ECR Link Protocol v1.8 implementation.
/// TLV (Tag-Length-Value) protocol for Ingenico terminals.
/// </summary>
public static class SmartPayProtocol
{
    // Frame constants
    public const byte STX = 0x02;
    public const byte ETX = 0x03;
    public const byte ACK = 0x06;
    public const byte NAK = 0x15;
    public const byte ENQ = 0x05;

    // Request Command Tags (0xA000)
    public const ushort TAG_COMMAND = 0xA000;
    public const ushort TAG_AMOUNT = 0xA001;
    public const ushort TAG_CURRENCY_NAME = 0xA002;
    public const ushort TAG_CURRENCY_CODE = 0xA003;
    public const ushort TAG_TRANSACTION_INDEX = 0xA004;
    public const ushort TAG_RECEIPT_INFO = 0xA005;
    public const ushort TAG_STAN = 0xA006;
    public const ushort TAG_CASHBACK_AMOUNT = 0xA007;
    public const ushort TAG_UNIQUE_ID = 0xA008;
    public const ushort TAG_COMMAND_FLAGS = 0xA009;
    public const ushort TAG_TRANSACTION_CODE = 0xA012;

    // Response Tags (0xA100)
    public const ushort TAG_RESPONSE = 0xA100;
    public const ushort TAG_SOFTWARE_VERSION = 0xA101;
    public const ushort TAG_HARDWARE_VERSION = 0xA102;
    public const ushort TAG_TERMINAL_ID = 0xA103;
    public const ushort TAG_MERCHANT_ID = 0xA104;
    public const ushort TAG_PAYMENT_DATE = 0xA105;
    public const ushort TAG_APPROVED_AMOUNT = 0xA106;
    public const ushort TAG_RESPONSE_CODE = 0xA107;
    public const ushort TAG_RESPONSE_TEXT = 0xA108;
    public const ushort TAG_RECEIPT_STAN = 0xA109;
    public const ushort TAG_RRN = 0xA10A;
    public const ushort TAG_AUTH_CODE = 0xA10B;
    public const ushort TAG_PAN = 0xA10C;
    public const ushort TAG_CARDHOLDER_NAME = 0xA10D;
    public const ushort TAG_TRANSACTION_COUNT = 0xA10E;
    public const ushort TAG_BATCH_NUMBER = 0xA10F;
    public const ushort TAG_TOTAL_AMOUNT = 0xA110;
    public const ushort TAG_PRINTER_STATUS = 0xA111;
    public const ushort TAG_TRANSACTION_TYPE = 0xA112;
    public const ushort TAG_EMV_APP_LABEL = 0xA113;
    public const ushort TAG_EMV_APP_ID = 0xA114;
    public const ushort TAG_CASHBACK_LIMIT = 0xA115;
    public const ushort TAG_TRANSACTION_FLAGS = 0xA116;
    public const ushort TAG_UNIQUE_ID_RESPONSE = 0xA117;
    public const ushort TAG_OFFLINE_AMOUNT = 0xA119;
    public const ushort TAG_CARD_FLEET_DATA = 0xA11A;
    public const ushort TAG_CURRENCY_NAME_RESPONSE = 0xA11B;
    public const ushort TAG_CURRENCY_CODE_RESPONSE = 0xA11C;
    public const ushort TAG_TRANSACTION_CODE_RESPONSE = 0xA128;
    public const ushort TAG_CARD_TOKEN = 0xA150;

    // Command Codes (Tag 0xA000 values)
    public enum CommandCode : byte
    {
        GetInfo = 0x01,
        Sale = 0x02,
        Settlement = 0x03,
        ReportInit = 0x04,
        ReportRecord = 0x05,
        Void = 0x06,
        CashbackLimit = 0x07,
        SendOffline = 0x08,
        RemoveCard = 0x09,
        VoidPreauth = 0x0A,
        Preauthorization = 0x0C,
        SaleCompletion = 0x0D,
        ReadFleetCard = 0x0E,
        CashAdvance = 0x11,
        GetCardToken = 0x18
    }

    // Response Codes (Tag 0xA100 values)
    public enum ResponseCode : byte
    {
        Success = 0x00,
        GenericError = 0x01,
        HostCommunicationError = 0x02,
        OutOfRange = 0x03,
        InvalidInput = 0x04,
        InvalidTerminalParams = 0x05,
        MissingPinKey = 0x06,
        CashbackAllowed = 0x07,
        CashbackNotAllowed = 0x08,
        CancelByUser = 0x09
    }

    // Transaction Flags
    [Flags]
    public enum TransactionFlags : byte
    {
        ChipCard = 0x01,
        Contactless = 0x02,
        MagneticStripe = 0x04,
        SignatureRequested = 0x08,
        PinEntered = 0x10,
        OfflineTransaction = 0x20
    }

    /// <summary>
    /// Builds a TLV packet for SmartPay protocol
    /// </summary>
    public static byte[] BuildPacket(CommandCode command, Dictionary<ushort, byte[]>? tags = null)
    {
        using var ms = new MemoryStream();
        
        // Build TLV data
        var data = new List<byte>();
        
        // Add command tag (0xA000)
        data.AddRange(BuildTlv(TAG_COMMAND, new byte[] { (byte)command }));
        
        // Add additional tags
        if (tags != null)
        {
            foreach (var tag in tags.OrderBy(t => t.Key))
            {
                data.AddRange(BuildTlv(tag.Key, tag.Value));
            }
        }
        
        var dataBytes = data.ToArray();
        
        // Build full packet: STX + Length + Data + ETX + CRC
        ms.WriteByte(STX);
        
        // Length (MSB, LSB) - length of data only
        ushort length = (ushort)dataBytes.Length;
        ms.WriteByte((byte)(length >> 8));
        ms.WriteByte((byte)(length & 0xFF));
        
        // Data
        ms.Write(dataBytes, 0, dataBytes.Length);
        
        // ETX
        ms.WriteByte(ETX);
        
        // Calculate CRC on everything from STX to ETX
        // IMPORTANT: Use CRC-16/BUYPASS, not IBM!
        var packet = ms.ToArray();
        var crc = Crc16Buypass.CalculateForRequest(packet);
        ms.Write(crc, 0, crc.Length);
        
        var finalPacket = ms.ToArray();
        
        // Debug: Log the packet
        System.Diagnostics.Debug.WriteLine($"[SmartPay] BuildPacket: {ToHexString(finalPacket)}");
        
        return finalPacket;
    }
    
    /// <summary>
    /// Test packet against known example from PDF
    /// </summary>
    public static void TestPacketBuilding()
    {
        // Build Get Info packet
        var packet = BuildPacket(CommandCode.GetInfo);
        var hex = ToHexString(packet);
        
        System.Diagnostics.Debug.WriteLine($"[SmartPay] Test GetInfo packet: {hex}");
        
        // Expected: 02 00 04 a0 00 01 01 03 06 35
        // If different, CRC is wrong!
    }
    
    private static string ToHexString(byte[] data)
    {
        return string.Join(" ", data.Select(b => $"{b:X2}"));
    }

    /// <summary>
    /// Builds a single TLV element
    /// </summary>
    private static byte[] BuildTlv(ushort tag, byte[] value)
    {
        var result = new List<byte>();
        // Tag (2 bytes)
        result.Add((byte)(tag >> 8));
        result.Add((byte)(tag & 0xFF));
        // Length (1 byte, max 255)
        result.Add((byte)value.Length);
        // Value
        result.AddRange(value);
        return result.ToArray();
    }

    /// <summary>
    /// Parses a response packet
    /// </summary>
    public static (bool Success, ResponseCode ResponseCode, Dictionary<ushort, byte[]> Tags) ParseResponse(byte[] packet)
    {
        var tags = new Dictionary<ushort, byte[]>();
        
        try
        {
            // Minimum packet size: STX(1) + Length(2) + At least one TLV(4) + ETX(1) + CRC(2) = 10
            if (packet.Length < 10)
                return (false, ResponseCode.GenericError, tags);

            // Check STX
            if (packet[0] != STX)
                return (false, ResponseCode.GenericError, tags);

            // Get length
            ushort length = (ushort)((packet[1] << 8) | packet[2]);
            
            // Extract data (between length and ETX)
            int dataStart = 3;
            int dataEnd = dataStart + length;
            
            // Verify ETX position
            if (dataEnd >= packet.Length || packet[dataEnd] != ETX)
                return (false, ResponseCode.GenericError, tags);

            // Verify CRC using CRC-16/BUYPASS
            var dataWithEtx = new byte[dataEnd + 1]; // From STX to ETX
            Array.Copy(packet, 0, dataWithEtx, 0, dataWithEtx.Length);
            
            if (!Crc16Buypass.VerifyResponse(dataWithEtx))
                return (false, ResponseCode.GenericError, tags);

            // Parse TLV tags from data
            int pos = dataStart;
            while (pos < dataEnd)
            {
                if (pos + 3 > dataEnd) break;
                
                // Read tag
                ushort tag = (ushort)((packet[pos] << 8) | packet[pos + 1]);
                byte len = packet[pos + 2];
                
                if (pos + 3 + len > dataEnd) break;
                
                // Read value
                byte[] value = new byte[len];
                Array.Copy(packet, pos + 3, value, 0, len);
                tags[tag] = value;
                
                pos += 3 + len;
            }

            // Get response code from tag 0xA100
            ResponseCode respCode = ResponseCode.GenericError;
            if (tags.TryGetValue(TAG_RESPONSE, out var respBytes) && respBytes.Length > 0)
            {
                respCode = (ResponseCode)respBytes[0];
            }

            return (respCode == ResponseCode.Success, respCode, tags);
        }
        catch
        {
            return (false, ResponseCode.GenericError, tags);
        }
    }

    /// <summary>
    /// Converts amount to 12-digit ASCII format (e.g., "000000007000" for 70.00)
    /// </summary>
    public static string FormatAmount(decimal amount)
    {
        // Amount is in cents, 12 digits with leading zeros
        int cents = (int)(amount * 100);
        return cents.ToString("D12");
    }

    /// <summary>
    /// Parses amount from ASCII bytes
    /// </summary>
    public static decimal ParseAmount(byte[] asciiBytes)
    {
        string str = Encoding.ASCII.GetString(asciiBytes);
        if (int.TryParse(str, out int cents))
        {
            return cents / 100m;
        }
        return 0m;
    }
}
