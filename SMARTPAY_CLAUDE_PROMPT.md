# SmartPay/Ingenico ECR Link Protocol - Help Needed

## Context
I'm implementing the SmartPay ECR Link Protocol v1.8 for connecting to Ingenico payment terminals (iCT220, iPP320, etc.) via USB Serial (virtual COM port).

## Protocol Specification (from PDF)

### Frame Structure
```
[STX] [Length(2)] [Tag(2)] [Len(1)] [Data...] [ETX] [CRC(2)]
  0x02   MSB,LSB    MSB,LSB   0xXX   Variable   0x03   MSB,LSB
```

### Communication Flow (from PDF)
```
SEND: 0x05 (ENQ)
RECV: 0x06 (ACK)

SEND: [STX][LEN][DATA][ETX][CRC]  (full packet)
RECV: 0x06 (ACK)

RECV: [STX][LEN][DATA][ETX][CRC]  (response packet)
SEND: 0x06 (ACK)
```

### Control Characters
- STX = 0x02 (Start of Text)
- ETX = 0x03 (End of Text)
- ENQ = 0x05 (Enquiry)
- ACK = 0x06 (Acknowledge)
- NAK = 0x15 (Negative Acknowledge)

### CRC16 IBM
- Polynomial: 0xA001
- Initial: 0xFFFF
- For requests: MSB, LSB format
- For responses: LSB, MSB format

### Example from PDF (Get Info command)
```
SEND 1 Byte:  05                    (ENQ)
RECV 1 Byte:  06                    (ACK)

SEND 10 Bytes: 02 00 04 a0 00 01 01 03 06 35
  - 02        = STX
  - 00 04     = Length (4 bytes)
  - a0 00 01 01 = Tag 0xA000, Len 1, Data 0x01 (Get Info)
  - 03        = ETX
  - 06 35     = CRC

RECV 1 Byte:  06                    (ACK)

RECV 63 Bytes: 02 00 39 a1 00 01 00 a1 01 08 31 2e 30 33 2e 30 38 31 a1 02 20 30 30 30 30 31 38 36 39 30 30 30 30 3b 32 30 30 30 31 34 35 35 20 20 20 20 20 20  20 20 20 20 20 a1 11 04 00 00 00 00 03 60 eb
  - Response with device info

SEND 1 Byte:  06                    (ACK)
```

## Current Implementation

### SmartPaySerialWrapper.cs (SendAndReceive)
```csharp
// Step 1: Clear buffers
_serialPort.DiscardInBuffer();
_serialPort.DiscardOutBuffer();
Thread.Sleep(50);

// Step 2: Send ENQ
_serialPort.Write(new byte[] { SmartPayProtocol.ENQ }, 0, 1);
_serialPort.BaseStream.Flush();

// Step 3: Wait for ACK
int response = _serialPort.ReadByte();
if (response != SmartPayProtocol.ACK)
    throw new Exception($"Expected ACK after ENQ, got 0x{response:X2}");

// Step 4: Send packet
_serialPort.Write(packet, 0, packet.Length);
_serialPort.BaseStream.Flush();

// Step 5: Wait for ACK
int packetAck = _serialPort.ReadByte();
if (packetAck != SmartPayProtocol.ACK)
    throw new Exception($"Expected ACK after packet, got 0x{packetAck:X2}");

// Step 6: Wait for response with retry
Thread.Sleep(100);
int stx = -1;
int retryCount = 0;
while (stx != SmartPayProtocol.STX && retryCount < 3)
{
    try
    {
        stx = _serialPort.ReadByte();
        if (stx == SmartPayProtocol.STX)
            break;
        if (stx == SmartPayProtocol.ACK)
        {
            Thread.Sleep(100);
        }
        retryCount++;
    }
    catch (TimeoutException)
    {
        retryCount++;
    }
}

if (stx != SmartPayProtocol.STX)
    throw new Exception($"Expected STX, got 0x{stx:X2}");

// Step 7: Read length (2 bytes)
byte[] lengthBytes = new byte[2];
_serialPort.Read(lengthBytes, 0, 2);
uint16 dataLength = (ushort)((lengthBytes[0] << 8) | lengthBytes[1]);

// Step 8: Read data + ETX + CRC
byte[] remaining = new byte[dataLength + 3];
int read = 0;
while (read < remaining.Length)
{
    int r = _serialPort.Read(remaining, read, remaining.Length - read);
    if (r == 0) Thread.Sleep(50);
    read += r;
}

// Step 9: Send final ACK
_serialPort.Write(new byte[] { SmartPayProtocol.ACK }, 0, 1);
```

### Current Errors

**Error 1:**
```
Expected STX, got 0x06
```

**Error 2:**
```
Expected STX, got 0x04
```

**Error 3:**
```
Timeout waiting for SmartPay response
```

### Serial Port Settings
- Baud Rate: 115200
- Data Bits: 8
- Parity: None
- Stop Bits: One
- Handshake: None
- ReadTimeout: 30000ms
- WriteTimeout: 5000ms

## What We've Tried

1. ✅ Different baud rates (115200, 9600)
2. ✅ Adding delays between operations (50-100ms)
3. ✅ Retry logic for reading STX (up to 3 attempts)
4. ✅ Clearing buffers before each operation
5. ✅ Full ENQ-ACK handshake before each packet
6. ✅ Different USB ports and cables

## Device Information

- Device: Ingenico terminal (detected as "USB Serial Device")
- COM Port: COM10
- Driver: Windows USB Serial driver
- Connection: USB cable

## Questions

1. **What could cause getting ACK (0x06) instead of STX (0x02)?**
   - Is the device sending multiple ACKs?
   - Is the timing between packet send and response read too fast?

2. **What does 0x04 (EOT) mean in this context?**
   - Is it an error code?
   - Does it indicate end of transmission?

3. **Are there common pitfalls with Ingenico USB serial communication?**
   - Special initialization sequence?
   - Different protocol modes?
   - DTR/RTS handshake needed?

4. **How should I properly synchronize the communication?**
   - Should I wait longer after sending packet?
   - Is there a way to poll for data availability?
   - Should I use DataReceived event instead of blocking Read?

5. **What debugging steps do you recommend?**
   - How to verify the CRC calculation?
   - How to sniff the actual bytes on the wire?
   - Common tools for serial debugging?

## Additional Context

- The device is detected by Windows as "USB Serial Device" on COM10
- The driver appears to be installed correctly
- The device is powered on and responsive
- Using .NET System.IO.Ports.SerialPort on Windows 10/11

## Help Needed

Please analyze the protocol flow and timing. What am I doing wrong? What's the correct sequence and timing for reliable communication with Ingenico terminals via SmartPay ECR Link protocol?
