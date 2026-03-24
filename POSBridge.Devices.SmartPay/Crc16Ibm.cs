namespace POSBridge.Devices.SmartPay;

/// <summary>
/// CRC16 IBM implementation for SmartPay ECR Link protocol.
/// Appendix 2 - CRC description from SmartPay_ECR_Link_v1.8.pdf
/// </summary>
public static class Crc16Ibm
{
    private const ushort Polynomial = 0xA001; // IBM CRC16 polynomial

    /// <summary>
    /// Calculates CRC16 IBM for the given data.
    /// For requests: CRC is MSB, LSB
    /// For responses: CRC is LSB, MSB
    /// </summary>
    public static byte[] Calculate(byte[] data)
    {
        ushort crc = 0xFFFF;

        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x0001) != 0)
                {
                    crc = (ushort)((crc >> 1) ^ Polynomial);
                }
                else
                {
                    crc >>= 1;
                }
            }
        }

        // Return as MSB, LSB (for request messages)
        return new byte[] { (byte)(crc >> 8), (byte)(crc & 0xFF) };
    }

    /// <summary>
    /// Calculates CRC and returns as LSB, MSB (for response messages)
    /// </summary>
    public static byte[] CalculateLsbMsb(byte[] data)
    {
        var crc = Calculate(data);
        // Swap bytes for LSB, MSB format
        return new byte[] { crc[1], crc[0] };
    }

    /// <summary>
    /// Verifies CRC for received data (excluding the CRC bytes themselves)
    /// </summary>
    public static bool Verify(byte[] dataWithCrc)
    {
        if (dataWithCrc.Length < 3) return false;

        // Extract data (all except last 2 bytes) and CRC (last 2 bytes)
        byte[] data = new byte[dataWithCrc.Length - 2];
        Array.Copy(dataWithCrc, 0, data, 0, data.Length);

        byte[] receivedCrc = new byte[2];
        Array.Copy(dataWithCrc, dataWithCrc.Length - 2, receivedCrc, 0, 2);

        // Calculate expected CRC (response format: LSB, MSB)
        ushort calculated = 0xFFFF;
        foreach (byte b in data)
        {
            calculated ^= b;
            for (int i = 0; i < 8; i++)
            {
                if ((calculated & 0x0001) != 0)
                {
                    calculated = (ushort)((calculated >> 1) ^ Polynomial);
                }
                else
                {
                    calculated >>= 1;
                }
            }
        }

        // Compare with received CRC (LSB, MSB format)
        ushort received = (ushort)((receivedCrc[1] << 8) | receivedCrc[0]);
        return calculated == received;
    }
}
