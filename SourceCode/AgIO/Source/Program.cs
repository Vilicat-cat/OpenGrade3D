using AgLibrary.Logging;
using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AgIO
{
    internal static class Program
    {
        private static Mutex _mutex;

        public static readonly string Version = Assembly.GetEntryAssembly().GetName().Version.ToString(3); // Major.Minor.Patch

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        private static void Main()
        {
            using (_mutex = new Mutex(true, "{8F6F0AC4-B9A1-55fd-A8CF-72F04E6BDE8F}", out bool mutexCreated))
            {
                if (mutexCreated)
                {
                    //load the profile name and set profile directory
                    RegistrySettings.Load();

                    Log.EventWriter("Program Started: " + DateTime.Now.ToString("f", CultureInfo.InvariantCulture));
                    Log.EventWriter("AgIO Version: " + Application.ProductVersion.ToString(CultureInfo.InvariantCulture));

                    Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(RegistrySettings.culture);
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo(RegistrySettings.culture);
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new FormLoop());
                }
                else
                {
                    // Parche integración OG3D: aviso en lugar de salida silenciosa.
                    // Sin esto, lanzar AgIO cuando ya hay otro AgIO en marcha (por
                    // ejemplo el auto-arrancado por OpenGrade3D) "termina con código 0"
                    // sin abrir ventana y parece un fallo al depurar con F5.
                    MessageBox.Show(
                        "Ya hay una instancia de AgIO en ejecución en este equipo.\n\n" +
                        "Ciérrala (o revísala en el Administrador de tareas) y vuelve a lanzar AgIO.\n\n" +
                        "Nota: al depurar con F5, esto se ve como \"AgIO terminó con código 0\" " +
                        "sin abrirse la ventana, porque solo puede haber un AgIO a la vez.",
                        "AgIO ya está en marcha",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
        }

        // If the application needs to be restarted, it should be done via this method.
        // Manually disposing the Mutex before calling Application.Restart() ensures
        // that the Mutex is not still owned by the current application instance while
        // the new application instance is being started.
        // See: https://stackoverflow.com/a/9456822
        public static void Restart()
        {
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
                _mutex = null;
                Application.Restart();
            }
        }
    }
}
