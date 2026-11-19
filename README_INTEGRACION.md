# AgIO integration into OpenGrade3D (beta-UPD)

> *English translation of `LEEME_INTEGRACION.md` (the Spanish original is the authoritative
> version). Code identifiers, file names, PGNs and ports are unchanged; on-screen labels are
> translated, e.g. the UI label «Fuente GPS:» is "GPS Source:".*

> **Goal:** a single development environment where AgOpenGrade3D carries *inside* the **AgIO**
> module extracted from the official AgOpenGPS repository, so that on the **same tablet**
> **AgOpenGPS (AOG) and OpenGrade3D (OG3D) can run simultaneously**, sharing the blade GPS
> input and the blade module, **keeping OpenGrade's original PGNs intact**.

> **How to check you are running this integration's AgIO:** AgIO's title bar must **end with
> `| OG3D v3`** (e.g. "AgIO v1.0.0 Profile: new   |   OG3D v3") and the OG3D box must include
> the **"GPS Source:"** label with the selector button. If your AgIO does not meet both, you
> are running an old `AgIO.exe`: rebuild the solution and launch the `AgIO.exe` that ends up
> in the `Application` folder (or simply run `OpenGrade3D.exe` from that folder: it launches
> AgIO by itself).

---

## 1. Why they could not coexist (root cause)

The beta-UPD version of OpenGrade3D already talks to AgIO, but **both apps on the tablet
fought over the same loopback port**:

| Application         | Loop receive socket           | Send toward AgIO       |
|---------------------|-------------------------------|------------------------|
| AgOpenGPS (AOG)     | `127.0.0.1:15555`             | `127.255.255.255:17777`|
| OpenGrade3D (OG3D)  | `0.0.0.0:15555` ← **same**    | `127.255.255.255:17777`|

AgIO only forwarded the stream to **one** destination (`15555`). The first app to start
grabbed the port and the second received nothing. The PGNs were not the problem: it was
purely a port issue.

## 2. Solution applied

1. **OpenGrade3D** now listens on its own port: **15556** (previously 15555).
2. **AgIO** (integrated) **duplicates the loop send** toward `15555` (AOG) and toward
   `15556` (OG3D). Each app ignores the PGNs that are not its own, so no filtering is
   needed and **OpenGrade's PGNs do not change a single byte**.
3. **OpenGrade3D auto-starts AgIO.exe** if none is running. It looks next to
   `OpenGrade3D.exe` (the `Application` folder) first, then in the AgIO project outputs.
   If AOG already started its AgIO, OG3D detects it and does not launch another. If it
   cannot find AgIO.exe — or if it starts and closes instantly (missing DLLs) — it shows
   an on-screen warning instead of failing silently.
4. **AgIO's output goes to the `Application` folder** (the same one as
   `OpenGrade3D.exe`): when you build the solution, `AgIO.exe` and all its DLLs end up
   together without copying anything by hand.
5. **"GPS Source" button in AgIO** — the OG3D box gains a selector to choose which GPS
   OpenGrade3D uses: **AOG's GPS** (forwarded by AgIO, the working one) or
   **OpenGrade3D's own GPS** (the OG3D module's own GNSS, ideal for field survey). The
   choice is stored in the profile and survives restarts.

### OpenGrade3D "GPS Source" button (OG3D box)

| Mode | What OG3D receives (15556) | Typical use |
|---|---|---|
| **AOG's GPS** (default) | PGN `0x7C 0xD6`, which AgIO rebuilds from AgOpenGPS's GPS NMEA. The OG3D module's own GNSS (`0x6A 0xD1`) is **not** forwarded to OG3D | Work with the tractor's GPS, the blade following AOG's position |
| **OpenGrade3D's GPS** | The OG3D module's own GNSS (`0x6A 0xD1` from the Teensy). PGN `0x7C 0xD6` is **not** forwarded to OG3D | **Field survey** with the blade machine's GNSS, taking advantage of the working pass |

In both modes AgOpenGPS (15555) keeps receiving everything as always, the blade
(`0x6A 0xE1`) and telemetry (`0x6A 0xE2`) reach OG3D untouched, and the button state is
stored in the profile (`setOG3D_gpsFromAOG` in the profile XML). So the switch is instant
without restarting OpenGrade3D, the app releases its internal `isGNSSfromOG1` flag if OG1
frames stop for ~2.5 s (new watchdog in `ScanForNMEA()`).

### Final network map (same tablet)

| Origin → Destination | Port | Content |
|---|---|---|
| OG3D Teensy (blade + GNSS) → AgIO | **9999** | Blade NMEA (`$GPGGA`, `$GPVTG`, …) + binary `0x80 0x81` OpenGrade PGNs (`0x6A 0xD1` GNSS, `0x6A 0xE1` blade, `0x6A 0xE2` telemetry…) |
| AgIO → modules (Teensy) | **8888** | Commands/settings (including what OG3D sends through the loop) |
| AOG → AgIO (loop) | **17777** | `0x80 0x81` frames from AOG (auto-steer, sections, settings…) |
| OG3D → AgIO (loop) | **17777** | `0x80 0x81` frames from OG3D (blade control…) |
| AgIO → AOG (loop) | **15555** | Everything received from modules + GPS PGN `0x7C 0xD6` generated while parsing NMEA |
| AgIO → OG3D (loop) | **15556** *(new)* | All of the above except the position PGNs (`0x7C 0xD6` and `0x6A 0xD1`): OG3D only receives the one from the source selected with the "GPS Source" button |

### Blade GPS flow

```
Blade GNSS receiver → Teensy (GPS.ino) → NMEA over UDP:9999
      → AgIO: gate ('$' + G/P/K, GGA/VTG/RMC/PANDA/PAOGI…) → parsing
      → PGN 0x7C 0xD6 → 15555 (AOG) and 15556 (OG3D)  ← PGN untouched
```

The OG module's binary frames (`0x6A 0xD1` OG1 GNSS and `0x6A 0xE1` blade status) travel
**as-is** from AgIO to both apps; OG3D keeps parsing them exactly as in beta-UPD
(`ParseOG1GpsPacket` / `ParseOGBlade1Packet`). OG3D's blade commands go out through
`17777` and AgIO forwards them through `8888` to the Teensy, as before.

## 3. What was added/modified

| File / folder | Change |
|---|---|
| `SourceCode/AgIO/**` | **NEW** — AgIO project extracted as-is from `AgOpenGPS-Official/AgOpenGPS → SourceCode/AgIO` (net48, WinForms) |
| `SourceCode/AgLibrary/**` | **NEW** — library AgIO depends on (also from the official repo) |
| `SourceCode/AgIO_Keypad/**` | **NEW** — copy of AgOpenGPS's Keypad for AgIO; assembly renamed to `AgIOKeypad.dll` so it does not clash with OG3D's own `Keypad.dll` |
| `SourceCode/Directory.Build.props` | **NEW** — pins `net48` for the extracted SDK-style projects |
| `SourceCode/OpenGrade3D.sln` | The 3 new projects added (new GUIDs) |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` | `Port = 15555` → **`15556`** (with patch comment) |
| `SourceCode/GPS/Program.cs` | New `TryStartAgIO()`: launches `AgIO.exe` from its folder if none is running |
| `SourceCode/AgIO/Source/Forms/UDP.designer.cs` | New `epOG3D` endpoint (15556) and double send in `SendToLoopBackMessageAOG()`; `cntrGPSOG3D`/`cntrBladeOG3D` counters and `ParseOG3DGpsPacket()` for the GPS-2 source; `SendGpsToLoopBack()` with the source filter for OG3D |
| `SourceCode/AgIO/Source/Forms/NMEA.Designer.cs` | The GPS PGN `0x7C 0xD6` now goes out via `SendGpsToLoopBack(nmeaPGN, true)` (to OG3D only if the selected source is "AOG's GPS") |
| `SourceCode/AgIO/Source/Forms/FormLoop.Designer.cs` | **New OG3D box** in the window: GPS-2 antenna, counter, Lat/Lon and blade icon; **"GPS Source:" label + selector button**; bottom row moved down and taller window |
| `SourceCode/AgIO/Source/Forms/Controls.Designer.cs` | `btnOG3DGpsSource_Click()` toggles the source and saves it; `UpdateOG3DGpsSourceButton()` paints the button text |
| `SourceCode/AgIO/Source/Properties/Settings.cs` | New persistent setting `setOG3D_gpsFromAOG` (profile XML) |
| `SourceCode/GPS/Forms/UDPComm.Designer.cs` | `Port = 15555` → **`15556`** (with patch comment); `og1GpsWatchdog` watchdog to release `isGNSSfromOG1` when switching sources |
| `SourceCode/GPS/Forms/Position.designer.cs` | `ScanForNMEA()` releases `isGNSSfromOG1` if OG1 frames stop for ~2.5 s (hot source switch) |
| `SourceCode/AgIO/Source/Forms/FormLoop.cs` | Window height 706 px; GPS-2 counter in `DoTraffic()` and green/red state of the OG3D antenna in `DoHelloAlarmLogic()` |
| `SourceCode/AgIO/Source/Properties/Resources.resx` + `Resources.Designer.cs` | New `Blade` resource — blade icon (`btnImages/Blade.png`) |
| `SourceCode/AgIO/Source/AgIO.csproj` | Removed `PublishDir` from the AOG repo tree; the Keypad reference points to `AgIO_Keypad` |

All changes carry the `// Parche integración OG3D:` comment so they can be found at a
glance (that literal string lives in the code).
**Not a single PGN changed** — neither in AgIO nor in OG3D.

### OG3D box in the AgIO window (separate GPS-2 source + blade)

Below the module column there is now an **OG3D** box with:

- **GPS-2 antenna** — the GNSS source arriving from the OpenGrade3D module via PGN
  `0x6A/0xD1` (today the Teensy's antenna; when we add the blade GPS input, it will be
  that one). Turns **green** when the module sends any `0x6A`-family frame (GNSS `0xD1`,
  blade `0xE1` or telemetry `0xE2`) and **red** when nothing arrives, like the other
  modules.
- **`---` / N counter** — GNSS `0x6A/0xD1` frames received in the last 2 s
  (`---` = no data from that source).
- **Lat / Lon** — decoded position of the GPS-2 source (same NMEA DDMM.MMMM ×1e9 format
  that OpenGrade3D decodes in `ParseOG1GpsPacket`).
- **Blade icon** — `Blade.png` embedded as an AgIO resource.
- **"GPS Source:" + selector button** — picks the GPS OpenGrade3D consumes:
  `AOG's GPS` or `OpenGrade3D's GPS` (see section 2). The button text always shows the
  active source and the choice is stored in the profile.

The usual GPS-1 (NMEA on port 9999) is still shown at the top with its big antenna; no
existing indicator moved or changed meaning. All `0x6A` traffic is still forwarded to the
loopback (15555 AOG + 15556 OG3D) untouched.

## 4. How to build

Requirements: **Visual Studio 2019/2022** with the *.NET desktop development* workload
(.NET Framework 4.8 already ships with Windows 10/11).

1. Open `SourceCode/OpenGrade3D.sln` → **5 projects** (`OpenGrade3D`, `Keypad`,
   `AgLibrary`, `AgIOKeypad`, `AgIO`). This solution **does not include the installer
   project**, so it opens with no warnings on any Visual Studio.
2. The 3 new projects restore NuGet packages (System.Memory, System.Resources.Extensions,
   Accord.*).
3. Build `Release | Any CPU` (or `x86` for OG3D, as always).
4. Outputs (both into the same `Application` folder of the repo):
   - `Application/OpenGrade3D.exe` (GPS project, as always)
   - `Application/AgIO.exe` + `AgLibrary.dll` + `AgIOKeypad.dll` + `Accord*.dll` +
     `System.Memory.dll` + `System.Resources.Extensions.dll` + `AgIO.exe.config`
     (AgIO project)

### What about the installer project (Setup)?

The `Setup\Setup.vdproj` project is a *Visual Studio Installer* project (GUID
`54435603-dbb4-11d2-8724-00a0c9a8b90c`) and **requires the extension**
[*Microsoft Visual Studio Installer Projects*](https://marketplace.visualstudio.com/items?itemName=VisualStudioClient.MicrosoftVisualStudio2022InstallerProjects).
Without it, VS shows the message *"The application for this project type is not
installed"* and loads the project as unusable.

That is why there are **two solutions**:

| Solution | Projects | Use |
|---|---|---|
| `OpenGrade3D.sln` | 5 (no Setup) | **The normal one** — build OG3D + AgIO without installing anything |
| `OpenGrade3D_con_Setup.sln` | 6 (with Setup) | Only if you installed the extension and want to regenerate the MSI installer |

`Setup.vdproj` remains untouched in its folder; if you ever install the extension you can
open `OpenGrade3D_con_Setup.sln` directly.

## 5. How to deploy to the tablet

1. Build the **full solution**: `AgIO.exe` and its DLLs are copied by themselves into the
   `Application` folder, next to `OpenGrade3D.exe`. There is nothing to copy by hand.
2. To move the program to another folder or tablet, copy **the whole `Application` folder**
   (OpenGrade3D.exe + AgIO.exe + AgIO.exe.config + all the DLLs). If you copy only the exe,
   OpenGrade3D will warn that AgIO is missing or that DLLs are missing.
3. Run OpenGrade3D: if no AgIO is running, it launches one. AgIO can also be run manually
   (it is the full usual AgIO: NTRIP, modules, UDP monitor, profiles…).
4. **This AgIO replaces the official AgIO on the tablet.** It uses the same configuration
   (`HKCU\SOFTWARE\AgOpenGPS`) and the same ports, so:
   - Never run two AgIOs at once. If you do, the second one **exits without opening a
     window** ("exited with code 0 (0x0)" when debugging with F5): it is not an error, it
     is AgIO's single-instance control (Mutex). This integration additionally warns with
     an **"AgIO is already running"** message; close the previous AgIO (Task Manager →
     `AgIO` processes) and launch it again.
   - Your existing module subnet, IPs and settings are preserved.
5. With AOG + OG3D open at the same time: both receive the full stream. AOG ignores
   OpenGrade's PGNs and vice versa.

## 6. Hook points for future modifications

This is where we will touch the next pieces:

| I want to… | Touch… |
|---|---|
| See the blade GPS position in AgIO | **Already works**: OG3D box (`ParseOG3DGpsPacket` in `UDP.designer.cs`); when the firmware sends the blade GPS `0x6A/0xD1` it will appear there |
| Add more data to the blade GPS input | `AgIO/Source/Forms/NMEA.Designer.cs` (parser) and `UDP.designer.cs` (sending PGN `0x7C 0xD6`) |
| Treat the blade as a module with its own hello/scan | `AgIO/Source/Forms/UDP.designer.cs` → `ReceiveFromUDP()` (PGN `0x6A`, OG module source address) |
| Send OG3D commands to the blade through a different module | `AgIO/Source/Forms/UDP.designer.cs` → `ReceiveFromLoopBack()` (today everything goes out via `epModule` 8888) |
| Change OG3D's port | `GPS/Forms/UDPComm.Designer.cs` (`Port`) and AgIO's `UDP.designer.cs` (`epOG3D`) |
| Change which GPS OpenGrade3D uses | The OG3D box's "GPS Source" button (AgIO's `Controls.Designer.cs`); the setting is `setOG3D_gpsFromAOG` |

## 7. Firmware compatibility

The firmware does not need changes: the Teensy keeps sending NMEA + binary to `9999` and
listening on `8888`, exactly as with the currently patched firmware (v9, with Patches
8/9/10).

## 8. Pat's commit incorporated (c6e6fa, "cleaning and speed from VTG repair", 2026-11-06)

This tree integrates commit
[`c6e6fa`](https://github.com/Pat-I/OpenGrade3D/commit/c6e6fa16f377e49be27311c12603e9de86aa833c)
from the original repo. What it changes:

**Firmware (`ArduinoCode/OG3D_Teensy_Firmware/`, identical to v9's Patch 10):**

1. **VTG: speed index 7 → 6** — field `[7]` of the sentence is the "K" unit; reading there
   gave `speedOG = 0` and the speed jumps in the video. (Already fixed the same way in v6;
   now it is also Pat's official criterion.)
2. **`NMEAParser<3>` → `<5>`** — headroom for more NMEA handlers.
3. **No-fix sentinels** — `lon/lat` from `200000000` (0.2°, a real coordinate → ghost
   position in OG3D) to `20000000000000` (20000°, which OG3D rejects) and altitude to
   `21000000` mm (21000 m, above OG3D's 20000 filter).
4. **`BuildNmea()` ALWAYS sends the `0x6A/0xD1`** — CRC and send move out of the fix `if`;
   without a fix the frame goes out with the sentinels. NOTE: the original commit wrote
   `memcpy(&GNSS1[5], 20000000000000, ...)` with the literal as a pointer (does not
   compile); here it is applied with real addresses.

**OpenGrade3D (`SourceCode/GPS/`):**

5. **Title bar with vehicle and field** — new `UpdateTitleBar()` in `FormGPS.cs`
   (+ calls in `FormGPS_Load`, `JobNewOpenResume`, `FileSaveEverythingBeforeClosingField`
   and after loading/saving the vehicle in `GUI.Designer.cs`).
6. **OG1 packet (0x6A/0xD1) speed cap: 500 → 300 km/h** in `UDPComm.Designer.cs`
   (`ParseOG1GpsPacket`).
7. **Ethernet window (`FormUDP`) cleaned up** — the legacy port/IP controls are hidden
   (`nudThisPort`, `label9`, `label2`, `label3`, `tboxAutoSteerIP`, `tboxRateRelayIP`) and
   the warning becomes "** Start AgIO to automatically use UDP", consistent with this
   tree's AgIO auto-start.

From the commit we do **not** copy: the base64 churn of `FormUDP.resx` (icon of a button
that stays hidden), `obj/*`, `Error Log.txt` or the `exe/pdb` binaries. The ProgBar
variable renumbering in `FormGPS.Designer.cs`/`FormGPS.resx` (designer churn, no
functional effect) **was** incorporated for commit fidelity, verifying that Designer and
resx stay coherent and the resx remains valid XML.

---

## 9. Dark cabin theme (modernized UI)

**What it is:** the whole OpenGrade3D interface (main screen, settings forms, on-screen
keypad and numeric keyboard, dialogs) is presented in a **dark cabin theme**: background
`#1E1E1E`, fields `#252526`, buttons `#333334` with a subtle border, light text `#EBEBEB`
and **Segoe UI** typography. In dialogs, text buttons grow to a 100×40 px minimum (gloved
touch use on tablets) and the font rises to 9 pt; on the main screen the font size is
preserved so the working layout does not move.

**How it works (`SourceCode/GPS/UiTheme.cs`):**

- A single `UiTheme` class walks each form recursively and applies the palette per control
  type (flat buttons with hover, dark fields with a thin border, grids, tabs, menus with
  its own dark renderer, etc.). There is no new application logic.
- It is invoked as `UiTheme.Apply(this);` at the end of each `InitializeComponent()` of
  the project's 17 Designers (automatic injection), so every window is dressed **before
  it is shown**: no flicker or light-theme flash.
- **Functionality is respected:** the colors the code assigns at runtime (NUD alarm red,
  `btnFreeDrive` green/red, status chips) are applied after the theme and still win. The
  3D canvas (`SharpGL.OpenGLControl`), the camera (WebEye) and the toolbar images are
  untouched.
- To tweak the palette just edit the constants at the top of `UiTheme.cs` (`Window`,
  `Surface`, `Field`, `Text`…). `Arial Black` (headlines) is kept.

**csproj changes (invisible to Visual Studio):** in your Visual Studio the project reads
its `.resx` resources through the classic .NET Framework path, **exactly like the
original and with no new dependency** (so the exe does not reference
`System.Resources.Extensions` at all). The reference to that DLL — included in
`SourceCode/GPS/Dependencies/` — is **conditioned** on building with
`/p:GenerateResourceUsePreserializedResources=true`, something only the
outside-of-Windows verification builds use. Besides, OpenGrade3D's `Release|x86` now
defines `AllowUnsafeBlocks` (only Debug|x86 had it, and `CContour.cs` uses unsafe code)
and Keypad defines Debug|x86 and Release|x86 outputs so the csproj builds directly.

> **Note on a startup error already fixed:** an intermediate release enabled preserialized
> resources unconditionally, which made the new exe depend on `System.Resources.Extensions`
> 4.0.0.0; when building the solution, the AgIO build (which uses package 9.0.0) copied its
> DLL into `Application/`, overwriting the other one and producing
> `System.IO.FileLoadException` when reading images from the `.resx`. With the current model
> that dependency disappears from the exe and the error cannot recur: rebuild the solution
> from this tree and it starts normally.

AgIO is not touched in this round (it keeps its classic look); the previous release's
"GPS Source" button is still included.

## 10. Left sidebar (user's template) — iteration 8

With your 16 icons (`OG3D Images.zip`) we built FormGPS's **left sidebar**: dark panel
(UiTheme palette) anchored between the menu and the status bar, with the icons embedded in
the executable. Each icon invokes **the real action that already existed** (same handlers
and same `ct` flags as the original buttons):

| Group / icon (fixed column) | Action (real handler) |
| --- | --- |
| **Group 1 (top): Field** | **The old program's Start**: create a new field, resume or load AGD (`JobNewOpenResume` → FormJob dialog) |
| **Group 1 (top): Config** | **The old program's Config**, direct (`btnSettings`) |
| **Group 2 (MODE): Survey_mode** | Survey mode (`SelectMode`) + staged popup (below) — highlights the current mode |
| **Group 2 (MODE): Grade_mode** | Grade mode (`SelectMode`) + staged popup (below) — highlights the current mode |
| **Group 3 (bottom): Profile** | Toggles the OpenGL elevation panel (replaces the Batman emblem) |
| Transport Start/Pause/Stop | Start/pause (`btnStartPause`) and close-save (`JobClose`) — only with survey active |

**Current flow map (iteration 4, per SurveyModeRun.png and GradeModeRun.png):**

| Popup | Stages and behavior |
| --- | --- |
| Survey_mode | **Root**: Benchmark (only places the mark, hides nothing), Boundary (opens two more options), Survey (point recording: Start/Stop/Pause appear and the other options disappear; pressing it again turns it off). **Boundary stage**: Boundary_Left / Boundary_Right — picking a side arms boundary recording and Start/Stop/Pause appear; the other side disappears; pressing the active side again deselects it and both reappear; Boundary returns to the root |
| Grade_mode | **Root**: CutFill (CutFill.png icon) and Altitude. **CutFill stage**: 3-icon cycle in the user's order — CutFillActual → CutActual → CutFillProposed (`btnPropExist` cycles Actual → Cut/Fill → Proposed); pressing CutFill again deselects (Proposed) and returns to the root. **Altitude stage**: ElevationExisting / ElevationProposed; pressing the active one again deselects the elevation view |
| Transport | Only appears with the survey machine recording boundary (RecBnd) or points (Rec); disappears when turned off, with Stop (closes and saves) or when returning to Grade |

**Iteration 2 — template structure (popups):** the fixed column moved to showing only the
main icons and the rest of the actions went into a **popup to the right of the bar** when
pressing the main icon, closing when clicking outside (3D scene) or pressing the same icon
again.

The **Profile icon replaces the Batman emblem** on the status bar (hidden; same job:
toggle the elevation panel). There are no more gray icons: each popup only contains its
mode's actions. The Boundary_Left/Boundary_Right icons stay embedded but unused for now
(the boundary side is changed with the original button).

**Iteration 3 — polish (sizes and substitutions):** icons go at their **native 64 px
size** in 72 px buttons — the same icon size as the window's right column; the column
grows from 58 to **88 px** wide and popups and transport grow likewise (transport: 72 px
buttons with 64 px icon, icon only, no text). The **Start and Config pills on the status
bar are hidden**: they are replaced by the sidebar's Start and Config icons (what they had
as a dropdown menu — Field, Reset trip — remains reachable from the File menu). And the
**Start/Stop/Pause transport no longer appears when pressing Survey**: it only shows with
the **survey started** (field open in Survey mode); it hides with Stop (closes the job),
when closing the field or when returning to Grade.

**Iteration 4 — flow map and 5 new icons:** the fixed column becomes **Survey_mode,
Grade_mode, Field, Config, Profile**: the **Field icon runs the old program's "Start"**
(`JobNewOpenResume` → FormJob dialog: create new field / resume / load AGD) and **Config
opens the old program's Config directly** (`btnSettings`, no popup); the bar's Start icon
disappears (starting the job is Field's job, and start/pause/stop live in the transport).
The popups follow your step map: in Survey, pressing **Boundary or Survey hides the other
options** (and Benchmark only places the mark, hiding nothing), Boundary opens both sides
(pressing the active side again deselects it and both icons reappear) and Survey shows the
Start/Stop/Pause transport; in Grade, **CutFill opens the surface cycle with a single
status icon** (Actual → Cut/Fill → Proposed) and **Altitude opens the Existing /
Proposed views**. The transport only appears with the survey machine recording (boundary
or points). The 5 icons from `NuevosIconos.zip` are added (CutActual, CutFillActual,
CutFillProposed, ElevationExisting, ElevationProposed) → **21 embedded PNGs** in total.

**Iteration 5 — three groups like the template and big badges:** the column is ordered
like `Template-OG3D.png`: **Field and Config on top**, below the **MODE** block (MODE
label + Survey_mode and Grade_mode) and **Profile anchored at the bottom** of the bar,
separated by the gap the window leaves. The Start/Stop/Pause transport moves to the
**native 120×50 badges** of `Stop.zip` (no rescaling, on 136×66 buttons), replacing the
same-name 64 px PNGs.

**Iteration 6 — readable Cut/Fill, 3-icon cycle and aligned column:** with the icons from
`CutFillProposed.zip` (white letters, legible on the dark theme) Grade's root switches to
showing **CutFill.png** and its cycle uses the **three icons in your order: CutFillActual
→ CutActual → CutFillProposed** (all three replaced by the white-letter versions). And the
freed column to the left of the bar is fixed: **only tilt and Google Earth shift**, the
**simulator panel stays aligned at x=94** with them (was 91, off-grid) and
**btnManualOffOn no longer moves** — shifting it landed it on top of the recording Start /
BoundarySide buttons.

**Iteration 7 — top row, simulator box and blade bars:** the floating top row (tilt
up/down, Google Earth, Manual/Auto and the recording Start and BoundarySide buttons)
shifts **together (+88 px)** preserving its original gaps — so they never stack in any
visibility combination (in #6 Manual/Auto was left fixed and ended up over the right
tilt; in 1-5 it moved alone and covered the recording Start). The **simulator box** is
reordered with no overlaps: angle and step sliders on the left with their `>0<` and Reset
buttons, elevation and `Go To` in their own column on the right, and the Lat/Long row
below (the scale labels — `B N M`, `L`, `H J K` and the angle value — stay over their
trackbars, like the original design). And in the right column, the **blade color legend
now follows the status bars when resizing**: it spreads over the bars' real height in
`FormGPS_Resize` (it used to stay fixed and misalign when maximized).

**Iteration 8 — right column like the original (user's reference screenshot):** with your
photo of how the right side should look, the real misalignment showed up: the dark theme
applied its **100×40 touch minimum to small text buttons**, which turned the **blade color
legend's 32 px squares into wide bands** and fattened the `+` / `−` scale and the `NC`
button; besides, iteration 7 spread the legend over the bars when resizing, whereas in the
original program the legend **stays fixed below Blade Offset**. Now those ten buttons
(`btnColor*`, `btnScalePlus`/`btnScaleMinus`, `btnModuleStatus`) **keep their design size
and color** (`UiTheme.TamanoDiseno` list), the legend does not move and `FormGPS_Resize`
returns to its original code: the blade bars stretch with the window and the legend stays
put, exactly like your screenshot.

**Iteration 9 — the missing resource DLL (error setting the offset and opening the
on-screen keyboard for the field name):** pressing the Blade Offset box opens the
**on-screen keyboard** (`KeypadToNUD` → Keypad.dll) and creating a field does the same
with the name window (`FormJob`): it threw `Could not load file or assembly
'System.Resources.Extensions, Version=4.0.0.0, ... PublicKeyToken=cc7b13ffcd2ddd51'`.
Cause: the modern compiler (.NET SDK on Linux) generates `.resources` in **preserialized**
format and, at runtime, .NET Framework 4.8 needs that DLL next to the exe to read them —
forms with icons or Bitmaps in their resx (Keypad, FormJob, Config, dialogs…) crashed on
open, while FormGPS started fine because its images go as raw bytes. Solution: **full
dependency closure** next to the exe, all from the official NuGet package (net461 build):
`System.Resources.Extensions.dll` 4.7.1 (54 KB) and the DLLs it in turn references —
`System.Memory.dll` 4.5.5, `System.Buffers.dll` 4.5.1, `System.Numerics.Vectors.dll`
4.5.0 and `System.Runtime.CompilerServices.Unsafe.dll` 4.5.3 (verified by reading each
one's assembly references: with these five the 4.8 GAC chain is complete). They live in
`SourceCode/GPS/Dependencies/`, the reference is in the csproj with `Private=True` and all
five ship in `Application/`. When installing you must replace the **entire Application
folder**, not just the exe — if only OpenGrade3D.exe is copied the DLLs are missing and
the error stays the same.

**Iteration 9b — the error changed to "manifest definition does not match":** with the
DLLs in place the loader found them but rejected the version: mscorlib asks for
`System.Resources.Extensions` **4.0.0.0** while package 4.7.1 serves assembly **4.0.1.0**
(measured with a metadata reader); besides, `System.Memory` asked for 4.0.1.1 and 4.5.5
serves 4.0.1.2. The old package versions (which did use 4.0.0.0) are no longer on the
NuGet CDN, so the standard solution is the **bindingRedirect**:
`SourceCode/GPS/app.config` now carries an `<runtime><assemblyBinding>` block redirecting
each request to the served version (Resources.Extensions → 4.0.1.0, Memory → 4.0.1.2, and
for safety Buffers → 4.0.3.0, Vectors → 4.1.4.0, Unsafe → 4.0.4.1). The build copies that
block to `Application/OpenGrade3D.exe.config`, and since the change is in **source code**,
it also works when debugging in Visual Studio (in your screenshot the failure hit
`new Keypad.NumKeypad()` inside the field-name window's `InitializeComponent` — same
origin, same remedy). The packaging assertions check the five redirects both in
app.config and in the generated exe.config.

**Iteration 9c — overlapping keyboard keys:** the keypad and keyboard now open, but in
"Enter a Value" (FormNumeric) the number row was half covered by the letters row. Cause:
the dark theme (UiTheme) changed the dialogs' typography (Tahoma → Segoe UI, raising small
sizes to 9 pt) and also applied a 100×40 touch minimum to text buttons; with
`AutoScaleMode.Font`, changing the font re-scales the form's controls, and in a nested
container (the `Keypad.NumKeypad` control inside FormNumeric or FormJob) the cascading
re-scale leaves the rows with uneven gaps — overlapping keys. The theme rule is now
defined forever: **paints colors, never touches fonts or sizes** (the font change and the
touch minimums were removed from UiTheme.cs); geometry and letters are always the
Designer's, exactly like the original program. The keyboard and keypad return to their
factory layout.

**Iteration 10 — AgIO opens by itself and gets its own sidebar button:** AgIO's
auto-start existed since the integration (`Program.TryStartAgIO` looks for AgIO.exe next
to OpenGrade3D.exe) but AgIO had never been built, so there was no exe to launch. Now the
AgIO project builds with the rest and its output lands directly in `Application/` (csproj
OutputPath): AgIO.exe, AgLibrary.dll, AgIOKeypad.dll and AgIO.exe.config with the same
bindingRedirects as the GPS; besides, AgIO's PackageReference were pinned to the same set
OpenGrade3D ships (`System.Memory` 4.5.5 and `System.Resources.Extensions` 4.7.1) so no
build overwrites DLLs with different versions. On the sidebar, below the MODE block
(Survey_mode and Grade_mode) with a separating gap, there is a new **AgIO** icon — the
64×64 PNG you sent, embedded in OG3DImages with the same mechanism as the other icons: if
AgIO is not running it launches it (same routine as auto-start) and if it is, it gives it
the focus, restoring it if minimized (`ShowWindow` + `SetForegroundWindow` from user32).
The auto-start when opening OpenGrade3D stays the same: if an AgIO is already running it
does not launch another.

For AgIO to also build on Linux, the **55 icons its `Resources.resx` references** had to
be committed (`SourceCode/AgIO/Source/btnImages/` and the `btnimages/` alias): the
original author did not include them and the build failed (MSB3554). They were downloaded
from the official AgOpenGPS repo (which ships them capitalized — `Trash.png`,
`Satellite.png` — and on Linux the lowercase copies used by OpenGrade3D's resx were
added). The restore-script step that deleted those two aliases was removed: it was a patch
for when they were missing.

**Iteration 11 — dark AgIO, Boundary popup and no duplicate icons on the 3D:** three
changes. (1) **AgIO wears the same dark cabin theme as OpenGrade3D**: the painter was
ported (`SourceCode/AgIO/Source/UiTheme.cs`, identical palette) and the integration script
injects `UiTheme.Apply(this)` at the end of the `InitializeComponent()` of AgIO's 21
forms, as already done for the GPS. It paints colors — never fonts or sizes (same rule as
iteration 9c) — and the functional colors AgIO assigns at runtime (alarms, LEDs…) still
win because they are applied after. (2) **The sidebar's Boundary popup now shows the
Boundary icon itself on top** (the dashed red square) followed by the two left/right
sides, like the reference screenshot: pressing the dashed icon returns the popup to the
survey root menu. (3) **The survey and boundary icons disappear from the OpenGL window**:
when recording points or boundary, the `recordSurveyPts` and `recordSurveyBoundary`
routines no longer re-show the floating START or the "boundary Left" button (their
function lives on the sidebar: Boundary popup for the side and Start/Stop/Pause transport
to record and stop).

**Iteration 12 — clean 3D window: the 12 original floating icons, gone.** Iteration 11
removed START and "boundary Left", but the zoom magnifiers, Cut/Fill and Proposed (right)
were still floating over the OpenGL, plus the tilt arrows, the Survey/"Recording
Boundary" button, the Google Earth globe, "Add pts from AOG elevation" and "RTK fix only"
(left). All are now hidden when the bar is built (`floaters3D` in `BuildSideBarCore`) and
**no routine brings them back**: `SelectMode` no longer re-brings Cut/Fill and Proposed
when moving to Grade, nor Survey and "RTK fix only" when moving to Survey, and the RecBnd
case no longer re-shows the elevation button. Where each function lives: the
survey/benchmark/boundary/points flow in the Survey_mode popup; Cut/Fill, Proposed and the
elevation views in Grade_mode's; Google Earth stays in the Display menu (and in the 3D
context menu). Since zoom and tilt **had no equivalent** (no wheel and no keyboard
shortcuts), the bar gains a **View** icon (below AgIO) with a 4-action popup — ZoomIn,
ZoomOut, TiltUp, TiltDown — using the same original handlers: each press applies one step
and the popup stays open. Their icons (magnifiers and blue arrows) were extracted from
`FormGPS.resx` into `OG3DImages/`. Note: the "RTK fix only" toggle (`ct.FloatIsOK`) is out
of the UI and keeps its current value. The +88 shift of the floating row disappears along
with the row (the simulator stays aligned to the right of the bar).

**Iteration 13 — your icons integrated: Field with an extra step, ScreenConfig, zoom on
the 3D and an Extras tab.** With the 10 new icons from `IconosExtras.zip`:

- **Field** now shows a two-option popup: **FileOpen** (folder, `FileOpen.png` icon from
  the original btnImages) which opens the usual jobs dialog (create/resume/load AGD, the
  "current tab"), and **GoogleEarth** which launches the Google Earth app with the field's
  flags (the menu item, which warns if no field is open).
- **ScreenConfig** (it13.4: third icon of the top group, below Config; previously under
  AgIO — replaces iteration 12's View icon): popup with **2DView / 3DView / TiltUp /
  TiltDown** and *auto-reclose*: it closes by itself after 1.8 s without pressing anything
  and each press resets the counter. The "2D" is a top-down camera preset
  (`camPitch = -80`, the tilt's cap) because OG3D has no 2D renderer; returning to 3D
  restores the previous pitch. Tilting manually also returns to 3D.
- **Zoom returns to the redesigned 3D, AgOpenGPS-style** (it13.2, after the two user
  screenshots): the two buttons float loose over the 3D, no container box, classic button
  style; "+" (`ZoomIn48.png`) upper right and "−" (`ZoomOut48.png`) about 260 px below,
  well separated for the finger. They reuse the original magnifiers' handlers, **with the
  mapping corrected**: the native handlers had the names crossed (`btnZoomIn_MouseDown`
  actually zoomed out), so "+" calls `btnZoomOut_MouseDown` and "-" the opposite. The
  iteration 12 resx icons `ZoomIn/ZoomOut/Vista` are retired from `OG3DImages/` (36 PNGs
  remain, with Folder, Feet and Hectare, those of it13.5).
- **Extras tab** in Config (`FormSettings`): **RTKOnly** toggles `ct.FloatIsOK`
  (text "RTK fix only"/"RTK or float") and **FromAOG** launches `FileOpenElevationFromAOG`
  (the former "Add pts from AOG elevation"); `btnBuildFromElevation_Click` and
  `LoadSideImage` were moved to `internal` so FormSettings can call them. (it13.3) The
  5 tabs are painted auto-sized (`TabSizeMode.Normal`: no scroll arrows, all visible on
  open) and the Extras tab gets the dark theme with `UiTheme.Apply` — it was built after
  `InitializeComponent`'s Apply and came out light-themed.
- **Bar cosmetics** (it13.4): more air between the top group and the MODE block; the
  active icon is no longer marked with the faint gray (Hover over SurfaceHi was invisible
  under the cabin sun) but with blue fill (`UiTheme.Selected`, #264F78) and a light
  border, also on the survey transport buttons.
- **Files and Ac/Ha out of the right panel** (it13.5, with the three icons from the
  user's `Folder.zip`): the folder is now **Folder.png** in Field's popup and opens the
  fields directory in the file explorer (with a job in progress it opens its folder;
  otherwise the Fields root). The unit switch moves to the Extras tab as a **System of
  units** section: **Hectare.png = metric** and **Feet.png = imperial**, the active one
  highlighted (blue + border, same criterion as the bar) and metric by default. The logic
  lives in `FormGPS.SetUnits`; the `btnUnits` and `btnFileExplorer` buttons were removed
  from the Designer and their slots are taken by `btnFlag` and `btnGPSData`, which move
  up to the panel's top row.
- **Maximum Cutting Depth finally implemented** (it13.6): the vehicle page's NUD is no
  longer decorative — `vehicle.maxCuttingDepth` (**0 = no limit**, the default) caps the
  cut depth **per pass** in the map guide: in the Actual Cut/Fill views the pass target
  never goes deeper than that cap relative to the current surface (`PassTargetAltitude`
  in `CContour`), and in the original cut/fill view the areas whose cut exceeds the cap
  are painted blue ("several passes will be needed here"). The same rule is the anchor
  point of the future **hydraulic clamp**: the setpoint sent to the valve must be the
  pass target, not the final design. The label no longer says "(not implemented yet)".
- **Hydraulic clamp + Teensy module** (it13.7): the control chain already lived in the
  code — PGN **0xBA** goes out on every GPS tick with the cut altitude and valve demand
  (cm error → byte 100±990), **0xB8** carries the vehicle's PWM tuning and the module
  answers **0xE1**. What was missing: (1) in `Position.designer.cs` the valve target is
  now clamped to `bladeAltitude − maxCuttingDepth` before the mapping (normal and laser
  modes) — **the valve never asks to cut deeper than configured**; (2) AgIO routes
  0xBA/0xB8 to the module port (new cases in `ReceiveFromLoopBack`); (3) the blade module
  is a **Teensy 4.1 over Ethernet/UDP** (detail in it13.8), not serial: AgIO forwards
  **all** loop PGNs to `{subnet}.255:8888` (`epModule`, line 228 of `UDP.designer.cs`)
  and that is where the Teensy listens; it answers 0xE1 + telemetry on 9999 toward OG3D.
  The 0xBA/0xB8 cases added to AgIO's serial switch remain as optional support for a
  future COM module; with this hardware they do not take part.
- **Real module firmware integrated** (it13.8): the folder
  `SourceCode/TeensyBladeValve/OG3D_Teensy_Firmware/` contains the full sketch
  (5 .ino/.h + `OG3D_Globals.h`) with the 8 patches documented in its
  `CAMBIOS_PARCHES.md` and the diff in `parches.diff`: module with **its own GNSS** on
  Serial2 (measures the blade height and sends it to OG3D in the 0x6A/0xD1 — the same
  sensor the clamp uses in the app), proportional control with deadband and PWM gains
  persistent in EEPROM (they arrive via 0xB8), **GNSS failsafe** (GGA > 600 ms → neutral,
  Patch 1), incoming CRC verification on 0xBA/0xB8 (Patch 2, byte-for-byte compatible
  with the CRC `SendPgnToLoop` computes), manual lever with priority, heartbeat to AgIO
  (Patch 4) and **UDP telemetry** 0x6A/0xE2 at 5 Hz with `decodificar_telemetria.py`
  included (Patch 8). AiO v4.5 board: DIR pin 4, PWM pin 2 (100 Hz, 12 bits), LOCK pin 3,
  `proportionalValve = true`. **The it13.7 hydraulic clamp works end to end with this
  firmware with no additional changes**: OG3D sends in the 0xBA the target already
  clamped to the per-pass cap and the module compares it against its own fresh GNSS
  height. Iteration 13.7's serial sketch `TeensyBladeValve.ino` was retired as obsolete
  against the real firmware.
- **Zip with no WinRAR warnings** (it13.9): since it10 the tree carried the lowercase
  `btnimages/` folder next to `btnImages/`, and inside `trash.png` + `Trash.png` and
  `satellite.png` + `Satellite.png` — on Linux they are distinct files, but Windows
  considers them the same path and WinRAR asked "the following file already exists" when
  extracting. They were identical copies (same md5): one copy of each icon remains in the
  canonical `btnImages/`, `btnimages/` was deleted and AgIO's `Resources.resx` was
  normalized to `..\btnImages\trash.png` and `..\btnImages\Satellite.png` (the
  `satellite` resource is used by FormCommSetGPS; it compiles the same on Windows and
  Linux). The packager now verifies the zip contains **no** path that collides when
  ignoring case: if an alias reappears, the check fails and the zip is not shipped.

This error also uncovered a packaging failure: **iteration 8's zip shipped with iteration
7's exe** — the project builds directly into `Application/` (OutputPath) and the later
restore step overwrote it with the previous zip's. The flow is now **restore → build →
package**, and the verifications check inside the binary that it carries the fix
(`TamanoDiseno` marker in the exe).

**What changed:** new file `SourceCode/GPS/Forms/FormGPS.SideBar.cs` (FormGPS partial),
folder `SourceCode/GPS/OG3DImages/` with your 21 embedded PNGs (`EmbeddedResource`),
`BuildSideBar()` call in FormGPS's constructor and a `scripts/patch_sidebar.py` script
that applies the two previous changes idempotently.

**Iteration 1 — nothing has been removed yet:** all original buttons stay in place so the
survey/grade flows do not break (several change visibility per mode). Once you approve the
structure, we remove the duplicates marked with X in your template (Start/Config on the
bottom bar, floating Cut/Fill and Proposed, AVG circle) and fine-tune groups/order.

**Startup fix:** the `BuildSideBar()` call goes **at the end of FormGPS's constructor**
(after creating `ct`, `pn`, `mc`, `sim`…). At first it was right after
`InitializeComponent()` and reading `ct.surveyMode` threw `NullReferenceException`
because `CContour` did not exist yet. `UpdateSideBarState()` also ignores the call if
`ct` is null, in case it is invoked too early.

**Bar robustness:** the icon path can no longer take the app down — per-icon loading with
error capture (button without image if a PNG failed), and `BuildSideBar()` wrapped in
try/catch: if anything fails a box with the details is shown and the app **starts anyway,
without the bar**. If you see that diagnostic box, send its screenshot: it includes the
full stack trace.

**Panel visibility:** the bar is brought to the front (BringToFront) — the Designer order
left it BEHIND the OpenGL controls, which is why it was invisible — and occupies its
**own 58 px column on the left edge**. The original floating left buttons (tilt arrows,
Google Earth, manual boundary and the simulation panel) shift 58 px right so they are not
covered and keep their gap when resizing (Left-anchored). The right-edge widgets return
to their original Designer positions. Also `HideTabControl()` was removed from startup:
the left elevation panel (hidden at start by the original app) now appears from the
beginning and is toggled from the bar's **Profile** icon (the status bar's Batman emblem
was hidden, replaced by Profile). Note: the manual boundary button is hidden by design in
Grade mode and reappears in Survey mode.
