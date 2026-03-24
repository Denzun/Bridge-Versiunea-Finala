# 🛡️ Ghid Rapid - Cum să NU Strici Aplicația

## 📌 Problema Identificată

Când am adăugat butoanele pentru Raport X și Z și am reorganizat UI-ul în tab-uri, aplicația nu se mai conecta la casa de marcat. 

**Cauza**: Modificări multiple simultan (UI + funcționalitate nouă) fără testare intermediară.

---

## ✅ Soluția: Protocol în 3 Pași

### **ÎNAINTE** de modificare:
```powershell
# 1. Salvează starea actuală
git add .
git commit -m "backup: înainte de [ce vrei să faci]"

# 2. Rulează check-ul
.\scripts\check.ps1
```

### **ÎN TIMPUL** modificării:
- **Fă O SINGURĂ modificare odată**
  - ✅ BUN: Adaugă butonul X Report
  - ❌ RAU: Adaugă X Report + reorganizează UI + modifică backend

- **Când adaugi un buton nou:**
  1. Adaugă în XAML cu `x:Name="NumeButon"` și `IsEnabled="False"`
  2. Creează event handler `NumeButon_Click` în `.xaml.cs`
  3. Activează butonul în `ConnectAndMaybeStartWatcherAsync` (linia ~137)
  4. Dezactivează butonul în `DisconnectButton_Click` (linia ~187)

### **DUPĂ** modificare:
```powershell
# 1. Verifică automat
.\scripts\check.ps1

# 2. Testează manual
# - Pornește aplicația
# - Conectează la COM6 (115200, 1/0001)
# - Testează funcția veche (ex: Test Fiscal Receipt)
# - Testează funcția nouă

# 3. Commit doar dacă totul merge
git add .
git commit -m "feat: descriere clară"
```

---

## 🚨 Dacă Ceva Se Strică

### Aplicația nu pornește?
```powershell
# Verifică erori
dotnet build POSBridge.sln

# Verifică crash.log
cat "POSBridge.WPF\bin\Debug\net8.0-windows\crash.log"

# Revino la versiunea funcțională
git log --oneline -10
git reset --hard [commit-id-functional]
```

### Nu se conectează?
```powershell
# Verifică că auto-connect NU este comentat
Select-String -Path "POSBridge.WPF\MainWindow.xaml.cs" -Pattern "ConnectAndMaybeStartWatcherAsync" -Context 2,0

# Verifică COM ports
[System.IO.Ports.SerialPort]::getportnames()

# Verifică că toate controalele există
Select-String -Path "POSBridge.WPF\MainWindow.xaml" -Pattern 'x:Name="(PortComboBox|BaudComboBox|ConnectButton)"'
```

---

## 📋 Checklist Rapid

Înainte de commit, verifică:
- [ ] `.\scripts\check.ps1` - PASS
- [ ] Aplicația pornește
- [ ] Conexiunea funcționează
- [ ] Funcțiile vechi merg
- [ ] Funcția nouă merge
- [ ] Ai testat cu un bon real

---

## 🎯 Reguli de Aur

1. **O modificare la un moment dat**
2. **Backup înainte, test după**
3. **Nu modifica cod funcțional fără motiv**
4. **Când nu ești sigur, folosește scriptul de check**

---

## 📚 Fișiere Importante

- `DEVELOPMENT_SAFETY_PROTOCOL.md` - Protocol complet detaliat
- `scripts/check.ps1` - Verificare automată (rulează acesta!)
- `scripts/safety-check.ps1` - Verificare extinsă (are bug, nu rula)
- `scripts/quick-check.ps1` - Verificare rapidă (are bug, nu rula)

**Folosește `scripts/check.ps1` - este singurul funcțional și testat!**
