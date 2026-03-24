rem %1 =COM#   %2 =file.txt
mode %1 BAUD=115200 PARITY=n DATA=8
copy %2 \\.\%1  /B
