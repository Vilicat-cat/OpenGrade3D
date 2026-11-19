using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AgIO
{
    /// <summary>
    /// Parche integracion OG3D (iteracion 11): tema oscuro de cabina para AgIO,
    /// el mismo pintor que usa OpenGrade3D (GPS/UiTheme.cs).
    ///
    /// El script de integracion inyecta UiTheme.Apply(this) al final del
    /// InitializeComponent() de cada Designer de AgIO, asi que todo formulario
    /// se viste antes de verse. Reglas basicas:
    ///  - Nunca toca la logica: solo propiedades visuales (colores y planos; nunca
    ///    tipografias ni tamanos, misma regla que la iteracion 9c del GPS).
    ///  - Los colores funcionales que el codigo asigna en tiempo de ejecucion
    ///    (alarmas rojas, LEDs verdes...) se aplican DESPUES de este tema, asi que
    ///    siguen ganando.
    ///  - Todo el metodo va en try/catch: el tema no puede impedir el arranque.
    /// </summary>
    internal static class UiTheme
    {
        // ---- Paleta cabina (identica a la de OpenGrade3D) ----------------------
        public static readonly Color Window    = Color.FromArgb(30, 30, 30);   // fondo #1E1E1E
        public static readonly Color Surface   = Color.FromArgb(37, 37, 38);   // campos #252526
        public static readonly Color SurfaceHi = Color.FromArgb(51, 51, 52);   // botones #333334
        public static readonly Color Hover     = Color.FromArgb(72, 72, 76);
        public static readonly Color Pressed   = Color.FromArgb(88, 88, 94);
        public static readonly Color Border    = Color.FromArgb(67, 67, 70);
        public static readonly Color Field     = Color.FromArgb(22, 22, 22);   // cajas de datos
        public static readonly Color Text      = Color.FromArgb(235, 235, 235);
        public static readonly Color TextDim   = Color.FromArgb(165, 165, 165);
        public static readonly Color LinkC     = Color.FromArgb(120, 180, 255);

        // Fondos "decorativos" de los Designer que se pueden reemplazar por el tema
        // (los demas -Red, Lime, Gold...- son chips funcionales y se conservan).
        private static readonly HashSet<string> NeutralBacks = new HashSet<string>
        {
            "control", "controllight", "controllightlight", "controllightdark", "controldark",
            "controldarkdark", "controltext", "window", "windowframe", "windowtext", "aliceblue",
            "lavender", "scrollbar", "buttonface", "buttonshadow", "inactiveborder", "inactivecaption",
            "menubar", "appworkspace", "transparent", "white", "whitesmoke", "gainsboro",
            "lightgray", "gray", "darkgray", "gradientactivecaption", "gradientinactivecaption",
            "", "0",
        };

        private static readonly HashSet<Control> Done = new HashSet<Control>();

        // AgIO no tiene la columna especial de botones con color de diseno del GPS
        // (leyenda de la cuchilla, escala...); la mecanica se conserva vacia por si
        // algun boton de AgIO necesita quedarse con su color de fabrica.
        private static readonly HashSet<string> TamanoDiseno = new HashSet<string>();

        /// <summary>Tabla de color oscura compartida para menus/toolbars/status.</summary>
        private sealed class DarkColorTable : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => SurfaceHi;
            public override Color ImageMarginGradientBegin => SurfaceHi;
            public override Color ImageMarginGradientMiddle => SurfaceHi;
            public override Color ImageMarginGradientEnd => SurfaceHi;
            public override Color MenuBorder => Border;
            public override Color MenuItemBorder => Border;
            public override Color MenuItemSelected => Hover;
            public override Color MenuItemSelectedGradientBegin => Hover;
            public override Color MenuItemSelectedGradientEnd => Pressed;
            public override Color MenuItemPressedGradientBegin => Surface;
            public override Color MenuItemPressedGradientEnd => SurfaceHi;
            public override Color ToolStripGradientBegin => Surface;
            public override Color ToolStripGradientMiddle => Surface;
            public override Color ToolStripGradientEnd => Surface;
            public override Color ToolStripBorder => Surface;
            public override Color StatusStripGradientBegin => SurfaceHi;
            public override Color StatusStripGradientEnd => SurfaceHi;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Border;
            public override Color ButtonSelectedHighlight => Hover;
            public override Color ButtonSelectedGradientBegin => Hover;
            public override Color ButtonSelectedGradientEnd => Hover;
            public override Color ButtonPressedHighlight => Pressed;
            public override Color ButtonPressedGradientBegin => Pressed;
            public override Color ButtonPressedGradientEnd => Pressed;
            public override Color ButtonSelectedBorder => Border;
            public override Color CheckBackground => Hover;
            public override Color CheckSelectedBackground => Pressed;
        }

        private static readonly ToolStripProfessionalRenderer Renderer =
            new ToolStripProfessionalRenderer(new DarkColorTable());

        /// <summary>
        /// Punto de entrada: tema el formulario completo. Se llama al final de cada
        /// InitializeComponent(); se puede llamar tambien tras cambios dinamicos grandes.
        /// </summary>
        public static void Apply(Control root)
        {
            try
            {
                PurgeDisposed();
                Walk(root, true);
            }
            catch { /* el tema nunca debe romper la aplicacion */ }
        }

        private static void PurgeDisposed()
        {
            if (Done.Count == 0) return;
            List<Control> dead = null;
            foreach (Control c in Done)
            {
                if (c.IsDisposed)
                {
                    if (dead == null) dead = new List<Control>();
                    dead.Add(c);
                }
            }
            if (dead != null) foreach (Control c in dead) Done.Remove(c);
        }

        // El tema NO toca tipografias ni tamanos (misma regla que en el GPS): cambiar
        // la fuente de un formulario con AutoScaleMode.Font re-escala sus controles y
        // descuadra los layouts anidados. El tema pinta colores; la geometria y la
        // tipografia son del Designer.

        private static bool IsNeutralBack(Color c)
        {
            return NeutralBacks.Contains(c.Name.ToLowerInvariant());
        }

        private static void Walk(Control c, bool isRoot = false)
        {
            if (c == null || Done.Contains(c)) return;
            Done.Add(c);

            string fullName = c.GetType().FullName ?? "";
            // Lienzos que dibujan su propio contenido: no tocar.
            if (fullName == "SharpGL.OpenGLControl" || fullName.Contains("WebCameraControl"))
                return;

            Form form = c as Form;
            if (form != null && isRoot)
            {
                form.BackColor = Window;
                form.ForeColor = Text;
            }

            TryTheme(c);

            foreach (Control child in c.Controls)
                Walk(child);
        }

        private static void TryTheme(Control c)
        {
            // ---- Botones y similares (CheckBox/RadioButton heredan de ButtonBase) ----
            ButtonBase btn = c as ButtonBase;
            if (btn != null)
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Border;
                btn.FlatAppearance.BorderSize = 1;
                btn.FlatAppearance.MouseOverBackColor = Hover;
                btn.FlatAppearance.MouseDownBackColor = Pressed;
                bool textOnly = btn.BackgroundImage == null && btn.Image == null;
                bool tamanoDiseno = TamanoDiseno.Contains(c.Name ?? "");
                btn.ForeColor = Text;
                if (textOnly && !tamanoDiseno)
                {
                    // Botones de texto toman el color de panel; los que llevan imagen
                    // conservan su fondo (sus PNG ya estan pensados para el tema).
                    btn.BackColor = SurfaceHi;
                }
                return;
            }

            // ---- Enlaces (LinkLabel hereda de Label: se comprueba primero) ----
            LinkLabel link = c as LinkLabel;
            if (link != null)
            {
                link.LinkColor = LinkC;
                link.ActiveLinkColor = Color.FromArgb(160, 210, 255);
                link.VisitedLinkColor = TextDim;
                link.ForeColor = Text;
                return;
            }

            // ---- Etiquetas ----
            Label lbl = c as Label;
            if (lbl != null)
            {
                if (IsNeutralBack(lbl.BackColor))
                {
                    lbl.BackColor = Color.Transparent;
                    Color fc = lbl.ForeColor;
                    double lumF = 0.299 * fc.R + 0.587 * fc.G + 0.114 * fc.B;
                    if (lumF < 110 || fc == SystemColors.ControlText || fc == SystemColors.WindowText)
                        lbl.ForeColor = Text;
                }
                else
                {
                    // Chip funcional (Red/Lime/Gold...): se conserva el fondo, contraste del texto.
                    Color b = lbl.BackColor;
                    double lum = 0.299 * b.R + 0.587 * b.G + 0.114 * b.B;
                    lbl.ForeColor = lum > 140 ? Color.FromArgb(20, 20, 20) : Text;
                }
                return;
            }

            // ---- Cajas de texto ----
            TextBoxBase tb = c as TextBoxBase;
            if (tb != null)
            {
                tb.BackColor = Field;
                tb.ForeColor = tb.ReadOnly ? TextDim : Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ComboBox cb = c as ComboBox;
            if (cb != null)
            {
                cb.FlatStyle = FlatStyle.Flat;
                cb.BackColor = Surface;
                cb.ForeColor = Text;
                return;
            }

            // ---- Up/Down y listas ----
            NumericUpDown nud = c as NumericUpDown;
            if (nud != null)
            {
                nud.BackColor = Field;
                nud.ForeColor = Text;
                nud.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            DomainUpDown dud = c as DomainUpDown;
            if (dud != null)
            {
                dud.BackColor = Field;
                dud.ForeColor = Text;
                dud.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ListBox lb = c as ListBox;
            if (lb != null)
            {
                lb.BackColor = Field;
                lb.ForeColor = Text;
                lb.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ListView lv = c as ListView;
            if (lv != null)
            {
                lv.BackColor = Field;
                lv.ForeColor = Text;
                lv.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            TreeView tv = c as TreeView;
            if (tv != null)
            {
                tv.BackColor = Field;
                tv.ForeColor = Text;
                tv.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            DataGridView grid = c as DataGridView;
            if (grid != null)
            {
                grid.BackgroundColor = Window;
                grid.GridColor = Border;
                grid.BorderStyle = BorderStyle.FixedSingle;
                grid.EnableHeadersVisualStyles = false;
                DataGridViewCellStyle baseStyle = new DataGridViewCellStyle
                {
                    BackColor = Surface,
                    ForeColor = Text,
                    SelectionBackColor = Pressed,
                    SelectionForeColor = Text,
                };
                grid.DefaultCellStyle = baseStyle;
                grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceHi;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                grid.RowHeadersDefaultCellStyle.BackColor = SurfaceHi;
                grid.RowHeadersDefaultCellStyle.ForeColor = Text;
                return;
            }

            DateTimePicker dtp = c as DateTimePicker;
            if (dtp != null)
            {
                dtp.CalendarMonthBackground = Surface;
                dtp.CalendarForeColor = Text;
                dtp.ForeColor = Text;
                return;
            }

            // ---- Menus, toolbars y status ----
            ToolStrip strip = c as ToolStrip;
            if (strip != null)
            {
                strip.Renderer = Renderer;
                strip.BackColor = strip is StatusStrip ? SurfaceHi : Surface;
                strip.ForeColor = Text;
                ThemeStripItems(strip.Items);
                return;
            }

            // ---- Contenedores y resto ----
            GroupBox gb = c as GroupBox;
            if (gb != null)
            {
                gb.BackColor = Window;
                gb.ForeColor = Text;
                return;
            }

            TabPage page = c as TabPage;
            if (page != null)
            {
                page.BackColor = Window;
                page.ForeColor = Text;
                return;
            }

            if (IsNeutralBack(c.BackColor) || c.BackColor == SystemColors.Control)
            {
                c.BackColor = Window;
            }
            if (c.ForeColor == SystemColors.ControlText || c.ForeColor == SystemColors.WindowText)
            {
                c.ForeColor = Text;
            }
        }

        private static void ThemeStripItems(ToolStripItemCollection items)
        {
            if (items == null) return;
            foreach (ToolStripItem it in items)
            {
                try
                {
                    it.ForeColor = Text;
                    if (it.BackColor == SystemColors.Control || it.BackColor.IsEmpty)
                        it.BackColor = Color.Transparent;
                }
                catch { }
                ToolStripControlHost host = it as ToolStripControlHost;
                if (host != null && host.Control != null)
                    Walk(host.Control);
                ToolStripDropDownItem ddi = it as ToolStripDropDownItem;
                if (ddi != null && ddi.DropDown != null)
                {
                    ThemeStripItems(ddi.DropDown.Items);
                }
            }
        }
    }
}
