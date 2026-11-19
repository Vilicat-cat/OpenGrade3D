# Integración AgIO en OpenGrade3D (beta-UPD)

> **Objetivo:** un solo entorno de desarrollo en el que AgOpenGrade3D lleve *dentro* el módulo
> **AgIO** extraído del repositorio oficial de AgOpenGPS, de modo que sobre la **misma tablet**
> puedan funcionar **AgOpenGPS (AOG) y OpenGrade3D (OG3D) a la vez**, compartiendo la entrada
> del GPS de la cuchilla y el módulo de la cuchilla, **conservando intactos los PGN originales
> de OpenGrade**.

> **Cómo comprobar que ejecutas el AgIO de esta integración:** la barra de título de AgIO debe
> **terminar en `| OG3D v3`** (p. ej. «AgIO v1.0.0 Profile: new   |   OG3D v3») y el recuadro
> OG3D debe incluir el rótulo **«Fuente GPS:»** con el botón selector. Si tu AgIO no cumple
> ambas cosas, estás ejecutando un `AgIO.exe` antiguo: recompila la solución y lanza el
> `AgIO.exe` que queda en la carpeta `Application`
> (o simplemente ejecuta `OpenGrade3D.exe` de esa carpeta: lo arranca él solo).

---

## 1. Por qué no podían convivir (causa raíz)

La versión beta-UPD de OpenGrade3D ya habla con AgIO, pero **ambas apps de la tablet se
peleaban por el mismo puerto de bucle**:

| Aplicación        | Socket de recepción del bucle | Envío hacia AgIO            |
|-------------------|-------------------------------|-----------------------------|
| AgOpenGPS (AOG)   | `127.0.0.1:15555`             | `127.255.255.255:17777`     |
| OpenGrade3D (OG3D)| `0.0.0.0:15555` ← **igual**   | `127.255.255.255:17777`     |

El AgIO solo reenviaba el flujo a **un** destino (`15555`). La primera app que arrancaba
ocupaba el puerto y la segunda se quedaba sin recibir nada. Los PGN no eran el problema:
era puramente de puertos.

## 2. Solución aplicada

1. **OpenGrade3D** pasa a escuchar en un puerto propio: **15556** (antes 15555).
2. **AgIO** (integrado) **duplica el envío del bucle** hacia `15555` (AOG) y hacia
   `15556` (OG3D). Cada aplicación ignora los PGN que no le pertenecen, así que no hace
   falta filtrar nada y **los PGN de OpenGrade no cambian ni un byte**.
3. **OpenGrade3D auto-arranca AgIO.exe** si no hay ninguno en marcha. Lo busca primero junto a
   `OpenGrade3D.exe` (carpeta `Application`) y luego en las salidas del proyecto AgIO. Si AOG ya
   arrancó su AgIO, OG3D lo detecta y no lanza otro. Si no encuentra AgIO.exe — o si arranca y se
   cierra al instante (faltan DLL) — muestra un aviso en pantalla en lugar de fallar en silencio.
4. **La salida de AgIO va a la carpeta `Application`** (la misma de `OpenGrade3D.exe`): al compilar
   la solución, `AgIO.exe` y todas sus DLL quedan juntos sin copiar nada a mano.
5. **Botón «Fuente GPS» en AgIO** — el recuadro OG3D incorpora un selector para elegir qué GPS
   usa OpenGrade3D: **GPS de AOG** (reenviado por AgIO, el de trabajar) o **GPS de OpenGrade3D**
   (el GNSS propio del módulo OG3D, ideal para el survey de la parcela). La elección se guarda
   en el perfil y sobrevive a reinicios.

### Botón «Fuente GPS» de OpenGrade3D (recuadro OG3D)

| Modo | Qué recibe OG3D (15556) | Uso típico |
|---|---|---|
| **GPS de AOG** (por defecto) | PGN `0x7C 0xD6` que AgIO reconstruye desde el NMEA del GPS de AgOpenGPS. El GNSS propio del módulo OG3D (`0x6A 0xD1`) **no** se reenvía a OG3D | Trabajar con el GPS del tractor, con la cuchilla siguiendo la posición de AOG |
| **GPS de OpenGrade3D** | GNSS propio del módulo OG3D (`0x6A 0xD1` del Teensy). El PGN `0x7C 0xD6` **no** se reenvía a OG3D | **Survey de la parcela** con el GNSS de la máquina de la cuchilla, aprovechando la pasada de trabajo |

En ambos modos AgOpenGPS (15555) sigue recibiendo todo como siempre, la cuchilla (`0x6A 0xE1`)
y la telemetría (`0x6A 0xE2`) llegan a OG3D sin tocar, y el estado del botón se guarda en el
perfil (`setOG3D_gpsFromAOG` en el XML del perfil). Para que el cambio sea instantáneo sin
reiniciar OpenGrade3D, la app libera su bloqueo interno `isGNSSfromOG1` si deja de recibir
tramas OG1 durante ~2,5 s (vigilancia nueva en `ScanForNMEA()`).

### Mapa de red final (misma tablet)

| Origen → Destino | Puerto | Contenido |
|---|---|---|
| Teensy OG3D (cuchilla + GNSS) → AgIO | **9999** | NMEA de la cuchilla (`$GPGGA`, `$GPVTG`, …) + binario `0x80 0x81` PGN OpenGrade (`0x6A 0xD1` GNSS, `0x6A 0xE1` cuchilla, `0x6A 0xE2` telemetría…) |
| AgIO → módulos (Teensy) | **8888** | Comandos/configuración (incluye lo que OG3D manda por el bucle) |
| AOG → AgIO (bucle) | **17777** | `0x80 0x81` de AOG (auto-steer, secciones, settings…) |
| OG3D → AgIO (bucle) | **17777** | `0x80 0x81` de OG3D (control de cuchilla…) |
| AgIO → AOG (bucle) | **15555** | Todo lo recibido de módulos + PGN GPS `0x7C 0xD6` generado al parsear el NMEA |
| AgIO → OG3D (bucle) | **15556** *(nuevo)* | Todo lo anterior, salvo los PGN de posición (`0x7C 0xD6` y `0x6A 0xD1`): a OG3D solo llega el de la fuente marcada en el botón «Fuente GPS» |

### Flujo del GPS de la cuchilla

```
Receptor GNSS de la cuchilla → Teensy (GPS.ino) → NMEA por UDP:9999
      → AgIO: gate ('$' + G/P/K, GGA/VTG/RMC/PANDA/PAOGI…) → parseo
      → PGN 0x7C 0xD6 → 15555 (AOG) y 15556 (OG3D)  ← PGN intacto
```

El binario del módulo OG (`0x6A 0xD1` GNSS OG1 y `0x6A 0xE1` estado de la cuchilla) viaja
**tal cual** de AgIO a las dos apps; OG3D lo sigue parseando igual que en beta-UPD
(`ParseOG1GpsPacket` / `ParseOGBlade1Packet`). Los comandos de la cuchilla de OG3D salen por
`17777` y AgIO los reenvía por `8888` al Teensy, como hasta ahora.

## 3. Qué se ha añadido/modificado

| Archivo / carpeta | Cambio |
|---|---|
| `SourceCode/AgIO/**` | **NUEVO** — proyecto AgIO extraído tal cual de `AgOpenGPS-Official/AgOpenGPS → SourceCode/AgIO` (net48, WinForms) |
| `SourceCode/AgLibrary/**` | **NUEVO** — librería de la que AgIO depende (también del repo oficial) |
| `SourceCode/AgIO_Keypad/**` | **NUEVO** — copia del Keypad de AgOpenGPS para AgIO, ensamblado renombrado a `AgIOKeypad.dll` para no chocar con el `Keypad.dll` propio de OG3D |
| `SourceCode/Directory.Build.props` | **NUEVO** — fija `net48` para los proyectos SDK-style extraídos |
| `SourceCode/OpenGrade3D.sln` | Añadidos los 3 proyectos nuevos (GUIDs nuevos) |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` | `Port = 15555` → **`15556`** (con comentario del parche) |
| `SourceCode/GPS/Program.cs` | Nuevo `TryStartAgIO()`: lanza `AgIO.exe` de su carpeta si no hay ninguno en marcha |
| `SourceCode/AgIO/Source/Forms/UDP.designer.cs` | Nuevo endpoint `epOG3D` (15556) y envío doble en `SendToLoopBackMessageAOG()`; contadores `cntrGPSOG3D`/`cntrBladeOG3D` y `ParseOG3DGpsPacket()` para la fuente GPS-2; `SendGpsToLoopBack()` con el filtro de fuente para OG3D |
| `SourceCode/AgIO/Source/Forms/NMEA.Designer.cs` | El PGN GPS `0x7C 0xD6` sale ahora por `SendGpsToLoopBack(nmeaPGN, true)` (a OG3D solo si la fuente marcada es «GPS de AOG») |
| `SourceCode/AgIO/Source/Forms/FormLoop.Designer.cs` | **Nuevo recuadro OG3D** en la ventana: antena GPS-2, contador, Lat/Lon e icono de la cuchilla; **rótulo «Fuente GPS:» + botón selector**; fila inferior bajada y ventana más alta |
| `SourceCode/AgIO/Source/Forms/Controls.Designer.cs` | `btnOG3DGpsSource_Click()` alterna la fuente y la guarda; `UpdateOG3DGpsSourceButton()` pinta el texto del botón |
| `SourceCode/AgIO/Source/Properties/Settings.cs` | Nuevo ajuste persistente `setOG3D_gpsFromAOG` (XML del perfil) |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` | `Port = 15555` → **`15556`** (con comentario del parche); vigilancia `og1GpsWatchdog` para liberar `isGNSSfromOG1` al cambiar de fuente |
| `SourceCode/GPS/Forms/Position.designer.cs` | `ScanForNMEA()` libera `isGNSSfromOG1` si cesan las tramas OG1 ~2,5 s (cambio de fuente en caliente) |
| `SourceCode/AgIO/Source/Forms/FormLoop.cs` | Alto de ventana 706 px; contador GPS-2 en `DoTraffic()` y estado verde/rojo de la antena OG3D en `DoHelloAlarmLogic()` |
| `SourceCode/AgIO/Source/Properties/Resources.resx` + `Resources.Designer.cs` | Nuevo recurso `Blade` — icono de la cuchilla (`btnImages/Blade.png`) |
| `SourceCode/AgIO/Source/AgIO.csproj` | Eliminado `PublishDir` del árbol del repo AOG; la referencia a Keypad apunta a `AgIO_Keypad` |

Todos los cambios llevan el comentario `// Parche integración OG3D:` para localizarlos de un vistazo.
**Ni un PGN ha cambiado** — ni en AgIO ni en OG3D.

### Recuadro OG3D en la ventana de AgIO (fuente GPS-2 separada + cuchilla)

Debajo de la columna de módulos hay ahora un recuadro **OG3D** con:

- **Antena GPS-2** — la fuente GNSS que llega del módulo OpenGrade3D por el PGN
  `0x6A/0xD1` (hoy la antena del Teensy; cuando añadamos la entrada del GPS de la cuchilla,
  será esa). Se pone **verde** cuando el módulo manda tramas de la familia `0x6A`
  (GNSS `0xD1`, cuchilla `0xE1` o telemetría `0xE2`) y **roja** si deja de llegar nada,
  igual que los demás módulos.
- **Contador `---` / N** — tramas GNSS `0x6A/0xD1` recibidas en los últimos 2 s
  (`---` = sin datos de esa fuente).
- **Lat / Lon** — posición decodificada de la fuente GPS-2 (mismo formato NMEA
  DDMM.MMMM ×1e9 que decodifica la app OpenGrade3D en `ParseOG1GpsPacket`).
- **Icono de la cuchilla** — `Blade.png` incrustado como recurso de AgIO.
- **«Fuente GPS:» + botón selector** — elige el GPS que consume OpenGrade3D:
  `GPS de AOG` o `GPS de OpenGrade3D` (ver sección 2). El texto del botón muestra siempre
  la fuente activa y la elección se guarda en el perfil.

El GPS-1 de siempre (NMEA por el puerto 9999) sigue mostrándose arriba con su antena
grande; ningún indicador existente ha cambiado de sitio ni de significado. Todo el tráfico
`0x6A` se sigue reenviando al loopback (15555 AOG + 15556 OG3D) intacto.

## 4. Cómo compilar

Requisitos: **Visual Studio 2019/2022** con carga de trabajo *«Desarrollo de escritorio de .NET»*
(.NET Framework 4.8 ya viene con Windows 10/11).

1. Abrir `SourceCode/OpenGrade3D.sln` → **5 proyectos** (`OpenGrade3D`, `Keypad`, `AgLibrary`,
   `AgIOKeypad`, `AgIO`). Esta solución **no incluye el proyecto de instalador**, así que se
   abre sin ningún aviso en cualquier Visual Studio.
2. Los 3 proyectos nuevos restauran paquetes NuGet (System.Memory, System.Resources.Extensions,
   Accord.*).
3. Compilar `Release | Any CPU` (o `x86` para OG3D, como siempre).
4. Salidas (ambas en la misma carpeta `Application` del repo):
   - `Application/OpenGrade3D.exe` (proyecto GPS, como siempre)
   - `Application/AgIO.exe` + `AgLibrary.dll` + `AgIOKeypad.dll` + `Accord*.dll` +
     `System.Memory.dll` + `System.Resources.Extensions.dll` + `AgIO.exe.config` (proyecto AgIO)

### ¿Y el proyecto de instalador (Setup)?

El proyecto `Setup\Setup.vdproj` es del tipo *Visual Studio Installer* (GUID
`54435603-dbb4-11d2-8724-00a0c9a8b90c`) y **requiere la extensión**
[*Microsoft Visual Studio Installer Projects*](https://marketplace.visualstudio.com/items?itemName=VisualStudioClient.MicrosoftVisualStudio2022InstallerProjects).
Sin ella, VS muestra el mensaje *"No se encontró la aplicación en la que se basa este tipo de
proyecto"* y carga el proyecto como inapto.

Por eso hay **dos soluciones**:

| Solución | Proyectos | Uso |
|---|---|---|
| `OpenGrade3D.sln` | 5 (sin Setup) | **La normal** — compilar OG3D + AgIO sin instalar nada |
| `OpenGrade3D_con_Setup.sln` | 6 (con Setup) | Solo si has instalado la extensión y quieres regenerar el instalador MSI |

El `Setup.vdproj` sigue intacto en su carpeta; si algún día instalas la extensión puedes
abrir directamente `OpenGrade3D_con_Setup.sln`.

## 5. Cómo desplegar en la tablet

1. Compila la **solución completa**: `AgIO.exe` y sus DLL se copian solos a la carpeta
   `Application`, junto a `OpenGrade3D.exe`. No hay que copiar nada a mano.
2. Para llevar el programa a otra carpeta o tableta, copia **toda la carpeta `Application`**
   (OpenGrade3D.exe + AgIO.exe + AgIO.exe.config + todas las DLL). Si copias solo el exe, al
   arrancar OpenGrade3D te avisará de que falta AgIO o de que faltan DLL.
3. Ejecutar OpenGrade3D: si no hay AgIO en marcha, lo lanza solo. También puede ejecutarse
   AgIO manualmente (es el AgIO completo de siempre: NTRIP, módulos, monitor UDP, perfiles…).
4. **Este AgIO sustituye al AgIO oficial en la tablet.** Usa la misma configuración
   (`HKCU\SOFTWARE\AgOpenGPS`) y los mismos puertos, así que:
   - No ejecutes nunca dos AgIO a la vez. Si lo haces, el segundo **sale sin abrir ventana**
     («terminó con código 0 (0x0)» al depurar con F5): no es un error, es el control de
     instancia única (Mutex) de AgIO. Esta integración además avisa con un mensaje
     **«AgIO ya está en marcha»**; cierra el AgIO previo (Administrador de tareas → procesos
     `AgIO`) y vuelve a lanzarlo.
   - La subred del módulo, IPs y ajustes que ya tengas se conservan.
5. Con AOG + OG3D abiertos a la vez: ambos reciben el flujo completo. AOG ignora los PGN de
   OpenGrade y viceversa.

## 6. Puntos de enganche para las modificaciones futuras

Aquí es donde tocaremos las siguientes piezas:

| Quiero… | Toca… |
|---|---|
| Ver la posición del GPS de la cuchilla en AgIO | **Ya funciona**: recuadro OG3D (`ParseOG3DGpsPacket` en `UDP.designer.cs`); cuando el firmware mande el `0x6A/0xD1` del GPS de la cuchilla aparecerá ahí |
| Añadir más datos a la entrada del GPS de la cuchilla | `AgIO/Source/Forms/NMEA.Designer.cs` (parser) y `UDP.designer.cs` (envío del PGN `0x7C 0xD6`) |
| Tratar la cuchilla como un módulo con su propio hello/scan | `AgIO/Source/Forms/UDP.designer.cs` → `ReceiveFromUDP()` (PGN `0x6A`, src del módulo OG) |
| Mandar comandos de OG3D a la cuchilla por un módulo distinto | `AgIO/Source/Forms/UDP.designer.cs` → `ReceiveFromLoopBack()` (hoy todo sale por `epModule` 8888) |
| Cambiar el puerto de OG3D | `GPS/Forms/UDPComm.Designer.cs` (`Port`) y `UDP.designer.cs` de AgIO (`epOG3D`) |
| Cambiar qué GPS usa OpenGrade3D | Botón «Fuente GPS» del recuadro OG3D (`Controls.Designer.cs` de AgIO); el ajuste es `setOG3D_gpsFromAOG` |

## 7. Compatibilidad firmware

No hay que tocar el firmware: el Teensy sigue enviando NMEA + binario a `9999` y escuchando
en `8888`, exactamente como con el firmware parcheado actual (v9, con Parches 8/9/10).

## 8. Commit de Pat incorporado (c6e6fa, «cleaning and speed from VTG repair», 06-11-2026)

Este árbol integra el commit [`c6e6fa`](https://github.com/Pat-I/OpenGrade3D/commit/c6e6fa16f377e49be27311c12603e9de86aa833c)
del repo original. Lo que cambia:

**Firmware (`ArduinoCode/OG3D_Teensy_Firmware/`, idéntico al Parche 10 de la v9):**

1. **VTG: índice de la velocidad 7 → 6** — el campo `[7]` de la frase es la unidad «K»;
   leer ahí daba `speedOG = 0` y los saltos de velocidad del vídeo. (Ya se había
   corregido igual en la v6; ahora además es el criterio oficial de Pat.)
2. **`NMEAParser<3>` → `<5>`** — margen para más manejadores NMEA.
3. **Centinelas sin fix** — `lon/lat` de `200000000` (0.2°, coordenada real → posición
   fantasma en OG3D) a `20000000000000` (20000°, que OG3D rechaza) y altitud a
   `21000000` mm (21000 m, por encima del filtro de 20000 de OG3D).
4. **`BuildNmea()` envía SIEMPRE el `0x6A/0xD1`** — CRC y envío salen del `if` del fix;
   sin fix la trama sale con los centinela. OJO: el commit original escribía
   `memcpy(&GNSS1[5], 20000000000000, ...)` con el literal como puntero (no compila);
   aquí se aplica con direcciones reales.

**OpenGrade3D (`SourceCode/GPS/`):**

5. **Barra de título con vehículo y campo** — nuevo `UpdateTitleBar()` en `FormGPS.cs`
   (+ llamadas en `FormGPS_Load`, `JobNewOpenResume`, `FileSaveEverythingBeforeClosingField`
   y tras cargar/guardar vehículo en `GUI.Designer.cs`).
6. **Tope de velocidad del paquete OG1 (0x6A/0xD1): 500 → 300 km/h** en
   `UDPComm.Designer.cs` (`ParseOG1GpsPacket`).
7. **Ventana Ethernet (`FormUDP`) limpiada** — se ocultan los controles legacy de
   puertos/IP (`nudThisPort`, `label9`, `label2`, `label3`, `tboxAutoSteerIP`,
   `tboxRateRelayIP`) y el aviso pasa a «\*\* Start AgIO to automatically use UDP»,
   coherente con el auto-arranque de AgIO de este árbol.

Del commit **no** se copian: el churn base64 de `FormUDP.resx` (icono de un botón que queda
oculto), `obj/*`, `Error Log.txt` ni los binarios `exe/pdb`. La renumeración de variables
ProgBar en `FormGPS.Designer.cs`/`FormGPS.resx` (churn del diseñador, sin efecto funcional)
**sí** se ha incorporado por fidelidad al commit, verificando que Designer y resx quedan
coherentes y el resx sigue siendo XML válido.

---

## 9. Tema oscuro de cabina (interfaz modernizada)

**Qué es:** toda la interfaz de OpenGrade3D (pantalla principal, formularios de
configuración, teclado y numérico en pantalla, diálogos) se presenta en un **tema oscuro
de cabina**: fondo `#1E1E1E`, campos `#252526`, botones `#333334` con borde sutil, texto
claro `#EBEBEB` y tipografía **Segoe UI**. En los diálogos los botones de texto crecen a
un mínimo de 100×40 px (uso táctil con guante en tablet) y la letra sube a 9 pt; en la
pantalla principal se conserva el tamaño de fuente para no mover el layout de trabajo.

**Cómo funciona (`SourceCode/GPS/UiTheme.cs`):**

- Una única clase `UiTheme` recorre cada formulario en profundidad y aplica la paleta por
  tipo de control (botones planos con hover, campos oscuros con borde fino, cuadrículas,
  pestañas, menús con renderer oscuro propio, etc.). No hay lógica nueva de la aplicación.
- Se invoca como `UiTheme.Apply(this);` al final de cada `InitializeComponent()` de los
  17 Designer del proyecto (inyección automática), así que cada ventana se viste **antes
  de verse**: sin parpadeo ni flash de tema claro.
- **Se respeta lo funcional:** los colores que el código asigna en tiempo de ejecución
  (rojo de alarma del NUD, verde/rojo de `btnFreeDrive`, chips de estado) se aplican
  después del tema y siguen ganando. El lienzo 3D (`SharpGL.OpenGLControl`), la cámara
  (WebEye) y las imágenes de la barra de herramientas no se tocan.
- Para retocar la paleta basta editar las constantes del principio de `UiTheme.cs`
  (`Window`, `Surface`, `Field`, `Text`…). `Arial Black` (titulares) se conserva.

**Cambios en los csproj (invisibles para Visual Studio):** en tu Visual Studio el
proyecto lee sus recursos `.resx` por la vía clásica de .NET Framework, **exactamente
igual que el original y sin ninguna dependencia nueva** (así que el exe no referencia
`System.Resources.Extensions` en absoluto). La referencia a esa DLL — incluida en
`SourceCode/GPS/Dependencies/` — está **condicionada** a compilar con
`/p:GenerateResourceUsePreserializedResources=true`, algo que solo usan los builds de
verificación fuera de Windows. Además `Release|x86` de OpenGrade3D ahora define
`AllowUnsafeBlocks` (solo lo tenía Debug|x86, y `CContour.cs` usa código unsafe) y Keypad
define salidas Debug|x86 y Release|x86 para compilar el csproj directamente.

> **Nota sobre un error de arranque ya corregido:** una entrega intermedia activaba los
> recursos preserializados sin condición, lo que hacía que el exe nuevo dependiera de
> `System.Resources.Extensions` 4.0.0.0; al compilar la solución, el build de AgIO (que
> usa el paquete 9.0.0) copiaba su DLL a `Application/` y pisaba la otra, produciendo
> `System.IO.FileLoadException` al leer imágenes del `.resx`. Con el modelo actual esa
> dependencia desaparece del exe y el error no puede repetirse: recompila la solución
> con este árbol y arranca normal.

AgIO no se toca en esta ronda (mantiene su aspecto clásico); el botón «Fuente GPS» de la
entrega anterior sigue incluido.

## 10. Barra lateral izquierda (template del usuario) — iteración 8

Con tus 16 iconos (`OG3D Images.zip`) se construyó la **barra lateral izquierda** del
FormGPS: panel oscuro (paleta `UiTheme`) anclado entre el menú y la barra de estado,
con los iconos embebidos en el ejecutable. Cada icono invoca la **acción real que ya
existía** (mismos handlers y mismos flags de `ct` que los botones originales):

| Grupo / icono (columna fija) | Acción (handler real) |
| --- | --- |
| **Grupo 1 (arriba): Field** | **El Start del programa antiguo**: crear campo nuevo, continuar o cargar AGD (`JobNewOpenResume` → diálogo FormJob) |
| **Grupo 1 (arriba): Config** | **El Config del programa antiguo**, directo (`btnSettings`) |
| **Grupo 2 (MODE): Survey_mode** | Modo Survey (`SelectMode`) + popup por fases (abajo) — resalta el modo actual |
| **Grupo 2 (MODE): Grade_mode** | Modo Grade (`SelectMode`) + popup por fases (abajo) — resalta el modo actual |
| **Grupo 3 (abajo): Perfil** | Alterna el panel de elevación de OpenGL (sustituye al emblema Batman) |
| Transporte Start/Pause/Stop | Iniciar/pausar (`btnStartPause`) y cerrar-guardar (`JobClose`) — solo en survey activo |

**Mapa de flujo actual (iteración 4, según SurveyModeRun.png y GradeModeRun.png):**

| Popup | Fases y comportamiento |
| --- | --- |
| Survey_mode | **Raíz**: Benchmark (solo pone la marca, no oculta nada), Boundary (abre dos opciones más), Survey (grabación de puntos: aparecen Start/Stop/Pause y las otras opciones desaparecen; pulsar otra vez lo desactiva). **Fase límite**: Boundary_Left / Boundary_Right — elegir lado arma la grabación del límite y aparecen Start/Stop/Pause; el otro lado desaparece; pulsar el lado activo otra vez desactiva y reaparecen los dos; Boundary vuelve a la raíz |
| Grade_mode | **Raíz**: CutFill (icono CutFill.png) y Altitude. **Fase CutFill**: ciclo de 3 iconos en el orden del usuario — CutFillActual → CutActual → CutFillProposed (`btnPropExist` cicla Actual → Cut/Fill → Proposed); pulsar CutFill de nuevo deselecciona (Proposed) y vuelve a la raíz. **Fase Altitude**: ElevationExisting / ElevationProposed; pulsar la activa otra vez deselecciona la vista de elevación |
| Transporte | Solo aparece con la máquina survey en grabación de límite (RecBnd) o de puntos (Rec); desaparece al desactivar, con Stop (cierra y guarda) o al volver a Grade |

**Iteración 2 — estructura del template (popups):** la columna fija pasó a mostrar solo
los iconos principales y el resto de acciones salió en un **popup a la derecha de la
barra** al pulsar el icono principal, cerrándose al pulsar fuera (escena 3D) o al
volver a pulsar el mismo icono.

El icono **Perfil sustituye al emblema Batman** de la barra de estado (ocultado;
mismo trabajo: alternar el panel de elevación). Ya no hay iconos grises: cada popup solo
contiene acciones de su modo. Los iconos Boundary_Left/Boundary_Right siguen embebidos
pero sin uso por ahora (el lado del límite se cambia con el botón original).

**Iteración 3 — pulido (tamaños y sustituciones):** los iconos van a su **tamaño nativo
de 64 px** en botones de 72 — el mismo tamaño de icono que la columna derecha de la
ventana; la columna pasa de 58 a **88 px** de ancho y popups y transporte crecen igual
(transporte: botones de 72 con icono de 64, solo icono, sin texto). Las **píldoras Start
y Config de la barra de estado quedan ocultas**: las sustituyen los iconos Start y Config
de la barra lateral (lo que tenían de menú desplegable — Campo, Reset trip — sigue
alcanzable desde el menú File). Y el **transporte Start/Stop/Pause ya no aparece al
pulsar Survey**: solo se muestra con el **survey iniciado** (campo abierto en modo
Survey); se oculta con Stop (cierra el trabajo), al cerrar el campo o al volver a Grade.

**Iteración 4 — mapa de flujo y 5 iconos nuevos:** la columna fija pasa a
**Survey_mode, Grade_mode, Field, Config, Perfil**: el icono **Field ejecuta el "Start"
del programa antiguo** (`JobNewOpenResume` → diálogo FormJob: crear campo nuevo /
continuar / cargar AGD) y **Config abre directamente el Config del programa antiguo**
(`btnSettings`, sin popup); el icono Start de la barra desaparece (el arranque del
trabajo es Field, y iniciar/pausar/parar queda en el transporte). Los popups siguen tu
mapa por pasos: en Survey, pulsar **Boundary o Survey oculta las otras opciones** (y
Benchmark solo pone la marca, sin ocultar nada), Boundary abre los dos lados (el lado
activo pulsado de nuevo se desactiva y reaparecen los dos iconos) y Survey muestra el
transporte Start/Stop/Pause; en Grade, **CutFill abre el ciclo de superficie con un
solo icono de estado** (Actual → Cut/Fill → Proposed) y **Altitude abre las vistas
Existing / Proposed**. El transporte solo aparece con la máquina survey en grabación
(límite o puntos). Se añaden los 5 iconos de `NuevosIconos.zip` (CutActual,
CutFillActual, CutFillProposed, ElevationExisting, ElevationProposed) → **21 PNG
embebidos** en total.

**Iteración 5 — tres grupos como el template y badges grandes:** la columna se ordena
como en `Template-OG3D.png`: **Field y Config arriba**, debajo el bloque **MODE** (rótulo
MODE + Survey_mode y Grade_mode) y **Perfil anclado al fondo** de la barra, separado por
el hueco que deje la ventana. El transporte Start/Stop/Pause pasa a los **badges nativos
de 120×50** de `Stop.zip` (sin reescalado, en botones de 136×66), que sustituyen a los
PNG de 64 del mismo nombre.

**Iteración 6 — Cut/Fill legible, ciclo de 3 y columna alineada:** con los iconos de
`CutFillProposed.zip` (letras blancas, legibles en el tema oscuro) la raíz de Grade pasa
a mostrar **CutFill.png** y su ciclo usa los **tres iconos en tu orden: CutFillActual →
CutActual → CutFillProposed** (los tres sustituidos por las versiones con letras
blancas). Y se arregla la columna liberada a la izquierda de la barra: **solo se
desplazan tilt y Google Earth**, el **panel del simulador queda alineado en x=94** con
ellos (antes en 91, descuadrado) y **btnManualOffOn ya no se mueve** — al desplazarlo
acababa encima de los botones Start / BoundarySide de grabación.

**Iteración 7 — fila superior, cuadro del simulador y barras de la cuchilla:** la fila
flotante de arriba (tilt arriba/abajo, Google Earth, Manual/Auto y los botones de
grabación Start y BoundarySide) se desplaza **junta (+88 px)** conservando sus huecos
originales — así no se apilan en ninguna combinación de visibilidad (en la 6 se dejó
Manual/Auto fijo y quedaba sobre el tilt derecho; en las 1-5 se movía solo y tapaba el
Start de grabación). El **cuadro del simulador** se reordena sin solapes: sliders de
ángulo y de paso a la izquierda con sus botones `>0<` y Reset, elevación y `Go To` en
columna propia a la derecha, y la fila Lat/Long debajo (las etiquetas de escala —
`B N M`, `L`, `H J K` y el valor del ángulo — siguen sobre sus trackbars, como en el
diseño original). Y en la columna derecha, la **leyenda de colores de la cuchilla ahora
acompaña a las barras de estado al redimensionar**: se reparte sobre la altura real de
las barras en `FormGPS_Resize` (antes quedaba fija y se descuadraba al maximizar).

**Iteración 8 — columna derecha como el original (captura de referencia del usuario):**
con tu foto de cómo debe verse la derecha se vio el descuadre real: el tema oscuro
aplicaba su **mínimo táctil de 100×40 a los botones de texto pequeños**, y eso
convertía los **cuadrados de 32 px de la leyenda de colores de la cuchilla en bandas
anchas** y engordaba la escala `+` / `−` y el botón `NC`; además la iteración 7
repartía la leyenda sobre las barras al redimensionar, cuando en el programa original
la leyenda **queda fija bajo Blade Offset**. Ahora esos diez botones (`btnColor*`,
`btnScalePlus`/`btnScaleMinus`, `btnModuleStatus`) **conservan tamaño y color de
diseño** (lista `UiTheme.TamanoDiseno`), la leyenda no se mueve y `FormGPS_Resize`
vuelve a su código original: las barras de la cuchilla se estiran con la ventana y la
leyenda se queda quieta, exactamente como en tu captura.

**Iteración 9 — la DLL de recursos que faltaba (error al poner el offset y al abrir
el teclado virtual para el nombre del campo):** al pulsar el recuadro de Blade
Offset se abre el **teclado en pantalla** (`KeypadToNUD` → Keypad.dll) y al crear un
campo pasa lo mismo con la ventana del nombre (`FormJob`): salía `Could not load
file or assembly 'System.Resources.Extensions, Version=4.0.0.0, ...
PublicKeyToken=cc7b13ffcd2ddd51'`. Causa: el compilador moderno (SDK de .NET sobre
Linux) genera los `.resources` en formato **preserialized** y, en ejecución, .NET
Framework 4.8 necesita esa DLL junto al exe para leerlos — los formularios con
iconos o Bitmap en su resx (Keypad, FormJob, Config, diálogos...) crasheaban al
abrirse, mientras FormGPS arrancaba bien porque sus imágenes van como bytes crudos.
Solución: **cierre de dependencias completo** junto al exe, todo del paquete oficial
NuGet (compilación net461): `System.Resources.Extensions.dll` 4.7.1 (54 KB) y las
DLLs que esta referencia a su vez — `System.Memory.dll` 4.5.5,
`System.Buffers.dll` 4.5.1, `System.Numerics.Vectors.dll` 4.5.0 y
`System.Runtime.CompilerServices.Unsafe.dll` 4.5.3 (lo comprobé leyendo las
referencias de ensamblado de cada una: con estas cinco más el GAC del 4.8 queda la
cadena completa). Viven en `SourceCode/GPS/Dependencies/`, la referencia está en el
csproj con `Private=True` y las cinco se entregan en `Application/`. Al instalar hay
que reemplazar la **carpeta Application entera**, no solo el exe — si solo se copia
OpenGrade3D.exe las DLLs no llegan y el error sigue igual.

**Iteración 9b — el error cambió a "manifest definition does not match":** con las
DLL ya en su sitio el cargador las encontraba pero rechazaba la versión: mscorlib
pide `System.Resources.Extensions` **4.0.0.0** y el paquete 4.7.1 sirve ensamblado
**4.0.1.0** (medido con un lector de metadatos); además `System.Memory` pedía 4.0.1.1
y la 4.5.5 sirve 4.0.1.2. Las versiones antiguas del paquete (que sí usaban la
4.0.0.0) ya no están en el CDN de NuGet, así que la solución estándar es el
**bindingRedirect**: `SourceCode/GPS/app.config` lleva ahora un bloque
`<runtime><assemblyBinding>` que redirige cada petición a la versión servida
(Resources.Extensions → 4.0.1.0, Memory → 4.0.1.2, y por seguridad Buffers → 4.0.3.0,
Vectors → 4.1.4.0 y Unsafe → 4.0.4.1). El build copia ese bloque a
`Application/OpenGrade3D.exe.config`, y como el cambio está en el **código fuente**,
también funciona al depurar en Visual Studio (en tu captura el fallo saltaba en
`new Keypad.NumKeypad()` dentro del `InitializeComponent` de la ventana del nombre
de campo — mismo origen, mismo remedio). Las aserciones del empaquetado comprueban
los cinco redirects tanto en el app.config como en el exe.config generado.

**Iteración 9c — las teclas del teclado superpuestas:** el keypad y el teclado ya
abren, pero en "Enter a Value" (FormNumeric) la fila de números quedaba medio tapada
por la de letras. Causa: el tema oscuro (UiTheme) cambiaba la tipografía de los
diálogos (Tahoma → Segoe UI, subiendo los tamaños pequeños a 9 pt) y además aplicaba
un mínimo táctil de 100×40 a los botones de texto; con `AutoScaleMode.Font`, cambiar
la fuente re-escala los controles del formulario, y en un contenedor anidado (el
control `Keypad.NumKeypad` dentro de FormNumeric o FormJob) el re-escalado en cascada
deja las filas con separaciones desiguales — teclas superpuestas. La regla del tema
queda definida para siempre: **pinta colores, no toca tipografías ni tamaños** (se
eliminaron el cambio de fuente y los mínimos táctiles en UiTheme.cs); la geometría y
las letras son siempre las del Designer, exactamente como el programa original. El
teclado y el keypad vuelven a su disposición de fábrica.

**Iteración 10 — AgIO se abre solo y con botón propio en la barra:** el arranque
automático de AgIO existía desde la integración (`Program.TryStartAgIO` busca
AgIO.exe junto a OpenGrade3D.exe) pero AgIO nunca se había compilado, así que no
había exe que lanzar. Ahora el proyecto AgIO se compila con el resto y su salida
cae directamente en `Application/` (OutputPath del csproj): AgIO.exe,
AgLibrary.dll, AgIOKeypad.dll y AgIO.exe.config con las mismas bindingRedirect que
el GPS; además se fijaron los PackageReference de AgIO al mismo set que entrega
OpenGrade3D (`System.Memory` 4.5.5 y `System.Resources.Extensions` 4.7.1) para que
ninguna build pise DLLs con versiones distintas. En la barra lateral, debajo del
bloque MODE (Survey_mode y Grade_mode) y con un hueco de separación, hay un icono
nuevo **AgIO** — el PNG de 64×64 que enviaste, embebido en OG3DImages con el mismo
mecanismo que los demás iconos: si AgIO no está en marcha lo lanza (misma rutina
del arranque automático) y si ya está le devuelve el foco, restaurándolo si estaba
minimizado (`ShowWindow` + `SetForegroundWindow` de user32). El arranque automático
al abrir OpenGrade3D sigue igual: si ya hay un AgIO corriendo no lanza otro.

Para que AgIO compile también en Linux hubo que versionar los **55 iconos que su
`Resources.resx` referencia** (`SourceCode/AgIO/Source/btnImages/` y el alias
`btnimages/`): el autor original no los incluyó y el build fallaba (MSB3554).
Se descargaron del repo oficial de AgOpenGPS (que los trae con mayúscula —
`Trash.png`, `Satellite.png`— y en Linux se añadieron las copias en minúscula que
usa el resx de OpenGrade3D). El paso del script de restauración que borraba esos
dos alias quedó retirado: era un apaño de cuando faltaban.

**Iteración 11 — AgIO oscuro, popup de Boundary y sin iconos duplicados en el 3D:**
tres cambios. (1) **AgIO viste el mismo tema oscuro de cabina que OpenGrade3D**: se
portó el pintor (`SourceCode/AgIO/Source/UiTheme.cs`, paleta idéntica) y el script
de integración inyecta `UiTheme.Apply(this)` al final del `InitializeComponent()`
de los 21 formularios de AgIO, como ya se hacía en el GPS. Pinta colores — nunca
tipografías ni tamaños (misma regla de la iteración 9c) — y los colores funcionales
que AgIO asigna en ejecución (alarmas, LEDs...) siguen ganando porque se aplican
después. (2) **El popup de Boundary de la barra lateral muestra ahora el propio
icono de Boundary arriba** (el cuadrado rojo punteado) seguido de los dos lados
izquierda/derecha, tal como en la captura de referencia: pulsar el icono punteado
devuelve el popup al menú raíz del survey. (3) **Los iconos de survey y boundary
desaparecen de la ventana OpenGL**: al grabar puntos o límite, las rutinas
`recordSurveyPts` y `recordSurveyBoundary` ya no re-muestran el START flotante ni
el botón "boundary Left" (su función vive en la barra lateral: popup de Boundary
para el lado y transporte Start/Stop/Pause para grabar y parar).

**Iteración 12 — ventana 3D limpia: los 12 iconos flotantes originales, fuera.**
La iteración 11 retiró START y "boundary Left", pero seguían flotando sobre el
OpenGL las lupas de zoom, Cut/Fill y Proposed (derecha) y las flechas de
inclinación, el botón Survey/"Recording Boundary", el globo de Google Earth, "Add
pts from AOG elevation" y "RTK fix only" (izquierda). Todos quedan ahora ocultos
al construir la barra (`floaters3D` en `BuildSideBarCore`) y **ninguna rutina los
vuelve a mostrar**: `SelectMode` ya no re-trae Cut/Fill y Proposed al pasar a
Grade, ni Survey y "RTK fix only" al pasar a Survey, y el caso RecBnd no
re-muestra el botón de elevación. Dónde vive cada función: el flujo
survey/benchmark/límite/puntos en el popup de Survey_mode; Cut/Fill, Proposed y
las vistas de elevación en el de Grade_mode; Google Earth sigue en el menú Display
(y en el menú contextual del 3D). Como el zoom y la inclinación **no tenían
equivalente** (sin rueda ni atajos de teclado), la barra gana un icono **Vista**
(bajo AgIO) con un popup de 4 acciones — ZoomIn, ZoomOut, TiltUp, TiltDown — que
usan los mismos handlers originales: cada pulsación aplica un paso y el popup
sigue abierto. Sus iconos (lupas y flechas azules) se extrajeron del
`FormGPS.resx` a `OG3DImages/`. Nota: el toggle "RTK fix only" (`ct.FloatIsOK`)
queda fuera de la interfaz y mantiene su valor actual. El desplazamiento +88 de la
fila flotante desaparece junto con la fila (el simulador sigue alineado a la
derecha de la barra).

**Iteración 13 — tus iconos integrados: Field con paso extra, ScreenConfig, zoom
en el 3D y pestaña Extras.** Con los 10 iconos nuevos de `IconosExtras.zip`:

- **Field** ahora muestra un popup de dos opciones: **FileOpen** (carpeta, icono
  `FileOpen.png` de los btnImages originales) que abre el diálogo de trabajos de
  siempre (crear/continuar/cargar AGD, la "pestaña actual"), y **GoogleEarth**
  que lanza la app de Google Earth con los flags del campo (el item de menú,
  que avisa si no hay campo abierto).
- **ScreenConfig** (it13.4: tercer icono del grupo superior, debajo de Config;
  antes iba bajo AgIO — sustituye al icono Vista de la it. 12): popup con
  **2DView / 3DView / TiltUp / TiltDown** y *autocrerre*: se cierra solo tras
  1,8 s sin pulsar nada y cada pulsación reinicia el contador. El "2D" es un
  preset de cámara cenital (`camPitch = -80`, el tope del tilt) porque OG3D no
  tiene renderizador 2D; al volver al 3D se recupera el pitch que había.
  Inclinar manualmente también devuelve al 3D.
- **El zoom vuelve al 3D rediseñado, estilo AgOpenGPS** (it13.2, tras las dos
  capturas del usuario): los dos botones van sueltos sobre el 3D, sin caja
  contenedora y con el estilo clásico de botón; el "+" (`ZoomIn48.png`) arriba a
  la derecha y el "−" (`ZoomOut48.png`) unos 260 px más abajo, bien separados
  para el dedo. Reutilizan los handlers de las lupas originales, **con el
  mapeo corregido**: los handlers nativos llevaban los nombres cruzados
  (`btnZoomIn_MouseDown` alejaba de verdad), de modo que "+" llama a
  `btnZoomOut_MouseDown` y "-" al contrario. Los iconos resx
  `ZoomIn/ZoomOut/Vista` de la it. 12 se retiran de `OG3DImages/` (quedan 36 PNG
  con Folder, Feet y Hectare, los de la it13.5).
- **Pestaña Extras** en el Config (`FormSettings`): **RTKOnly** alterna
  `ct.FloatIsOK` (texto "RTK fix only"/"RTK or float") y **FromAOG** lanza
  `FileOpenElevationFromAOG` (el ex "Add pts from AOG elevation"); se ha pasado
  `btnBuildFromElevation_Click` y `LoadSideImage` a `internal` para que
  FormSettings pueda llamarlos. (it13.3) Las 5 pestañas se pintan auto-tamaño
  (`TabSizeMode.Normal`: sin flechas de scroll y todas visibles al abrir) y la
  pestaña Extras recibe el tema oscuro con `UiTheme.Apply` — se construía
  después del Apply del `InitializeComponent` y salía con el tema claro.
- **Cosmética de la barra** (it13.4): más aire entre el grupo superior y el
  bloque MODE; el icono activo ya no se marca con el gris tenue (Hover sobre
  SurfaceHi era invisible con el sol de cabina) sino con relleno azul
  (`UiTheme.Selected`, #264F78) y borde claro, también en los botones del
  transporte del survey.
- **Files y Ac/Ha fuera del panel derecho** (it13.5, con los tres iconos del
  `Folder.zip` del usuario): la carpeta ahora es **Folder.png** en el popup de
  Field y abre el directorio de campos en el explorador (con labor en curso
  abre su carpeta; si no hay, la raíz de Fields). El cambio de unidades se
  traslada a la pestaña Extras como sección **Sistema de unidades**:
  **Hectare.png = métrico** y **Feet.png = imperial**, el activo va resaltado
  (azul + borde, mismo criterio que la barra) y por defecto queda el métrico.
  La lógica vive en `FormGPS.SetUnits`; los botones `btnUnits` y
  `btnFileExplorer` se retiraron del Designer y sus huecos los ocupan
  `btnFlag` y `btnGPSData`, que suben a la fila de arriba del panel.
- **Maximum Cutting Depth por fin implementado** (it13.6): el NUD de la página
  de vehículo ya no es decorativo — `vehicle.maxCuttingDepth` (**0 = sin
  límite**, el default) topea la profundidad de corte **por pase** en la guía
  del mapa: en las vistas Actual Cut/Fill el objetivo de la pasada nunca baja
  más de ese tope respecto a la superficie actual (`PassTargetAltitude` en
  `CContour`), y en la vista de corte/relleno original las zonas cuyo corte
  excede el tope se pintan de azul ("aquí harán falta varias pasadas"). La
  misma regla es el punto de anclaje del futuro **clamp hidráulico**: el
  setpoint que se le mande a la válvula debe ser el objetivo del pase, no el
  diseño final. El label deja de decir "(not implemented yet)".
- **Clamp hidráulico + módulo Teensy** (it13.7): la cadena de control ya vivía
  en el código — el PGN **0xBA** sale en cada tick GPS con la altitud de corte
  y la demanda de válvula (error cm → byte 100±990), el **0xB8** lleva la
  sintonía PWM del vehículo y el módulo contesta **0xE1**. Lo que faltaba:
  (1) en `Position.designer.cs` el objetivo de válvula ahora se clampea a
  `bladeAltitude − maxCuttingDepth` antes del mapeo (modos normal y láser) —
  **la válvula nunca pide bajar más de lo configurado**; (2) AgIO enruta
  0xBA/0xB8 al puerto del módulo (nuevos casos en `ReceiveFromLoopBack`);
  (3) el módulo de cuchilla es **Teensy 4.1 por Ethernet/UDP** (detalle en
  it13.8), no serial: AgIO reenvía **todos** los PGN del loop a
  `{subnet}.255:8888` (`epModule`, línea 228 de `UDP.designer.cs`) y ahí
  escucha el Teensy; contesta 0xE1 + telemetría por el 9999 hacia OG3D. Los
  casos 0xBA/0xB8 añadidos al switch serial de AgIO quedan como soporte
  opcional para un futuro módulo por COM; con este hardware no participan.
- **Firmware real del módulo integrado** (it13.8): la carpeta
  `SourceCode/TeensyBladeValve/OG3D_Teensy_Firmware/` contiene el sketch
  completo (5 .ino/.h + `OG3D_Globals.h`) con los 8 parches documentados en
  su `CAMBIOS_PARCHES.md` y el diff en `parches.diff`: módulo con **GNSS
  propio** en Serial2 (mide la altura de la cuchilla y la envía a OG3D en el
  0x6A/0xD1 — es el mismo sensor que usa el clamp en la app), control
  proporcional con deadband y ganancias PWM persistentes en EEPROM (llegan
  por 0xB8), **failsafe GNSS** (GGA > 600 ms → neutro, Parche 1),
  verificación de CRC entrante en 0xBA/0xB8 (Parche 2, compatible byte a
  byte con el CRC que calcula `SendPgnToLoop`), palanca manual con
  prioridad, heartbeat a AgIO (Parche 4) y **telemetría UDP** 0x6A/0xE2 a
  5 Hz con `decodificar_telemetria.py` incluido (Parche 8). Placa AiO v4.5:
  DIR pin 4, PWM pin 2 (100 Hz, 12 bits), LOCK pin 3,
  `proportionalValve = true`. **El clamp hidráulico de la it13.7 funciona
  extremo a extremo con este firmware sin cambios adicionales**: OG3D manda
  en el 0xBA el objetivo ya clampeado al tope por pase y el módulo lo compara
  contra su propia altura GNSS fresca. El sketch serial
  `TeensyBladeValve.ino` de la it13.7 se retiró por obsoleto ante el
  firmware real.
- **Zip sin avisos de WinRAR** (it13.9): desde la it10 el árbol traía la carpeta
  `btnimages/` (minúsculas) junto a `btnImages/`, y dentro `trash.png` +
  `Trash.png` y `satellite.png` + `Satellite.png` — en Linux son archivos
  distintos, pero Windows los considera la misma ruta y WinRAR preguntaba
  "el siguiente archivo ya existe" al descomprimir. Eran copias idénticas
  (mismo md5): quedó una sola copia de cada icono en la canónica `btnImages/`,
  se eliminó `btnimages/` y el `Resources.resx` de AgIO se normalizó a
  `..\btnImages\trash.png` y `..\btnImages\Satellite.png` (el recurso
  `satellite` lo usa FormCommSetGPS; compila igual en Windows y Linux). El
  empaquetador ahora verifica que el zip no contenga **ninguna** ruta que
  colisione ignorando mayúsculas: si reaparece un alias, el check [FALLO]
  y no se entrega.

Este error destapó además un fallo de empaquetado: **el zip de la iteración 8 se
entregó con el exe de la iteración 7** — el proyecto compila directamente en
`Application/` (`OutputPath`) y el paso de restauración posterior lo machacaba con el
del zip anterior. El flujo ahora es **restaurar → compilar → empaquetar**, y las
verificaciones comprueban dentro del binario que lleva el arreglo (marca
`TamanoDiseno` en el exe).

**Qué cambió:** archivo nuevo `SourceCode/GPS/Forms/FormGPS.SideBar.cs` (parcial de
FormGPS), carpeta `SourceCode/GPS/OG3DImages/` con tus 21 PNG embebidos
(`EmbeddedResource`), llamada `BuildSideBar()` en el constructor de FormGPS y
script `scripts/patch_sidebar.py` que aplica los dos cambios anteriores de forma
idempotente.

**Iteración 1 — nada se ha retirado todavía:** todos los botones originales siguen en
su sitio para no romper los flujos survey/grade (varios cambian visibilidad por modo).
Cuando apruebes la estructura, quitamos los duplicados marcados con X en tu template
(Start/Config de la barra inferior, Cut/Fill y Proposed flotantes, círculo AVG) y
afinamos grupos/orden.

**Corrección de arranque:** la llamada `BuildSideBar()` va **al final del
constructor** de FormGPS (después de crear `ct`, `pn`, `mc`, `sim`…). Al principio
estaba justo tras `InitializeComponent()` y al leer `ct.surveyMode` saltaba
`NullReferenceException` porque `CContour` aún no existía. `UpdateSideBarState()`
además ignora la llamada si `ct` es null, por si se invoca antes de tiempo.

**Robustez de la barra:** ahora la ruta de iconos no puede tumbar la app —
carga por icono con captura de errores (botón sin imagen si un PNG fallara),
y `BuildSideBar()` envuelto en try/catch: si algo
falla se muestra un cuadro con el detalle y la app **arranca igual, sin barra**.
Si ves ese cuadro de diagnóstico, manda su captura: incluye el stack completo.

**Visibilidad de paneles:** la barra se trae al frente (BringToFront) — el orden
del Designer la dejaba DETRÁS de los controles OpenGL y por eso era invisible —
y ocupa una **columna propia de 58 px en el borde izquierdo**. Los botones
flotantes originales de la izquierda (flechas de inclinación, Google Earth,
límite manual y panel de simulación) se desplazan 58 px a la derecha para que
no queden tapados y conservan el hueco al redimensionar (anclados a Left).
Los widgets del borde derecho vuelven a sus posiciones originales del Designer.
Además se retiró `HideTabControl()` del arranque: el panel izquierdo de
elevación (que la app original ocultaba al iniciar) ahora aparece desde el
principio y se alterna desde el icono **Perfil** de la barra (el emblema
Batman de la barra de estado quedó oculto, sustituido por Perfil). Nota: el
botón de límite manual está oculto por diseño en modo Grade y reaparece en
modo Survey.
