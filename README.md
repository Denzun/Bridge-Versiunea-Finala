# 🏭 POS Bridge - Multi-Vendor Fiscal Device Integration

![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)
![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)
![License](https://img.shields.io/badge/license-Proprietary-red.svg)
![Status](https://img.shields.io/badge/status-Production-green.svg)

**POS Bridge** este o aplicație universală pentru integrarea caselor de marcat fiscale, construită pe o arhitectură multi-vendor extensibilă. Suportă multiple branduri de dispozitive fiscale printr-o interfață comună, eliminând dependența de un singur producător.

---

## 📋 Cuprins

- [Caracteristici](#-caracteristici)
- [Arhitectură](#-arhitectură)
- [Branduri Suportate](#-branduri-suportate)
- [Instalare](#-instalare)
- [Configurare](#-configurare)
- [Utilizare](#-utilizare)
- [Protocoale de Comunicare](#-protocoale-de-comunicare)
- [API & Integrare](#-api--integrare)
- [Dezvoltare](#-dezvoltare)
- [Troubleshooting](#-troubleshooting)

---

## ✨ Caracteristici

### Core Features
- ✅ **Multi-Vendor Architecture** - Suport pentru Datecs, Tremol, Elcom (în dezvoltare)
- ✅ **Monitorizare Folder Automată** - Procesare automată comenzi prin fișiere text
- ✅ **Interfață Grafică Modernă** - WPF cu design material și animații
- ✅ **Logging Avansat** - Înregistrare completă operațiuni și erori
- ✅ **Reconnect Automat** - Reconectare automată în caz de pierdere conexiune
- ✅ **Suport COM/USB/WiFi** - Multiple protocoale de comunicare (vendor-specific)

### Operațiuni Fiscale
- 🧾 **Bonuri Fiscale** - Creare, anulare, subtotal, discount
- 💰 **Management Cash** - Introducere/scoatere numerar, reconciliere automată
- 📊 **Rapoarte** - Rapoarte Z, X, perioade, operatori, departamente
- 📋 **Info Dispozitiv** - Status, capabilități, informații AMEF
- 🔍 **Validări Business** - Verificări automate (bon deschis, sume, TVA)

---

## 🏗️ Arhitectură

### Design Pattern: Multi-Vendor Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     POSBridge.WPF                           │
│              (User Interface - WPF Application)             │
└────────────────────────┬────────────────────────────────────┘
                         │
                         │ uses
                         ▼
┌─────────────────────────────────────────────────────────────┐
│              POSBridge.Abstractions                         │
│         (Common Interfaces & Factory Pattern)               │
│                                                             │
│  • IFiscalDevice (interface)                               │
│  • FiscalDeviceFactory (static factory)                    │
│  • DeviceCapabilities, ConnectionSettings (models)         │
│  • OperationResult, ReceiptResult, etc. (results)          │
└────────────────────────┬────────────────────────────────────┘
                         │
                         │ implements
         ┌───────────────┼───────────────┐
         │               │               │
         ▼               ▼               ▼
┌─────────────┐  ┌─────────────┐  ┌─────────────┐
│  Datecs     │  │   Tremol    │  │   Elcom     │
│  Device     │  │   Device    │  │   Device    │
│  ✅ ACTIV   │  │  🔜 SOON    │  │  🔜 SOON    │
└─────────────┘  └─────────────┘  └─────────────┘
      │
      │ uses
      ▼
┌─────────────┐
│ DUDE COM    │
│ Server      │
└─────────────┘
```

### Componente Principale

#### 1️⃣ POSBridge.Abstractions
**Rol**: Definește contracte comune pentru toate dispozitivele fiscale

**Fișiere cheie**:
- `IFiscalDevice.cs` - Interfață comună pentru toate operațiunile fiscale
- `FiscalDeviceFactory.cs` - Factory pentru crearea instanțelor de device
- `Enums/` - DeviceType, ConnectionType, PaymentType
- `Models/` - ConnectionSettings, DeviceCapabilities, ReceiptInfo, DailyAmounts

**Exemplu utilizare**:
```csharp
// Create device instance using factory
IFiscalDevice device = FiscalDeviceFactory.CreateDevice(DeviceType.Datecs);

// Connect to device
var settings = new ConnectionSettings
{
    ConnectionType = ConnectionType.RS232,
    ComPort = "COM7",
    BaudRate = 115200
};
await device.ConnectAsync(settings);

// Open receipt and add sale
await device.OpenReceiptAsync(operatorCode: 1, password: "0001");
await device.AddSaleAsync("Produs Test", price: 10.50m, quantity: 2, taxGroup: 2);
await device.CloseReceiptAsync();
```

#### 2️⃣ POSBridge.Devices.Datecs
**Rol**: Implementare specifică pentru dispozitive Datecs

**Fișiere cheie**:
- `DatecsDevice.cs` - Implementare IFiscalDevice pentru Datecs
- `DudeComWrapper.cs` - Wrapper pentru DUDE COM Server
- `FiscalEngine.cs` - Engine original (legacy, va fi deprecat)

**Dependențe**:
- DUDE COM Server (ProgID: `dude.CFD_DUDE`)
- RS232 connection (COM port)

#### 3️⃣ POSBridge.WPF
**Rol**: Interfață grafică și orchestrare aplicație

**Features**:
- Modern material design UI
- Multi-vendor device selection dropdown
- Real-time status & logging
- Folder watcher pentru comenzi automate
- Tray icon & notifications

#### 4️⃣ POSBridge.Core
**Rol**: Logică business comună, modele, utilități

---

## 🏪 Branduri Suportate

### 📟 DATECS - ✅ ACTIV
**Status**: Production Ready  
**Conexiuni**: RS232 (COM port)  
**Middleware**: DUDE COM Server  
**Capabilități**:
- ✅ Bonuri fiscale complete
- ✅ ReadCurrentReceiptInfo (previne erori "Receipt is opened")
- ✅ SubtotalReturn (validări business)
- ✅ ReadDailyAvailableAmounts (reconciliere automată)
- ✅ Rapoarte Z/X/perioade
- ✅ Management cash

**Limite**:
- Lungime nume articol: 36 caractere
- Operatori: 30
- Departamente: 16
- Tipuri plată: 5

### 📱 TREMOL - 🔜 SOON (Phase 2)
**Status**: Planned  
**Conexiuni**: WiFi, GPRS, RS232  
**Middleware**: TBD (probabil SDK proprietar sau REST API)  
**Avantaje**:
- Suport WiFi/GPRS pentru instalații remote
- API modern
- Ecran touch (modele selective)

### 🖨️ ELCOM - 🔜 SOON (Phase 3)
**Status**: Planned  
**Conexiuni**: RS232, USB  
**Middleware**: TBD  
**Avantaje**:
- Suport USB nativ
- Prețuri competitive

---

## 📦 Instalare

### Cerințe Sistem
- **OS**: Windows 10/11 (64-bit)
- **.NET Runtime**: 8.0 sau mai nou
- **Hardware**: 
  - RAM: 512 MB minim
  - Disk: 100 MB spațiu liber
  - Port: COM (serial) pentru Datecs

### Pași Instalare

1. **Descarcă ultima versiune** din folder `Distributie/POSBridge/`

2. **Instalează .NET 8.0 Runtime** (dacă nu este instalat):
   ```
   https://dotnet.microsoft.com/download/dotnet/8.0
   ```

3. **Pentru Datecs - Instalează DUDE COM Server**:
   - Locație: `Drivere/DUDE/`
   - Rulează `setup.exe` ca Administrator
   - Verifică instalare: caută `dude.CFD_DUDE` în Registry

4. **Configurează aplicația** (vezi secțiunea [Configurare](#-configurare))

5. **Rulează `POSBridge.WPF.exe`**

---

## ⚙️ Configurare

### Fișier: `settings.txt`

```ini
# Folder monitorizat pentru comenzi automate
BonFolder=D:\Proiecte Cursor\POS Bridge\Bon\De tiparit

# Operator implicit pentru bonuri
OperatorCode=1
OperatorPassword=0001

# Pornire automată cu Windows
RunAtStartup=1

# Configurare conexiune (Datecs - RS232)
ComPort=COM7
BaudRate=115200

# Path către DUDE (pentru Datecs)
DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

### Detectare Port COM

Aplicația detectează automat toate porturile COM disponibile la pornire. Verifică log-urile:
```
[16:13:22] Porturi COM detectate: COM3, COM4, COM7
[16:13:22] Port COM selectat din setări: COM7
```

---

## 🚀 Utilizare

### 1. Conectare Dispozitiv

**Metoda 1: Conectare Automată (la pornire)**
- Setează `RunAtStartup=1` în `settings.txt`
- Aplicația se conectează automat la pornire

**Metoda 2: Conectare Manuală**
- Apasă butonul **"Conectare dispozitiv"** din GUI
- Verifică status în panoul "Device Info"

**Verificare conexiune**:
```
✓ Conectat la imprimanta fiscală Datecs pe COM7 @ 115200
```

### 2. Procesare Comenzi (File-Based)

**Folder monitorizat**: `Bon\De tiparit\`

**Format fișier** (exemplu: `bon.txt`):
```
S^Paine alba^2.50^2^buc^2^1
S^Lapte 3.5%^5.00^1^l^2^1
TL
```

**Comenzi disponibile**:

| Comandă | Format | Descriere |
|---------|--------|-----------|
| `S` | `S^DENUMIRE^PRET^CANT^UM^GRTVA^GRDEP` | Adaugă vânzare |
| `DP` | `DP^VALOARE^TEXT` | Discount procentual |
| `DV` | `DV^VALOARE^TEXT` | Discount valoric |
| `MP` | `MP^TIP^VALOARE` | Plată multiplă |
| `TL` | `TL` | Închide bon |
| `ST` | `ST` | Subtotal |
| `P` | `P^TEXT` | Printează text pe bon |
| `X` | `X^` | Raport X |
| `Z` | `Z^` | Raport Z |
| `I` | `I^VALOARE^` | Introducere numerar |
| `O` | `O^VALOARE^` | Scoatere numerar |

**Exemplu bon complet**:
```
S^Cafea Lavazza^15.50^1^kg^2^1
S^Zahar alb^4.20^2^kg^2^1
DP^10^Discount client fidel
MP^0^20.00
MP^1^3.78
TL
```

**Procesare**:
1. Aplicația detectează fișierul nou
2. Procesează comenzile secvențial
3. **Success**: mută fișierul în `Bon\De tiparit\Istoric\`
4. **Eroare**: mută fișierul în `Bon\De tiparit\Erori\` + creare `{filename}.log`

### 3. Utilizare GUI

#### Tab "Comenzi"
- Butoane pentru operațiuni comune (Raport X, Z, Numerar)
- Activat doar când dispozitivul este conectat

#### Tab "Multi-Vendor" 🏭
- **Vendor Cards**: Status Datecs/Tremol/Elcom
- **Capabilities**: Afișare capabilități device curent
- **Device Selection**: Dropdown pentru selecție brand (viitor)

#### Status Bar
Afișează status curent aplicație:
- `✓ Conectat` - Dispozitiv conectat OK
- `✗ Deconectat` - Dispozitiv deconectat
- `⚠ Procesare...` - Se procesează o comandă

---

## 📡 Protocoale de Comunicare

### RS232 (Serial COM) - Datecs
**Configurare**:
```csharp
var settings = new ConnectionSettings
{
    ConnectionType = ConnectionType.RS232,
    ComPort = "COM7",
    BaudRate = 115200
};
```

**Parametri comuni**:
- Baud Rate: 9600, 19200, 38400, 57600, **115200** (recomandat)
- Data Bits: 8
- Parity: None
- Stop Bits: 1
- Flow Control: None

### USB - Elcom (viitor)
TBD - va folosi drivere USB dedicate

### WiFi/GPRS - Tremol (viitor)
TBD - probabil REST API sau TCP socket

---

## 🔌 API & Integrare

### IFiscalDevice Interface

Toate dispozitivele implementează interfața comună `IFiscalDevice`:

```csharp
public interface IFiscalDevice
{
    // Connection
    Task<bool> ConnectAsync(ConnectionSettings settings);
    Task DisconnectAsync();
    
    // Receipt Operations
    Task<ReceiptResult> OpenReceiptAsync(int operatorCode, string password);
    Task<SaleResult> AddSaleAsync(string itemName, decimal price, decimal quantity, int taxGroup, int department = 1);
    Task<SubtotalResult> SubtotalAsync(bool print = true, bool display = true);
    Task<DiscountResult> AddDiscountAsync(decimal value, bool isPercentage, string description = "");
    Task<PaymentResult> AddPaymentAsync(PaymentType paymentType, decimal amount);
    Task<CloseResult> CloseReceiptAsync();
    Task CancelReceiptAsync();
    
    // Receipt Info (CRITICAL for multi-vendor)
    Task<ReceiptInfo> ReadCurrentReceiptInfoAsync();
    
    // Cash Management
    Task CashInAsync(decimal amount);
    Task CashOutAsync(decimal amount);
    Task<DailyAmounts> ReadDailyAvailableAmountsAsync();
    
    // Reports
    Task PrintDailyReportAsync(bool resetCounters);
    Task PrintDailyReportAsync(); // Raport X
    
    // Device Info
    Task<DeviceInfo> GetDeviceInfoAsync();
    Task<DeviceStatus> GetStatusAsync();
    DeviceCapabilities DeviceCapabilities { get; }
}
```

### Exemplu Integrare

**Scenario**: Creează bon fiscal cu 2 produse + discount

```csharp
using POSBridge.Abstractions;
using POSBridge.Abstractions.Enums;

// 1. Create device instance
IFiscalDevice device = FiscalDeviceFactory.CreateDevice(DeviceType.Datecs);

// 2. Connect
var settings = new ConnectionSettings
{
    ConnectionType = ConnectionType.RS232,
    ComPort = "COM7",
    BaudRate = 115200
};
bool connected = await device.ConnectAsync(settings);
if (!connected)
    throw new Exception("Failed to connect");

// 3. Check if receipt is already open (CRITICAL!)
var receiptInfo = await device.ReadCurrentReceiptInfoAsync();
if (receiptInfo.IsOpen)
{
    Console.WriteLine("Receipt already open! Cancelling...");
    await device.CancelReceiptAsync();
}

// 4. Open new receipt
await device.OpenReceiptAsync(operatorCode: 1, password: "0001");

// 5. Add sales
await device.AddSaleAsync("Paine alba", price: 2.50m, quantity: 2, taxGroup: 2, department: 1);
await device.AddSaleAsync("Lapte 3.5%", price: 5.00m, quantity: 1, taxGroup: 2, department: 1);

// 6. Get subtotal
var subtotal = await device.SubtotalAsync();
Console.WriteLine($"Subtotal: {subtotal.SubtotalAmount} RON");

// 7. Apply discount
await device.AddDiscountAsync(value: 10, isPercentage: true, description: "Discount 10%");

// 8. Add payments
await device.AddPaymentAsync(PaymentType.Cash, amount: 10.00m);

// 9. Close receipt
var closeResult = await device.CloseReceiptAsync();
Console.WriteLine($"Receipt closed. Change: {closeResult.Change} RON");

// 10. Disconnect
await device.DisconnectAsync();
```

---

## 🛠️ Dezvoltare

### Prerequisites
- Visual Studio 2022 sau mai nou
- .NET 8.0 SDK
- Git pentru version control

### Structură Proiect

```
POS Bridge/
├── POSBridge.Abstractions/         # Common interfaces & factory
│   ├── IFiscalDevice.cs
│   ├── FiscalDeviceFactory.cs
│   ├── Enums/
│   └── Models/
├── POSBridge.Devices.Datecs/       # Datecs implementation
│   ├── DatecsDevice.cs
│   ├── DudeComWrapper.cs
│   └── FiscalEngine.cs (legacy)
├── POSBridge.Core/                  # Business logic & utilities
├── POSBridge.WPF/                   # GUI application
│   ├── MainWindow.xaml
│   └── MainWindow.xaml.cs
├── Bon/                             # File-based commands
│   ├── De tiparit/                  # Input folder (watched)
│   ├── Procesate/                   # Success folder
│   └── Erori/                       # Error folder
├── Drivere/                         # Device drivers & SDKs
│   └── DUDE/                        # Datecs DUDE COM Server
└── Distributie/                     # Release builds
```

### Build & Deploy

**Build Debug**:
```powershell
dotnet build "POSBridge.WPF\POSBridge.WPF.csproj" --configuration Debug
```

**Build Release**:
```powershell
dotnet build "POSBridge.WPF\POSBridge.WPF.csproj" --configuration Release
```

**Copy to Distribution** (vezi `.cursor/rules/distributie.mdc`):
```powershell
# Script automat pentru actualizare folder Distributie
# Copiază: POSBridge.*.dll, POSBridge.WPF.exe, dependencies
```

### Adăugare Vendor Nou

**Exemplu: Adăugare Tremol Device**

1. **Creează proiect nou**: `POSBridge.Devices.Tremol`
2. **Adaugă referință**: `POSBridge.Abstractions`
3. **Implementează interfața**:
```csharp
public class TremolDevice : IFiscalDevice
{
    public DeviceCapabilities DeviceCapabilities => new DeviceCapabilities
    {
        SupportsRS232 = true,
        SupportsWiFi = true,
        SupportsGPRS = true,
        MaxItemNameLength = 72,  // Tremol-specific
        // ... other capabilities
    };
    
    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        // Tremol-specific connection logic
        // WiFi: connect via IP
        // RS232: similar to Datecs
    }
    
    // ... implement all IFiscalDevice methods
}
```

4. **Update Factory**:
```csharp
// In FiscalDeviceFactory.cs
public static IFiscalDevice CreateDevice(DeviceType deviceType)
{
    return deviceType switch
    {
        DeviceType.Datecs => CreateDatecsDevice(),
        DeviceType.Tremol => CreateTremolDevice(),  // NEW
        // ...
    };
}

private static IFiscalDevice CreateTremolDevice()
{
    var tremolAssembly = AppDomain.CurrentDomain.GetAssemblies()
        .FirstOrDefault(a => a.GetName().Name == "POSBridge.Devices.Tremol");
    // ... reflection logic
}
```

5. **Test & Deploy**

---

## 🔍 Troubleshooting

### ❌ "Device not connected" (Error -33022)

**Cauze posibile**:
1. Casa de marcat este oprită
2. Cablu serial defect sau deconectat
3. Port COM greșit în `settings.txt`
4. Baud rate greșit
5. Alt proces folosește COM port-ul

**Soluții**:
```powershell
# 1. Verifică procese care folosesc COM port
Get-Process | Where-Object { $_.Modules.ModuleName -like "*dude*" }

# 2. Verifică porturi COM disponibile
[System.IO.Ports.SerialPort]::GetPortNames()

# 3. Test manual cu DUDE standalone
# Rulează: C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

### ❌ "Receipt is opened" (Error -32003)

**Cauză**: Există deja un bon fiscal deschis

**Soluție**:
```csharp
// Verifică status înainte de deschidere bon
var receiptInfo = await device.ReadCurrentReceiptInfoAsync();
if (receiptInfo.IsOpen)
{
    await device.CancelReceiptAsync();
}
await device.OpenReceiptAsync(1, "0001");
```

### ❌ Build Errors - "File is locked"

**Cauză**: Aplicația rulează în background

**Soluție**:
```powershell
# Oprește toate instanțele
Get-Process | Where-Object { $_.ProcessName -eq "POSBridge.WPF" } | Stop-Process -Force

# Apoi rebuild
dotnet build --configuration Debug
```

### ⚠️ Logging

**Log files**:
- `Logs/app_{date}.log` - Log principal aplicație
- `Logs/log_{date}.txt` - Log detaliat operațiuni
- `Bon/De tiparit/Erori/*.log` - Erori procesare comenzi

**Nivel logging**: Configurabil în cod (momentan fix: DEBUG)

---

## 📊 Diagrame Arhitectură

### Flux Procesare Comenzi (File-Based)

```
┌─────────────┐
│  User/App   │
│ creates txt │
└──────┬──────┘
       │
       ▼
┌─────────────────┐      ┌──────────────────┐
│ FileSystemWatch │─────▶│  SemaphoreSlim   │
│   (monitors)    │      │  (serial queue)  │
└─────────────────┘      └────────┬─────────┘
                                  │
                                  ▼
                         ┌─────────────────┐
                         │ Delay 500ms     │
                         │ (file unlock)   │
                         └────────┬────────┘
                                  │
                                  ▼
                         ┌─────────────────┐
                         │ Read & Parse    │
                         │ txt file        │
                         └────────┬────────┘
                                  │
                    ┌─────────────┴─────────────┐
                    │                           │
                    ▼                           ▼
         ┌──────────────────┐        ┌─────────────────┐
         │ Execute Commands │        │  Error Occurs   │
         │ via IFiscalDevice│        └────────┬────────┘
         └──────────┬───────┘                 │
                    │                          │
                    ▼                          ▼
         ┌──────────────────┐        ┌─────────────────┐
         │  Move to         │        │  Move to        │
         │  "Istorie/"      │        │  "Erori/"       │
         └──────────────────┘        │  + create .log  │
                                     └─────────────────┘
```

### Multi-Vendor Factory Pattern

```
┌──────────────────────────────────────────────┐
│         Application Code                     │
│                                              │
│  var device = FiscalDeviceFactory            │
│      .CreateDevice(DeviceType.Datecs);       │
│                                              │
└──────────────────┬───────────────────────────┘
                   │
                   ▼
┌──────────────────────────────────────────────┐
│      FiscalDeviceFactory (static)            │
│                                              │
│  CreateDevice(DeviceType type) {             │
│    switch(type) {                            │
│      Datecs  => CreateDatecsDevice()         │
│      Tremol  => CreateTremolDevice()         │
│      Elcom   => CreateElcomDevice()          │
│    }                                         │
│  }                                           │
└──────────────────┬───────────────────────────┘
                   │
                   │ Reflection (dynamic load)
                   │
         ┌─────────┴──────────┬──────────┐
         │                    │          │
         ▼                    ▼          ▼
┌────────────────┐  ┌────────────────┐  ┌────────────────┐
│ DatecsDevice   │  │ TremolDevice   │  │ ElcomDevice    │
│ : IFiscalDevice│  │ : IFiscalDevice│  │ : IFiscalDevice│
└────────────────┘  └────────────────┘  └────────────────┘
```

---

## 📝 Versioning & Changelog

### Version 2.0.0 (Current) - Multi-Vendor Architecture
**Release Date**: 2026-02-18

**Major Changes**:
- ✨ Multi-Vendor Architecture implementată
- ✨ `POSBridge.Abstractions` project cu `IFiscalDevice` interface
- ✨ `FiscalDeviceFactory` pentru device instantiation
- ✨ `DatecsDevice` class - refactored din `FiscalEngine`
- 🎨 Modern GUI cu vendor cards și capabilities display
- 🎨 Gradient hero section în tab Multi-Vendor
- 📚 Documentație completă README

**Datecs Support**:
- ✅ Full implementation via `DatecsDevice` class
- ✅ CRITICAL features: ReadCurrentReceiptInfo, SubtotalReturn, ReadDailyAvailableAmounts

**Known Issues**:
- Tremol & Elcom sunt placeholder (throw NotSupportedException)

### Version 1.0.0 (Legacy) - Datecs Only
**Original implementation** cu `FiscalEngine` direct în POSBridge.WPF

---

## 👥 Contribuție & Contact

**Dezvoltator Principal**: [Nume Companie/Developer]  
**Email**: [contact@example.com]  
**Repository**: [Link GitHub/GitLab - dacă e public]

**Cerințe contribuție**:
1. Fork & branch din `master`
2. Respectă code style existent (C# conventions)
3. Adaugă unit tests pentru cod nou
4. Update documentație README
5. Submit Pull Request cu descriere detaliată

---

## 📄 Licență

**Proprietary Software** - Toate drepturile rezervate.

Acest software este proprietar și nu poate fi copiat, modificat, distribuit sau vândut fără permisiunea explicită scrisă a proprietarului.

Pentru licențiere comercială, contactați: [licensing@example.com]

---

## 🎯 Roadmap

### Phase 1: Datecs Foundation ✅ COMPLETED
- [x] Multi-Vendor Architecture
- [x] IFiscalDevice interface
- [x] DatecsDevice implementation
- [x] Factory Pattern
- [x] Modern GUI

### Phase 2: Tremol Integration 🔜
- [ ] Tremol SDK integration
- [ ] WiFi/GPRS connectivity
- [ ] TremolDevice class
- [ ] Advanced features (touch screen, etc.)

### Phase 3: Elcom Integration 🔜
- [ ] Elcom SDK integration
- [ ] USB connectivity
- [ ] ElcomDevice class

### Phase 4: Cloud & Analytics 💡
- [ ] Cloud sync pentru rapoarte
- [ ] Dashboard analytics
- [ ] Remote device monitoring
- [ ] Multi-location support

---

**🏭 POS Bridge - Universal Fiscal Device Integration Platform**

*Built with ❤️ using .NET 8.0 & WPF*
