using System.IO.Ports;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Serial communication wrapper for SmartPay/Ingenico terminals.
/// Implements the ENQ-ACK handshake and packet communication protocol.
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
                    ReadTimeout = 30000,  // 30 seconds for transaction operations
                    WriteTimeout = 5000   // 5 seconds for writes
                };

                _serialPort.Open();

                // Wait a moment for port to stabilize
                Thread.Sleep(100);

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
    /// Sends a packet with full ENQ-ACK handshake and waits for response
    /// </summary>
    public byte[] SendAndReceive(byte[] packet)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new FiscalDeviceException("Not connected to SmartPay terminal");

            try
            {
                // Step 1: Clear buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                Thread.Sleep(50); // Small delay after clearing

                // Step 2: Send ENQ to initiate communication
                System.Diagnostics.Debug.WriteLine("[SmartPay] Sending ENQ...");
                _serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
                _serialPort.BaseStream.Flush();

                // Step 3: Wait for ACK
                int response = _serialPort.ReadByte();
                System.Diagnostics.Debug.WriteLine($"[SmartPay] After ENQ got: 0x{response:X2}");
                
                if (response != SmartPayProtocol.ACK)
                {
                    if (response == SmartPayProtocol.NAK)
                        throw new FiscalDeviceException("SmartPay returned NAK to ENQ - device busy or error");
                    throw new FiscalDeviceException($"Expected ACK after ENQ, got 0x{response:X2}");
                }

                // Step 4: Send the actual packet
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Sending packet ({packet.Length} bytes)...");
                _serialPort.Write(packet, 0, packet.Length);
                _serialPort.BaseStream.Flush();

                // Step 5: Wait for ACK confirming packet received
                int packetAck = _serialPort.ReadByte();
                System.Diagnostics.Debug.WriteLine($"[SmartPay] After packet got: 0x{packetAck:X2}");
                
                if (packetAck != SmartPayProtocol.ACK)
                {
                    if (packetAck == SmartPayProtocol.NAK)
                        throw new FiscalDeviceException("SmartPay returned NAK - packet rejected");
                    throw new FiscalDeviceException($"Expected ACK after packet, got 0x{packetAck:X2}");
                }

                // Step 6: Wait for response packet with timeout handling
                // The device might take some time to process and respond
                Thread.Sleep(100); // Give device time to prepare response
                
                var responseData = new List<byte>();
                
                // Read STX (with retry for timing issues)
                int stx = -1;
                int retryCount = 0;
                while (stx != SmartPayProtocol.STX && retryCount < 3)
                {
                    try
                    {
                        stx = _serialPort.ReadByte();
                        System.Diagnostics.Debug.WriteLine($"[SmartPay] Read byte: 0x{stx:X2}");
                        
                        if (stx == SmartPayProtocol.STX)
                            break;
                            
                        // If we got something else (like another ACK), wait and retry
                        if (stx == SmartPayProtocol.ACK)
                        {
                            System.Diagnostics.Debug.WriteLine("[SmartPay] Got extra ACK, waiting for STX...");
                            Thread.Sleep(100);
                        }
                        retryCount++;
                    }
                    catch (TimeoutException)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SmartPay] Timeout waiting for STX, retry {retryCount + 1}/3");
                        retryCount++;
                        if (retryCount >= 3)
                            throw;
                    }
                }
                
                if (stx != SmartPayProtocol.STX)
                    throw new FiscalDeviceException($"Expected STX, got 0x{stx:X2}");
                
                responseData.Add((byte)stx);

                // Step 7: Read length (2 bytes)
                byte[] lengthBytes = new byte[2];
                _serialPort.Read(lengthBytes, 0, 2);
                responseData.AddRange(lengthBytes);
                
                ushort dataLength = (ushort)((lengthBytes[0] << 8) | lengthBytes[1]);
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Response length: {dataLength}");

                // Step 8: Read data + ETX + CRC
                int totalToRead = dataLength + 3; // data + ETX(1) + CRC(2)
                byte[] remaining = new byte[totalToRead];
                int read = 0;
                
                while (read < totalToRead)
                {
                    int r = _serialPort.Read(remaining, read, totalToRead - read);
                    if (r == 0) 
                    {
                        System.Diagnostics.Debug.WriteLine($"[SmartPay] Read 0 bytes, waiting... ({read}/{totalToRead})");
                        Thread.Sleep(50);
                        continue;
                    }
                    read += r;
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Read {r} bytes, total {read}/{totalToRead}");
                }
                
                responseData.AddRange(remaining);

                // Step 9: Send final ACK
                _serialPort.Write(new byte[] { SmartPayProtocol.ACK }, 0, 1);
                _serialPort.BaseStream.Flush();
                
                System.Diagnostics.Debug.WriteLine($"[SmartPay] Response received: {responseData.Count} bytes");

                return responseData.ToArray();
            }
            catch (TimeoutException)
            {
                throw new FiscalDeviceException("Timeout waiting for SmartPay response. Check:\n1. Device is powered on\n2. Correct COM port selected\n3. Baud rate is correct (usually 115200)");
            }
            catch (Exception ex) when (ex is not FiscalDeviceException)
            {
                throw new FiscalDeviceException($"Communication error: {ex.Message}");
            }
        }
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
