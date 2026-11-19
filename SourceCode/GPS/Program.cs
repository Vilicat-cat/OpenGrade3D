
using System;
using System.Windows.Forms;
using System.Threading;
using Microsoft.Win32;
using OpenGrade.Properties;

namespace OpenGrade
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static readonly Mutex Mutex = new Mutex(true, "{8F6F0AC5-B9A1-55fd-A8CF-72F04E6BDE8F}");
        [STAThread]
        static void Main()
        {
            if (Mutex.WaitOne(TimeSpan.Zero, true))
            {
                // Parche integración OG3D: arrancar el AgIO integrado si no hay ninguno en marcha.
                // OG3D necesita AgIO para todo el tráfico UDP (9999/8888/loopback 17777/15556).
                TryStartAgIO();

                //opening the subkey  
                RegistryKey regKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\OpenGrade");

                //create default keys if not existing
                if (regKey == null)
                {
                    RegistryKey Key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\OpenGrade");

                    //storing the values  
                    Key.SetValue("Language", "en");
                    Key.SetValue("Directory", "Default");
                    Key.Close();

                    Settings.Default.set_culture = "en";
                    Settings.Default.setF_workingDirectory = "Default";
                    Settings.Default.Save();
                }

                else
                {
                    Settings.Default.set_culture = regKey.GetValue("Language").ToString();
                    Settings.Default.setF_workingDirectory = regKey.GetValue("Directory").ToString();
                    Settings.Default.Save();
                    regKey.Close();
                }



                //if (Environment.OSVersion.Version.Major >= 6) SetProcessDPIAware();
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(Properties.Settings.Default.set_culture);
                Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(Properties.Settings.Default.set_culture);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new FormGPS());
            }
            else
            {
                MessageBox.Show("OpenGrade is Already Running");
            }
        }

        /// <summary>
        /// Parche integración OG3D: lanza AgIO.exe si no hay ya un AgIO en marcha
        /// (por ejemplo el lanzado por AgOpenGPS). Busca AgIO.exe primero junto a
        /// OpenGrade3D.exe (carpeta Application, donde también copia su salida el
        /// proyecto AgIO al compilar) y, si no está, en las salidas típicas del
        /// proyecto AgIO. Si no lo encuentra, avisa en lugar de fallar en silencio.
        /// </summary>
        internal static void TryStartAgIO()
        {
            try
            {
                // Ya hay un AgIO en marcha (AgOpenGPS o el nuestro): no lanzar otro.
                if (System.Diagnostics.Process.GetProcessesByName("AgIO").Length > 0) return;

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[]
                {
                    // Despliegue normal: AgIO.exe junto a OpenGrade3D.exe.
                    System.IO.Path.Combine(baseDir, "AgIO.exe"),
                    // Desarrollo: salidas típicas del proyecto AgIO integrado.
                    System.IO.Path.Combine(baseDir, @"..\SourceCode\AgIO\Source\bin\Release\AgIO.exe"),
                    System.IO.Path.Combine(baseDir, @"..\SourceCode\AgIO\Source\bin\Debug\AgIO.exe"),
                };

                foreach (string candidate in candidates)
                {
                    string agioPath = System.IO.Path.GetFullPath(candidate);
                    if (!System.IO.File.Exists(agioPath)) continue;

                    System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = agioPath,
                        // Que AgIO encuentre sus DLL (AgLibrary, AgIOKeypad, System.Memory...)
                        // en su propia carpeta.
                        WorkingDirectory = System.IO.Path.GetDirectoryName(agioPath),
                    };
                    System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi);

                    if (p != null && p.WaitForExit(2000))
                    {
                        // Arrancó pero se cerró al instante: normalmente faltan DLL
                        // al lado de AgIO.exe.
                        MessageBox.Show(
                            "AgIO arrancó pero se cerró inmediatamente.\n" +
                            "Normalmente faltan DLL junto a AgIO.exe (AgLibrary.dll,\n" +
                            "AgIOKeypad.dll, System.Memory.dll, System.Resources.Extensions.dll...).\n\n" +
                            "Compila la solución completa: AgIO.exe y sus DLL se copian solos\n" +
                            "a la carpeta Application, junto a OpenGrade3D.exe.",
                            "AgIO se ha cerrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    return; // encontrado y lanzado: no buscar más
                }

                // No encontrado en ningún sitio: aviso claro en lugar de fallo silencioso.
                MessageBox.Show(
                    "No se ha encontrado AgIO.exe y no hay ningún AgIO en marcha:\n" +
                    "OpenGrade3D funcionará sin datos GPS ni de la cuchilla.\n\n" +
                    "Compila la solución completa (AgIO.exe y sus DLL se copian solos a la\n" +
                    "carpeta Application) o copia a la carpeta de OpenGrade3D.exe el archivo\n" +
                    "AgIO.exe, AgIO.exe.config y todas las DLL de la carpeta Application.",
                    "AgIO no encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch
            {
                // Sin permisos u otro error al lanzar: OpenGrade3D continúa;
                // el usuario siempre puede arrancar AgIO manualmente.
            }
        }

        //[System.Runtime.InteropServices.DllImport("user32.dll")]
        //private static extern bool SetProcessDPIAware();
    }
}
