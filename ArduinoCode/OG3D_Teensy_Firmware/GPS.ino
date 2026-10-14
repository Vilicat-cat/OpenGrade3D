// Definimos los punteros para los puertos dinámicos
HardwareSerial* SerialGPS = &Serial7; 
HardwareSerial* SerialGPS2 = &Serial2;

#define GPS_RTK Serial3
#define RTK_Baud 115200

char rxbuffer[512];   
char txbuffer[1023];  
char rxbuffer_RTK[1023];      

char nmeaBuffer[200];
int count = 0;
bool stringComplete = false;

byte CK_A = 0;
byte CK_B = 0;
byte ackPacket[72] = { 0xB5, 0x62, 0x01, 0x3C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

// Función auxiliar para detectar tramas GGA en un puerto
bool detectGGAOnPort(HardwareSerial* port, uint32_t detectionMs) {
  const char* pattern1 = "$GNGGA";
  const char* pattern2 = "$GPGGA";
  uint8_t idx1 = 0;
  uint8_t idx2 = 0;

  port->begin(460800);
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
          return true;
        }
      } else {
        idx1 = (c == pattern1[0]) ? 1 : 0;
      }

      if (c == pattern2[idx2]) {
        idx2++;
        if (pattern2[idx2] == '\0') {
          port->end();
          return true;
        }
      } else {
        idx2 = (c == pattern2[0]) ? 1 : 0;
      }
    }
  }
  port->end();
  return false;
}

//**************************************************************

void GPS_setup() {
  Serial.println("Detectando puerto del GPS (Serial2 o Serial7)...");
  
  // Comprobamos primero en el Serial2 (donde lo tienes conectado)
  bool foundOnSerial2 = detectGGAOnPort(&Serial2, 400);
  
  if (foundOnSerial2) {
    Serial.println("¡GPS detectado automáticamente en Serial2!");
    SerialGPS = &Serial2;
    SerialGPS2 = &Serial7;
  } else {
    Serial.println("No se encontró en Serial2, probando Serial7...");
    bool foundOnSerial7 = detectGGAOnPort(&Serial7, 400);
    if (foundOnSerial7) {
      Serial.println("¡GPS detectado automáticamente en Serial7!");
      SerialGPS = &Serial7;
      SerialGPS2 = &Serial2;
    } else {
      Serial.println("No se detectó GPS. Usando Serial2 por defecto.");
      SerialGPS = &Serial2;
      SerialGPS2 = &Serial7;
    }
  }

  // Inicializamos el puerto seleccionado con 460800 baudios
  SerialGPS->begin(460800);
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

bool calcChecksum() {
  CK_A = 0;
  CK_B = 0;
  for (int i = 2; i < 70; i++) {
    CK_A = CK_A + ackPacket[i];
    CK_B = CK_B + CK_A;
  }
  return (CK_A == ackPacket[70] && CK_B == ackPacket[71]);
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

//-------------------------------------------------------------------------------------------------

void clearBufferArray() {
  strcpy(nmeaBuffer, "");
  stringComplete = false;
}
