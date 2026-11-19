// ----------------------------------------------------------
// Send a hardware message with proper header, payload, and CRC
//
// Parche 7: include que declara Udp (definido en OG3D_Teensy_Firmware.ino) y los
// tipos de red. Debe ir antes de cualquier otra sentencia de este archivo para
// que compile en cualquier orden de concatenacion de los .ino.
#include "OG3D_Globals.h"

String HWmessage = "Error";

// sendHardwareMessage("put your message here", 3, 1, ipDestination, 9999)

// Parche 7: la IP de destino se recibe como uint8_t[4] (p.ej. ipDestination) en
// lugar de IPAddress. Asi los prototipos automaticos que genera el IDE no
// dependen de NativeEthernet.h y el sketch compila en cualquier orden.
void sendHardwareMessage(const String& message, uint8_t seconds, uint8_t color, uint8_t ipDest[], uint16_t port) {
  uint8_t hardwareMessage[128] = { 0x80, 0x81, 0x7E, 221 };

  int msgLen = message.length();  // byte count (ASCII assumed)
  if (msgLen > 120) {
    Serial.println("Error: Message too long for hardware message buffer");
    return;
  }
  int totalLength = 7 + msgLen + 1;  // header(7) + message + CRC(1)

  hardwareMessage[4] = msgLen + 2;  // message length + display config
  hardwareMessage[5] = seconds;     // seconds to display
  hardwareMessage[6] = color;       // color (0 = normal, 1 = alt)

  // Copy message into buffer
  message.getBytes(&hardwareMessage[7], msgLen + 1);

  // Sum for checksum
  int16_t CK_A = 0;
  for (int i = 2; i < 7 + msgLen; i++) {
    CK_A += hardwareMessage[i];
  }
  hardwareMessage[7 + msgLen] = CK_A;  // CRC

  SendHardwareMessage(hardwareMessage, totalLength, ipDest, port);
}

void SendHardwareMessage(uint8_t data[], uint8_t size, uint8_t ipDest[], uint16_t port) {
  IPAddress dest(ipDest[0], ipDest[1], ipDest[2], ipDest[3]);
  Udp.beginPacket(dest, port);
  Udp.write(data, size);
  Udp.endPacket();
}
/* exemple
void showCanbusStateOnAOG(uint8_t state) {
  uint8_t messageSeconds = 2;  //duration in seconds
  uint8_t messageColor = 0;    // 1 for Light message, 0 for red warning

  HWmessage = String("Test");

  // ipDestination es uint8_t[4], compatible con el nuevo parametro ipDest[]
  sendHardwareMessage(HWmessage, messageSeconds, messageColor, ipDestination, 9999);
}
*/