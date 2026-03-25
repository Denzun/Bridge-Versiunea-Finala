using System.IO.Ports;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.SmartPay;

/// <summary>
/// Serial communication wrapper for SmartPay/Ingenico terminals.
/// Full implementation per SmartPay ECR Link v1.8 specification.
/// </summary>
public class SmartPaySerialWrapper : IDisposable
{
    private SerialPort? _serialPort;
    private bool _disposed;
    private readonly object _lock = new();

    public bool IsConnected => _serialPort?.IsOpen ?? false;

    public void Connect(string portName, int baudRate = 115200)
    {
        lock (_lock)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            SmartPayDebug.Log($"[Connect] Log file: {SmartPayDebug.GetLogFilePath()}");
            SmartPayDebug.Log($"[Connect] Starting connection to {portName} @ {baudRate}...");
            SmartPayDebug.Log($"[Connect] Current thread: {System.Threading.Thread.CurrentThread.ManagedThreadId}");
            
            Disconnect();
            SmartPayDebug.Log($"[Connect] Disconnect completed in {sw.ElapsedMilliseconds}ms");

            try
            {
                var availablePorts = SerialPort.GetPortNames();
                SmartPayDebug.Log($"[Connect] Available ports: {string.Join(", ", availablePorts)}");
                
                if (!availablePorts.Contains(portName))
                {
                    throw new FiscalDeviceException($"Serial port {portName} not found. Available: {string.Join(", ", availablePorts)}");
                }

                SmartPayDebug.Log($"[Connect] Creating SerialPort...");
                
                // EXACTLY like diagnostic tool - create with settings at construction
                _serialPort = new SerialPort();
                _serialPort.PortName = portName;
                _serialPort.BaudRate = baudRate;
                _serialPort.DataBits = 8;
                _serialPort.Parity = Parity.None;
                _serialPort.StopBits = StopBits.One;
                _serialPort.Handshake = Handshake.None;
                _serialPort.DtrEnable = false;
                _serialPort.RtsEnable = true;  // Important: RTS high
                _serialPort.ReadTimeout = 3000;  // Match diagnostic tool
                _serialPort.WriteTimeout = 3000;
                
                SmartPayDebug.Log($"[Connect] SerialPort created at T+{sw.ElapsedMilliseconds}ms");

                SmartPayDebug.Log($"[Connect] Opening port...");
                _serialPort.Open();
                SmartPayDebug.Log($"[Connect] Port.Open() completed at T+{sw.ElapsedMilliseconds}ms");
                SmartPayDebug.Log($"[Connect] Port settings - IsOpen: {_serialPort.IsOpen}, RTS: {_serialPort.RtsEnable}, DTR: {_serialPort.DtrEnable}");
                
                // EXACTLY like diagnostic tool: 200ms sleep after open
                SmartPayDebug.Log($"[Connect] Sleeping 200ms for port stabilization...");
                Thread.Sleep(200);
                SmartPayDebug.Log($"[Connect] Sleep done at T+{sw.ElapsedMilliseconds}ms");
                
                // Clear any garbage
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                
                int pendingBefore = _serialPort.BytesToRead;
                if (pendingBefore > 0)
                {
                    byte[] junk = new byte[pendingBefore];
                    _serialPort.Read(junk, 0, pendingBefore);
                    SmartPayDebug.Log($"[Connect] Cleared {pendingBefore} junk bytes from buffer");
                }
                
                SmartPayDebug.Log($"[Connect] SUCCESS - Port ready at T+{sw.ElapsedMilliseconds}ms");
            }
            catch (UnauthorizedAccessException)
            {
                SmartPayDebug.Log($"[Connect] FAILED - UnauthorizedAccessException at T+{sw.ElapsedMilliseconds}ms");
                throw new FiscalDeviceException($"Cannot access {portName}. Port in use.");
            }
            catch (Exception ex)
            {
                SmartPayDebug.Log($"[Connect] FAILED - {ex.GetType().Name}: {ex.Message} at T+{sw.ElapsedMilliseconds}ms");
                throw new FiscalDeviceException($"Failed to connect: {ex.Message}");
            }
        }
    }

    public byte[] SendAndReceive(byte[] packet)
    {
        lock (_lock)
        {
            // CRITICAL DEBUG OUTPUT
            System.Diagnostics.Debug.WriteLine($"[SmartPay] === Transaction {DateTime.Now} ===");
            System.Diagnostics.Debug.WriteLine($"[SmartPay] Packet bytes: {Hex(packet)}");
            System.Console.WriteLine($"[SmartPay] Packet: {Hex(packet)}");
            
            SmartPayDebug.Log($"=== Transaction {DateTime.Now} ===");
            SmartPayDebug.Log($"Packet: {Hex(packet)}");
            
            if (_serialPort == null || !_serialPort.IsOpen)
                throw new FiscalDeviceException("Not connected");

            try
            {
                // Step 1: Send ENQ and wait for ACK (simplified like diagnostic tool)
                SmartPayDebug.Log("Step 1: Sending ENQ...");
                
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
                
                _serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
                _serialPort.BaseStream.Flush();
                
                // Wait for ENQ to be transmitted
                int enqWait = 0;
                while (_serialPort.BytesToWrite > 0 && enqWait < 50)
                {
                    Thread.Sleep(1);
                    enqWait++;
                }
                
                SmartPayDebug.Log("  TX: ENQ (0x05)");
                
                int enqResponse;
                try
                {
                    enqResponse = _serialPort.ReadByte();
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] ENQ Response: 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                    System.Console.WriteLine($"[SmartPay] ENQ Response: 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                    SmartPayDebug.Log($"  RX: 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                }
                catch (TimeoutException)
                {
                    SmartPayDebug.Log("  TIMEOUT - no response to ENQ, trying once more...");
                    Thread.Sleep(500);
                    _serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
                    enqResponse = _serialPort.ReadByte();
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] ENQ Response (2nd): 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                    System.Console.WriteLine($"[SmartPay] ENQ Response (2nd): 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                    SmartPayDebug.Log($"  RX (2nd try): 0x{enqResponse:X2} ({CtrlName((byte)enqResponse)})");
                }
                
                if (enqResponse != SmartPayProtocol.ACK)
                {
                    SmartPayDebug.Log($"  Expected ACK, got {CtrlName((byte)enqResponse)} - aborting");
                    _serialPort.Write(new byte[] { SmartPayProtocol.EOT }, 0, 1);
                    throw new FiscalDeviceException($"Terminal not ready (got {CtrlName((byte)enqResponse)})");
                }
                
                SmartPayDebug.Log("  ENQ accepted!");

                // Step 2: Small delay and flush before packet (like diagnostic tool)
                SmartPayDebug.Log("Step 2: 50ms delay then send packet...");
                Thread.Sleep(50);
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();

                // Step 3: Send packet with retry (Spec says try on NAK)
                SmartPayDebug.Log("Step 3: Sending packet (up to 3 attempts)...");
                SmartPayDebug.Log($"  Packet bytes ({packet.Length}): {Hex(packet)}");
                
                // Verify packet structure
                if (packet.Length >= 2)
                {
                    ushort lenInPacket = (ushort)((packet[1] << 8) | packet[2]);
                    SmartPayDebug.Log($"  Packet length field: {lenInPacket}, actual data length: {packet.Length - 5}");
                }
                
                byte[]? responseData = null;
                
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    SmartPayDebug.Log($"  Packet attempt {attempt + 1}/3");
                    SmartPayDebug.Log($"  TX ({packet.Length} bytes): {Hex(packet)}");
                    
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Writing {packet.Length} bytes to port...");
                    System.Console.WriteLine($"[SmartPay] TX: {Hex(packet)}");
                    
                    _serialPort.Write(packet, 0, packet.Length);
                    
                    // CRITICAL FIX: Force flush at OS level (Claude's fix + BaseStream.Flush)
                    _serialPort.BaseStream.Flush();
                    
                    // Wait for OS transmit buffer to empty
                    int waitLoops = 0;
                    while (_serialPort.BytesToWrite > 0 && waitLoops < 100)
                    {
                        Thread.Sleep(1);
                        waitLoops++;
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] BytesToWrite after Write: {_serialPort.BytesToWrite} (waited {waitLoops}ms)");
                    SmartPayDebug.Log($"  BytesToWrite after Write: {_serialPort.BytesToWrite} (waited {waitLoops}ms)");
                    
                    System.Diagnostics.Debug.WriteLine($"[SmartPay] Write complete, waiting for ACK/NAK...");
                    
                    int packetAck;
                    try
                    {
                        packetAck = _serialPort.ReadByte();
                        System.Diagnostics.Debug.WriteLine($"[SmartPay] Packet response: 0x{packetAck:X2} ({CtrlName((byte)packetAck)})");
                        System.Console.WriteLine($"[SmartPay] RX: 0x{packetAck:X2} ({CtrlName((byte)packetAck)})");
                        SmartPayDebug.Log($"  RX: 0x{packetAck:X2} ({CtrlName((byte)packetAck)})");
                    }
                    catch (TimeoutException)
                    {
                        SmartPayDebug.Log("  TIMEOUT - no response to packet");
                        Thread.Sleep(500);
                        continue; // Retry
                    }
                    
                    if (packetAck == SmartPayProtocol.NAK) // 0x15
                    {
                        // Spec: NAK after packet means retry
                        SmartPayDebug.Log("  NAK received - packet rejected by terminal!");
                        SmartPayDebug.Log("  This usually means CRC error or malformed packet.");
                        
                        // Check if terminal sends any diagnostic bytes after NAK
                        Thread.Sleep(200);
                        int diagBytes = _serialPort.BytesToRead;
                        if (diagBytes > 0)
                        {
                            byte[] diag = new byte[diagBytes];
                            _serialPort.Read(diag, 0, diagBytes);
                            SmartPayDebug.Log($"  Diagnostic bytes after NAK: {Hex(diag)}");
                        }
                        
                        SmartPayDebug.Log("  Will retry in 500ms...");
                        Thread.Sleep(500);
                        continue;
                    }
                    
                    if (packetAck != SmartPayProtocol.ACK) // Not 0x06
                    {
                        SmartPayDebug.Log($"  UNEXPECTED: expected ACK (0x06), got 0x{packetAck:X2} ({CtrlName((byte)packetAck)})");
                        throw new FiscalDeviceException($"Expected ACK after packet, got 0x{packetAck:X2}");
                    }
                    
                    // ACK received - now read response
                    SmartPayDebug.Log("  Packet ACKed! Reading terminal response...");
                    responseData = ReadResponsePacket();
                    
                    // Verify response CRC
                    if (!ValidateResponseCRC(responseData, out byte[] tlvData))
                    {
                        // Spec: NAK bad response, terminal will resend
                        SmartPayDebug.Log("  Response CRC bad - sending NAK to request resend...");
                        _serialPort.Write(new byte[] { SmartPayProtocol.NAK }, 0, 1);
                        Thread.Sleep(1000);
                        continue;
                    }
                    
                    // Good response - ACK it
                    SmartPayDebug.Log("  Response OK - sending ACK");
                    _serialPort.Write(new byte[] { SmartPayProtocol.ACK }, 0, 1);
                    SmartPayDebug.Log("SUCCESS!");
                    return tlvData;
                }
                
                // All 3 attempts failed
                SmartPayDebug.Log("ERROR: All packet attempts failed - sending EOT");
                _serialPort.Write(new byte[] { SmartPayProtocol.EOT }, 0, 1);
                throw new FiscalDeviceException("Failed after 3 packet attempts");
            }
            catch (Exception)
            {
                // On any error, try to send EOT to abort session
                try
                {
                    SmartPayDebug.Log("Exception - sending EOT to abort session");
                    _serialPort.Write(new byte[] { SmartPayProtocol.EOT }, 0, 1);
                }
                catch { }
                throw;
            }
        }
    }
    
    private byte[] ReadResponsePacket()
    {
        // Read STX
        int stx = _serialPort.ReadByte();
        if (stx != SmartPayProtocol.STX)
            throw new FiscalDeviceException($"Expected STX, got 0x{stx:X2}");
        
        // Read length (2 bytes, MSB first)
        byte[] lenBytes = new byte[2];
        lenBytes[0] = (byte)_serialPort.ReadByte();
        lenBytes[1] = (byte)_serialPort.ReadByte();
        ushort dataLen = (ushort)((lenBytes[0] << 8) | lenBytes[1]);
        
        SmartPayDebug.Log($"  Response length: {dataLen}");
        
        // Read data + ETX + CRC
        byte[] respData = new byte[dataLen + 3]; // data + ETX + CRC(2)
        int totalRead = 0;
        while (totalRead < respData.Length)
        {
            totalRead += _serialPort.Read(respData, totalRead, respData.Length - totalRead);
        }
        
        SmartPayDebug.Log($"  RX response: {Hex(respData)}");
        
        // Verify ETX
        if (respData[dataLen] != SmartPayProtocol.ETX)
            throw new FiscalDeviceException("Missing ETX in response");
        
        return respData;
    }
    
    private bool ValidateResponseCRC(byte[] responseData, out byte[] tlvData)
    {
        // responseData = [data bytes...][ETX][CRC LSB][CRC MSB]
        int dataLen = responseData.Length - 3;
        
        tlvData = new byte[dataLen];
        Array.Copy(responseData, 0, tlvData, 0, dataLen);
        
        ushort receivedCrc = (ushort)(responseData[dataLen + 1] | (responseData[dataLen + 2] << 8));
        ushort computedCrc = Crc16Buypass.CalculateValue(tlvData);
        
        SmartPayDebug.Log($"  CRC: received=0x{receivedCrc:X4}, computed=0x{computedCrc:X4}");
        
        return receivedCrc == computedCrc;
    }

    public void Disconnect()
    {
        lock (_lock)
        {
            if (_serialPort != null)
            {
                try
                {
                    // Spec: Send EOT to properly close session
                    if (_serialPort.IsOpen)
                    {
                        SmartPayDebug.Log("Sending EOT before disconnect...");
                        try { _serialPort.Write(new byte[] { SmartPayProtocol.EOT }, 0, 1); } catch { }
                        Thread.Sleep(100);
                        
                        _serialPort.DiscardInBuffer();
                        _serialPort.DiscardOutBuffer();
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                }
                catch { /* ignore */ }
                finally
                {
                    _serialPort = null;
                    Thread.Sleep(300);
                }
            }
        }
    }

    private static string Hex(byte[] data)
    {
        return string.Join(" ", data.Select(b => $"{b:X2}"));
    }

    private static string CtrlName(byte b)
    {
        return b switch
        {
            0x02 => "STX",
            0x03 => "ETX",
            0x04 => "EOT",
            0x05 => "ENQ",
            0x06 => "ACK",
            0x15 => "NAK",
            _ => $"0x{b:X2}"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        Disconnect();
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
