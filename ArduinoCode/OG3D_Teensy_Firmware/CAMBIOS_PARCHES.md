# Parches aplicados al firmware OG3D (Teensy 4.1) — 2026-10-31

Carpeta: `OG3D_firmware_parcheado/` — copia íntegra del proyecto con los parches
aplicados. `zNMEAParser.h`, `LEDS.h` y `outputs.h` **no** necesitaban cambios;
`OG3D_Globals.h` es **nueva** (Parche 7).
Todas las tramas UDP (GNSS1 0x6A/0xD1, OG3D 0x6A/0xE1, PGNs entrantes) son
**byte a byte idénticas** a las originales: compatible con tu versión de AgIO/OG3D.
El **Parche 8** solo **añade** una trama nueva de telemetría (0x6A/0xE2) que AgIO
muestra en su monitor UDP y no interpreta como dato: no modifica ninguna trama existente.

Verificación: el conjunto compila sin errores (comprobación sintáctica con gcc)
en **7 configuraciones**: los .ino concatenados en 3 órdenes (principal primero,
alfabético —el caso que daba errores en el IDE— y permutación arbitraria) para
las placas AiO v4.5 (la activa) y v5 proto (`isAllInOneBoardProto`), más cada
.ino compilado como unidad de traducción separada. Ojo: la v5 proto **no
compilaba tampoco antes** de los parches (ver parche 0k).

`parches.diff` contiene el diff unificado completo (4 archivos modificados y 1
cabecera nueva, `OG3D_Globals.h`).

---

## Parche 1 — Failsafe GNSS en `SetPWM` (crítico de seguridad)

**Problema:** si se perdía la altitud GNSS (antena bloqueada, RTK caído) con un
objetivo válido de OG3D y el auto enganchado, `altitudeOG` saltaba al valor
centinela 22 000 000 mm → `cutValve` saturaba a **200** → error +100 → la cuchilla
mandaba **hacia abajo a máxima velocidad**. No había ninguna comprobación local de
frescura de la altura.

**Cambio:**
- `OG3D_Teensy_Firmware.ino`: nuevas constantes junto a `dataGNSSrecieved`:
  ```cpp
  const uint32_t GNSS_FRESH_MS = 600;  // edad máxima de la GGA (ms)
  uint32_t lastGGAms = 0;              // millis() de la última GGA
  ```
- `zHandlers.ino` `GGA_Handler()`: tras `dataGNSSrecieved = 0;` se marca
  `lastGGAms = millis();`
- `SetPWM()`: cuando `targetAltitude` es válido, la desviación **solo se calcula
  si `millis() - lastGGAms < GNSS_FRESH_MS`**; en caso contrario `cutValve = 100`
  (neutro: error 0 → `pwmValue = 0`).

**Impacto:** con GGA a 2 Hz o más rápida el comportamiento es idéntico al actual.
Si la GGA se retrasa más de 600 ms con auto activo, la válvula pasa a neutro hasta
recuperar altura fresca (antes: caída a tope). Ajustable con `GNSS_FRESH_MS`.
Nota: usa un `millis()` dedicado porque `dataGNSSrecieved` queda "enclavado" a 100
por el disparador del bucle y no sirve para medir frescura dentro de `SetPWM`.

## Parche 2 — Verificación del CRC entrante + cota objetivo acotada

**Problema:** en `0x61/0xBA` y `0x61/0xB8` el CRC entrante no se comprobaba
("Incoming CRC, not verified"): un paquete UDP corrupto podía escribir
`targetAltitude` con un `memcpy` directo y mover la cuchilla, o escribir ganancias
PWM en EEPROM.

**Cambio en `OG3D_Teensy_Firmware.ino`:**
- Nueva función `validChecksum(byte*, int)`: suma los bytes 2..4+len y compara con
  el byte 5+len (mismo esquema que usan las tramas que el módulo **envía**). Incluye
  protección contra paquetes truncados y longitudes absurdas.
- `0xBA`: se exige longitud 8 y CRC correcto, si no se descarta el paquete.
- `0xB8`: igual (así no se toca la EEPROM con un paquete corrupto).
- `targetAltitude` se acota a `[-1 000 000 .. 22 000 000]` mm (−1000 m .. +22 km);
  los valores ≥ 20 000 000 conservan su significado de centinela "sin objetivo".

**Conmutador:** si tu versión de la app OG3D **no** envía CRC en estos PGNs (el
síntoma sería que el pin 13 deja de parpadear y las ganancias no se aplican),
pon `#define VERIFY_INCOMING_CRC 0` al inicio del sketch (no recomendado).

## Parche 3 — Autodetección del GPS con barrido de baudios

**Problema:** `detectGGAOnPort()` y `SerialGPS->begin()` usaban siempre 460800
baudios. Si el receptor estaba configurado a otro baud, no se detectaba ni había
datos, sin diagnóstico.

**Cambio en `GPS.ino`:**
- Nueva lista `gpsBaudRates[] = { 460800, 115200, 921600, 38400, 9600 }` (orden de
  probabilidad; 38400 es el por defecto de u-blox, 9600 el NMEA clásico).
- `detectGGAOnPort()` ahora devuelve `uint32_t`: el **baud al que encontró GGA**
  (0 si no encontró nada), probando cada baud con ventana de 500 ms.
- `GPS_setup()` abre el puerto con el baud detectado y lo imprime por serie
  ("GPS a NNNNN baudios"). Si no detecta nada, usa Serial2 a 460800 como antes.

**Impacto:** arranque igual de rápido si el GPS ya está a 460800 en Serial2
(~0,6 s). Si no hay GPS conectado, el arranque tarda ~6 s más por el barrido.
Edita `gpsBaudRates[]` si quieres otros baudios.

## Parche 4 — Heartbeat hacia AgIO reactivado

El bloque que enviaba el "hello" a AgIO cada ~1 s estaba comentado: el módulo solo
respondía al escaneo, no figuraba como "vivo". Se ha descomentado tal cual
(PGN 0x7F/0xC7 a `ipDestination:9999`, `helloCounter` cada 200 ticks = 1 s).
Su CRC ya era correcto (0x47).

## Parche 5 — Hemisferios en `pivotLat` / `pivotLon` (bug latente)

**Problema en `GGA_Handler()`:** `pivotLon` se negaba **siempre**
(`pivotLon *= -1`, solo correcto en hemisferio Oeste) y `pivotLat` nunca aplicaba
N/S. Hoy esas variables no se usan en ningún sitio, pero si se reutilizan darían
coordenadas erróneas.

**Cambio:** se asignan después de leer `latNS`/`lonEW` y se niegan igual que
`latitudeOG`/`longitudeOG` (S → latitud negativa, W → longitud negativa).

## Parche 7 — Compilación multi-archivo robusta (arregla los errores del IDE)

**Síntoma reportado compilando en Arduino 1.8.19 / Teensyduino 1.57:**

```
HW_messages:8:  error: 'IPAddress' has not been declared
GPS:99:         error: 'parser' was not declared in this scope
GPS:116:        error: 'NtripUdp' was not declared in this scope
GPS:119:        error: 'NtripData' was not declared in this scope
HW_messages:36: error: 'Udp' was not declared in this scope
```

**Causa:** el IDE concatena todos los .ino del sketch en un solo .cpp, pero el
orden solo es «el principal primero» cuando el nombre del archivo principal
coincide EXACTAMENTE (mayúsculas incluidas) con el de la carpeta. Con la carpeta
`OG3D_Teensy_firmware` y el archivo `OG3D_Teensy_Firmware.ino` no hay
coincidencia: se concatena en orden alfabético y `GPS.ino` / `HW_messages.ino`
quedan **antes** del principal, sin ver `parser`, `Udp`, `NtripUdp`, `NtripData`
ni el tipo `IPAddress` (que llega con el `#include <NativeEthernet.h>` del
principal). Se reprodujo el fallo en banco y se verificó que desaparece con este
parche.

**Cambios:**
- **Nueva cabecera `OG3D_Globals.h`**: declara `extern` los objetos compartidos
  (`parser`, `Udp`, `NtripUdp`, `NtripData`, `ipDestination`, `GNSS1`, variables
  GNSS que usa `zHandlers.ino`, `SerialGPS`, `gpsBaud`) y los prototipos de las
  funciones entre archivos. Coste: 0 bytes de RAM/Flash (solo declaraciones).
- Los 4 .ino incluyen `OG3D_Globals.h` como primera línea: cada archivo compila
  en cualquier orden de concatenación e incluso como unidad de traducción
  separada.
- `HW_messages.ino`: `sendHardwareMessage` y `SendHardwareMessage` reciben ahora
  la IP como `uint8_t ipDest[]` (p. ej. `ipDestination`) en vez de `IPAddress`;
  dentro se construye `IPAddress dest(ipDest[0..3])`. Así los prototipos
  automáticos que genera el IDE no dependen de `NativeEthernet.h`. Tramas y
  comportamiento idénticos.
- `OG3D_Teensy_Firmware.ino`: `ipDestination` deja de ser `static` (queda
  declarado `extern` en la cabecera). Sin cambio de comportamiento.

**Regla de oro del IDE (apícala igualmente):** la carpeta del sketch debe
llamarse igual que el archivo principal → carpeta `OG3D_Teensy_Firmware` que
contenga `OG3D_Teensy_Firmware.ino`. El ZIP entregado ya viene con esa
estructura: extraer y abrir `OG3D_Teensy_Firmware.ino`.

## Parche 8 — Telemetría por UDP (PGN 0x6A/0xE2 = 27362)

**Motivación:** monitorizar el módulo en tractor sin depender del puerto serie USB
(sin cortar la pista de alimentación USB del Teensy). El módulo ya emite UDP hacia
AgIO, así que la telemetría viaja como un PGN nuevo que el **monitor UDP de AgIO**
muestra directamente (PGN + IP de origen, la del módulo `x.x.x.155`).

**Comportamiento:**
- Nueva trama global `TELEM[]` (40 bytes, 40 de RAM) con el mismo formato que el resto:
  `0x80 0x81 | 0x6A | 0xE2 | 34 | payload(34) | CRC`. El CRC es la suma mod 256 de los
  bytes 2..4+len, el mismo esquema que validó el Parche 2.
- Se envía a `ipDestination:TELEM_PORT` (9999 por defecto) cada `TELEM_MS` ms
  (200 ms = **5 Hz**, ~1,6 kB/s) desde el bucle temporizado, igual que el hello del Parche 4.
- AgIO no conoce este PGN: lo lista en su monitor UDP y lo ignora como dato. Ninguna
  trama existente cambia (0x6A/0xD1, 0x6A/0xE1, 0x7F/0xC7, 0x7E/0xCB y entrantes).
- Conmutador: `#define SEND_UDP_TELEMETRY 1` (0 = no se envía nada, 0 bytes de flash).
- Coste: ~40 bytes de RAM (la trama) y una función de relleno; sin RAM dinámica.

**Payload (34 bytes, little-endian):**

| Off | Tipo | Contenido | Escala / notas |
|-----|------|-----------|----------------|
| +0  | u8  | `fixQualityOG` | 0-8, calidad de fix GGA |
| +1  | u8  | `satNbrOG` | satélites usados |
| +2  | u8  | `fixTypeGGA` | tipo de fix |
| +3  | u8  | flags | b0 `workSwitch` (1=manual/seguro), b1 `autoEnable`, b2 `pwmValue>0` (subir), b3 `pwmValue<0` (bajar), b4 GNSS fresco (<`GNSS_FRESH_MS`, Parche 1), b5 `settingsRecieved` |
| +4  | u16 | `hdopOG` | ×100 |
| +6  | u16 | `speedOG` | ×100 km/h (32000 = inválida) |
| +8  | u16 | edad de la GGA | ms desde la última GGA (satura a 65535); la misma edad que decide el failsafe del Parche 1 |
| +10 | u16 | `watchdogTimer` | 0-250; se mantiene bajo mientras AgIO envíe 0x61 |
| +12 | i32 | `altitudeOG` | mm (22000000 = sin altura) |
| +16 | i32 | `targetAltitude` | mm (≥20000000 = sin objetivo/manual) |
| +20 | u16 | `cutValve` | 0-200 (100 = neutro) |
| +22 | i16 | `pwmValue` | con signo: + subir / − bajar |
| +24 | u32 | `gpsBaud` | baudios detectados (Parche 3) |
| +28 | u32 | uptime | segundos desde el arranque |
| +32 | u8  | versión del formato | 1 |
| +33 | u8  | reservado | 0 |

**Lectura rápida:** si `altitudeOG` vale 22000000 o el bit 4 de flags se apaga con auto
conectado, estás viendo el failsafe del Parche 1 en acción (la GGA llegó tarde): la
cuchilla debe quedarse en neutro (`cutValve` 100, `pwmValue` 0).

**Cómo ver el payload (el monitor de AgIO solo muestra PGN + IP):**
- **Wireshark (recomendado):** captura pasiva en cualquier PC de la subred — la trama
  va a broadcast `x.x.x.255`, no hace falta espejo de puertos ni tocar AgIO. Filtro:
  `udp.port == 9999 && udp[8:4] == 80:81:6a:e2`. Los bytes del payload se leen con la
  tabla de arriba (little-endian).
- **Packet Sender:** muestra el hex en vivo, pero un puerto UDP solo lo puede abrir
  un programa por equipo. Si AgIO corre en el mismo PC, o ejecutas Packet Sender en
  otro dispositivo, o cambias `TELEM_PORT` a `9998` en el firmware (reflash) y
  escuchas el 9998.
- **Decodificador incluido:** `decodificar_telemetria.py` (Python 3, sin
  dependencias) convierte el hex en valores legibles:
  - pegando el hex copiado de Wireshark/Packet Sender:
    `py decodificar_telemetria.py 80816AE2...`
  - o escuchando la red en vivo desde **otro** equipo de la subred:
    `py decodificar_telemetria.py --listen` (o `--listen 9998` si cambiaste TELEM_PORT)

## Parches menores (0a–0j) y corrección de compilación v5 (0k)

| ID | Archivo | Cambio |
|----|---------|--------|
| 0a | main .ino | Banner con el resumen de parches |
| 0b | main .ino | `#define VERIFY_INCOMING_CRC 1` (conmutable) |
| 0c | main .ino | Typo en versión: `(AIO v4 PCB))` → `(AIO v4 PCB)` |
| 0d | main .ino | Eliminado `helloFromAutoSteer[]` / `helloSteerPosition` (sin uso) |
| 0e | main .ino | Eliminado `GNSS1_Size` (sin uso) |
| 0f | main .ino | Eliminado `pwmHist` (nunca se calculaba; `OG3D[12]` sigue enviando 0) |
| 0g | main .ino | `LED_AUTO` arranca apagado en v4.5 (lógica invertida: HIGH = off) |
| 0h/0i | main .ino | LEDs de enlace Ethernet consolidados en un solo bloque (había dos seguidos que duplicaban escrituras) |
| 0j | main .ino | `OG3D[12] = 0; // reservado` (antes `pwmHist`) |
| 0k | main .ino | **Compilación v5 proto**: los bloques GPIO de LEDs de estado (setup y loop) ahora se compilan solo con `useLEDs && !isAllInOneBoardProto`, porque los pines `GGAReceivedLED`, `Ethernet_Active_LED`, `GPSRED_LED`, `GPSGREEN_LED` solo existen en la rama v4.5. En la v5 proto el estado lo pintan los LEDs RGB del PCA9685 (clase `LEDS`). Sin efecto en la v4.5. |

## Limpieza de código muerto (parche 6)

| Eliminado | Archivo | Motivo |
|-----------|---------|--------|
| `SerialGPS2` | GPS.ino | Se asignaba en la detección y jamás se leía |
| `nmeaBuffer`, `count`, `stringComplete` | GPS.ino | Restos de Panda sin uso |
| `CK_A`, `CK_B`, `ackPacket[72]`, `calcChecksum()` | GPS.ino | ACK u-blox que nunca se leía ni se llamaba |
| `clearBufferArray()` | GPS.ino | Sin llamadas |
| `asciiHex`, `nmea[100]`, `CalculateChecksum()` | zHandlers.ino | El $PANDA ya no se construye (se envía binario) |
| `useHPR`, `dualReadyHPR` | zHandlers.ino | Banderas sin uso (se conservan `hprHeading/hprRoll/hprSolQuality`) |
| `temp[128]`, `checksumLen` | HW_messages.ino | Copia temporal que no se usaba en el checksum |

Se conservan intactos los comentarios de referencia ($PANDA, GGA, RMC, VTG) de
`zHandlers.ino` y la estructura de `ZDA_Handler()` (gancho vacío).

---

## Notas adicionales

- **EEPROM y 0xB8:** no hizo falta cambio: en Teensy `EEPROM.put()` solo reescribe
  los bytes que cambian, así que el desgaste por enviar ajustes repetidos no es
  un problema real (corrección de lo apuntado en el análisis inicial).
- **Advertencia:** si tu sketch tiene archivos `.ino/.cpp` adicionales que no
  subiste y que usaran `calcChecksum`, `clearBufferArray` o `SerialGPS2`,
  reponlos antes de compilar (no hay indicios de ello en lo entregado).
- **Prueba en banco recomendada:** (1) verificar en el monitor serie que imprime
  "GPS a 460800 baudios"; (2) con OG3D conectado, ver el módulo "vivo" en AgIO;
  (3) probar auto con objetivo cercano y tapar la antena >1 s: la cuchilla debe
  quedarse en neutro (LED_DW/LED_UP apagados), no caer a tope.
  (4) abrir el monitor UDP de AgIO y buscar el PGN 27362 (0x6A/0xE2): la telemetría
  del Parche 8 debe aparecer cada 200 ms con la IP del módulo.

## Parámetros ajustables introducidos

| Parámetro | Valor | Dónde |
|-----------|-------|-------|
| `GNSS_FRESH_MS` | 600 ms | constantes globales del sketch principal |
| `VERIFY_INCOMING_CRC` | 1 | inicio del sketch principal |
| `gpsBaudRates[]` | 460800, 115200, 921600, 38400, 9600 | GPS.ino |
| Ventana de detección por baud | 500 ms | llamada en `GPS_setup()` |
| `SEND_UDP_TELEMETRY` | 1 | globals del sketch principal (Parche 8) |
| `TELEM_MS` | 200 ms (5 Hz) | globals del sketch principal (Parche 8) |
| `TELEM_PORT` | 9999 | globals del sketch principal (Parche 8; pásalo a 9998 si Packet Sender comparte PC con AgIO) |
