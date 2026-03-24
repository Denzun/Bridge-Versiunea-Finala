namespace POSBridge.Devices.SmartPay;

/// <summary>
/// CRC-16/BUYPASS implementation for SmartPay ECR Link protocol.
/// 
/// CRITICAL: This is NOT CRC-16/IBM!
/// 
/// From SmartPay spec Appendix 2:
/// - Algorithm: CRC-16/BUYPASS
/// - Initial value: 0x0000 (NOT 0xFFFF!)
/// - Polynomial: 0x8005 (NOT 0xA001!)
/// - Non-reflected: processes MSB first
/// 
/// Test vector from spec:
/// Input:  02 00 04 a0 00 01 01 03 (STX through ETX)
/// Output: 0x0635
/// Sent as: 0x06, 0x35 (MSB first for requests)
/// </summary>
public static class Crc16Buypass
{
    /// <summary>
    /// Calculates CRC-16/BUYPASS for the given data.
    /// </summary>
    public static ushort CalculateValue(byte[] data)
    {
        ushort crc = 0x0000;  // Initial value is ZERO, not 0xFFFF!
        
        for (int offset = 0; offset < data.Length; offset++)
        {
            crc ^= (ushort)((data[offset] << 8) & 0xFFFF);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc <<= 1;
                    crc ^= 0x8005;  // Polynomial
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        
        return crc;
    }

    /// <summary>
    /// Calculates CRC and returns as MSB, LSB for requests.
    /// </summary>
    public static byte[] CalculateForRequest(byte[] data)
    {
        ushort crc = CalculateValue(data);
        // MSB first for requests: 0x0635 → 0x06, 0x35
        return new byte[] { (byte)(crc >> 8), (byte)(crc & 0xFF) };
    }

    /// <summary>
    /// Calculates CRC and returns as LSB, MSB for responses.
    /// </summary>
    public static byte[] CalculateForResponse(byte[] data)
    {
        ushort crc = CalculateValue(data);
        // LSB first for responses
        return new byte[] { (byte)(crc & 0xFF), (byte)(crc >> 8) };
    }

    /// <summary>
    /// Verifies CRC for received data.
    /// </summary>
    public static bool VerifyResponse(byte[] dataWithCrc)
    {
        if (dataWithCrc.Length < 3) return false;

        // Extract data (all except last 2 bytes)
        byte[] data = new byte[dataWithCrc.Length - 2];
        Array.Copy(dataWithCrc, 0, data, 0, data.Length);

        // CRC is LSB, MSB in response
        ushort receivedCrc = (ushort)((dataWithCrc[dataWithCrc.Length - 1] << 8) | 
                                       dataWithCrc[dataWithCrc.Length - 2]);

        ushort computedCrc = CalculateValue(data);
        
        return computedCrc == receivedCrc;
    }

    /// <summary>
    /// Test against known example from PDF.
    /// </summary>
    public static bool TestAgainstExample()
    {
        // Get Info packet: STX through ETX
        byte[] test = { 0x02, 0x00, 0x04, 0xa0, 0x00, 0x01, 0x01, 0x03 };
        ushort crc = CalculateValue(test);
        
        // Must equal 0x0635
        // Sent as MSB first: 0x06, 0x35
        return crc == 0x0635;
    }

    /// <summary>
    /// Returns hex string of CRC bytes for request.
    /// </summary>
    public static string GetRequestCrcHex(byte[] data)
    {
        var crc = CalculateForRequest(data);
        return $"{crc[0]:X2} {crc[1]:X2}";
    }
}
