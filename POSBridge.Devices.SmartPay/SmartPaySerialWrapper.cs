using System.IO.Ports;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Serial communication wrapper for SmartPay/Ingenico terminals.
/// Implements the handshake and packet communication protocol.
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

                // Perform initial handshake
                if (!PerformHandshake())
                {
                    _serialPort.Close();
                    throw new FiscalDeviceException("SmartPay handshake failed. Terminal not responding.");
                }
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
    /// Performs initial handshake: ENQ-ACK protocol
    /// </summary>
    private bool PerformHandshake()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return false;

        try
        {
            // Clear any pending data
            _serialPort.DiscardInBuffer();
            _serialPort.DiscardOutBuffer();

            // Send ENQ (0x05)
            _serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
            _serialPort.BaseStream.Flush();

            // Wait for ACK (0x06)
            int response = _serialPort.ReadByte();
            return response == SmartPayProtocol.ACK;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends a packet and waits for response
    /// </summary>
    public byte[] SendAndReceive(byte[] packet)
    {
        lock (_lock)
        {
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new FiscalDeviceException("Not connected to SmartPay terminal");

            try
            {
                // Clear buffers
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // Send packet
                _serialPort.Write(packet, 0, packet.Length);
                _serialPort.BaseStream.Flush();

                // Wait for ACK
                int ack = _serialPort.ReadByte();
                if (ack != SmartPayProtocol.ACK)
                {
                    if (ack == SmartPayProtocol.NAK)
                        throw new FiscalDeviceException("SmartPay returned NAK - packet rejected");
                    throw new FiscalDeviceException($"Unexpected response from SmartPay: 0x{ack:X2}");
                }

                // Now wait for response packet
                // Response starts with STX
                var responseData = new List<byte>();
                
                // Read STX
                int stx = _serialPort.ReadByte();
                if (stx != SmartPayProtocol.STX)
                    throw new FiscalDeviceException($"Expected STX, got 0x{stx:X2}");
                
                responseData.Add((byte)stx);

                // Read length (2 bytes)
                byte[] lengthBytes = new byte[2];
                _serialPort.Read(lengthBytes, 0, 2);
                responseData.AddRange(lengthBytes);
                
                ushort dataLength = (ushort)((lengthBytes[0] << 8) | lengthBytes[1]);

                // Read data + ETX + CRC
                byte[] remaining = new byte[dataLength + 3]; // data + ETX(1) + CRC(2)
                int read = 0;
                while (read < remaining.Length)
                {
                    int r = _serialPort.Read(remaining, read, remaining.Length - read);
                    if (r == 0) throw new TimeoutException();
                    read += r;
                }
                responseData.AddRange(remaining);

                // Send ACK
                _serialPort.Write(new byte[] { SmartPayProtocol.ACK }, 0, 1);

                return responseData.ToArray();
            }
            catch (TimeoutException)
            {
                throw new FiscalDeviceException("Timeout waiting for SmartPay response");
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
