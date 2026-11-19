using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace OpenGrade
{
    // Parche integracion OG3D: barra lateral izquierda, iteracion 8 (template,
    // badges grandes, ciclo Cut/Fill de 3 iconos y acomodo de columnas).
    // La barra se organiza en TRES GRUPOS como el Template-OG3D: Field+Config
    // arriba, debajo el bloque MODE (rótulo MODE + Survey_mode y Grade_mode) y
    // Perfil anclado al fondo de la barra, separado por el hueco que deje la
    // ventana. Los popups y el flujo por fases de la iteración 4 no cambian.
    //  - Field  -> JobNewOpenResume: el "Start" del programa antiguo (crear campo
    //              nuevo, continuar o cargar AGD via el dialogo FormJob).
    //  - Config -> btnSettings_Click_1: el Config del programa antiguo, directo.
    // POPUP de Survey_mode por fases (lo pulsado oculta al resto, como el mapa):
    //   raiz [Benchmark, Boundary, Survey]
    //     Benchmark -> solo pone la marca (ct.markBM); no oculta nada
    //     Boundary  -> fase limite [Boundary, Boundary_Left, Boundary_Right]:
    //                  elegir lado arma la grabacion del limite (recBoundary) y
    //                  aparecen Start/Stop/Pause; pulsar el lado activo de nuevo
    //                  desactiva y vuelven a verse los dos iconos; Boundary vuelve al raiz
    //     Survey    -> grabacion de puntos (recordSurveyPts) y aparecen Start/Stop/
    //                  Pause; el resto de opciones desaparecen; pulsar de nuevo desactiva
    // POPUP de Grade_mode por fases:
    //   raiz [CutFill, Altitude]
    //     CutFill  -> raiz con el icono CutFill.png; al pulsarlo entra en el ciclo
    //                 de 3 iconos en el orden del usuario: CutFillActual ->
    //                 CutActual -> CutFillProposed (btnPropExist); CutFill.png de
    //                 nuevo deselecciona (Proposed) y vuelve al raiz
    //     Altitude -> fase [Altitude, ElevationExisting, ElevationProposed]: elegir
    //                 vista de elevacion; pulsar la activa de nuevo deselecciona
    // TRANSPORTE Start/Stop/Pause (flotante arriba al centro del 3D): solo aparece
    // con el survey iniciado, es decir con la maquina survey en RecBnd (limite) o
    // Rec (puntos); desaparece al desactivar, al parar (Stop = JobClose) o en Grade.
    // Los iconos de la barra van a su tamano nativo de 64 px en botones de 72;
    // columna de 88 px; el transporte usa los badges nativos de 120x50 del usuario
    // en botones de 136x66. La columna liberada queda alineada en x=94 (88 de
    // barra + 6 de hueco) y desde la it. 12 los iconos flotantes sobre el 3D
    // están OCULTOS (la barra y sus popups los sustituyen); desde la it. 13 el
    // zoom vuelve como panel propio en la esquina superior derecha del OpenGL
    // (ZoomIn48/ZoomOut48 del usuario, en vertical con hueco tactil);
    // la columna derecha conserva el diseno original (UiTheme.TamanoDiseno): la
    // leyenda de colores de la cuchilla queda fija bajo Blade Offset, sin que el
    // minimo tactil de 100x40 convierta sus cuadrados de 32 px en bandas anchas.
    // Las pildoras Start/Config y el emblema Batman de la barra de estado quedan
    // sustituidos por los iconos de la barra. Cada icono invoca la accion REAL que
    // ya existia (mismos handlers y mismos flags de ct que los botones originales).
    public partial class FormGPS
    {
        private Panel sideBarPanel;
        private Panel sidePopupPanel;         // popup a la derecha de la barra
        private Panel sideTransportPanel;     // Start/Stop/Pause arriba al centro del 3D
        private Button sideBtnZoomIn;         // zoom sobre el 3D, estilo AgOpenGPS
        private Button sideBtnZoomOut;        // (it13.2) botones sueltos, sin caja
        private Timer sideAutoClose;          // autocrerre del popup de ScreenConfig
        private double sidePitchBefore2D;     // pitch que restaura la vuelta al 3D
        private string sidePopupOwner;        // icono principal que abrió el popup
        private string sideSurveyNav;         // navegación popup survey: "boundary" o null
        private string sideGradeNav;          // navegación popup grade: "cutfill"/"altitude"/null
        private ToolTip sideBarTip;
        private readonly Dictionary<string, Image> sideIcons = new Dictionary<string, Image>();
        private readonly Dictionary<string, string> sideTooltips = new Dictionary<string, string>();

        // barra en TRES GRUPOS como el template: Field, Config y ScreenConfig
        // arriba (it13.4: ScreenConfig es el 3er icono, debajo de Config), bloque
        // MODE (rótulo + Survey_mode y Grade_mode) y Perfil anclado al fondo
        private static readonly string[] sideBarGroupTop = new string[] { "Field", "Config", "ScreenConfig" };
        private static readonly string[] sideBarGroupMode = new string[] { "Survey_mode", "Grade_mode" };

        // popup survey, fase raíz: stem del PNG, acción (Tag) y tooltip
        private static readonly string[][] sidePopupSurveyRoot = new string[][]
        {
            new[] { "Benchmark", "Benchmark", "Pone un benchmark (marca); no oculta nada" },
            new[] { "Boundary", "Boundary", "Grabar límite: elige el lado (aparecen dos opciones)" },
            new[] { "Survey", "Survey", "Grabar puntos survey: aparecen Start/Stop/Pause" }
        };

        // popup survey, fase límite: el lado activo se desactiva pulsándolo de nuevo.
        // (iteración 11) el propio Boundary se mantiene arriba, como en la captura de
        // referencia del usuario: pulsarlo devuelve el popup al menú raíz del survey
        private static readonly string[][] sidePopupBoundary = new string[][]
        {
            new[] { "Boundary", "Boundary", "Límite del campo: pulsa para volver al menú survey" },
            new[] { "Boundary_Left", "Boundary_Left", "Límite por la izquierda (pulsa otra vez para desactivar)" },
            new[] { "Boundary_Right", "Boundary_Right", "Límite por la derecha (pulsa otra vez para desactivar)" }
        };

        // popup survey, fase puntos: solo el icono Survey (el resto desaparece)
        private static readonly string[][] sidePopupSurveyPts = new string[][]
        {
            new[] { "Survey", "Survey", "Grabando puntos survey (pulsa otra vez para desactivar)" }
        };

        // popup grade, fase raíz: dos grupos (el ciclo de superficie y la elevación)
        private static readonly string[][] sidePopupGradeRoot = new string[][]
        {
            new[] { "CutFill", "CutFill", "Superficie Cut/Fill: abre el ciclo de 3 vistas (Actual -> Cut/Fill -> Proposed)" },
            new[] { "Altitude", "Altitude", "Vista de elevación: Existing o Proposed" }
        };

        // popup grade, fase elevación: el grupo Altitude vuelve al raíz y deselecciona
        private static readonly string[][] sidePopupAltitude = new string[][]
        {
            new[] { "ElevationExisting", "ElevationExisting", "Elevación existente (pulsa otra vez para desactivar)" },
            new[] { "ElevationProposed", "ElevationProposed", "Elevación propuesta (pulsa otra vez para desactivar)" }
        };

        // popup field (iteración 13): un paso más al pulsar Field — carpeta y
        // Google Earth; FileOpen abre el diálogo de trabajos de siempre.
        // (it13.5) Folder sustituye al btnFileExplorer del panel derecho: abre
        // el directorio de campos en el explorador, con el icono del usuario
        private static readonly string[][] sidePopupFieldRoot = new string[][]
        {
            new[] { "Folder", "Folder", "Abrir el directorio de campos en el explorador" },
            new[] { "FileOpen", "FileOpen", "Abrir/continuar el trabajo actual (diálogo de siempre)" },
            new[] { "GoogleEarth", "GoogleEarth", "Abrir Google Earth con el campo abierto" }
        };

        // popup screen config (iteración 13): 2D/3D e inclinación; el popup se
        // autocierra tras unos segundos sin operar (temporizador)
        private static readonly string[][] sidePopupScreenConfig = new string[][]
        {
            new[] { "2DView", "2DView", "Vista 2D: cámara cenital (pulsa de nuevo para volver al 3D)" },
            new[] { "3DView", "3DView", "Vista 3D normal con inclinación libre" },
            new[] { "TiltUp", "TiltUp", "Inclinar la cámara hacia arriba (por pasos)" },
            new[] { "TiltDown", "TiltDown", "Inclinar la cámara hacia abajo (por pasos)" }
        };

        internal void BuildSideBar()
        {
            if (sideBarPanel != null) return;

            // la barra es un extra de la UI: si algo falla NO debe impedir que la
            // app arranque; se muestra el error con su stack y se sigue sin barra
            try
            {
                BuildSideBarCore();
            }
            catch (Exception ex)
            {
                MessageBox.Show("La barra lateral izquierda se ha desactivado por un error y "
                    + "la app continúa sin ella. Detalle para reportar:\r\n\r\n" + ex,
                    "OpenGrade3D - barra lateral", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void BuildSideBarCore()
        {
            // liberar la columna izquierda (88 px) para la barra. (Iteraciones 12-13)
            // los iconos flotantes originales sobre la ventana 3D DESAPARECEN: survey
            // y boundary viven en el popup de Survey_mode, Cut/Fill y Proposed en el
            // de Grade_mode, Google Earth en el popup de Field, Only RTK y FromAOG
            // en la pestaña Extras del Config, la inclinación en el popup de
            // ScreenConfig y el zoom en el panel de la esquina superior derecha.
            Control[] floaters3D = new Control[]
            {
                btnTiltUp, btnTiltDown, btnGoogleEarth, btnManualOffOn,
                btnStartPause, btnBoundarySide, btnBuildFromElevation,
                btnFixQuality, btnZoomIn, btnZoomOut, btnCutFillElev, btnPropExist
            };
            foreach (Control c in floaters3D) c.Visible = false;
            // el panel del simulador queda a la derecha de la barra
            // (94 = 88 de barra + 6 de hueco)
            panelSimControls.Left = 94;

            // cuadro del simulador reordenado sin solapes: sliders de angulo y
            // paso a la izquierda con sus botones '>0<' y Reset, elevacion y
            // 'Go To' en columna propia y la fila Lat/Long debajo; las etiquetas
            // de escala ('B N M', 'L', 'H J K' y el valor del angulo) siguen
            // sobre sus trackbars, como en el diseno original
            tbarSteerAngle.SetBounds(2, 8, 150, 40);
            label11.SetBounds(10, 30, 40, 16);
            lblSteerAngle.SetBounds(95, 27, 19, 19);
            btnResetSteerAngle.SetBounds(156, 12, 46, 28);
            btnResetSim.SetBounds(206, 12, 52, 28);
            tbarStepDistance.SetBounds(264, 8, 118, 40);
            label3.SetBounds(268, 30, 13, 16);
            label12.SetBounds(330, 30, 35, 16);
            nudElevation.SetBounds(390, 8, 123, 36);
            label6.SetBounds(8, 62, 30, 19);
            nudLatitude.SetBounds(42, 58, 120, 27);
            label13.SetBounds(168, 62, 44, 19);
            nudLongitude.SetBounds(212, 58, 120, 27);
            btnSimGoTo.SetBounds(390, 50, 124, 36);

            // los iconos Perfil, Start y Config de la barra sustituyen a los tres
            // controles equivalentes de la barra de estado (Batman, Start, Config);
            // lo que tenían de menú (Campo, Reset trip) sigue en el menú File
            try
            {
                btnHideTabs.Visible = false;
                toolStripDropDownBtnFuncs.Visible = false;
                toolStripDropDownButton2.Visible = false;
            }
            catch (Exception) { /* cosmético: si faltara alguno, no afecta a la barra */ }

            sideBarPanel = new Panel
            {
                Name = "sideBarPanel",
                BackColor = UiTheme.Surface,
                Width = 88,
                AutoScroll = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };

            sideBarTip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400, ReshowDelay = 200 };

            sideTooltips["Survey_mode"] = "Modo Survey (levantamiento): abre su popup";
            sideTooltips["Grade_mode"] = "Modo Grade (nivelación): abre su popup";
            sideTooltips["Field"] = "Trabajo: abrir/continuar (carpeta) o Google Earth";
            sideTooltips["Config"] = "Configuración del programa (el Config del programa antiguo)";
            sideTooltips["Perfil"] = "Perfil de elevación y estado de la cuchilla "
                + "(sustituye al emblema Batman)";
            sideTooltips["AgIO"] = "AgIO: si no está abierto lo lanza; si ya está, "
                + "lo trae al frente (módulos, UDP y puertos)";
            sideTooltips["ScreenConfig"] = "Pantalla: vista 2D/3D e inclinación de la cámara";

            int y = 8;
            // grupo 1 del template: Field y Config arriba
            foreach (string stem in sideBarGroupTop)
            {
                Button btn = MakeSideButton(stem, ActionFor(stem), sideTooltips[stem]);
                btn.Top = y;
                sideBarPanel.Controls.Add(btn);
                y += 80;
            }

            // rótulo del bloque de modos, como en el template
            var modeLabel = new Label
            {
                Text = "MODE",
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = UiTheme.Text,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(72, 22),
                Left = 8,
                Top = y + 58, // (it13.4) más aire entre el grupo superior y MODE
                TextAlign = ContentAlignment.MiddleLeft
            };
            sideBarPanel.Controls.Add(modeLabel);
            y = modeLabel.Bottom + 6;

            // grupo 2 del template: Survey_mode y Grade_mode bajo el rótulo MODE
            foreach (string stem in sideBarGroupMode)
            {
                Button btn = MakeSideButton(stem, ActionFor(stem), sideTooltips[stem]);
                btn.Top = y;
                sideBarPanel.Controls.Add(btn);
                y += 80;
            }

            // grupo extra (iteración 10): AgIO debajo de los dos modos, con un
            // hueco de separación respecto al bloque MODE, como pidió el usuario
            y += 40;
            Button btnAgio = MakeSideButton("AgIO", "AgIO", sideTooltips["AgIO"]);
            btnAgio.Top = y;
            sideBarPanel.Controls.Add(btnAgio);
            y += 80;

            // (it13.4) ScreenConfig ya no va bajo AgIO: es el 3er icono del grupo
            // superior (debajo de Config). Su popup (2D/3D e inclinación) se
            // autocierra tras unos segundos sin operar; el zoom vive sobre el 3D,
            // no en la barra.

            // grupo 3 del template: Perfil anclado al fondo de la barra; el hueco
            // flexible hasta él lo da la altura de la ventana
            Button btnPerfil = MakeSideButton("Perfil", "PerfilPanel", sideTooltips["Perfil"]);
            btnPerfil.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            sideBarPanel.Controls.Add(btnPerfil);

            sideBarPanel.Height = y + 10;
            btnPerfil.Top = sideBarPanel.Height - 80;

            // popup lateral: contenido dinámico según el icono principal pulsado
            sidePopupPanel = new Panel
            {
                Name = "sidePopupPanel",
                BackColor = UiTheme.Surface,
                Width = 88,
                Visible = false
            };

            // transporte survey flotante: Start/Stop/Pause arriba al centro del 3D,
            // con los badges nativos de 120x50 del usuario (sin reescalado)
            sideTransportPanel = new Panel
            {
                Name = "sideTransportPanel",
                BackColor = UiTheme.Surface,
                Width = 436,
                Height = 82,
                Visible = false
            };
            int tx = 8;
            string[][] transportDefs = new string[][]
            {
                new[] { "tr_Start", "Start", "Iniciar / continuar el trabajo" },
                new[] { "tr_Pause", "Pause", "Pausar el trabajo" },
                new[] { "tr_Stop", "Stop", "Cerrar y guardar el trabajo" }
            };
            foreach (string[] def in transportDefs)
            {
                Button tr = new Button
                {
                    Name = def[0],
                    Tag = def[1],
                    Size = new Size(136, 66),
                    Left = tx,
                    Top = 8,
                    Image = SideIconRaw(def[1]),
                    ImageAlign = ContentAlignment.MiddleCenter,
                    BackColor = UiTheme.SurfaceHi,
                    FlatStyle = FlatStyle.Flat,
                    Cursor = Cursors.Hand
                };
                tr.FlatAppearance.BorderSize = 0;
                tr.FlatAppearance.MouseOverBackColor = UiTheme.Hover;
                tr.FlatAppearance.MouseDownBackColor = UiTheme.Pressed;
                tr.Click += SideTransport_Click;
                sideBarTip.SetToolTip(tr, def[2]);
                sideTransportPanel.Controls.Add(tr);
                tx += 142;
            }

            // botones de zoom (it13.2, como en AgOpenGPS): sueltos sobre el 3D, sin
            // caja contenedora y con el estilo clásico de botón; el "+" arriba a la
            // derecha y el "−" bien más abajo (los separa PositionZoom). Reutilizan
            // los handlers de las lupas originales.
            sideBtnZoomIn = MakeZoomButton("side_zi", "ZoomIn", "ZoomIn48", "Acercar la vista (por pasos)");
            sideBtnZoomOut = MakeZoomButton("side_zo", "ZoomOut", "ZoomOut48", "Alejar la vista (por pasos)");

            // temporizador de autocrerre del popup de ScreenConfig (iteración 13):
            // unos segundos sin pulsar nada lo cierran; cada pulsación lo reinicia
            sideAutoClose = new Timer { Interval = 1800 };
            sideAutoClose.Tick += SideAutoClose_Tick;

            this.Controls.Add(sideBarPanel);
            this.Controls.Add(sidePopupPanel);
            this.Controls.Add(sideTransportPanel);
            this.Controls.Add(sideBtnZoomIn);  // (it13.1) sin Controls.Add nunca se pinta
            this.Controls.Add(sideBtnZoomOut);
            // al frente: el orden del Designer deja la barra DETRAS de los controles
            // OpenGL y por eso era invisible
            sideBarPanel.BringToFront();
            sidePopupPanel.BringToFront();
            sideTransportPanel.BringToFront();
            sideBtnZoomIn.BringToFront();
            sideBtnZoomOut.BringToFront();
            PositionZoom();
            // un click sobre la escena 3D cierra el popup abierto
            openGLControl.MouseDown += SideGlMouseDown;
            this.Resize += SideBar_OnResize;
            UpdateSideBarBounds();
            UpdateSideBarState();
        }

        // accion (Tag) de cada icono principal: Perfil sustituye a Batman y por eso
        // su accion es distinta del stem del PNG
        private static string ActionFor(string stem)
        {
            return stem == "Perfil" ? "PerfilPanel" : stem;
        }

        // fabrica compartida de botones de la barra y de los popups (72 px con el
        // icono nativo de 64: mismo tamaño de icono que la columna derecha)
        private Button MakeSideButton(string stem, string action, string tooltip)
        {
            var btn = new Button
            {
                Name = "side_" + action,
                Tag = action,
                Size = new Size(72, 72),
                Left = 8,
                Image = SideIcon(stem),
                ImageAlign = ContentAlignment.MiddleCenter,
                BackColor = UiTheme.SurfaceHi,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = UiTheme.Hover;
            btn.FlatAppearance.MouseDownBackColor = UiTheme.Pressed;
            btn.Click += SideBar_Click;
            sideBarTip.SetToolTip(btn, tooltip);
            return btn;
        }

        // limites de la barra: entre el menu superior y la barra de estado inferior
        private void SideBar_OnResize(object sender, EventArgs e)
        {
            UpdateSideBarBounds();
            if (sidePopupPanel != null && sidePopupPanel.Visible) PositionPopup();
            if (sideTransportPanel != null && sideTransportPanel.Visible) PositionTransport();
            if (sideBtnZoomIn != null) PositionZoom();
        }

        private void UpdateSideBarBounds()
        {
            if (sideBarPanel == null || menuStrip1 == null || statusStrip1 == null) return;
            int top = menuStrip1.Bottom;
            int bottom = statusStrip1.Top;
            if (bottom <= top) bottom = top + sideBarPanel.Height;
            sideBarPanel.SetBounds(0, top, 88, bottom - top);
        }

        private void SideGlMouseDown(object sender, MouseEventArgs e)
        {
            ClosePopup();
        }

        // ---- popup de acciones (a la derecha del icono principal pulsado) ----

        private void TogglePopup(string owner)
        {
            if (sidePopupPanel == null) return;
            if (sidePopupOwner == owner && sidePopupPanel.Visible)
            {
                ClosePopup();
                return;
            }

            sidePopupOwner = owner;
            RenderPopup();
            sidePopupPanel.Visible = true;
            sidePopupPanel.BringToFront();
        }

        // reconstruye el popup según la fase activa (mapa de flujo del usuario)
        private void RenderPopup()
        {
            if (sidePopupPanel == null || sidePopupOwner == null) return;

            sidePopupPanel.Controls.Clear();
            string[][] items = ItemsForPhase(sidePopupOwner);

            int y = 8;
            foreach (string[] def in items)
            {
                Button btn = MakeSideButton(def[0], def[1], def[2]);
                btn.Top = y;
                sidePopupPanel.Controls.Add(btn);
                y += 80;
            }
            sidePopupPanel.Height = y + 8;
            PositionPopup();
            UpdateSideBarState();
        }

        // opciones visibles del popup según la navegación y el estado real:
        // survey -> raíz / límite / puntos; grade -> raíz / ciclo cutfill / elevación
        private string[][] ItemsForPhase(string owner)
        {
            // (iteración 13) popup de Field: carpeta y Google Earth
            if (owner == "Field") return sidePopupFieldRoot;

            // (iteración 13) popup de ScreenConfig: 2D/3D e inclinación
            if (owner == "ScreenConfig") return sidePopupScreenConfig;

            if (owner == "Survey_mode")
            {
                if (manualBtnState == btnStates.Rec) return sidePopupSurveyPts;
                if (manualBtnState == btnStates.RecBnd || sideSurveyNav == "boundary")
                    return sidePopupBoundary;
                return sidePopupSurveyRoot;
            }

            // Grade_mode: la fase se recuerda mientras el popup está abierto, pero
            // al reabrir se deduce del estado real de ct (vista activa)
            string phase = sideGradeNav;
            if (phase == null)
            {
                if (ct.isElevation) phase = "altitude";
                else if (ct.isActualCut || ct.isActualFill) phase = "cutfill";
            }

            if (phase == "cutfill")
            {
                // ciclo de 3 iconos en el orden del usuario: CutFillActual ->
                // CutActual -> CutFillProposed; el estado real de ct decide cual
                // se ve en cada paso (btnPropExist los cicla)
                string stem = ct.isActualCut ? "CutActual"
                    : ct.isActualFill ? "CutFillProposed" : "CutFillActual";
                return new string[][]
                {
                    sidePopupGradeRoot[0],
                    new[] { stem, "CutState", "Cambiar vista: Actual -> Cut/Fill -> Proposed" }
                };
            }
            if (phase == "altitude")
            {
                return new string[][] { sidePopupGradeRoot[1], sidePopupAltitude[0], sidePopupAltitude[1] };
            }
            return sidePopupGradeRoot;
        }

        private void ClosePopup()
        {
            if (sideAutoClose != null) sideAutoClose.Stop();
            if (sidePopupPanel != null) sidePopupPanel.Visible = false;
            sidePopupOwner = null;
            sideSurveyNav = null;
            sideGradeNav = null;
        }

        private void PositionPopup()
        {
            if (sidePopupPanel == null || sideBarPanel == null || statusStrip1 == null) return;
            Control ownerBtn = sidePopupOwner == null ? null : FindSide(sidePopupOwner);
            int top = sideBarPanel.Top + (ownerBtn != null ? ownerBtn.Top : 8);
            int maxBottom = statusStrip1.Top - 8;
            if (top + sidePopupPanel.Height > maxBottom) top = maxBottom - sidePopupPanel.Height;
            if (top < sideBarPanel.Top) top = sideBarPanel.Top;
            sidePopupPanel.Location = new Point(88, top);
        }

        // desactiva la grabación survey (lado del límite o puntos) y deja la
        // máquina en Off; los puntos ya grabados se conservan hasta cerrar el trabajo
        private void StopSurveyRecording()
        {
            manualBtnState = btnStates.Off;
            ct.isSurveyOn = false;
            ct.recBoundary = false;
            ct.recSurveyPt = false;
            ct.isBtnStartPause = false;
            ct.markBM = false;
            sideSurveyNav = null;
            try
            {
                btnStartPause.Visible = false;
                btnBoundarySide.Visible = false;
                btnBuildFromElevation.Visible = false;
            }
            catch (Exception) { /* cosmético: los botones flotantes originales */ }
        }

        // ---- transporte survey flotante (arriba al centro de la ventana 3D) ----

        private void ShowTransport()
        {
            if (sideTransportPanel == null) return;
            PositionTransport();
            sideTransportPanel.Visible = true;
            sideTransportPanel.BringToFront();
        }

        private void HideTransport()
        {
            if (sideTransportPanel != null) sideTransportPanel.Visible = false;
        }

        // (it13.2) el zoom vive sobre el 3D como en AgOpenGPS: "+" arriba a la
        // derecha y "−" notablemente más abajo, separados para no confundirlos
        private void PositionZoom()
        {
            if (sideBtnZoomIn == null || sideBtnZoomOut == null || openGLControl == null) return;
            int x = openGLControl.Right - sideBtnZoomIn.Width - 12;
            sideBtnZoomIn.Location = new Point(x, openGLControl.Top + 10);
            sideBtnZoomOut.Location = new Point(x, openGLControl.Top + 260);
        }

        // (it13.2) botón de zoom estilo AgOpenGPS: clásico, sin panel contenedor
        private Button MakeZoomButton(string name, string action, string stem, string tooltip)
        {
            var zb = new Button
            {
                Name = name,
                Tag = action,
                Size = new Size(52, 52),
                Image = SideIconRaw(stem),
                ImageAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand
            };
            zb.Click += SideBar_Click;
            sideBarTip.SetToolTip(zb, tooltip);
            return zb;
        }

        // (iteración 13) autocrerre del popup de ScreenConfig
        private void SideAutoClose_Tick(object sender, EventArgs e)
        {
            ClosePopup();
        }

        private void PositionTransport()
        {
            if (sideTransportPanel == null || openGLControl == null) return;
            int x = openGLControl.Left + (openGLControl.Width - sideTransportPanel.Width) / 2;
            int y = openGLControl.Top + 10;
            sideTransportPanel.Location = new Point(
                Math.Max(openGLControl.Left, x), Math.Max(openGLControl.Top, y));
        }

        private void SideTransport_Click(object sender, EventArgs e)
        {
            string action = (string)((Control)sender).Tag;
            try
            {
                if (action == "Stop")
                {
                    JobClose();
                    sideSurveyNav = null;
                }
                else
                {
                    btnStartPause_Click(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("La acción '" + action + "' del transporte survey falló:\r\n\r\n"
                    + ex.Message, "OpenGrade3D - barra lateral",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateSideBarState();
        }

        private void SideBar_Click(object sender, EventArgs e)
        {
            string action = (string)((Control)sender).Tag;

            // las acciones son las mismas que ejecutan los botones originales; si
            // una falla se informa y la app sigue, sin tumbarla desde la barra
            try
            {
                switch (action)
                {
                    // ---- barra fija ----
                    case "Survey_mode":
                        if (!ct.surveyMode) SelectMode();
                        TogglePopup("Survey_mode");
                        break;
                    case "Grade_mode":
                        if (ct.surveyMode) SelectMode();
                        HideTransport();
                        TogglePopup("Grade_mode");
                        break;
                    case "Field":
                        // (iteración 13) un paso más antes del diálogo: carpeta o Google Earth
                        TogglePopup("Field");
                        break;
                    case "FileOpen":
                        // el Start del programa antiguo: crear campo nuevo, continuar
                        // o cargar AGD (diálogo FormJob) — la "pestaña" de siempre
                        JobNewOpenResume();
                        ClosePopup();
                        break;
                    case "Folder":
                        // (it13.5) ex btnFileExplorer del panel derecho: abre el
                        // explorador en el directorio de campos (el de la labor
                        // en curso si la hay; si no, la raíz de Fields)
                        try
                        {
                            string sideDir = fieldsDirectory + currentFieldDirectory;
                            if (!System.IO.Directory.Exists(sideDir)) sideDir = fieldsDirectory;
                            System.Diagnostics.Process.Start(sideDir);
                        }
                        catch { }
                        ClosePopup();
                        break;
                    case "GoogleEarth":
                        // abre la app de Google Earth con el campo (el item de menú,
                        // que avisa si no hay campo abierto)
                        googleEarthToolStripMenuItem_Click(this, EventArgs.Empty);
                        ClosePopup();
                        break;
                    case "Config":
                        // el Config del programa antiguo, directo
                        btnSettings_Click_1(this, EventArgs.Empty);
                        ClosePopup();
                        break;
                    case "AgIO":
                        // abre AgIO o trae su ventana al frente (no toca el popup)
                        LaunchOrFocusAgIO();
                        break;
                    case "ScreenConfig":
                        // (iteración 13) popup de pantalla: 2D/3D e inclinación,
                        // con autocrerre si no se opera
                        TogglePopup("ScreenConfig");
                        break;
                    case "PerfilPanel":
                        HideTabControl();
                        ClosePopup();
                        if (sideTransportPanel != null && sideTransportPanel.Visible)
                            PositionTransport();
                        break;

                    // ---- popup survey ----
                    case "Benchmark":
                        // solo pone la marca: no oculta nada ni cambia el popup
                        ct.isSurveyOn = true;
                        ct.markBM = true;
                        break;
                    case "Boundary":
                        // entra/sale del submenú del límite (dos opciones más)
                        sideSurveyNav = sideSurveyNav == "boundary" ? null : "boundary";
                        RenderPopup();
                        break;
                    case "Boundary_Left":
                    case "Boundary_Right":
                    {
                        bool right = action == "Boundary_Right";
                        if (manualBtnState == btnStates.RecBnd && ct.isBoundarySideRight == right)
                        {
                            // pulsar el lado activo de nuevo: desactivar y volver a
                            // verse los dos iconos del límite
                            StopSurveyRecording();
                        }
                        else
                        {
                            // elegir lado y armar la grabación del límite; aparecen
                            // Start/Stop/Pause y el otro lado desaparece
                            ct.isBoundarySideRight = right;
                            recordSurveyBoundary();
                            ct.recBoundary = true;
                            btnStartPause.Visible = false;
                            btnBoundarySide.Visible = false;
                            sideSurveyNav = "boundary";
                        }
                        RenderPopup();
                        break;
                    }
                    case "Survey":
                        if (manualBtnState == btnStates.Rec)
                        {
                            // pulsar de nuevo desactiva la grabación de puntos
                            StopSurveyRecording();
                        }
                        else
                        {
                            // grabación de puntos survey: aparecen Start/Stop/Pause
                            // y desaparecen las otras opciones del popup
                            recordSurveyPts();
                            btnStartPause.Visible = false;
                            btnBoundarySide.Visible = false;
                        }
                        RenderPopup();
                        break;

                    // ---- popup grade ----
                    case "CutFill":
                        // pulsar de nuevo deselecciona: volver a Proposed y al raíz
                        if (sideGradeNav == "cutfill")
                        {
                            ct.isActualCut = false;
                            ct.isActualFill = false;
                            ct.needsRebuildTerrainBuffers = true;
                            sideGradeNav = null;
                        }
                        else
                        {
                            sideGradeNav = "cutfill";
                        }
                        RenderPopup();
                        break;
                    case "CutState":
                        // ciclo de superficie: Actual -> Cut/Fill -> Proposed
                        btnPropExist_Click(this, EventArgs.Empty);
                        RenderPopup();
                        break;
                    case "Altitude":
                        // pulsar de nuevo deselecciona la vista de elevación
                        if (sideGradeNav == "altitude")
                        {
                            ct.isElevation = false;
                            ct.needsRebuildTerrainBuffers = true;
                            sideGradeNav = null;
                        }
                        else
                        {
                            sideGradeNav = "altitude";
                        }
                        RenderPopup();
                        break;
                    case "ElevationExisting":
                    case "ElevationProposed":
                    {
                        bool existing = action == "ElevationExisting";
                        if (ct.isElevation && ct.isExistingElevation == existing)
                        {
                            // la vista activa se pulsa de nuevo: deseleccionar
                            ct.isElevation = false;
                            ct.needsRebuildTerrainBuffers = true;
                        }
                        else
                        {
                            ct.isElevation = true;
                            ct.isExistingElevation = existing;
                            ct.needsRebuildTerrainBuffers = true;
                        }
                        RenderPopup();
                        break;
                    }

                    // ---- popup ScreenConfig (iteración 13): vista 2D/3D ----
                    // "2D" es un preset de cámara cenital (camPitch al tope -80):
                    // no existe renderizador 2D en OG3D; al volver al 3D se recupera
                    // el pitch que se tenía. Pulsar 2DView de nuevo = volver a 3D.
                    case "2DView":
                        if (!ct.isCam2D)
                        {
                            sidePitchBefore2D = camera.camPitch;
                            ct.isCam2D = true;
                            camera.camPitch = -80;
                        }
                        else
                        {
                            ct.isCam2D = false;
                            camera.camPitch = sidePitchBefore2D;
                        }
                        ct.needsRebuildTerrainBuffers = true;
                        RenderPopup();
                        break;
                    case "3DView":
                        if (ct.isCam2D)
                        {
                            ct.isCam2D = false;
                            camera.camPitch = sidePitchBefore2D;
                            ct.needsRebuildTerrainBuffers = true;
                        }
                        RenderPopup();
                        break;

                    // ---- acciones de cámara: zoom e inclinación ----
                    // cada pulsación aplica un paso y el popup sigue abierto, como
                    // los botones originales de lupa y flecha (mismos handlers, que
                    // no usan el argumento del ratón, de ahi el null); una inclinación
                    // manual saca de la vista 2D
                    case "ZoomIn":
                        // (it13.2) los handlers originales llevan los nombres
                        // cruzados: quien acercaba de verdad era btnZoomOut
                        // (camSetDistance = zoomValue^2 * -1: más valor, más lejos);
                        // el "+" del icono debe ACERCAR
                        btnZoomOut_MouseDown(this, null);
                        break;
                    case "ZoomOut":
                        btnZoomIn_MouseDown(this, null);
                        break;
                    case "TiltUp":
                        ct.isCam2D = false;
                        btnTiltUp_MouseDown(this, null);
                        break;
                    case "TiltDown":
                        ct.isCam2D = false;
                        btnTiltDown_MouseDown(this, null);
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("La acción '" + action + "' de la barra lateral falló:\r\n\r\n"
                    + ex.Message, "OpenGrade3D - barra lateral",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // (iteración 13) el popup de ScreenConfig se cierra solo: cada pulsación
            // en la barra reinicia el temporizador; si el popup abierto no es ese,
            // el temporizador queda detenido
            if (sideAutoClose != null)
            {
                sideAutoClose.Stop();
                if (sidePopupOwner == "ScreenConfig" && sidePopupPanel != null
                    && sidePopupPanel.Visible)
                {
                    sideAutoClose.Start();
                }
            }

            UpdateSideBarState();
        }

        // refleja el estado real de ct en la barra, el popup y el transporte
        public void UpdateSideBarState()
        {
            if (sideBarPanel == null || ct == null) return;

            // el resaltado de estado es cosmético: si algo falla se pierde el
            // resaltado de esa pasada, pero no puede romper nada
            try
            {
                UpdateSideBarStateCore();
            }
            catch (Exception)
            {
                // sin resaltado en esta pasada
            }
        }

        private void UpdateSideBarStateCore()
        {
            bool survey = ct.surveyMode;
            SetSideState("Survey_mode", survey);
            SetSideState("Grade_mode", !survey);
            SetSideState("PerfilPanel", ct.isOpenGLControlBackVisible);

            // popup survey: resalta la grabación activa (límite o puntos)
            bool bndRec = manualBtnState == btnStates.RecBnd;
            bool ptsRec = manualBtnState == btnStates.Rec;
            SetSideState("Survey", ptsRec);
            SetSideState("Boundary_Left", bndRec && !ct.isBoundarySideRight);
            SetSideState("Boundary_Right", bndRec && ct.isBoundarySideRight);

            // popup grade: resalta la vista de superficie y de elevación activa
            SetSideState("CutState", ct.isActualCut || ct.isActualFill);
            SetSideState("ElevationExisting", ct.isElevation && ct.isExistingElevation);
            SetSideState("ElevationProposed", ct.isElevation && !ct.isExistingElevation);

            // popup ScreenConfig: resalta la vista 2D/3D activa (iteración 13)
            SetSideState("2DView", ct.isCam2D);
            SetSideState("3DView", !ct.isCam2D);

            // el transporte solo aparece con el survey iniciado: máquina en grabación
            // de límite o de puntos; desaparece al desactivar, con Stop o en Grade
            if (survey && (bndRec || ptsRec)) ShowTransport(); else HideTransport();

            UpdateTransportState();
        }

        private void UpdateTransportState()
        {
            if (sideTransportPanel == null || ct == null) return;
            SetTrState("tr_Start", !ct.isBtnStartPause);
            SetTrState("tr_Pause", ct.isBtnStartPause);
        }

        private void SetTrState(string name, bool active)
        {
            Control c = sideTransportPanel.Controls[name];
            if (c != null)
            {
                // (it13.4) mismo resalte que la barra: azul de selección + borde
                Button b = c as Button;
                if (b != null)
                {
                    b.FlatAppearance.BorderSize = active ? 2 : 0;
                    b.FlatAppearance.BorderColor = UiTheme.LinkC;
                }
                c.BackColor = active ? UiTheme.Selected : UiTheme.SurfaceHi;
            }
        }

        private void SetSideState(string icon, bool active)
        {
            Control c = FindSide(icon);
            if (c == null) return;
            // (it13.4) el activo destaca de verdad: relleno azul + borde claro.
            // El gris Hover sobre SurfaceHi era invisible con el sol de cabina.
            Button b = c as Button;
            if (b != null)
            {
                b.FlatAppearance.BorderSize = active ? 2 : 0;
                b.FlatAppearance.BorderColor = UiTheme.LinkC;
            }
            c.BackColor = active ? UiTheme.Selected : UiTheme.SurfaceHi;
        }

        // ------------------------------------------------------------------
        // AgIO (iteración 10): abrir o traer al frente la ventana de AgIO.
        // Mismo criterio que el arranque automático (Program.TryStartAgIO):
        // si ya hay un AgIO en marcha no se lanza otro, se restaura y se trae
        // al frente con user32. Si no hay ninguno, se lanza desde la carpeta
        // Application (donde el proyecto AgIO deja su exe al compilar).
        // ------------------------------------------------------------------
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

        private void LaunchOrFocusAgIO()
        {
            try
            {
                System.Diagnostics.Process[] agio =
                    System.Diagnostics.Process.GetProcessesByName("AgIO");
                if (agio == null || agio.Length == 0)
                {
                    // sin AgIO en marcha: misma rutina del arranque automático
                    Program.TryStartAgIO();
                    return;
                }

                // con AgIO en marcha: restaurar y traer al frente
                System.Diagnostics.Process p = agio[0];
                p.Refresh(); // por si el handle principal aún no estaba resuelto
                IntPtr h = p.MainWindowHandle;
                if (h != IntPtr.Zero)
                {
                    ShowWindow(h, SW_RESTORE);
                    SetForegroundWindow(h);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir AgIO:\r\n\r\n" + ex.Message,
                    "OpenGrade3D - barra lateral",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private Control FindSide(string icon)
        {
            Control c = sideBarPanel.Controls["side_" + icon];
            if (c == null && sidePopupPanel != null)
                c = sidePopupPanel.Controls["side_" + icon];
            return c;
        }

        // carga el PNG embebido (OpenGrade.OG3DImages.<stem>.png): con box se escala
        // al cuadro pedido (64 para los iconos de la barra) y sin box se entrega
        // el tamaño nativo (badges de transporte de 120x50)
        private Image SideIcon(string stem)
        {
            return LoadSideImage(stem, 64);
        }

        private Image SideIconRaw(string stem)
        {
            return LoadSideImage(stem, null);
        }

        internal Image LoadSideImage(string stem, int? box)
        {
            Image img;
            if (sideIcons.TryGetValue(stem, out img)) return img;

            // un icono que falle (recurso ausente, PNG ilegible...) nunca debe
            // romper el arranque ni un click: el botón queda sin imagen
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream("OpenGrade.OG3DImages." + stem + ".png"))
                {
                    if (stream == null) return null;
                    using (var src = Image.FromStream(stream))
                    {
                        img = box.HasValue ? (Image)ScaleIcon(src, box.Value) : (Image)new Bitmap(src);
                    }
                }
                sideIcons[stem] = img;
            }
            catch (Exception)
            {
                return null;
            }
            return img;
        }

        private Bitmap ScaleIcon(Image src, int box)
        {
            int w, h;
            if (src.Width >= src.Height)
            {
                w = box;
                h = Math.Max(1, src.Height * box / src.Width);
            }
            else
            {
                h = box;
                w = Math.Max(1, src.Width * box / src.Height);
            }

            var bmp = new Bitmap(box, box);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, (box - w) / 2, (box - h) / 2, w, h);
            }
            return bmp;
        }
    }
}
