using System.IO.Ports;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Serial communication wrapper for SmartPay/Ingenico terminals.
/// Implements the ENQ-ACK handshake and packet communication protocol.
/// Fixed based on Claude's analysis.
/// </summary>
public class SmartPaySerialWrapper : IDisposable
{
    private SerialPort? _serialPort;
    private bool _disposed;
    private readonly object _lock = new();

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    /// <summary>
    /// Connects to the SmartPay terminal via serial port.
    /// </summary>
    public void Connect(string portName, int baudRate = 115200)
    {
        lock (_lock)
        {
            Disconnect();

            try
            {
                _serialPort = new SerialPort(portName, baudRate)
                {
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    DtrEnable = true,  // CRITICAL: Required by Ingenico
                    RtsEnable = true,  // CRITICAL: Required by Ingenico
                    ReadTimeout = 10000,  // 10 seconds
                    WriteTimeout = 5000   // 5 seconds
                };

                _serialPort.Open();

                // Reset terminal state on connect
                ResetSession();

                System.Diagnostics.Debug.WriteLine($"[SmartPay] Connected to {portName} @ {baudRate}");
            }
            catch (TimeoutException)
            {
                throw new FiscalDeviceException($"Timeout connecting to SmartPay on {portName}");
            }
            catch (Exception ex) when (ex is not FiscalDeviceException)
            {
                throw new FiscalDeviceException($"Failed to connect to SmartPay: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Sends EOT to reset terminal session state.
    /// Call this before starting new communication after errors.
    /// </summary>
    public void ResetSession()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();
            
            // Send EOT to reset terminal state
            _serialPort.Write(new byte[] { 0x04 }, 0, 1);
            _serialPort.BaseStream.Flush();
            Thread.Sleep(200);
            
            _serialPort.DiscardInBuffer();
        }
        catch { }
    }

    /// <summary>
    /// Sends a packet with full ENQ-ACK handshake and waits for response.
    /// Fixed implementation based on Claude's analysis.
    /// </summary>
    public byte[] SendAndReceive(byte[] packet)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new FiscalDeviceException("Not connected to SmartPay terminal");

            try
            {
                // Step 1: Reset session state
                ResetSession();
                Thread.Sleep(100);

                // Step 2: Send ENQ to initiate communication
                System.Diagnostics.Debug.WriteLine("[SmartPay] Sending ENQ...");
                _serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
                _serialPort.BaseStream.Flush();

                // Step 3: Wait for ACK to ENQ
                int enqAck = _serialPort.ReadByte();
                System.Diagnostics.Debug.WriteLine($"[SmartPay] ENQ response: 0x{enqAck:X2}");
                
                if (enqAck != SmartPayProtocol.ACK)
                {
                    if (enqAck == SmartPayProtocol.NAK)
                        throw new FiscalDeviceException("SmartPay NAK to ENQ - device busy or error");
                    throw new FiscalDeviceException($"Expected ACK to ENQ, got 0x{enqAck:X2}");
                }

                // Step 4: Send the packet
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Sending packet ({packet.Length} bytes): {ToHexString(packet)}");
                _serialPort.Write(packet, 0, packet.Length);
                _serialPort.BaseStream.Flush();

                // Step 5: Wait for ACK confirming packet received
                int pktAck = _serialPort.ReadByte();
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Packet ACK: 0x{pktAck:X2}");
                
                if (pktAck == SmartPayProtocol.NAK)
                    throw new FiscalDeviceException("Packet NAKed - check CRC/framing");
                if (pktAck != SmartPayProtocol.ACK)
                    throw new FiscalDeviceException($"Expected ACK after packet, got 0x{pktAck:X2}");

                // Step 6: Drain to STX (response start)
                System.Diagnostics.Debug.WriteLine("[SmartPay] Waiting for response STX...");
                byte stx = DrainToStx();
                System.Diagnostics.Debug.WriteLine("[SmartPay] Got STX, reading response...");

                // Step 7: Read 2-byte length
                byte[] lengthBytes = ReadExact(2);
                ushort dataLength = (ushort)((lengthBytes[0] << 8) | lengthBytes[1]);
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Response data length: {dataLength}");

                // Step 8: Read data + ETX + CRC(2)
                byte[] remaining = ReadExact(dataLength + 3);

                // Step 9: Validate ETX position
                if (remaining[dataLength] != SmartPayProtocol.ETX)
                    throw new FiscalDeviceException($"Missing ETX at expected position, got 0x{remaining[dataLength]:X2}");

                // Step 10: Validate response CRC (LSB, MSB for responses)
                // Build array for CRC verification: STX + LEN + DATA + ETX
                byte[] crcInput = new byte[1 + 2 + dataLength + 1];
                crcInput[0] = stx;
                Array.Copy(lengthBytes, 0, crcInput, 1, 2);
                Array.Copy(remaining, 0, crcInput, 3, dataLength);
                crcInput[crcInput.Length - 1] = SmartPayProtocol.ETX;
                
                // IMPORTANT: Use CRC-16/BUYPASS, not IBM!
                ushort computedCrc = Crc16Buypass.CalculateValue(crcInput);
                ushort receivedCrc = (ushort)(remaining[dataLength + 1] | (remaining[dataLength + 2] << 8));
                
                System.Diagnostics.Debug.WriteLine($"[SmartPay] CRC: received=0x{receivedCrc:X4}, computed=0x{computedCrc:X4}");
                
                if (receivedCrc != computedCrc)
                    throw new FiscalDeviceException($"CRC mismatch: got 0x{receivedCrc:X4}, expected 0x{computedCrc:X4}");

                // Step 11: Send final ACK
                _serialPort.Write(new byte[] { SmartPayProtocol.ACK }, 0, 1);
                _serialPort.BaseStream.Flush();

                // Step 12: Return just the TLV data (exclude ETX and CRC)
                byte[] data = new byte[dataLength];
                Array.Copy(remaining, 0, data, 0, dataLength);
                
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Response received: {data.Length} bytes of data");
                return data;
            }
            catch (TimeoutException)
            {
                throw new FiscalDeviceException("Timeout waiting for SmartPay response. Check:\n1. Device is powered on\n2. Correct COM port selected\n3. Baud rate is correct (usually 115200)");
            }
            catch (FiscalDeviceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FiscalDeviceException($"Communication error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reads exactly 'count' bytes from the serial port.
    /// </summary>
    private byte[] ReadExact(int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        
        while (offset < count)
        {
            int r = _serialPort.Read(buffer, offset, count - offset);
            if (r == 0)
            {
                System.Diagnostics.Debug.WriteLine($"[SmartPay] ReadExact: 0 bytes, waiting... ({offset}/{count})");
                Thread.Sleep(50);
                continue;
            }
            offset += r;
            System.Diagnostics.Debug.WriteLine($"[SmartPay] ReadExact: read {r} bytes, total {offset}/{count}");
        }
        
        return buffer;
    }

    /// <summary>
    /// Drains bytes until STX is found.
    /// Logs any unexpected bytes for diagnostics.
    /// </summary>
    private byte DrainToStx()
    {
        const int maxAttempts = 30;
        
        for (int i = 0; i < maxAttempts; i++)
        {
            int b = _serialPort.ReadByte();
            System.Diagnostics.Debug.WriteLine($"[SmartPay] Drain byte: 0x{b:X2}");
            
            if (b == SmartPayProtocol.STX)
                return (byte)b;
            
            if (b == 0x04) // EOT
                throw new FiscalDeviceException("Terminal sent EOT (0x04) - session aborted. CRC or framing error likely.");
            
            if (b == SmartPayProtocol.NAK)
                throw new FiscalDeviceException("Terminal sent NAK (0x15) - packet rejected");
        }
        
        throw new FiscalDeviceException("Never received STX after 30 attempts");
    }

    /// <summary>
    /// Helper for hex logging.
    /// </summary>
    private static string ToHexString(byte[] data)
    {
        return string.Join(" ", data.Select(b => $"{b:X2}"));
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            try
            {
                _serialPort?.Close();
            }
            catch { }
            _serialPort = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
