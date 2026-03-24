# Verifică porturile COM disponibile
[System.IO.Ports.SerialPort]::GetPortNames() | Sort-Object
