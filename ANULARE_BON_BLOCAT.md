# Anulare Bon Fiscal Blocat

## 📋 Descriere

Procedura de anulare a unui bon fiscal blocat/deschis folosind **Command 60 (3Ch)** conform protocolului DUDE.

## ⚠️ Când Să Folosești Această Funcție

### **Situații în care ai nevoie de anulare:**

1. **Bon rămas deschis** după o eroare de comunicare
2. **Aplicație crashed** în timpul tipăririi bonului
3. **Întrerupere de curent** în timpul unei operații fiscale
4. **Eroare "Receipt already open"** când încerci să deschizi un bon nou
5. **Test** care nu s-a finalizat corect

### **NU folosi anularea dacă:**

- ❌ Bonul s-a tipărit corect dar vrei să îl anulezi retroactiv (prea târziu!)
- ❌ Vrei să corectezi un produs după închiderea bonului (folosește storno)
- ❌ Nu există bon deschis (vei primi eroare "Command not permitted")

## 🔧 Cum Să Folosești

### **Metoda 1: Buton în UI** (Recomandat)

1. Deschide aplicația POS Bridge
2. Conectează-te la casa de marcat
3. În secțiunea **TESTING**, click pe:
   ```
   ❌ Cancel Blocked Receipt
   ```
4. Confirmă în dialogul de avertizare
5. Bonul blocat va fi anulat

### **Metoda 2: Fișier .txt**

Creează un fișier `anuleaza.txt` în folder `.\Bon\`:

```
VB^
```

Aplicația va procesa automat fișierul și va anula bonul deschis.

### **Metoda 3: Programatic (C#)**

```csharp
var fiscalEngine = FiscalEngine.Instance;
fiscalEngine.CancelReceipt();
```

## 📡 Protocol DUDE

### **Command 60 (3Ch) - Cancel fiscal receipt**

**Parametri:**
```
none (fără parametri)
```

**Input:**
```
(empty)
```

**Output:**
```
{ErrorCode}<SEP>
```
- `ErrorCode = 0` → Bon anulat cu succes
- `ErrorCode < 0` → Eroare (ex: "Command not permitted" = nu există bon deschis)

### **Nume Comandă DUDE:**
```
receipt_Fiscal_Cancel
```

### **Workflow 5-Step:**
```
1. ClearInput()           → Șterge parametri anteriori
2. (skip - no input)      → Comanda nu are parametri
3. ExecuteCommand(60)     → Execută Command 60 (3Ch)
4. CheckErrorCode()       → Verifică ErrorCode
5. (skip - no output)     → Comanda nu returnează date
```

## 🔍 Verificare Status Bon

### **Command 76 (4Ch) - Status of the fiscal receipt**

Poți verifica dacă există un bon deschis:

```csharp
// Nume comandă DUDE
"receipt_Get_Status"

// Răspuns
IsOpen = 1  → Există bon deschis (poți anula)
IsOpen = 0  → Nu există bon deschis (nu poți anula)
```

## ⚙️ Implementare în Cod

### **FiscalEngine.cs**

```csharp
/// <summary>
/// Cancels the current open fiscal receipt (Command 60 - 3Ch).
/// Use ONLY if a receipt is open and needs to be cancelled.
/// </summary>
public void CancelReceipt()
{
    Invoke(() => _dude.ExecuteSafe("receipt_Fiscal_Cancel"));
}
```

### **MainWindow.xaml.cs**

```csharp
private async void CancelReceiptButton_Click(object sender, RoutedEventArgs e)
{
    // Confirmare utilizator
    var result = MessageBox.Show(
        "Această funcție va ANULA bonul fiscal deschis curent...",
        "Anulare Bon Blocat",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);

    if (result == MessageBoxResult.Yes)
    {
        await Task.Run(() => _fiscalEngine.CancelReceipt());
    }
}
```

## 📊 Exemple de Utilizare

### **Exemplu 1: Bon Blocat După Eroare**

**Situație:**
```
User: "Test Fiscal Receipt"
App: Deschide bon → Adaugă produse → ❌ EROARE comunicare
Casa: BON DESCHIS, BLOCAT!
```

**Soluție:**
```
1. Click "❌ Cancel Blocked Receipt"
2. Confirmă anularea
3. ✓ Bonul este anulat, casa este liberă
4. Retry: "Test Fiscal Receipt" → ✓ Funcționează
```

### **Exemplu 2: Eroare "Receipt already open"**

**Situație:**
```
User: Încearcă să deschidă bon nou
DUDE: Error -102XXX "Receipt already open"
```

**Soluție:**
```
1. Click "❌ Cancel Blocked Receipt"
2. ✓ Bonul vechi este anulat
3. Încearcă din nou → ✓ Funcționează
```

### **Exemplu 3: Nu Există Bon Deschis**

**Situație:**
```
User: Click "Cancel Blocked Receipt" (fără bon deschis)
```

**Rezultat:**
```
DUDE: Error "Command not permitted"
App: Dialog: "Nu există bon deschis de anulat!"
```

## 🚨 Erori Posibile

| Eroare | Cauză | Soluție |
|--------|-------|---------|
| **Command not permitted** | Nu există bon deschis | Normal - nu e nevoie de anulare |
| **Device not connected** | Casa nu este conectată | Conectează-te mai întâi |
| **Wrong operator password** | Parolă greșită | Corectează parola operatorului |
| **Timeout** | Casa nu răspunde | Verifică conexiunea |

## 📝 Log-uri

### **Anulare Reușită:**
```
═══════════════════════════════════════
❌ Attempting to CANCEL blocked receipt...
✓ Receipt cancelled successfully!
   The opened receipt has been annulled.
═══════════════════════════════════════
```

### **Eroare - Nu Există Bon:**
```
═══════════════════════════════════════
❌ Attempting to CANCEL blocked receipt...
✗ Cancel receipt failed: Command not permitted
═══════════════════════════════════════
```

## 🎯 Best Practices

### ✅ **DO:**
- Folosește anularea **doar când ai un bon blocat**
- Verifică log-urile pentru confirmarea anulării
- Încearcă să retrimitii operațiunea după anulare

### ❌ **DON'T:**
- Nu folosi anularea pentru bonuri deja tipărite
- Nu anula bonuri fără motiv (pentru test, folosește bonuri mici)
- Nu încerca să anulezi când nu există bon deschis

## 🔗 Comenzi Corelate

| Comandă | Cod | Descriere |
|---------|-----|-----------|
| **Open Receipt** | 48 (30h) | Deschide bon fiscal nou |
| **Close Receipt** | 56 (38h) | Închide bon fiscal normal |
| **Cancel Receipt** | 60 (3Ch) | **ANULEAZĂ bon deschis** |
| **Receipt Status** | 76 (4Ch) | Verifică status bon |

## 📚 Referințe

- **Manual DUDE**: `Drivere/DUDE/DOCUMENTATION/UserManual_EN.pdf`
- **Protocol**: `Drivere/DUDE/DOCUMENTATION/FP_Protocol_EN.pdf` (pagina 24, Command 60)
- **Cod Sursă**: 
  - `POSBridge.Devices.Datecs/FiscalEngine.cs` → metoda `CancelReceipt()`
  - `POSBridge.WPF/MainWindow.xaml.cs` → `CancelReceiptButton_Click()`

---

**Versiune**: 1.0  
**Data**: 2026-02-07  
**Autor**: POS Bridge Development Team
