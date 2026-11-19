//--------------------------------------------------------------------------------
// OG3D_Globals.h -- Parche 7: compatibilidad de compilacion multi-archivo
//
// POR QUE EXISTE ESTE ARCHIVO:
// El IDE de Arduino/Teensyduino concatena todos los .ino del sketch en un solo
// archivo .cpp, pero el orden NO siempre es "el sketch principal primero": si el
// nombre del archivo principal no coincide EXACTAMENTE (mayusculas incluidas)
// con el nombre de la carpeta, se concatenan en orden alfabetico y GPS.ino y
// HW_messages.ino quedan ANTES de OG3D_Teensy_Firmware.ino, sin ver sus
// declaraciones. Sintomas tipicos:
//   error: 'IPAddress' has not been declared
//   error: 'parser' was not declared in this scope
//   error: 'Udp' was not declared in this scope
//   error: 'NtripUdp' was not declared in this scope
//   error: 'NtripData' was not declared in this scope
//
// SOLUCION: este encabezado declara como 'extern' los objetos globales
// compartidos y los prototipos de las funciones entre archivos, de forma que
// cada .ino compila en cualquier orden. Las definiciones reales siguen donde
// estaban (OG3D_Teensy_Firmware.ino, GPS.ino, HW_messages.ino) y no cambia
// ningun comportamiento: coste de RAM/Flash adicional = 0 bytes.
//
// IMPORTANTE (regla de oro del IDE): la carpeta del sketch debe llamarse igual
// que el archivo principal. Aqui: carpeta "OG3D_Teensy_Firmware" que contenga
// "OG3D_Teensy_Firmware.ino". Con este parche compila incluso si no coinciden,
// pero manteniendo el nombre evitas otros efectos secundarios del IDE.
//--------------------------------------------------------------------------------
#ifndef OG3D_GLOBALS_H
#define OG3D_GLOBALS_H

#include <Arduino.h>
#include <NativeEthernet.h>
#include <NativeEthernetUdp.h>
#include "zNMEAParser.h"

//-------------------- Definidos en OG3D_Teensy_Firmware.ino --------------------
extern NMEAParser<3> parser;      // parser NMEA (3 manejadores como maximo)
extern EthernetUDP Udp;           // UDP principal (puerto local 8888)
extern EthernetUDP NtripUdp;      // UDP de correcciones NTRIP (puerto 2233)
extern byte udpData[128];         // buffer de recepcion UDP
extern byte NtripData[512];       // buffer de correcciones NTRIP
extern uint8_t ipDestination[];   // IP de broadcast AgIO/OG3D (x.x.x.255)
extern uint8_t GNSS1[];           // trama GNSS1 (0x6A/0xD1) hacia OG3D

// Datos GNSS compartidos con zHandlers.ino
extern bool blink;
extern float utcTime, geoidalGGA;
extern uint8_t fixTypeGGA, satsGGA;
extern float hdopGGA, rtkAgeGGA;
extern double pivotLat, pivotLon, fixHeading, pivotAltitude;
extern int64_t longitudeOG, latitudeOG;  // x 10^9
extern int32_t altitudeOG;               // mm
extern uint16_t dualHeadingOG, singleHeadingOG, imuHeadingOG;  // x 100
extern int16_t dualRollOG, imuRollOG, imuPitchOG, imuYawRateOG, speedOG, hdopOG, ageOG;
extern uint8_t fixQualityOG, satNbrOG;
extern int32_t dataGNSSrecieved;  // ticks de 5 ms desde la ultima GGA
extern uint32_t lastGGAms;        // millis() de la ultima GGA (Parche 1)

//------------------------------ Definidos en GPS.ino ------------------------------
extern HardwareSerial* SerialGPS;  // puerto serie donde se detecto el GPS
extern uint32_t gpsBaud;           // baudios detectados del GPS (Parche 3)

//------------------------ Prototipos de funciones compartidas ------------------------
// El IDE genera los suyos automaticamente; estos no son necesarios cuando todo
// se concatena en un solo .cpp, pero permiten que cada archivo compile tambien
// por separado y documentan la interfaz entre archivos.
void errorHandler();
void GGA_Handler();
void VTG_Handler();
void HPR_Handler();
void ZDA_Handler();
void BuildNmea();
uint32_t detectGGAOnPort(HardwareSerial *port, uint32_t detectionMs);
void GPS_setup();
void Panda_GPS();
void Forward_Ntrip();
// NOTA (Parche 7): la IP de destino se pasa como uint8_t[4] y no como IPAddress
// para que los prototipos automaticos del IDE no dependan de NativeEthernet.h.
void sendHardwareMessage(const String &message, uint8_t seconds, uint8_t color, uint8_t ipDest[], uint16_t port);
void SendHardwareMessage(uint8_t data[], uint8_t size, uint8_t ipDest[], uint16_t port);
bool validChecksum(byte *inData, int packetSize);
void udpMessageRecv(int sizeToRead);
void SetPWM(void);
void SaveToEEPROM();
void ReadFromEEPROM();
void SendUDPbladeData();
void sendTelemetry();  // Parche 8: trama de telemetria UDP 0x6A/0xE2 hacia AgIO

#endif  // OG3D_GLOBALS_H
