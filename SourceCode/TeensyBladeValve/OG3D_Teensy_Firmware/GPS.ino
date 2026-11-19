//------------------------------------------------------------------------------
// Parche 7: declara (extern) los objetos que viven en los otros .ino (parser,
// NtripUdp, NtripData...). Debe ser la PRIMERA linea de este archivo para que
// el sketch compile sea cual sea el orden de concatenacion de los .ino.
#include "OG3D_Globals.h"
//------------------------------------------------------------------------------

// Definimos los punteros para los puertos dinámicos
HardwareSerial* SerialGPS = &Serial7; 
#define GPS_RTK Serial3
#define RTK_Baud 115200

// Parche 3: baudios a probar en la autodetección del GPS (orden de probabilidad)
const uint32_t gpsBaudRates[] = { 460800, 115200, 921600, 38400, 9600 };
uint32_t gpsBaud = 460800;  // baud del GPS detectado (o el por defecto)

char rxbuffer[512];   
char txbuffer[1023];  
char rxbuffer_RTK[1023];      


// Función auxiliar para detectar tramas GGA en un puerto.
// Parche 3: prueba varios baudios y devuelve el baud al que se encontró el GPS (0 si no).
uint32_t detectGGAOnPort(HardwareSerial* port, uint32_t detectionMs) {
  const char* pattern1 = "$GNGGA";
  const char* pattern2 = "$GPGGA";

  for (uint8_t b = 0; b < sizeof(gpsBaudRates) / sizeof(gpsBaudRates[0]); b++) {
    uint8_t idx1 = 0;
    uint8_t idx2 = 0;

    port->begin(gpsBaudRates[b]);
    delay(100);

    while (port->available()) {
      port->read();
    }

    uint32_t start = millis();
    while (millis() - start < detectionMs) {
      while (port->available()) {
        char c = port->read();
        if (c == pattern1[idx1]) {
          idx1++;
          if (pattern1[idx1] == '\0') {
            port->end();
            return gpsBaudRates[b];
          }
        } else {
          idx1 = (c == pattern1[0]) ? 1 : 0;
        }

        if (c == pattern2[idx2]) {
          idx2++;
          if (pattern2[idx2] == '\0') {
            port->end();
            return gpsBaudRates[b];
          }
        } else {
          idx2 = (c == pattern2[0]) ? 1 : 0;
        }
      }
    }
    port->end();
  }
  return 0;
}

//**************************************************************

void GPS_setup() {
  Serial.println("Detectando puerto del GPS (Serial2 o Serial7)...");
  
  // Comprobamos primero en el Serial2 (donde lo tienes conectado)
  uint32_t foundBaud = detectGGAOnPort(&Serial2, 500);
  
  if (foundBaud) {
    Serial.println("¡GPS detectado automáticamente en Serial2!");
    SerialGPS = &Serial2;
  } else {
    Serial.println("No se encontró en Serial2, probando Serial7...");
    foundBaud = detectGGAOnPort(&Serial7, 500);
    if (foundBaud) {
      Serial.println("¡GPS detectado automáticamente en Serial7!");
      SerialGPS = &Serial7;
    } else {
      Serial.println("No se detectó GPS. Usando Serial2 a 460800 por defecto.");
      SerialGPS = &Serial2;
      foundBaud = 460800;
    }
  }

  gpsBaud = foundBaud;
  Serial.print("GPS a ");
  Serial.print(gpsBaud);
  Serial.println(" baudios");

  // Inicializamos el puerto seleccionado con el baud detectado
  SerialGPS->begin(gpsBaud);
  SerialGPS->addMemoryForRead(rxbuffer, 512);
  SerialGPS->addMemoryForWrite(txbuffer, 1023);

  GPS_RTK.begin(RTK_Baud);
  GPS_RTK.addMemoryForRead(rxbuffer_RTK, 1023);

  parser.setErrorHandler(errorHandler);
  parser.addHandler("G-GGA", GGA_Handler);
  parser.addHandler("G-VTG", VTG_Handler);
  parser.addHandler("G-HPR", HPR_Handler);
}

//**************************************************************

void Panda_GPS() {
  while (SerialGPS->available()) {
    parser << SerialGPS->read();
  }
}

//**************************************************************

void Forward_Ntrip() {
  int NtripSize = NtripUdp.parsePacket();

  if (NtripSize) {
    NtripUdp.read(NtripData, NtripSize);
    SerialGPS->write(NtripData, NtripSize);
  }

  if (GPS_RTK.available()) {
    SerialGPS->write(GPS_RTK.read());
  }
}

