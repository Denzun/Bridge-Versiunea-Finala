# 🚀 Quick Start Guide - POS Bridge

## În 5 minute la funcțional!

### 1️⃣ Prima Pornire

**Lansează aplicația**: `POSBridge.WPF.exe`

**Verifică log-urile**:
```
✓ Pornire automată la startup activată
✓ Porturi COM detectate: COM3, COM4, COM7
✓ Multi-Vendor: 1 device type(s) supported
✓ Aplicație pornită
```

---

### 2️⃣ Conectare Casa de Marcat

#### Opțiunea A: Conectare Automată (Recomandată)

1. **Editează** `settings.txt`:
   ```ini
   ComPort=COM7
   BaudRate=115200
   RunAtStartup=1
   ```

2. **Repornește** aplicația → conectare automată!

#### Opțiunea B: Conectare Manuală

1. Click **"Conectare dispozitiv"** în GUI
2. Verifică status: `✓ Conectat la imprimanta fiscală Datecs pe COM7`

---

### 3️⃣ Primul Bon Fiscal

#### Metoda 1: File-Based (Automată)

**Creează fișier**: `Bon\De tiparit\test_bon.txt`

**Conținut**:
```
S^Cafea^10.50^1^buc^2^1
S^Lapte^5.00^2^l^2^1
TL
```

**Rezultat**:
- Aplicația detectează fișierul automat
- Procesează comenzile
- Mută fișierul în `Istoric/` (succes) sau `Erori/` (eșec)

#### Metoda 2: GUI Buttons

1. Click **"Raport X"** → generează raport X
2. Click **"Raport Z"** → închide ziua fiscală
3. Click **"Introducere numerar"** → adaugă bani în sertar

---

### 4️⃣ Format Comenzi (Fișiere Text)

| Comandă | Format | Exemplu |
|---------|--------|---------|
| Vânzare | `S^NUME^PRET^CANT^UM^TVA^DEP` | `S^Paine^2.50^2^buc^2^1` |
| Discount % | `DP^PROCENT^TEXT` | `DP^10^Discount client` |
| Discount LEI | `DV^SUMA^TEXT` | `DV^5.00^Discount special` |
| Subtotal | `ST` | `ST` |
| Plată | `MP^TIP^SUMA` | `MP^0^20.00` (0=cash) |
| Închide bon | `TL` | `TL` |
| Raport X | `X^` | `X^` |
| Raport Z | `Z^` | `Z^` |
| Numerar IN | `I^SUMA^` | `I^100.00^` |
| Numerar OUT | `O^SUMA^` | `O^50.00^` |

**Exemplu complet (bon cu 2 produse + discount)**:
```
S^Cafea Lavazza^15.50^1^kg^2^1
S^Zahar^4.20^2^kg^2^1
DP^10^Discount 10%
MP^0^20.00
MP^1^3.78
TL
```

---

### 5️⃣ Multi-Vendor Features

#### Tab "Multi-Vendor" 🏭

**Ce vezi**:
- **Vendor Cards**: Datecs ✅ ACTIV | Tremol 🔜 SOON | Elcom 🔜 SOON
- **Capabilities**: Conexiuni, Features CRITICAL, Limite dispozitiv
- **Device Selection**: Dropdown pentru selecție brand (viitor)

#### Device Type Dropdown

**Locație**: Panoul "Device Info" → **"Device Type"** dropdown

**Status curent**:
- ✅ **Datecs (RS232)** - Production ready
- 🔜 **Tremol (WiFi/GPRS) - Coming Soon** - Phase 2
- 🔜 **Elcom - Coming Soon** - Phase 3

---

### 6️⃣ Troubleshooting Rapid

#### ❌ "Device not connected" (Error -33022)

**Verificări**:
1. Casa de marcat este pornită?
2. Cablul serial este conectat?
3. Port COM corect în `settings.txt`?
4. Baud rate corect (115200)?

**Test rapid**:
```powershell
# Verifică porturi COM disponibile
[System.IO.Ports.SerialPort]::GetPortNames()
```

#### ❌ "Receipt is opened" (Error -32003)

**Cauză**: Bon fiscal deschis anterior nu a fost închis

**Soluție automată**: Aplicația verifică automat și anulează bonul deschis înainte de operațiuni noi

#### ❌ Build Errors - "File is locked"

**Cauză**: Aplicația rulează în background

**Soluție**:
```powershell
Get-Process | Where-Object { $_.ProcessName -eq "POSBridge.WPF" } | Stop-Process -Force
```

---

### 7️⃣ Logs & Debug

**Fișiere log**:
- `Logs/app_2026-02-18.log` - Log principal aplicație
- `Logs/log_2026-02-18.txt` - Log detaliat operațiuni
- `Bon/De tiparit/Erori/*.log` - Erori procesare comenzi

**Verifică ultima linie**:
```powershell
Get-Content "Logs\log_2026-02-18.txt" -Tail 20
```

**Output exemplu**:
```
[16:13:22] ✓ Conectat la imprimanta fiscală Datecs pe COM7 @ 115200
[16:13:22] ✓ Monitorizarea folderului este activă
```

---

### 8️⃣ Settings.txt - Configurare Completă

**Locație**: `settings.txt` (lângă .exe)

**Parametri**:
```ini
# FOLDER MONITORIZAT
BonFolder=D:\Proiecte Cursor\POS Bridge\Bon\De tiparit

# OPERATOR IMPLICIT
OperatorCode=1
OperatorPassword=0001

# PORNIRE AUTOMATĂ
RunAtStartup=1

# CONEXIUNE RS232 (DATECS)
ComPort=COM7
BaudRate=115200

# PATH DUDE (DATECS)
DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

**Modifică și repornește** aplicația pentru aplicare.

---

### 9️⃣ Backup & Version Control

**Git backup rapid**:
```powershell
# Status
git status

# Add toate fișierele
git add .

# Commit
git commit -m "Update: descriere modificare"

# Push
git push origin master
```

---

### 🔟 Next Steps

**Ai finalizat Quick Start!** 🎉

**Ce urmează**:
- 📖 Citește [README.md](README.md) pentru detalii complete
- 🏗️ Vezi [ARCHITECTURE.md](ARCHITECTURE.md) pentru arhitectură
- 🧪 Testează toate operațiunile cu casa de marcat reală
- 🚀 Contribuie la dezvoltare (Tremol/Elcom support)

**Întrebări?**
- Check logs în `Logs/`
- Verifică fișierele `.log` din `Bon/De tiparit/Erori/`
- Consultă secțiunea [Troubleshooting](README.md#-troubleshooting) din README

---

**🏭 POS Bridge - Universal Fiscal Device Integration**

*Versiune: 2.0.0 | Data: 2026-02-18*
