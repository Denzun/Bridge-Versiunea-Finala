using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using POSBridge.Abstractions.Exceptions;

namespace POSBridge.Devices.Incotex;

/// <summary>
/// USB communication wrapper for Incotex fiscal devices using native WinUSB API.
/// STM32 CDC composite device: interface 0 = CDC Control, interface 1 = CDC Data (bulk endpoints).
/// Endpoint layout from Device Monitoring Studio capture of FiscalNet: OUT=0x03, IN=0x81.
/// </summary>
public class IncotexUsbWrapper : IDisposable
{
    private SafeFileHandle? _deviceHandle;
    private IntPtr _winUsbHandle;
    private IntPtr _dataInterfaceHandle;
    private bool _disposed;
    private readonly object _lock = new();
    
    private const int IncotexVendorId = 0x0483;
    private const int IncotexProductId = 0x5740;
    
    // Defaults from USB capture: CDC Data interface bulk endpoints
    private byte _bulkOutPipeId = 0x03;
    private byte _bulkInPipeId = 0x81;
    
    private const int DefaultTimeout = 3000;
    private const int DefaultConnectTimeoutMs = 2500;
    
    public bool IsConnected => _deviceHandle != null && !_deviceHandle.IsInvalid && _winUsbHandle != IntPtr.Zero;

    /// <summary>
    /// The WinUSB handle to use for pipe I/O.
    /// If the bulk endpoints live on an associated interface (interface 1), use that handle;
    /// otherwise fall back to the primary handle (interface 0).
    /// </summary>
    private IntPtr IoHandle => _dataInterfaceHandle != IntPtr.Zero ? _dataInterfaceHandle : _winUsbHandle;
    
    #region WinUSB P/Invoke Declarations
    
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        IntPtr enumerator,
        IntPtr hwndParent,
        uint flags);
    
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        uint memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);
    
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        uint deviceInterfaceDetailDataSize,
        out uint requiredSize,
        IntPtr deviceInfoData);
    
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
    
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Initialize(SafeFileHandle deviceHandle, out IntPtr interfaceHandle);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_Free(IntPtr interfaceHandle);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_GetAssociatedInterface(
        IntPtr interfaceHandle,
        byte associatedInterfaceIndex,
        out IntPtr associatedInterfaceHandle);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_WritePipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_ReadPipe(
        IntPtr interfaceHandle,
        byte pipeId,
        byte[] buffer,
        uint bufferLength,
        out uint lengthTransferred,
        IntPtr overlapped);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_GetOverlappedResult(
        IntPtr interfaceHandle,
        IntPtr overlapped,
        out uint numberOfBytesTransferred,
        bool wait);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_SetPipePolicy(
        IntPtr interfaceHandle,
        byte pipeId,
        uint policyType,
        uint valueLength,
        ref uint value);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryInterfaceSettings(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        out USB_INTERFACE_DESCRIPTOR usbAltInterfaceDescriptor);

    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_QueryPipe(
        IntPtr interfaceHandle,
        byte alternateInterfaceNumber,
        byte pipeIndex,
        out WINUSB_PIPE_INFORMATION pipeInformation);

    private enum USBD_PIPE_TYPE : int
    {
        UsbdPipeTypeControl = 0,
        UsbdPipeTypeIsochronous = 1,
        UsbdPipeTypeBulk = 2,
        UsbdPipeTypeInterrupt = 3
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct USB_INTERFACE_DESCRIPTOR
    {
        public byte bLength;
        public byte bDescriptorType;
        public byte bInterfaceNumber;
        public byte bAlternateSetting;
        public byte bNumEndpoints;
        public byte bInterfaceClass;
        public byte bInterfaceSubClass;
        public byte bInterfaceProtocol;
        public byte iInterface;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WINUSB_PIPE_INFORMATION
    {
        public USBD_PIPE_TYPE PipeType;
        public byte PipeId;
        public ushort MaximumPacketSize;
        public byte Interval;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public IntPtr Reserved;
    }
    
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SP_DEVICE_INTERFACE_DETAIL_DATA
    {
        public uint cbSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string DevicePath;
    }
    
    private const uint DIGCF_PRESENT = 0x00000002;
    private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint PIPE_TRANSFER_TIMEOUT = 3;
    private const int ERROR_IO_PENDING = 997;
    
    // Incotex WinUSB interface GUID (from Windows registry DeviceInterfaceGUIDs)
    private static readonly Guid GUID_DEVINTERFACE_INCOTEX = new Guid("ECF70154-40DB-4E2B-8E0B-6D6D754E5486");
    private static readonly Guid GUID_DEVINTERFACE_USB_DEVICE = new Guid("A5DCBF10-6530-11D2-901F-00C04FB951ED");
    
    #endregion

    private static void UsbLog(string msg)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [WinUSB] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        try
        {
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "usb_debug.log"), line + Environment.NewLine);
        }
        catch { }
    }
    
    public void Connect()
    {
        lock (_lock)
        {
            Cleanup();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                UsbLog("FindIncotexCandidateDevicePaths...");
                var candidatePaths = FindIncotexCandidateDevicePaths();
                UsbLog($"Found {candidatePaths.Count} path(s) in {sw.ElapsedMilliseconds}ms");

                if (candidatePaths.Count == 0)
                {
                    throw new FiscalDeviceException(
                        "Incotex device not found in SetupAPI enumeration.\n\n" +
                        "Check:\n" +
                        "1. Incotex is connected via USB\n" +
                        $"2. Device VID:PID = {IncotexVendorId:X4}:{IncotexProductId:X4}\n\n" +
                        "Note:\n" +
                        "• WinUSB transport works ONLY if the device is bound to WinUSB (Microsoft)\n" +
                        "• If the device shows up as a COM port, use the Serial/COM connection instead");
                }

                var attemptErrors = new List<string>();

                foreach (string devicePath in candidatePaths)
                {
                    SafeFileHandle? handle = null;
                    IntPtr winUsb = IntPtr.Zero;

                    try
                    {
                        UsbLog($"CreateFile({devicePath})...");
                        long t0 = sw.ElapsedMilliseconds;
                        handle = CreateFile(
                            devicePath,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            FILE_FLAG_OVERLAPPED,
                            IntPtr.Zero);
                        UsbLog($"CreateFile returned in {sw.ElapsedMilliseconds - t0}ms, valid={!handle.IsInvalid}");

                        if (handle.IsInvalid)
                        {
                            int error = Marshal.GetLastWin32Error();
                            attemptErrors.Add($"CreateFile failed (err {error}) for path: {devicePath}");
                            continue;
                        }

                        UsbLog($"WinUsb_Initialize (timeout={DefaultConnectTimeoutMs}ms)...");
                        long t1 = sw.ElapsedMilliseconds;
                        if (!TryWinUsbInitializeWithTimeout(handle, DefaultConnectTimeoutMs, out winUsb, out int initErr, out bool timedOut))
                        {
                            UsbLog($"WinUsb_Initialize FAILED in {sw.ElapsedMilliseconds - t1}ms, timedOut={timedOut}, err={initErr}");
                            if (timedOut)
                                attemptErrors.Add($"WinUsb_Initialize timed out after {DefaultConnectTimeoutMs}ms for path: {devicePath}");
                            else
                                attemptErrors.Add($"WinUsb_Initialize failed (err {initErr}) for path: {devicePath}");

                            try { handle.Close(); } catch { }
                            continue;
                        }
                        UsbLog($"WinUsb_Initialize OK in {sw.ElapsedMilliseconds - t1}ms");

                        _deviceHandle = handle;
                        _winUsbHandle = winUsb;
                        break;
                    }
                    catch (Exception ex)
                    {
                        attemptErrors.Add($"Exception for path {devicePath}: {ex.Message}");
                        try { if (winUsb != IntPtr.Zero) WinUsb_Free(winUsb); } catch { }
                        try { handle?.Close(); } catch { }
                    }
                }

                if (!IsConnected)
                {
                    string details = string.Join("\n", attemptErrors.Take(6));
                    if (attemptErrors.Count > 6) details += $"\n... (+{attemptErrors.Count - 6} more)";

                    bool hasAccessDenied = attemptErrors.Any(e => e.Contains("err 5)"));
                    bool fiscalNetRunning = IncotexComPortFinder.IsFiscalNetRunning();

                    string cause;
                    if (hasAccessDenied && fiscalNetRunning)
                    {
                        cause = "FiscalNet ruleaza si blocheaza dispozitivul.\n" +
                                "Inchideti FiscalNet inainte de a conecta POS Bridge.";
                    }
                    else if (hasAccessDenied)
                    {
                        cause = "Acces refuzat la dispozitiv (error 5).\n\n" +
                                "Solutii:\n" +
                                "• Inchideti alte aplicatii care folosesc Incotex (FiscalNet, etc.)\n" +
                                "• Rulati POS Bridge ca Administrator\n" +
                                "• Deconectati si reconectati cablul USB";
                    }
                    else
                    {
                        cause = "Interfata WinUSB nu a putut fi deschisa.\n\n" +
                                "Solutii:\n" +
                                "• Verificati ca driverul WinUSB este instalat (Device Manager)\n" +
                                "• Deconectati si reconectati cablul USB\n" +
                                "• Rulati POS Bridge ca Administrator";
                    }

                    throw new FiscalDeviceException(
                        $"Dispozitiv Incotex detectat dar conexiunea a esuat.\n\n{cause}\n\n" +
                        $"Incercari: {candidatePaths.Count} cale(i).\n" +
                        $"Detalii:\n{details}");
                }

                // STM32 CDC ACM: interface 0 = Control, interface 1 = Data (bulk endpoints).
                // We must claim interface 1 for bulk I/O.
                AcquireDataInterface();
                UsbLog($"Using pipes: OUT=0x{_bulkOutPipeId:X2}, IN=0x{_bulkInPipeId:X2}, ioHandle={(IoHandle == _dataInterfaceHandle ? "dataIface" : "primary")}");

                uint timeout = DefaultTimeout;
                WinUsb_SetPipePolicy(IoHandle, _bulkOutPipeId, PIPE_TRANSFER_TIMEOUT, 4, ref timeout);
                WinUsb_SetPipePolicy(IoHandle, _bulkInPipeId, PIPE_TRANSFER_TIMEOUT, 4, ref timeout);
                
                Thread.Sleep(100);
                UsbLog($"Connect complete in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) when (ex is not FiscalDeviceException)
            {
                Cleanup();
                throw new FiscalDeviceException(
                    $"Failed to connect to Incotex device: {ex.Message}\n\n" +
                    "Troubleshooting:\n" +
                    "• WinUSB driver should already be installed\n" +
                    "• Close FiscalNet or other applications\n" +
                    "• Try unplugging and replugging USB cable", ex);
            }
        }
    }

    /// <summary>
    /// STM32 CDC ACM exposes two interfaces: 0 = CDC Control, 1 = CDC Data (bulk endpoints).
    /// WinUsb_Initialize returns interface 0. We need WinUsb_GetAssociatedInterface(0)
    /// to get a handle to interface 1 for bulk I/O.
    /// </summary>
    private void AcquireDataInterface()
    {
        try
        {
            if (WinUsb_GetAssociatedInterface(_winUsbHandle, 0, out IntPtr assocHandle))
            {
                _dataInterfaceHandle = assocHandle;
                UsbLog("Acquired associated interface 1 (CDC Data)");
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                UsbLog($"WinUsb_GetAssociatedInterface failed (err {err}) -- bulk endpoints may be on interface 0");
            }
        }
        catch (Exception ex)
        {
            UsbLog($"AcquireDataInterface exception: {ex.Message}");
        }
    }

    private static bool TryWinUsbInitializeWithTimeout(
        SafeFileHandle deviceHandle,
        int timeoutMs,
        out IntPtr winUsbHandle,
        out int win32Error,
        out bool timedOut)
    {
        winUsbHandle = IntPtr.Zero;
        win32Error = 0;
        timedOut = false;

        IntPtr localHandle = IntPtr.Zero;
        bool localOk = false;
        int localErr = 0;

        var t = Task.Run(() =>
        {
            try
            {
                localOk = WinUsb_Initialize(deviceHandle, out localHandle);
                localErr = Marshal.GetLastWin32Error();
            }
            catch
            {
                localOk = false;
                localErr = Marshal.GetLastWin32Error();
            }
        });

        if (!t.Wait(timeoutMs))
        {
            timedOut = true;
            try { deviceHandle.Close(); } catch { }
            t.Wait(500);
            if (localHandle != IntPtr.Zero)
            {
                try { WinUsb_Free(localHandle); } catch { }
            }
            return false;
        }

        if (!localOk || localHandle == IntPtr.Zero)
        {
            win32Error = localErr;
            if (localHandle != IntPtr.Zero)
            {
                try { WinUsb_Free(localHandle); } catch { }
            }
            return false;
        }

        winUsbHandle = localHandle;
        return true;
    }
    
    private List<string> FindIncotexCandidateDevicePaths()
    {
        var primary = EnumerateDevicePathsByGuid(GUID_DEVINTERFACE_INCOTEX);
        if (primary.Count > 0)
            return primary;

        return EnumerateDevicePathsByGuid(GUID_DEVINTERFACE_USB_DEVICE);
    }

    private List<string> EnumerateDevicePathsByGuid(Guid guid)
    {
        var results = new List<string>();

        IntPtr deviceInfoSet = SetupDiGetClassDevs(
            ref guid,
            IntPtr.Zero,
            IntPtr.Zero,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);

        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet == new IntPtr(-1))
            return results;

        try
        {
            uint memberIndex = 0;
            while (true)
            {
                var interfaceData = new SP_DEVICE_INTERFACE_DATA();
                interfaceData.cbSize = (uint)Marshal.SizeOf(interfaceData);

                if (!SetupDiEnumDeviceInterfaces(
                    deviceInfoSet,
                    IntPtr.Zero,
                    ref guid,
                    memberIndex,
                    ref interfaceData))
                {
                    break;
                }

                SetupDiGetDeviceInterfaceDetail(
                    deviceInfoSet,
                    ref interfaceData,
                    IntPtr.Zero,
                    0,
                    out uint requiredSize,
                    IntPtr.Zero);

                IntPtr detailDataBuffer = Marshal.AllocHGlobal((int)requiredSize);
                try
                {
                    Marshal.WriteInt32(detailDataBuffer, IntPtr.Size == 8 ? 8 : 6);

                    if (SetupDiGetDeviceInterfaceDetail(
                        deviceInfoSet,
                        ref interfaceData,
                        detailDataBuffer,
                        requiredSize,
                        out _,
                        IntPtr.Zero))
                    {
                        string devicePath = Marshal.PtrToStringAuto(IntPtr.Add(detailDataBuffer, 4)) ?? "";
                        if (string.IsNullOrWhiteSpace(devicePath) && IntPtr.Size == 8)
                            devicePath = Marshal.PtrToStringAuto(IntPtr.Add(detailDataBuffer, 8)) ?? "";

                        if (string.IsNullOrWhiteSpace(devicePath))
                            continue;

                        if (devicePath.Contains("VID_0483&PID_5740", StringComparison.OrdinalIgnoreCase) ||
                            (devicePath.Contains("VID_0483", StringComparison.OrdinalIgnoreCase) &&
                             devicePath.Contains("PID_5740", StringComparison.OrdinalIgnoreCase)))
                        {
                            results.Add(devicePath);
                            break;
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailDataBuffer);
                }

                memberIndex++;
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }

        return results;
    }
    
    public void Disconnect()
    {
        lock (_lock)
        {
            Cleanup();
        }
    }
    
    private const int MaxNakRetries = 5;
    private const int SynWaitMs = 60;

    public byte[] ExecuteCommand(byte[] command, int expectedResponseLength = 256)
    {
        lock (_lock)
        {
            if (!IsConnected)
                throw new FiscalDeviceException("Not connected to device");
            
            UsbLog($"ExecuteCommand: writing {command.Length} bytes to pipe 0x{_bulkOutPipeId:X2}...");
            int nakCount = 0;
            int maxResponse = Math.Max(expectedResponseLength, 128);
            while (true)
            {
                try
                {
                    uint bytesWritten = WritePipeOverlapped(_bulkOutPipeId, command, (uint)command.Length);
                    UsbLog($"Write complete: {bytesWritten} bytes");
                    if (bytesWritten != command.Length)
                        throw new FiscalDeviceException($"Incomplete write: sent {bytesWritten}/{command.Length} bytes");

                    var response = new List<byte>(maxResponse);
                    var deadline = DateTime.UtcNow.AddMilliseconds(DefaultTimeout * 3);

                    while (DateTime.UtcNow < deadline)
                    {
                        byte[] chunk = new byte[maxResponse];
                        uint bytesRead = ReadPipeOverlapped(_bulkInPipeId, chunk, (uint)chunk.Length);
                        UsbLog($"Read chunk: {bytesRead} bytes");

                        if (bytesRead == 0)
                            throw new FiscalDeviceException("No response from device");

                        if (bytesRead == 1 && chunk[0] == IncotexProtocol.NAK)
                        {
                            if (nakCount >= MaxNakRetries)
                                throw new FiscalDeviceException("Incotex NAK after max retries.");
                            nakCount++;
                            response.Clear();
                            goto retransmit;
                        }

                        if (bytesRead == 1 && chunk[0] == IncotexProtocol.SYN)
                        {
                            Thread.Sleep(SynWaitMs);
                            continue;
                        }

                        for (int i = 0; i < bytesRead; i++)
                            response.Add(chunk[i]);

                        if (response.Count >= 2 && response[0] == 0x01 && response[^1] == 0x03)
                            return response.ToArray();

                        if (response.Count >= maxResponse)
                            return response.ToArray();
                    }

                    throw new FiscalDeviceException("Timed out waiting complete Incotex response (USB).");
                }
                catch (Exception ex) when (ex is not FiscalDeviceException)
                {
                    throw new FiscalDeviceException($"USB communication error: {ex.Message}", ex);
                }

            retransmit:
                continue;
            }
        }
    }

    private uint WritePipeOverlapped(byte pipeId, byte[] buffer, uint length)
        => PipeIoOverlapped(isRead: false, pipeId, buffer, length);

    private uint ReadPipeOverlapped(byte pipeId, byte[] buffer, uint length)
        => PipeIoOverlapped(isRead: true, pipeId, buffer, length);

    private const uint WAIT_OBJECT_0 = 0;
    private const uint WAIT_TIMEOUT = 258;
    private const int ERROR_TIMEOUT = 1460;
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
    
    [DllImport("winusb.dll", SetLastError = true)]
    private static extern bool WinUsb_AbortPipe(IntPtr InterfaceHandle, byte PipeID);
    
    private uint PipeIoOverlapped(bool isRead, byte pipeId, byte[] buffer, uint length)
    {
        IntPtr ioH = IoHandle;
        if (ioH == IntPtr.Zero)
            throw new FiscalDeviceException("WinUSB handle is invalid (not connected)");

        IntPtr evt = IntPtr.Zero;
        IntPtr ovPtr = IntPtr.Zero;
        uint timeoutMs = (uint)DefaultTimeout;
        string op = isRead ? "USB Read" : "USB Write";

        try
        {
            evt = CreateEvent(IntPtr.Zero, bManualReset: true, bInitialState: false, lpName: null);
            if (evt == IntPtr.Zero)
                throw new FiscalDeviceException($"CreateEvent failed (Error: {Marshal.GetLastWin32Error()})");

            int ovSize = Marshal.SizeOf<OVERLAPPED>();
            ovPtr = Marshal.AllocHGlobal(ovSize);
            for (int i = 0; i < ovSize; i++)
                Marshal.WriteByte(ovPtr, i, 0);
            Marshal.WriteIntPtr(ovPtr, ovSize - IntPtr.Size, evt);

            bool ok;
            uint immediateTransferred = 0;
            if (isRead)
                ok = WinUsb_ReadPipe(ioH, pipeId, buffer, length, out immediateTransferred, ovPtr);
            else
                ok = WinUsb_WritePipe(ioH, pipeId, buffer, length, out immediateTransferred, ovPtr);

            if (ok)
                return immediateTransferred;

            int err = Marshal.GetLastWin32Error();
            if (err != ERROR_IO_PENDING)
                throw new FiscalDeviceException($"{op} failed (Error: {err})");

            uint waitResult = WaitForSingleObject(evt, timeoutMs);
            if (waitResult == WAIT_TIMEOUT)
            {
                try { WinUsb_AbortPipe(ioH, pipeId); } catch { }
                throw new FiscalDeviceException($"{op} timed out after {timeoutMs}ms - device not responding");
            }
            if (waitResult != WAIT_OBJECT_0)
            {
                int waitErr = Marshal.GetLastWin32Error();
                throw new FiscalDeviceException($"{op} wait failed (Result: {waitResult}, Error: {waitErr})");
            }

            if (!WinUsb_GetOverlappedResult(ioH, ovPtr, out uint transferred, wait: true))
            {
                int err2 = Marshal.GetLastWin32Error();
                throw new FiscalDeviceException($"{op} overlapped result failed (Error: {err2})");
            }

            return transferred;
        }
        finally
        {
            if (ovPtr != IntPtr.Zero)
            {
                try { Marshal.FreeHGlobal(ovPtr); } catch { }
            }
            if (evt != IntPtr.Zero)
            {
                try { CloseHandle(evt); } catch { }
            }
        }
    }
    
    public string GetDeviceInfo()
    {
        if (!IsConnected)
            return "Not connected";
        
        return $"Incotex FP Device\n" +
               $"VID: 0x{IncotexVendorId:X4}\n" +
               $"PID: 0x{IncotexProductId:X4}\n" +
               $"Driver: WinUSB (Native Windows)\n" +
               $"Connection: USB Bulk Transfer\n" +
               $"Pipes: OUT=0x{_bulkOutPipeId:X2}, IN=0x{_bulkInPipeId:X2}";
    }
    
    private void Cleanup()
    {
        try
        {
            if (_dataInterfaceHandle != IntPtr.Zero)
            {
                WinUsb_Free(_dataInterfaceHandle);
                _dataInterfaceHandle = IntPtr.Zero;
            }

            if (_winUsbHandle != IntPtr.Zero)
            {
                WinUsb_Free(_winUsbHandle);
                _winUsbHandle = IntPtr.Zero;
            }
            
            _deviceHandle?.Close();
            _deviceHandle = null;
        }
        catch
        {
            _dataInterfaceHandle = IntPtr.Zero;
            _winUsbHandle = IntPtr.Zero;
            _deviceHandle = null;
        }
    }
    
    public void Dispose()
    {
        if (!_disposed)
        {
            Disconnect();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
