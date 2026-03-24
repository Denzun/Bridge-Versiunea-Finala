# Test Bon Fiscal - Ghid Rapid

## Descriere

Am adăugat un **buton de test** în aplicație care trimite automat un bon fiscal de exemplu către casa de marcat, conform manualului DUDE.

## Bonul Fiscal de Test

Butonul `🧪 Test Fiscal Receipt` va trimite următorul bon:

```
═══════════════════════════════
    BON FISCAL DE TEST
═══════════════════════════════

1 x Cafea       @ 5.50 lei
2 x Croissant   @ 3.00 lei
───────────────────────────────
SUBTOTAL:          11.50 lei
───────────────────────────────
PLATĂ NUMERAR:     20.00 lei
REST:               8.50 lei
═══════════════════════════════
```

## Cum să folosești testul

### 1. **Conectează-te la casa de marcat**
   - Selectează portul COM corect (ex: COM6)
   - Selectează baud rate (ex: 115200)
   - Setează **Operator Code** (ex: 1)
   - Setează **Operator Password** (ex: 0000)
   - Click pe `📡 Connect Device`

### 2. **Rulează testul**
   - Click pe butonul `🧪 Test Fiscal Receipt` din secțiunea **TESTING**
   - Vei vedea un dialog de confirmare cu detaliile bonului
   - Click `Yes` pentru a continua
   - Aplicația va executa automat toate comenzile:
     1. **Open Receipt** (Deschide bon fiscal)
     2. **Sale** (Cafea x 1 @ 5.50 lei)
     3. **Sale** (Croissant x 2 @ 3.00 lei)
     4. **Subtotal** (Calcul intermediar)
     5. **Payment** (Plată 20.00 lei numerar)
     6. **Close Receipt** (Închide bonul)

### 3. **Verifică rezultatul**
   - Bonul fiscal se va tipări pe casa de marcat
   - În log vei vedea detalii despre execuție
   - Dialog de confirmare cu numărul bonului și durata

## Structura Comenzilor Trimise

Testul creează un `ReceiptCommandFile` cu următoarele comenzi:

```csharp
Commands:
1. Sale          → Text: "Cafea", Price: 5.50, Qty: 1.000, Unit: "buc", TaxGroup: 1, Dept: 1
2. Sale          → Text: "Croissant", Price: 3.00, Qty: 2.000, Unit: "buc", TaxGroup: 1, Dept: 1
3. Subtotal      → (calcul intermediar)
4. Payment       → PaymentType: 0 (CASH), Value: 20.00
```

## Comenzile DUDE Execute Sub Capotă

### 1. **Command 48 (30h) - Open fiscal receipt**
```
Input: OpCode[TAB]OpPwd[TAB]TillNmb[TAB][TAB][TAB]
Exemplu: 1⟶0000⟶1⟶⟶⟶
```

### 2. **Command 49 (31h) - Register sale (Cafea)**
```
Input: PluName[TAB]TaxCd[TAB]Price[TAB]Quantity[TAB][TAB][TAB]Department[TAB]Unit
Exemplu: Cafea⟶1⟶5.50⟶1.000⟶⟶⟶1⟶buc
```

### 3. **Command 49 (31h) - Register sale (Croissant)**
```
Input: Croissant⟶1⟶3.00⟶2.000⟶⟶⟶1⟶buc
```

### 4. **Command 51 (33h) - Subtotal**
```
Input: 1⟶0⟶⟶
(Print=1, Display=0, no discount)
```

### 5. **Command 53 (35h) - Payment (Total)**
```
Input: PaidMode[TAB]Amount
Exemplu: 0⟶20.00
(0 = CASH/NUMERAR)
```

### 6. **Command 56 (38h) - Close fiscal receipt**
```
Input: (fără parametri)
```

## Troubleshooting

### ❌ "Wrong operator password"
- Verifică parola operatorului în casa de marcat
- Folosește butonul `🔐 Test Operator` pentru a valida credențialele
- Parola implicită este `0000`, dar poate fi diferită pe device-ul tău

### ❌ "Device not connected"
- Asigură-te că ești conectat la casă (butonul Connect)
- Verifică că DUDE COM Server este instalat
- Verifică portul COM și baud rate-ul

### ❌ "Command not permitted"
- Casa poate avea deja un bon deschis
- Încearcă să tipărești un raport X pentru a reseta starea
- Verifică că nu este în modul service/training

### ❌ "End of paper"
- Verifică hârtia în imprimantă
- Asigură-te că rola este montată corect

### ❌ "Receipt already open"
- Un bon este deja deschis
- Anulează bonul curent sau închide-l manual
- Reîncearcă testul

## Loguri în Aplicație

În fereastra de log vei vedea:

```
═══════════════════════════════════════
🧪 Starting TEST FISCAL RECEIPT...
═══════════════════════════════════════
1. Opening fiscal receipt...
   Operator: 1, Password: ****
2. Registering sale: Cafea x 1.000 buc @ 5.50 lei (Grupa A)
3. Registering sale: Croissant x 2.000 buc @ 3.00 lei (Grupa A)
4. Subtotal...
5. Payment: 20.00 lei (NUMERAR)
6. Closing receipt...
═══════════════════════════════════════
✓ Test receipt completed successfully!
   Receipt Number: 0001
   Duration: 2.34s
═══════════════════════════════════════
```

## Notă Importantă

⚠️ **Acest test TIPĂREȘTE UN BON FISCAL REAL** pe casa de marcat!
- Bonul va fi înregistrat în memoria fiscală
- Va apărea în rapoartele Z
- Folosește-l doar pentru testare, nu pentru producție
- După test, poți anula bonul sau să îl consideri ca și vânzare de test

## Cod Sursă

Funcția de test se află în:
- **UI**: `POSBridge.WPF\MainWindow.xaml` (buton)
- **Logic**: `POSBridge.WPF\MainWindow.xaml.cs` → metoda `TestFiscalReceiptButton_Click()`
- **Engine**: `POSBridge.Devices.Datecs\FiscalEngine.cs` → metoda `ProcessCommandFile()`

## Testare Manuală vs Automată

| Aspect | Test Manual (fișier .txt) | Test Automat (buton) |
|--------|---------------------------|----------------------|
| **Creare fișier** | Da, manual în folder Bon | Nu, generat automat |
| **Confirmare** | Da, per sesiune | Da, per test |
| **Monitorizare** | Folder watcher | Direct |
| **Rezultat** | Fișier răspuns în Bon/Raspuns | Dialog pe ecran |
| **Istoric** | Fișier mutat în Procesate | Doar în log |

## Următorii Pași

După ce testul funcționează:
1. ✅ Verifică bonul tipărit pe casă
2. ✅ Testează cu rapoarte X/Z
3. ✅ Testează procesarea de fișiere din folder `.\Bon\`
4. ✅ Creează propriile fișiere .txt cu comenzi

---

**Data creării**: 2026-02-07  
**Versiune**: 1.0  
**Autor**: POS Bridge Development Team
