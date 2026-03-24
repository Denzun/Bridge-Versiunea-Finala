# 🏗️ POS Bridge - Architecture Documentation

## Table of Contents
1. [System Overview](#system-overview)
2. [Component Architecture](#component-architecture)
3. [Design Patterns](#design-patterns)
4. [Data Flow](#data-flow)
5. [API Reference](#api-reference)
6. [Extension Guide](#extension-guide)

---

## System Overview

### Vision
POS Bridge is a **vendor-agnostic fiscal device integration platform** that eliminates dependency on single manufacturers by providing a unified interface (`IFiscalDevice`) for all fiscal operations.

### Key Principles
1. **Abstraction First** - Business logic operates on interfaces, not concrete implementations
2. **Factory Pattern** - Dynamic device instantiation without compile-time dependencies
3. **Extensibility** - New vendors can be added without modifying existing code
4. **Reliability** - Comprehensive error handling and automatic recovery

---

## Component Architecture

### High-Level Architecture

```
┌───────────────────────────────────────────────────────────────┐
│                    PRESENTATION LAYER                         │
│                   (POSBridge.WPF)                            │
│                                                               │
│  • MainWindow (GUI)                                          │
│  • FileSystemWatcher (folder monitoring)                     │
│  • TrayIcon & Notifications                                  │
│  • Real-time logging display                                 │
└────────────────────────┬──────────────────────────────────────┘
                         │
                         │ Uses
                         ▼
┌───────────────────────────────────────────────────────────────┐
│                   ABSTRACTION LAYER                           │
│               (POSBridge.Abstractions)                        │
│                                                               │
│  ┌─────────────────────────────────────────────────┐        │
│  │  IFiscalDevice (interface)                      │        │
│  │  • ConnectAsync / DisconnectAsync               │        │
│  │  • OpenReceipt / AddSale / CloseReceipt         │        │
│  │  • ReadCurrentReceiptInfo (CRITICAL)            │        │
│  │  • SubtotalAsync (CRITICAL)                     │        │
│  │  • ReadDailyAvailableAmounts (CRITICAL)         │        │
│  │  • PrintDailyReport / CashIn / CashOut          │        │
│  └─────────────────────────────────────────────────┘        │
│                                                               │
│  ┌─────────────────────────────────────────────────┐        │
│  │  FiscalDeviceFactory (static)                   │        │
│  │  • CreateDevice(DeviceType)                     │        │
│  │  • GetSupportedDeviceTypes()                    │        │
│  │  • IsDeviceTypeSupported(type)                  │        │
│  │  • Reflection-based instantiation               │        │
│  └─────────────────────────────────────────────────┘        │
│                                                               │
│  Models: ConnectionSettings, DeviceCapabilities,             │
│          ReceiptInfo, DailyAmounts, OperationResult          │
│  Enums:  DeviceType, ConnectionType, PaymentType             │
└────────────────────────┬──────────────────────────────────────┘
                         │
                         │ Implements
         ┌───────────────┼───────────────┬──────────────┐
         │               │               │              │
         ▼               ▼               ▼              ▼
┌────────────────┐ ┌────────────────┐ ┌──────────┐  ┌──────────┐
│   DATECS       │ │   TREMOL       │ │  ELCOM   │  │  FUTURE  │
│   DEVICE       │ │   DEVICE       │ │  DEVICE  │  │  VENDORS │
│   LAYER        │ │   LAYER        │ │  LAYER   │  │          │
│ ✅ Production  │ │  🔜 Phase 2    │ │ 🔜 Phase 3│ │  💡 TBD  │
└────────┬───────┘ └────────────────┘ └──────────┘  └──────────┘
         │
         │ Uses
         ▼
┌────────────────┐
│  DUDE COM      │
│  Server        │
│  (Middleware)  │
└────────────────┘
```

### Layer Responsibilities

#### 1. Presentation Layer (`POSBridge.WPF`)
**Purpose**: User interaction and visual feedback

**Components**:
- `MainWindow.xaml/.xaml.cs` - Main application window
- `App.xaml/.xaml.cs` - Application lifecycle
- File watcher for automatic command processing
- Tray icon for background operation

**Dependencies**:
- POSBridge.Abstractions (interfaces only)
- POSBridge.Core (utilities)
- POSBridge.Devices.Datecs (loaded at runtime)

**Key Features**:
- Material design UI
- Real-time status updates
- Multi-vendor device selection dropdown
- Capabilities display per device

#### 2. Abstraction Layer (`POSBridge.Abstractions`)
**Purpose**: Define common contracts and factory logic

**Zero Dependencies**: This layer has NO dependencies on concrete implementations

**Key Classes**:

**`IFiscalDevice` Interface**:
```csharp
public interface IFiscalDevice
{
    // Core lifecycle
    Task<bool> ConnectAsync(ConnectionSettings settings);
    Task DisconnectAsync();
    
    // Receipt operations
    Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password);
    Task<SaleResult> AddSaleAsync(string itemName, decimal price, 
                                   decimal quantity, int taxGroup, int department = 1);
    Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true);
    Task<PaymentResult> AddPaymentAsync(PaymentType paymentType, decimal amount);
    Task<CloseResult> CloseReceiptAsync();
    Task CancelReceiptAsync();
    
    // CRITICAL for multi-vendor (state management)
    Task<ReceiptInfo> ReadCurrentReceiptInfoAsync();
    Task<DailyAmounts> ReadDailyAvailableAmountsAsync();
    
    // Device properties
    DeviceCapabilities DeviceCapabilities { get; }
}
```

**`FiscalDeviceFactory` Class**:
- Static factory for device creation
- Uses **reflection** to avoid compile-time dependencies
- Loads device assemblies dynamically at runtime

```csharp
public static IFiscalDevice CreateDevice(DeviceType deviceType)
{
    return deviceType switch
    {
        DeviceType.Datecs => CreateDatecsDevice(),  // Reflection
        DeviceType.Tremol => throw new NotSupportedException("Coming soon"),
        DeviceType.Elcom  => throw new NotSupportedException("Coming soon"),
        _ => throw new ArgumentException("Unknown device type")
    };
}
```

**Why Reflection?**
- Abstractions project stays **vendor-agnostic**
- New vendors added without rebuilding Abstractions
- Clean separation of concerns

#### 3. Device Implementation Layers

**`POSBridge.Devices.Datecs`** ✅
- Implements `IFiscalDevice` for Datecs devices
- Wraps DUDE COM Server via `DudeComWrapper`
- RS232 (COM port) communication

**`POSBridge.Devices.Tremol`** 🔜 (Planned)
- WiFi/GPRS connectivity
- Modern REST API or proprietary SDK

**`POSBridge.Devices.Elcom`** 🔜 (Planned)
- USB native support
- RS232 fallback

#### 4. Business Logic Layer (`POSBridge.Core`)
**Purpose**: Shared utilities, models, helpers

**Components**:
- Common models (`DeviceInfo`, etc.)
- File utilities
- Logging infrastructure
- Configuration management

---

## Design Patterns

### 1. Factory Pattern

**Problem**: Application needs to create device instances without knowing concrete types at compile-time.

**Solution**: `FiscalDeviceFactory` encapsulates device creation logic.

**Implementation**:
```csharp
// Client code (POSBridge.WPF)
IFiscalDevice device = FiscalDeviceFactory.CreateDevice(DeviceType.Datecs);

// Factory uses reflection to load concrete implementation
private static IFiscalDevice CreateDatecsDevice()
{
    var assembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "POSBridge.Devices.Datecs");
    
    var deviceType = assembly.GetType("POSBridge.Devices.Datecs.DatecsDevice");
    return Activator.CreateInstance(deviceType) as IFiscalDevice;
}
```

**Benefits**:
- No compile-time dependency between Abstractions and Device layers
- New vendors added by dropping DLL in folder
- Easy to mock for testing

### 2. Strategy Pattern (via Interface)

**Problem**: Different vendors have different communication protocols and command sets.

**Solution**: `IFiscalDevice` defines common operations; each vendor implements its own strategy.

**Example**:
```csharp
// Common interface
await device.AddSaleAsync("Product", 10.0m, 1, taxGroup: 2);

// Datecs implementation
internal override async Task<SaleResult> AddSaleAsync(...)
{
    // Datecs-specific: Use DUDE command "receipt_Fiscal_Sale"
    _dude.ExecuteSafe("receipt_Fiscal_Sale", parameters);
}

// Tremol implementation (future)
internal override async Task<SaleResult> AddSaleAsync(...)
{
    // Tremol-specific: Use WiFi REST API
    await _httpClient.PostAsync("/api/sale", json);
}
```

### 3. Adapter Pattern

**Problem**: Legacy `FiscalEngine` code uses DUDE directly; new architecture uses `IFiscalDevice`.

**Solution**: `DatecsDevice` acts as an adapter between `IFiscalDevice` and `DudeComWrapper`.

```
┌──────────────┐          ┌──────────────┐          ┌──────────────┐
│  IFiscalDevice│ ◄─────  │DatecsDevice  │ ────────▶│DudeComWrapper│
│  (interface) │          │  (adapter)   │          │  (legacy)    │
└──────────────┘          └──────────────┘          └──────────────┘
```

### 4. Observer Pattern (File Watcher)

**Problem**: Detect new command files in real-time.

**Solution**: `FileSystemWatcher` monitors folder; raises events for new files.

```csharp
_watcher = new FileSystemWatcher(folderPath, "*.txt");
_watcher.Created += async (sender, e) => 
{
    await _semaphore.WaitAsync(); // Serial processing
    try
    {
        await Task.Delay(500); // Wait for file unlock
        ProcessFile(e.FullPath);
    }
    finally
    {
        _semaphore.Release();
    }
};
```

**Key Detail**: `SemaphoreSlim(1,1)` ensures **serial processing** - one file at a time.

---

## Data Flow

### Scenario: Process Receipt from File

```
┌──────────┐
│ User     │ Creates file: bon.txt
│          │ S^Paine^2.50^1^buc^2^1
└────┬─────┘ TL
     │
     ▼
┌────────────────────┐
│ FileSystemWatcher  │ Detects new file
│ (MainWindow)       │
└────┬───────────────┘
     │
     │ Event: Created
     ▼
┌────────────────────┐
│ SemaphoreSlim      │ Acquire lock (serial queue)
│ (ensures serial)   │
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ Task.Delay(500ms)  │ Wait for file unlock
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ Read file content  │ lines = File.ReadAllLines()
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ Parse commands     │ foreach line: ParseCommand()
│ S^ → AddSale       │
│ TL → CloseReceipt  │
└────┬───────────────┘
     │
     │ Call IFiscalDevice methods
     ▼
┌────────────────────┐
│ DatecsDevice       │ OpenReceiptAsync()
│ (IFiscalDevice)    │ AddSaleAsync("Paine", 2.50m, ...)
│                    │ CloseReceiptAsync()
└────┬───────────────┘
     │
     │ Call DUDE COM
     ▼
┌────────────────────┐
│ DudeComWrapper     │ ExecuteSafe("receipt_Fiscal_Sale", params)
│                    │
└────┬───────────────┘
     │
     │ COM Interop
     ▼
┌────────────────────┐
│ DUDE COM Server    │ Send commands to device
│ (dude.CFD_DUDE)    │
└────┬───────────────┘
     │
     │ RS232
     ▼
┌────────────────────┐
│ Datecs Fiscal      │ Print receipt
│ Device             │
└────────────────────┘
     │
     ◄────────────────── Success/Error
     │
     ▼
┌────────────────────┐
│ Move file:         │ Success → Istoric/
│                    │ Error   → Erori/ + .log
└────────────────────┘
```

### Scenario: Manual GUI Operation (Raport X)

```
┌──────────┐
│ User     │ Clicks "Raport X" button
└────┬─────┘
     │
     ▼
┌────────────────────┐
│ RaportXButton_Click│ Event handler (MainWindow.xaml.cs)
│ (GUI)              │
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ Check connection   │ if (!_engine.IsConnected) return;
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ _engine.PrintDaily │ Legacy FiscalEngine method
│ Report(false)      │ (false = no reset = Raport X)
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ DUDE COM call      │ command: "report_Daily"
│                    │ parameter: DoNotResetDailyData = true
└────┬───────────────┘
     │
     ▼
┌────────────────────┐
│ Datecs Device      │ Print Raport X
└────────────────────┘
```

**Note**: GUI currently uses legacy `FiscalEngine`. Future refactor will use `IFiscalDevice`.

---

## API Reference

### ConnectionSettings

**Purpose**: Encapsulate connection parameters for any device type.

```csharp
public class ConnectionSettings
{
    public ConnectionType ConnectionType { get; set; }  // RS232, USB, WiFi, etc.
    
    // RS232 specific
    public string ComPort { get; set; }         // "COM7"
    public int BaudRate { get; set; }           // 115200
    
    // Network specific (Tremol)
    public string IpAddress { get; set; }       // "192.168.1.100"
    public int TcpPort { get; set; }            // 8000
    
    // Authentication (if needed)
    public string Username { get; set; }
    public string Password { get; set; }
}
```

### DeviceCapabilities

**Purpose**: Declare what features a device supports.

```csharp
public class DeviceCapabilities
{
    // Connection types
    public bool SupportsRS232 { get; set; }
    public bool SupportsUSB { get; set; }
    public bool SupportsEthernet { get; set; }
    public bool SupportsWiFi { get; set; }
    
    // CRITICAL features
    public bool SupportsReceiptInfo { get; set; }         // ReadCurrentReceiptInfo
    public bool SupportsSubtotalReturn { get; set; }      // SubtotalAsync returns amount
    public bool SupportsDailyAmounts { get; set; }        // ReadDailyAvailableAmounts
    
    // Limits
    public int MaxItemNameLength { get; set; }    // 36 for Datecs, 72 for Tremol
    public int MaxOperators { get; set; }         // 30 for Datecs
    public int MaxDepartments { get; set; }       // 16 for Datecs
    public int MaxPaymentTypes { get; set; }      // 5 for Datecs
}
```

**Example - Datecs**:
```csharp
DeviceCapabilities = new DeviceCapabilities
{
    SupportsRS232 = true,
    SupportsReceiptInfo = true,
    SupportsSubtotalReturn = true,
    SupportsDailyAmounts = true,
    MaxItemNameLength = 36,
    MaxOperators = 30,
    MaxDepartments = 16,
    MaxPaymentTypes = 5
};
```

### ReceiptInfo (CRITICAL)

**Purpose**: Check if a receipt is currently open to prevent "Receipt is opened" errors.

```csharp
public class ReceiptInfo
{
    public bool IsOpen { get; set; }              // Is a receipt currently open?
    public int ReceiptNumber { get; set; }        // Current receipt number
    public DateTime OpenTimestamp { get; set; }   // When was it opened?
    public decimal CurrentTotal { get; set; }     // Running total
    public int ItemCount { get; set; }            // Number of items
}
```

**Usage**:
```csharp
var info = await device.ReadCurrentReceiptInfoAsync();
if (info.IsOpen)
{
    Log("Receipt already open - cancelling");
    await device.CancelReceiptAsync();
}
await device.OpenReceiptAsync(1, "0001");
```

### OperationResult Hierarchy

**Base Class**:
```csharp
public class OperationResult
{
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public int? ErrorCode { get; set; }
}
```

**Derived Classes**:

**`SubtotalResult`**:
```csharp
public class SubtotalResult : OperationResult
{
    public decimal SubtotalAmount { get; set; }   // Total before discounts
    public decimal TaxAmount { get; set; }        // Total VAT
}
```

**`CloseResult`**:
```csharp
public class CloseResult : OperationResult
{
    public decimal Change { get; set; }           // Rest de dat
    public int ReceiptNumber { get; set; }
    public DateTime Timestamp { get; set; }
}
```

---

## Extension Guide

### Adding a New Vendor (e.g., Tremol)

#### Step 1: Create Device Project

```
POSBridge.Devices.Tremol/
├── POSBridge.Devices.Tremol.csproj
├── TremolDevice.cs          # Main implementation
├── TremolApiClient.cs       # WiFi/REST communication
└── TremolExceptions.cs      # Vendor-specific exceptions
```

**Project References**:
```xml
<ItemGroup>
  <ProjectReference Include="..\POSBridge.Abstractions\POSBridge.Abstractions.csproj" />
</ItemGroup>
```

#### Step 2: Implement IFiscalDevice

**File**: `TremolDevice.cs`

```csharp
using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;
using POSBridge.Abstractions.Models;

namespace POSBridge.Devices.Tremol
{
    public class TremolDevice : IFiscalDevice
    {
        private TremolApiClient _apiClient;
        private ConnectionSettings _currentSettings;
        
        public DeviceCapabilities DeviceCapabilities => new DeviceCapabilities
        {
            SupportsRS232 = true,
            SupportsWiFi = true,
            SupportsGPRS = true,
            SupportsEthernet = true,
            SupportsReceiptInfo = true,
            SupportsSubtotalReturn = true,
            SupportsDailyAmounts = true,
            MaxItemNameLength = 72,    // Tremol supports longer names
            MaxOperators = 20,
            MaxDepartments = 20,
            MaxPaymentTypes = 11       // Tremol supports more payment types
        };
        
        public async Task<bool> ConnectAsync(ConnectionSettings settings)
        {
            _currentSettings = settings;
            
            if (settings.ConnectionType == ConnectionType.WiFi)
            {
                _apiClient = new TremolApiClient(settings.IpAddress, settings.TcpPort);
                return await _apiClient.ConnectAsync();
            }
            else if (settings.ConnectionType == ConnectionType.RS232)
            {
                // RS232 implementation similar to Datecs
                // ...
            }
            
            return false;
        }
        
        public async Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password)
        {
            try
            {
                // Tremol-specific API call
                var response = await _apiClient.PostAsync("/api/receipt/open", new
                {
                    operatorCode,
                    password
                });
                
                return new ReceiptResult { Success = response.Success };
            }
            catch (Exception ex)
            {
                return new ReceiptResult 
                { 
                    Success = false, 
                    ErrorMessage = ex.Message 
                };
            }
        }
        
        // ... implement all other IFiscalDevice methods
    }
}
```

#### Step 3: Update Factory

**File**: `POSBridge.Abstractions/FiscalDeviceFactory.cs`

```csharp
public static IFiscalDevice CreateDevice(DeviceType deviceType)
{
    return deviceType switch
    {
        DeviceType.Datecs => CreateDatecsDevice(),
        DeviceType.Tremol => CreateTremolDevice(),  // ← ADD THIS
        DeviceType.Elcom => throw new NotSupportedException("Coming soon"),
        _ => throw new ArgumentException($"Unknown device type: {deviceType}")
    };
}

private static IFiscalDevice CreateTremolDevice()
{
    try
    {
        var tremolAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "POSBridge.Devices.Tremol");
        
        if (tremolAssembly == null)
            throw new InvalidOperationException("POSBridge.Devices.Tremol assembly not found");
        
        var tremolDeviceType = tremolAssembly.GetType("POSBridge.Devices.Tremol.TremolDevice");
        if (tremolDeviceType == null)
            throw new InvalidOperationException("TremolDevice class not found");
        
        var instance = Activator.CreateInstance(tremolDeviceType) as IFiscalDevice;
        if (instance == null)
            throw new InvalidOperationException("Failed to create TremolDevice instance");
        
        return instance;
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException($"Failed to create Tremol device: {ex.Message}", ex);
    }
}

public static DeviceType[] GetSupportedDeviceTypes()
{
    return new[] { DeviceType.Datecs, DeviceType.Tremol };  // ← UPDATE
}

public static bool IsDeviceTypeSupported(DeviceType deviceType)
{
    return deviceType == DeviceType.Datecs || deviceType == DeviceType.Tremol;  // ← UPDATE
}

public static string GetDeviceTypeName(DeviceType deviceType)
{
    return deviceType switch
    {
        DeviceType.Datecs => "Datecs (RS232)",
        DeviceType.Tremol => "Tremol (WiFi/GPRS/RS232)",  // ← UPDATE
        DeviceType.Elcom => "Elcom - Coming Soon",
        _ => "Unknown"
    };
}
```

#### Step 4: Update GUI (Optional - Vendor Card)

**File**: `POSBridge.WPF/MainWindow.xaml`

Update Tremol card to show "ACTIV" instead of "SOON":

```xml
<!-- Tremol Card -->
<Border Grid.Column="2" 
        Background="White" 
        BorderBrush="#4CAF50"     <!-- Change to green -->
        BorderThickness="2" 
        CornerRadius="10" 
        Padding="15" 
        Opacity="1.0">            <!-- Full opacity -->
    <StackPanel>
        <!-- ... -->
        <Border Background="#E8F5E9"    <!-- Green background -->
                CornerRadius="15" 
                Padding="8,4" 
                HorizontalAlignment="Center"
                Margin="0,0,0,10">
            <TextBlock Text="✓ ACTIV"    <!-- Change status -->
                       FontSize="10" 
                       FontWeight="Bold" 
                       Foreground="#2E7D32"/>
        </Border>
        <!-- ... -->
    </StackPanel>
</Border>
```

#### Step 5: Build & Test

```powershell
# Build Tremol device project
dotnet build "POSBridge.Devices.Tremol\POSBridge.Devices.Tremol.csproj"

# Build main application
dotnet build "POSBridge.WPF\POSBridge.WPF.csproj"

# Run application
.\POSBridge.WPF\bin\Debug\net8.0-windows\POSBridge.WPF.exe
```

**Test Steps**:
1. Select "Tremol" from device type dropdown
2. Configure WiFi settings (IP address)
3. Click "Conectare dispozitiv"
4. Verify connection in log
5. Test basic operations (open receipt, add sale, close)

---

## Best Practices

### 1. Error Handling
Always wrap device calls in try-catch and return meaningful errors:

```csharp
public async Task<SaleResult> AddSaleAsync(...)
{
    try
    {
        // Device-specific logic
        return new SaleResult { Success = true };
    }
    catch (FiscalDeviceException ex)
    {
        return new SaleResult 
        { 
            Success = false,
            ErrorMessage = ex.Message,
            ErrorCode = ex.ErrorCode
        };
    }
}
```

### 2. Logging
Use structured logging for debugging:

```csharp
Log($"[{DeviceType}] Opening receipt - Operator: {operatorCode}");
// ... operation ...
Log($"[{DeviceType}] Receipt opened successfully - Number: {receiptNumber}");
```

### 3. State Management
Always check device state before operations:

```csharp
// Before opening receipt
var info = await ReadCurrentReceiptInfoAsync();
if (info.IsOpen)
{
    await CancelReceiptAsync();  // Clean up
}

// Then open new receipt
await OpenReceiptAsync(...);
```

### 4. Vendor-Specific Features
Use `DeviceCapabilities` to check feature support:

```csharp
if (device.DeviceCapabilities.SupportsReceiptInfo)
{
    var info = await device.ReadCurrentReceiptInfoAsync();
    // Use receipt info
}
else
{
    // Fallback logic for devices without this feature
}
```

### 5. Testing
Create integration tests for each vendor:

```csharp
[TestClass]
public class TremolDeviceTests
{
    private IFiscalDevice _device;
    
    [TestInitialize]
    public void Setup()
    {
        _device = FiscalDeviceFactory.CreateDevice(DeviceType.Tremol);
    }
    
    [TestMethod]
    public async Task TestConnectViaWiFi()
    {
        var settings = new ConnectionSettings
        {
            ConnectionType = ConnectionType.WiFi,
            IpAddress = "192.168.1.100",
            TcpPort = 8000
        };
        
        bool connected = await _device.ConnectAsync(settings);
        Assert.IsTrue(connected);
    }
    
    // ... more tests
}
```

---

## Performance Considerations

### Serial Processing (File Watcher)
File watcher uses `SemaphoreSlim(1,1)` to ensure **one file at a time**:

**Why?**
- Fiscal devices can't handle concurrent receipts
- Prevents race conditions
- Ensures proper error handling per file

**Trade-off**: Slower processing for high-volume scenarios

**Future Improvement**: Queue system with priority levels

### Reflection Overhead
Factory uses reflection for device instantiation:

**Impact**: ~10-50ms delay at first creation (then cached by .NET)

**Why Acceptable?**
- Happens once per application lifetime
- Eliminates compile-time dependencies
- Worth the trade-off for extensibility

### Connection Pooling (Future)
For network-based devices (Tremol WiFi), consider connection pooling:

```csharp
// Instead of connect/disconnect per operation
// Maintain persistent connection with heartbeat
```

---

## Security Considerations

### 1. COM Port Access
- Requires elevated permissions on some systems
- Handle `UnauthorizedAccessException` gracefully

### 2. Network Devices (Tremol)
- Use HTTPS for REST APIs
- Validate SSL certificates
- Store credentials securely (not in plain text)

### 3. File-Based Commands
- Sanitize file content before parsing
- Validate command syntax
- Prevent directory traversal attacks

---

## Troubleshooting Guide

### Issue: Factory returns null

**Symptom**: `CreateDevice()` returns null or throws exception

**Causes**:
1. Device assembly not loaded
2. Class name mismatch
3. Constructor parameters incorrect

**Solution**:
```csharp
// Check if assembly is loaded
var assemblies = AppDomain.CurrentDomain.GetAssemblies();
foreach (var assembly in assemblies)
{
    Console.WriteLine(assembly.GetName().Name);
}

// Ensure POSBridge.Devices.{Vendor} is in the list
```

### Issue: Interface method not implemented

**Symptom**: `NotImplementedException` when calling device method

**Solution**: Ensure all `IFiscalDevice` methods are implemented (no `throw new NotImplementedException()`)

---

**Document Version**: 2.0.0  
**Last Updated**: 2026-02-18  
**Maintained By**: POS Bridge Development Team
