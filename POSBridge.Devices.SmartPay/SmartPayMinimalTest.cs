using System.IO.Ports;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Minimal test that EXACTLY matches the diagnostic tool
/// </summary>
public static class SmartPayMinimalTest
{
    public static string Test(string portName = "COM10", int baudRate = 115200)
    {
        var log = new List<string>();
        
        const byte ENQ = 0x05;
        const byte ACK = 0x06;
        const byte NAK = 0x15;
        const byte STX = 0x02;
        const byte ETX = 0x03;
        
        SerialPort? port = null;
        
        try
        {
            log.Add($"=== MINIMAL TEST {portName} @ {baudRate} ===");
            
            // Step 1: Create port EXACTLY like diagnostic tool
            log.Add("Creating SerialPort...");
            port = new SerialPort(portName, baudRate)
            {
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                Handshake = Handshake.None,
                DtrEnable = false,
                RtsEnable = true,
                ReadTimeout = 3000,
                WriteTimeout = 3000
            };
            
            // Step 2: Open
            log.Add("Opening port...");
            port.Open();
            log.Add($"Port opened: IsOpen={port.IsOpen}");
            
            // Step 3: Sleep 200ms (diagnostic tool timing)
            log.Add("Sleeping 200ms...");
            Thread.Sleep(200);
            
            // Step 4: Clear buffers
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            
            // Step 5: Build packet EXACTLY like diagnostic tool
            byte[] tlvData = new byte[] { 0xA0, 0x00, 0x01, 0x01 };
            ushort crc = CalculateCrc(tlvData);
            
            byte[] packet = new byte[]
            {
                STX,                    // 0x02
                0x00, 0x04,             // Length = 4
                0xA0, 0x00, 0x01, 0x01, // TLV
                ETX,                    // 0x03
                (byte)(crc >> 8),       // CRC MSB
                (byte)(crc & 0xFF)      // CRC LSB
            };
            
            string packetHex = BitConverter.ToString(packet).Replace("-", " ");
            log.Add($"Packet ({packet.Length} bytes): {packetHex}");
            log.Add($"CRC: 0x{crc:X4} (expected 0x0635)");
            
            // Step 6: Send ENQ
            log.Add("Sending ENQ...");
            port.Write(new byte[] { ENQ }, 0, 1);
            
            int enqResp = port.ReadByte();
            log.Add($"ENQ Response: 0x{enqResp:X2} ({(enqResp == ACK ? "ACK" : enqResp == NAK ? "NAK" : "?")})");
            
            if (enqResp != ACK)
            {
                log.Add("ENQ failed - aborting");
                return string.Join("\n", log);
            }
            
            // Step 7: Wait 50ms and flush (diagnostic tool)
            Thread.Sleep(50);
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            
            // Step 8: Send packet
            log.Add("Sending packet...");
            port.Write(packet, 0, packet.Length);
            
            int pktResp = port.ReadByte();
            log.Add($"Packet Response: 0x{pktResp:X2} ({(pktResp == ACK ? "ACK" : pktResp == NAK ? "NAK" : "?")})");
            
            if (pktResp == ACK)
            {
                log.Add("SUCCESS! Terminal accepted the packet.");
            }
            else if (pktResp == NAK)
            {
                log.Add("FAILED! Terminal NAKed the packet (CRC error).");
            }
            else
            {
                log.Add($"UNEXPECTED response: 0x{pktResp:X2}");
            }
            
            return string.Join("\n", log);
        }
        catch (Exception ex)
        {
            log.Add($"ERROR: {ex.GetType().Name}: {ex.Message}");
            return string.Join("\n", log);
        }
        finally
        {
            try
            {
                if (port?.IsOpen == true)
                {
                    port.Write(new byte[] { 0x04 }, 0, 1); // EOT
                    Thread.Sleep(100);
                    port.Close();
                }
                port?.Dispose();
            }
            catch { }
        }
    }
    
    private static ushort CalculateCrc(byte[] data)
    {
        ushort crc = 0x0000;
        for (int offset = 0; offset < data.Length; offset++)
        {
            crc ^= (ushort)((data[offset] << 8) & 0xFFFF);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc <<= 1;
                    crc ^= 0x8005;
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        return crc;
    }
}
