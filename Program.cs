using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Avilla_Forensics
{
    static class Program
    {
        /// <summary>
        /// Ponto de entrada principal para o aplicativo.
        /// </summary>
        [STAThread]
        static void Main()
        {
            PrepareWorkingDirectory();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FormSplash());
        }

        // Avilla usa rutas relativas (adb\, bin\, temp\, coletas\...). Si el .exe
        // compilado no esta dentro de la version precompilada, se usa como carpeta de
        // trabajo la indicada en la variable de entorno AVILLA_HOME o
        // C:\Avilla-Forensics-Free si existe. Ademas se crean las carpetas de trabajo
        // que el programa espera encontrar.
        private static void PrepareWorkingDirectory()
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string workDir = exeDir;

            if (!File.Exists(Path.Combine(exeDir, "adb", "adb.exe")))
            {
                string[] candidates =
                {
                    Environment.GetEnvironmentVariable("AVILLA_HOME"),
                    @"C:\Avilla-Forensics-Free",
                };
                foreach (string candidate in candidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                    {
                        workDir = candidate;
                        break;
                    }
                }
            }

            Directory.SetCurrentDirectory(workDir);

            string[] workFolders = { "temp", "coletas", "acquisitions", "print", "find", Path.Combine("find", "jpg"), "down" };
            foreach (string folder in workFolders)
            {
                try { Directory.CreateDirectory(Path.Combine(workDir, folder)); }
                catch { }
            }
        }
    }
}
