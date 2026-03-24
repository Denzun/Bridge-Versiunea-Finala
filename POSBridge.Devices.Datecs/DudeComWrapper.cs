using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;
using POSBridge.Core;

namespace POSBridge.Devices.Datecs;

/// <summary>
/// Wrapper for DUDE COM Server (Datecs.FiscalDevice).
/// Implements the mandatory 5-step workflow: Clear → Input → Execute → CheckError → Output
/// 
/// Reference: DUDE Documentation (UserManual_EN.pdf, CommandsList.xls, ErrorCodes.xls)
/// </summary>
public class DudeComWrapper : IDisposable
{
    private dynamic? _device;
    private bool _disposed;
    private bool _connected;

    /// <summary>
    /// Gets the last error code from the device.
    /// 0 = Success
    /// Negative (ex: -33022) = COM/Driver error
    /// Positive (ex: 100101) = Device error (requires service)
    /// </summary>
    public int ErrorCode => _device?.lastError_Code ?? -1;

    /// <summary>
    /// Gets the last error message.
    /// </summary>
    public string ErrorMessage => _device?.lastError_Message ?? "Device not initialized";

    /// <summary>
    /// Gets connection status.
    /// </summary>
    public bool IsConnected => _connected && _device != null;

    /// <summary>
    /// Initializes the DUDE COM object.
    /// ProgID: "dude.CFD_DUDE" (CORRECTED - nu "Datecs.FiscalDevice")
    /// </summary>
    public void Initialize()
    {
        if (_device != null)
            return;

        try
        {
            // Create COM object
            // ProgID correct: "dude.CFD_DUDE" (CLSID: {A5A6DCE0-449A-43FC-B31C-3CE5442B1CCF})
            Type? type = Type.GetTypeFromProgID("dude.CFD_DUDE");
            if (type == null)
                throw new COMException("DUDE COM Server not registered. Install DUDE from C:\\Program Files (x86)\\Datecs Applications\\DUDE\\dude.exe");

            _device = Activator.CreateInstance(type);
            
            if (_device == null)
                throw new COMException("Failed to create dude.CFD_DUDE COM object");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to initialize DUDE COM Server. " +
                "Ensure DUDE is installed and application runs as x86.", ex);
        }
    }

    /// <summary>
    /// Connects to the fiscal device.
    /// </summary>
    /// <param name="portName">COM port (ex: COM3, COM5)</param>
    /// <param name="baudRate">Baud rate (default: 115200)</param>
    /// <param name="protocol">Protocol type (not used in this implementation)</param>
    public void Connect(string portName = "COM5", int baudRate = 115200, string protocol = "STX/ETX")
    {
        if (_device == null)
            throw new InvalidOperationException("Call Initialize() first");

        if (_connected)
            return;

        ConnectionLogger.WriteSection($"CONEXIUNE SERIAL - {portName} @ {baudRate}");
        try
        {
            int? TryExtractPortNumber(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return null;

                name = name.Trim();
                if (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(name.Substring(3), out int number))
                        return number;
                    return null;
                }

                if (int.TryParse(name, out int directNumber))
                    return directNumber;

                return null;
            }

            bool TryConnectOnPort(int portNumber)
            {
                int errorCode;

                // Close any existing connection first (port might be busy)
                try
                {
                    _device.close_Connection();
                }
                catch
                {
                    // Ignore close errors; continue with fresh connection attempt
                }

                errorCode = _device.set_TransportType(0); // 0 = RS232, 1 = TCP/IP
                if (errorCode != 0)
                {
                    // Some drivers return -1 with "No error" or "Device not connected" even if transport is set
                    var msg = _device.lastError_Message?.ToString() ?? string.Empty;
                    var ignorable = errorCode == -1 &&
                                    (msg.Equals("No error", StringComparison.OrdinalIgnoreCase) ||
                                     msg.Equals("Device not connected", StringComparison.OrdinalIgnoreCase));
                    if (!ignorable)
                        throw new InvalidOperationException($"set_TransportType failed: {errorCode} - {msg}");
                }

                // Some DUDE builds accept these properties as integers
                try { _device.rs232_ComPort = portNumber; } catch { }
                try { _device.rs232_BaudRate = baudRate; } catch { }

                errorCode = _device.set_RS232(portNumber, baudRate);
                if (errorCode != 0)
                    throw new InvalidOperationException($"set_RS232 failed: {errorCode} - {_device.lastError_Message}");

                Thread.Sleep(150);
                errorCode = _device.open_Connection();
                if (errorCode != 0)
                    throw new InvalidOperationException($"open_Connection failed: {errorCode} - {_device.lastError_Message}");

                return _device.connected_ToDevice == true;
            }

            var attemptedPorts = new List<string>();
            int? primaryPortNumber = TryExtractPortNumber(portName);
            if (primaryPortNumber == null)
                throw new ArgumentException($"Invalid COM port: {portName}. Expected format: COM5 or 5");

            attemptedPorts.Add($"COM{primaryPortNumber}");
            ConnectionLogger.Write($"  Încercare port: COM{primaryPortNumber}");
            if (TryConnectOnPort(primaryPortNumber.Value))
            {
                _connected = true;
                ConnectionLogger.Write($"  ✓ Conectat pe COM{primaryPortNumber}");
                return;
            }

            ConnectionLogger.Write($"  COM{primaryPortNumber} eșuat, încercare porturi alternative...");
            // Fallback: try other available COM ports
            foreach (var available in System.IO.Ports.SerialPort.GetPortNames())
            {
                int? number = TryExtractPortNumber(available);
                if (number == null || number == primaryPortNumber)
                    continue;

                attemptedPorts.Add($"COM{number}");
                if (TryConnectOnPort(number.Value))
                {
                    _connected = true;
                    return;
                }
            }

            throw new InvalidOperationException(
                "Device reported not connected after open_Connection(). " +
                $"Ports tried: {string.Join(", ", attemptedPorts)}");
        }
        catch (Exception ex)
        {
            ConnectionLogger.Write($"  ✗ EROARE Serial: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to connect to device on {portName} at {baudRate} baud. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Connects to the fiscal device via TCP/IP (Ethernet/WiFi).
    /// </summary>
    /// <param name="ipAddress">IP address (ex: 192.168.1.219)</param>
    /// <param name="port">TCP port (default: 9100 AppSocket, 3999 pentru DP-25X)</param>
    public void ConnectTCP(string ipAddress, int port = 9100)
    {
        if (_device == null)
            throw new InvalidOperationException("Call Initialize() first");

        if (_connected)
        {
            ConnectionLogger.Write($"  Already connected, skipping.");
            return;
        }

        ConnectionLogger.WriteSection($"CONEXIUNE TCP/IP - {ipAddress}:{port}");
        System.Diagnostics.Debug.WriteLine($"[DUDE] ConnectTCP called: {ipAddress}:{port}");
        
        try
        {
            // Close any existing connection first
            try
            {
                _device.close_Connection();
                ConnectionLogger.Write("  close_Connection() - OK");
            }
            catch (Exception cx) { ConnectionLogger.Write($"  close_Connection() - {cx.Message}"); }

            // Set transport type to TCP/IP (1)
            System.Diagnostics.Debug.WriteLine($"[DUDE] Calling set_TransportType(1)...");
            int errorCode = _device.set_TransportType(1); // 0 = RS232, 1 = TCP/IP
            ConnectionLogger.Write($"  set_TransportType(1=TCP/IP) -> {errorCode} ({_device.lastError_Message})");
            System.Diagnostics.Debug.WriteLine($"[DUDE] set_TransportType returned: {errorCode}, msg: {_device.lastError_Message}");
            
            if (errorCode != 0)
            {
                var msg = _device.lastError_Message?.ToString() ?? string.Empty;
                var ignorable = errorCode == -1 &&
                                (msg.Equals("No error", StringComparison.OrdinalIgnoreCase) ||
                                 msg.Equals("Device not connected", StringComparison.OrdinalIgnoreCase));
                if (!ignorable)
                    throw new InvalidOperationException($"set_TransportType(TCP) failed: {errorCode} - {msg}");
            }

            // Set TCP/IP parameters
            System.Diagnostics.Debug.WriteLine($"[DUDE] Calling set_TCPIP({ipAddress}, {port})...");
            errorCode = _device.set_TCPIP(ipAddress, port);
            ConnectionLogger.Write($"  set_TCPIP({ipAddress}, {port}) -> {errorCode}");
            System.Diagnostics.Debug.WriteLine($"[DUDE] set_TCPIP returned: {errorCode}");
            
            if (errorCode != 0)
                throw new InvalidOperationException($"set_TCPIP failed: {errorCode} - {_device.lastError_Message}");

            Thread.Sleep(200);
            System.Diagnostics.Debug.WriteLine($"[DUDE] Calling open_Connection()...");
            errorCode = _device.open_Connection();
            ConnectionLogger.Write($"  open_Connection() -> {errorCode} ({_device.lastError_Message})");
            System.Diagnostics.Debug.WriteLine($"[DUDE] open_Connection returned: {errorCode}, msg: {_device.lastError_Message}");
            
            if (errorCode != 0)
                throw new InvalidOperationException($"open_Connection failed: {errorCode} - {_device.lastError_Message}");

            System.Diagnostics.Debug.WriteLine($"[DUDE] Checking connected_ToDevice...");
            if (_device.connected_ToDevice != true)
            {
                ConnectionLogger.Write($"  ✗ connected_ToDevice=false (dispozitivul nu răspunde)");
                throw new InvalidOperationException("Connected but device not responding");
            }

            _connected = true;
            ConnectionLogger.Write($"  ✓ Conectat la {ipAddress}:{port}");
            System.Diagnostics.Debug.WriteLine($"[DUDE] Connection SUCCESS");
        }
        catch (Exception ex)
        {
            ConnectionLogger.Write($"  ✗ EROARE TCP: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DUDE] Connection FAILED: {ex.Message}");
            throw new InvalidOperationException(
                $"Failed to connect to device via TCP/IP at {ipAddress}:{port}. " +
                $"Error: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Tries to connect via TCP/IP using common Datecs ports automatically.
    /// Tries ports in order with quick timeouts to find the working one
    /// </summary>
    /// <param name="ipAddress">IP address (ex: 192.168.1.219)</param>
    /// <returns>The port number that succeeded, or null if all failed</returns>
    public int? ConnectTCPAutoPort(string ipAddress)
    {
        if (_device == null)
            throw new InvalidOperationException("Call Initialize() first");

        if (_connected)
            return null;

        ConnectionLogger.WriteSection($"SCANARE AUTO PORT TCP - {ipAddress}");

        // Extended list of common Datecs/printer TCP ports
        // Prioritized based on Datecs documentation and industry standards
        // PORT 3999 confirmed for Datecs DP-25X
        int[] commonPorts = { 
            3999,  // Datecs DP-25X confirmed working port (PRIORITY #1)
            9100,  // Standard AppSocket/JetDirect printer port (MOST COMMON)
            4999,  // Datecs FP specific (documented in some manuals)
            9999,  // Datecs alternative
            8000,  // Generic device port
            4000,  // Datecs specific
            5000,  // Common fiscal device port
            9001,  // Alternative printer
            515,   // LPR/LPD
            9200,  // Alternative
            8080,  // HTTP alternative
            1024,  // Common TCP service port
            5001,  // Fiscal printer alternative
            23     // Telnet (some old printers)
        };
        
        foreach (int port in commonPorts)
        {
            try
            {
                try { _device.close_Connection(); } catch { }

                int errorCode = _device.set_TransportType(1); // TCP/IP
                if (errorCode != 0)
                {
                    var msg = _device.lastError_Message?.ToString() ?? string.Empty;
                    var ignorable = errorCode == -1 &&
                                    (msg.Equals("No error", StringComparison.OrdinalIgnoreCase) ||
                                     msg.Equals("Device not connected", StringComparison.OrdinalIgnoreCase));
                    if (!ignorable)
                    {
                        ConnectionLogger.Write($"  Port {port}: set_TransportType eșuat ({errorCode})");
                        continue;
                    }
                }

                errorCode = _device.set_TCPIP(ipAddress, port);
                if (errorCode != 0)
                {
                    ConnectionLogger.Write($"  Port {port}: set_TCPIP eșuat ({errorCode})");
                    continue;
                }

                Thread.Sleep(300);
                errorCode = _device.open_Connection();
                if (errorCode != 0)
                {
                    ConnectionLogger.Write($"  Port {port}: open_Connection eșuat ({errorCode} - {_device.lastError_Message})");
                    continue;
                }

                Thread.Sleep(200);
                if (_device.connected_ToDevice == true)
                {
                    _connected = true;
                    ConnectionLogger.Write($"  ✓ PORT GĂSIT: {port} - conectat la {ipAddress}:{port}");
                    return port;
                }
                ConnectionLogger.Write($"  Port {port}: connected_ToDevice=false");
            }
            catch (Exception ex)
            {
                ConnectionLogger.Write($"  Port {port}: excepție - {ex.Message}");
                continue;
            }
        }

        ConnectionLogger.Write("  ✗ Niciun port funcțional");
        return null;
    }

    /// <summary>
    /// Disconnects from the fiscal device.
    /// </summary>
    public void Disconnect()
    {
        if (_device == null || !_connected)
            return;

        try
        {
            _device.close_Connection();
            _connected = false;
        }
        catch
        {
            // Ignore disconnect errors
        }
    }

    /// <summary>
    /// Force closes the COM transport even if internal state is inconsistent.
    /// Useful during application shutdown to release locked COM ports.
    /// </summary>
    public void ForceCloseConnection()
    {
        if (_device == null)
            return;

        try
        {
            _device.close_Connection();
        }
        catch
        {
            // Best effort during shutdown
        }
        finally
        {
            _connected = false;
        }
    }

    /// <summary>
    /// STEP 1: Clear input buffer (mandatory before each command).
    /// Note: DUDE doesn't have explicit ClearInput, parameters are cleared automatically.
    /// </summary>
    public void ClearInput()
    {
        if (_device == null)
            throw new InvalidOperationException("Device not initialized");

        // No explicit clear needed in DUDE - it clears automatically before each execute
    }

    /// <summary>
    /// STEP 2: Set input parameter.
    /// </summary>
    /// <param name="parameterName">Parameter name from CommandsList.xls</param>
    /// <param name="value">Parameter value</param>
    public void Input(string commandName, string parameterName, object value)
    {
        if (_device == null)
            throw new InvalidOperationException("Device not initialized");

        // Pass value as-is (DUDE expects typed variants, not always strings)
        _device.set_InputParam_ByName(commandName, parameterName, value ?? string.Empty);
    }

    /// <summary>
    /// STEP 3: Execute command by name.
    /// </summary>
    /// <param name="commandName">Command name from CommandsList.xls (ex: "Fiscal_Sale")</param>
    public void ExecuteCommand(string commandName)
    {
        if (_device == null)
            throw new InvalidOperationException("Device not initialized");

        if (!_connected)
            throw new InvalidOperationException("Device not connected. Call Connect() first.");

        _device.execute_Command_ByName(commandName);
    }

    /// <summary>
    /// STEP 4: Check error code after command execution.
    /// Throws exception if error occurred.
    /// </summary>
    public void CheckError()
    {
        int errorCode = ErrorCode;
        
        if (errorCode == 0)
            return; // Success

        string errorMessage = ErrorMessage;

        if (errorCode < 0)
        {
            // COM/Driver error
            throw new DudeComException(
                $"DUDE COM Error {errorCode}: {errorMessage}", 
                errorCode, 
                DudeErrorType.ComError);
        }
        else
        {
            // Device error
            throw new DudeComException(
                $"Device Error {errorCode}: {errorMessage}", 
                errorCode, 
                DudeErrorType.DeviceError);
        }
    }

    /// <summary>
    /// STEP 5: Get output parameter after successful command execution.
    /// </summary>
    /// <param name="parameterName">Parameter name from CommandsList.xls</param>
    /// <returns>Parameter value as string</returns>
    public string Output(string commandName, string parameterName)
    {
        if (_device == null)
            throw new InvalidOperationException("Device not initialized");

        try
        {
            object value = string.Empty;
            _device.get_OutputParam_ByName(commandName, parameterName, ref value);
            return value?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Universal safe execution method following the mandatory 5-step workflow.
    /// </summary>
    /// <param name="commandName">Command name (ex: "Fiscal_Sale")</param>
    /// <param name="parameters">Input parameters (key-value pairs)</param>
    /// <returns>True if successful</returns>
    public bool ExecuteSafe(string commandName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            // STEP 1: Clear
            ClearInput();

            // STEP 2: Input
            if (parameters != null)
            {
                foreach (var param in parameters)
                {
                    Input(commandName, param.Key, param.Value);
                }
            }

            // STEP 3: Execute
            ExecuteCommand(commandName);

            // STEP 4: Check Error
            CheckError();

            return true;
        }
        catch (DudeComException)
        {
            throw; // Re-throw DUDE errors
        }
        catch (Exception ex)
        {
            throw new DudeComException(
                $"Unexpected error executing command '{commandName}': {ex.Message}",
                -1,
                DudeErrorType.UnexpectedError,
                ex);
        }
    }

    /// <summary>
    /// Executes a raw command by command number (for commands not available via execute_Command_ByName).
    /// Uses the low-level execute_Command method with tab-separated input parameters.
    /// </summary>
    /// <param name="commandNumber">Command number (decimal), e.g. 94 for FM report by dates</param>
    /// <param name="inputData">Tab-separated input parameters per FP Protocol</param>
    /// <returns>Raw output string from the device</returns>
    public string ExecuteRawCommand(int commandNumber, string inputData = "")
    {
        if (_device == null)
            throw new InvalidOperationException("Device not initialized");

        if (!_connected)
            throw new InvalidOperationException("Device not connected. Call Connect() first.");

        try
        {
            object outputValue = string.Empty;
            _device.execute_Command(commandNumber, inputData, ref outputValue);

            int errorCode = ErrorCode;
            if (errorCode != 0)
            {
                string errorMessage = ErrorMessage;
                if (errorCode < 0)
                    throw new DudeComException($"DUDE COM Error {errorCode}: {errorMessage}", errorCode, DudeErrorType.ComError);
                else
                    throw new DudeComException($"Device Error {errorCode}: {errorMessage}", errorCode, DudeErrorType.DeviceError);
            }

            return outputValue?.ToString() ?? string.Empty;
        }
        catch (DudeComException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DudeComException(
                $"Unexpected error executing raw command {commandNumber}: {ex.Message}",
                -1,
                DudeErrorType.UnexpectedError,
                ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        Disconnect();

        if (_device != null)
        {
            try
            {
                Marshal.ReleaseComObject(_device);
            }
            catch
            {
                // Ignore cleanup errors
            }
            _device = null;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~DudeComWrapper()
    {
        Dispose();
    }
}

/// <summary>
/// DUDE COM exception with error code.
/// </summary>
public class DudeComException : Exception
{
    public int ErrorCode { get; }
    public DudeErrorType ErrorType { get; }

    public DudeComException(string message, int errorCode, DudeErrorType errorType, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorType = errorType;
    }
}

public enum DudeErrorType
{
    ComError,        // Negative error codes (driver/connection issues)
    DeviceError,     // Positive error codes (device/fiscal issues)
    UnexpectedError  // Other exceptions
}
