# Ghid Monitorizare Comunicație Incotex - USB/Serial

## Metoda 1: Serial Port Monitor (RECOMANDAT)

### Pași:

1. **Download Free Serial Port Monitor**
   - Link: https://freeserialanalyzer.com/
   - Instalare simplă, fără configurări complexe

2. **Setup monitoring:**
   - Pornește Serial Port Monitor
   - Selectează portul pe care comunică FiscalNet cu Incotex
   - Start Monitoring

3. **Lansează FiscalNet:**
   - Conectează-te la Incotex
   - Execută comenzi simple:
     - Citește informații dispozitiv
     - Deschide bon
     - Adaugă un articol
     - Închide bon
     - Raport X

4. **Analizează traficul:**
   - Vezi comenzile trimise (în hex și ASCII)
   - Vezi răspunsurile de la Incotex
   - Identifică pattern-ul comenzilor

5. **Export logs:**
   - Salvează capturile pentru analiză
   - Caută-mi fișierul și îl analizez

---

## Metoda 2: Wireshark + USBPcap (pentru USB direct)

### Pași:

1. **Instalare:**
   ```
   Download Wireshark: https://www.wireshark.org/
   Include USBPcap în instalare
   ```

2. **Captură USB:**
   - Deschide Wireshark
   - Selectează interfața USB (USBPcap)
   - Start capture
   - Filter: `usb.device_address == <device_address>`

3. **Identifică Incotex:**
   - Caută VID:PID = 0483:5740 (din scanare)
   - Filter Wireshark: `usb.idVendor == 0x0483 && usb.idProduct == 0x5740`

---

## Metoda 3: PowerShell Logging (Basic)

### Script pentru logging COM port activity:

```powershell
# Monitorizare evenimente USB/Serial
Get-WinEvent -LogName 'Microsoft-Windows-USB-USBPORT/Diagnostic' -MaxEvents 100 |
  Where-Object {$_.Message -like "*Incotex*" -or $_.Id -eq 2003} |
  Format-Table TimeCreated, Id, Message -AutoSize
```

---

## Ce să cauți în capturi:

### Pattern-uri comune protocoale case fiscale:

1. **Format comenzi:**
   - Text ASCII: `OPEN\r\n`, `SALE,item,price\r\n`
   - Hexadecimal: `01 48 XX XX ... CS` (STX + CMD + data + Checksum)
   - JSON: `{"cmd":"open","operator":1}`

2. **Delimitatori:**
   - `\r\n` (CR+LF)
   - `\x03` (ETX - End of Text)
   - `\x00` (NULL terminator)

3. **Checksum:**
   - XOR al tuturor bytes
   - CRC16
   - Sum modulo 256

4. **Răspunsuri:**
   - `OK\r\n`
   - `ERROR:code:message\r\n`
   - Status codes (00 = OK, FF = error)

### Comenzi de testat în FiscalNet:

1. ✅ **Info dispozitiv** - să vedem structura răspuns
2. ✅ **Raport X** - comandă simplă
3. ✅ **Bon simplu:**
   - Deschide bon
   - Un articol
   - Plată cash
   - Închide bon
4. ✅ **Cash In/Out** - comenzi auxiliare

---

## Exemplu analiză capturi:

### Exemplu Datecs (pentru comparație):
```
SEND: 48h 00h 00h 00h 00h 05h (OpenFiscalReceipt)
RECV: 00h 00h ... (OK + data)

SEND: 31h "Produs 1" ... (Sale)
RECV: 00h ...

SEND: 35h 00h (CloseFiscalReceipt)  
RECV: 00h "1234" (OK + receipt number)
```

### Ce vom căuta pentru Incotex:
- Același pattern?
- Format diferit?
- Comenzi ASCII vs binary?

---

## Output așteptat:

După monitoring, vom avea:
1. **Lista comenzilor** Incotex (format exact)
2. **Structura răspunsurilor**
3. **Coduri eroare**
4. **Parametri necesari** (operator, password, etc.)

Apoi **completăm `IncotexDevice.cs`** cu comenzile reale!

---

## Contact după capturi:

Salvează capturile în:
`d:\Proiecte Cursor\POS Bridge\Captures\incotex-traffic-<timestamp>.txt`

Și îmi arăți fișierul - voi analiza și voi implementa comenzile!
