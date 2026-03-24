# DUDE Auto-Launch Feature

## Descriere

POSBridge include acum funcționalitatea de auto-lansare a aplicației DUDE. Aplicația verifică la pornire dacă DUDE rulează și îl lansează automat dacă este necesar. La închiderea POSBridge, DUDE este închis automat.

## Cum Funcționează

### La Pornirea POSBridge:
1. Aplicația verifică dacă procesul DUDE rulează deja
2. Dacă DUDE nu rulează, îl lansează automat
3. Așteaptă 3 secunde pentru inițializarea DUDE
4. Continuă cu conectarea la imprimanta fiscală

### La Închiderea POSBridge:
1. Deconectează de la imprimanta fiscală
2. Închide procesul DUDE (dacă a fost lansat de POSBridge)
3. Folosește închidere grațioasă (CloseMainWindow)
4. Dacă nu răspunde în 5 secunde, forțează închiderea

## Configurare

### Calea către DUDE

Implicit, POSBridge caută DUDE la:
```
C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

Pentru a modifica calea, editați fișierul `settings.txt`:
```
BonFolder=D:\Proiecte Cursor\POS Bridge\Bon
OperatorCode=1
OperatorPassword=0000
RunAtStartup=1
ComPort=COM7
BaudRate=115200
DudePath=C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
```

### Parametrul DudePath

Puteți schimba calea către DUDE modificând linia:
```
DudePath=C:\CalePersonalizata\Datecs\DUDE\DUDE.exe
```

**Important:** Folosiți calea completă către executabilul DUDE.exe

## Comportament în Caz de Eroare

### DUDE nu este găsit
- Se afișează un MessageBox cu avertisment
- Aplicația continuă să pornească (fără DUDE)
- Conectarea la imprimantă poate să nu funcționeze

### DUDE nu poate fi lansat
- Se afișează un MessageBox cu eroarea
- Aplicația continuă să pornească
- Se loghează eroarea în fișierul de log

### DUDE nu poate fi închis
- Se încearcă închidere grațioasă
- Dacă nu răspunde, se forțează închiderea
- Aplicația se închide normal oricum
- Se loghează avertisment în log

## Verificare Log-uri

### App Log (Logs/app_YYYYMMDD.log)
Verificați pornirea DUDE:
```
[12:34:56] Lansare DUDE de la: C:\Program Files (x86)\Datecs Applications\DUDE\DUDE.exe
[12:34:56] ✓ DUDE lansat cu succes (PID: 12345)
[12:34:59] ✓ Așteptare inițializare DUDE completă
```

### MainWindow Log (Logs/log_YYYYMMDD.txt)
Verificați închiderea DUDE:
```
[12:45:00] Închidere proces DUDE (PID: 12345)...
[12:45:00] ✓ Proces DUDE închis cu succes
```

## Întrebări Frecvente

### DUDE se lansează de două ori?
Nu. Aplicația verifică dacă DUDE rulează deja înainte de a-l lansa.

### Ce se întâmplă dacă lansez manual DUDE înainte?
POSBridge va detecta că DUDE rulează și nu va lansa o a doua instanță.

### POSBridge închide DUDE dacă l-am deschis manual?
Da. La închidere, POSBridge închide întotdeauna DUDE, indiferent cine l-a deschis.

### Ce se întâmplă dacă DUDE nu este instalat?
Se afișează un avertisment și aplicația continuă să pornească, dar comunicarea cu imprimanta fiscală nu va funcționa.

### Pot dezactiva auto-lansarea DUDE?
Momentan nu există o opțiune pentru a dezactiva această funcționalitate. Dacă doriți acest lucru, contactați dezvoltatorul.

## Avantaje

✅ **Simplificare utilizare**: Nu mai trebuie să deschideți manual DUDE înainte de POSBridge  
✅ **Curățenie procese**: DUDE se închide automat, fără procese rămase în fundal  
✅ **Configurabil**: Calea către DUDE poate fi personalizată  
✅ **Robust**: Gestionează corect erorile și continuă funcționarea  
✅ **Transparent**: Toate acțiunile sunt logate pentru debugging  

## Support

Pentru probleme sau întrebări, verificați:
1. Fișierele de log în folderul `Logs/`
2. Documentul `IMPLEMENTATION_DUDE_AUTOLAUNCH.md` pentru detalii tehnice
3. Documentul `TEST_DUDE_AUTOLAUNCH.md` pentru scenarii de testare
