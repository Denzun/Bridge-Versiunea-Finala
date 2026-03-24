using System.IO.Ports;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// Serial/COM wrapper for Incotex Succes M7.
/// Implements NAK (retransmit) and SYN (wait 60ms) handling per protocol.
/// </summary>
internal sealed class IncotexSerialWrapper : IDisposable
{
    private SerialPort? _port;
    private readonly object _lock = new();
    
    private const int ResponseTimeoutMs = 500;
    private const int SynWaitMs = 60;
    private const int MaxNakRetries = 5;

    public bool IsConnected => _port?.IsOpen == true;
    public string? PortName => _port?.PortName;

    public void Connect(string portName, int baudRate, TimeSpan timeout)
    {
        lock (_lock)
        {
            if (IsConnected)
                throw new FiscalDeviceException("Already connected to Incotex (Serial).");

            try
            {
                _port = new SerialPort(portName, baudRate, Parity.None, dataBits: 8, StopBits.One)
                {
                    Handshake = Handshake.None,
                    ReadTimeout = Math.Clamp((int)timeout.TotalMilliseconds, 500, 10000),
                    WriteTimeout = Math.Clamp((int)timeout.TotalMilliseconds, 500, 10000),
                    DtrEnable = true,
                    RtsEnable = true
                };

                _port.Open();
                try { _port.DiscardInBuffer(); } catch { }
                try { _port.DiscardOutBuffer(); } catch { }
            }
            catch (Exception ex)
            {
                Cleanup();
                throw new FiscalDeviceException(
                    $"Failed to open Incotex COM port '{portName}' (baud {baudRate}). {ex.Message}", ex);
            }
        }
    }

    public void Disconnect()
    {
        lock (_lock) Cleanup();
    }

    /// <summary>
    /// Execute command with Succes M7 NAK/SYN handling.
    /// NAK (0x15): retransmit same packet. SYN (0x16): wait 60ms, read again.
    /// </summary>
    public byte[] ExecuteCommand(byte[] command, int maxResponseLength, TimeSpan timeout)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        if (command.Length == 0) throw new ArgumentException("Command is empty.", nameof(command));

        lock (_lock)
        {
            if (!IsConnected || _port == null)
                throw new FiscalDeviceException("Not connected to Incotex (Serial).");

            int toMs = (int)Math.Clamp(timeout.TotalMilliseconds, ResponseTimeoutMs, 10000);
            _port.ReadTimeout = toMs;
            _port.WriteTimeout = toMs;

            int nakCount = 0;
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(toMs);

            while (true)
            {
                try { _port.DiscardInBuffer(); } catch { }
                _port.Write(command, 0, command.Length);

                while (DateTime.UtcNow < deadline)
                {
                    int b;
                    try { b = _port.ReadByte(); }
                    catch (TimeoutException)
                    {
                        if (nakCount < MaxNakRetries)
                        {
                            nakCount++;
                            break; // retransmit
                        }
                        throw new FiscalDeviceException("Incotex serial read timeout.");
                    }

                    if (b < 0) break;

                    if (b == IncotexProtocol.NAK)
                    {
                        if (nakCount >= MaxNakRetries)
                            throw new FiscalDeviceException("Incotex NAK after max retries.");
                        nakCount++;
                        break;
                    }

                    if (b == IncotexProtocol.SYN)
                    {
                        Thread.Sleep(SynWaitMs);
                        continue;
                    }

                    if (b == 0x01) // Start of packet
                    {
                        var buffer = new List<byte> { (byte)b };
                        int remainingTimeout = Math.Max(200, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                        _port.ReadTimeout = Math.Min(100, remainingTimeout);
                        while (buffer.Count < maxResponseLength && DateTime.UtcNow < deadline)
                        {
                            try
                            {
                                int bb = _port.ReadByte();
                                if (bb < 0) break;
                                buffer.Add((byte)bb);
                                if (bb == 0x03) return buffer.ToArray();
                            }
                            catch (TimeoutException)
                            {
                                if (buffer.Count > 0 && buffer[^1] == 0x03) return buffer.ToArray();
                            }
                        }
                        if (buffer.Count > 0) return buffer.ToArray();
                    }
                }
            }

            throw new FiscalDeviceException("No response from Incotex (Serial).");
        }
    }

    private void Cleanup()
    {
        try
        {
            if (_port != null)
            {
                try { if (_port.IsOpen) _port.Close(); } catch { }
                try { _port.Dispose(); } catch { }
                _port = null;
            }
        }
        catch { }
    }

    public void Dispose() { Disconnect(); GC.SuppressFinalize(this); }
}
