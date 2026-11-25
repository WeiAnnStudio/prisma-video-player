// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("Prisma")]
[assembly: AssemblyDescription("Reproductor de vídeo para Windows")]
[assembly: AssemblyVersion("0.5.0.0")]

namespace Prisma
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        private static int Main(string[] args)
        {
            Hardening.Apply();
            string data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos");
            // Prisma 0.4 could be added to Claude or ChatGPT desktop, which start "Prisma.exe --mcp".
            // That connection no longer exists: answer nothing and quit instead of opening a window.
            if (args.Contains("--mcp")) { Console.Error.WriteLine("Prisma ya no ofrece conexión con Claude ni ChatGPT. Quita «prisma» de la configuración de esa app."); return 1; }

            bool testSession = args.Contains("--test-session");
            string[] files = args.Where(a => a != "--test-session").ToArray();
            SingleInstance instance = testSession ? null : new SingleInstance();
            if (instance != null && !instance.IsFirst)
            {
                bool delivered = instance.Send(files);
                instance.Dispose();
                if (delivered) return 0;
                instance = null; // The open window did not answer: open this one instead.
            }

            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show("No se pudo completar la acción.\r\n\r\n" + e.Exception.Message, "Prisma", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            string settings = Path.Combine(testSession ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos-prueba") : data, "preferencias.json");
            PlayerForm form = new PlayerForm(files, settings);
            if (instance != null)
            {
                instance.FilesReceived += form.OpenFromAnotherLaunch;
                instance.Listen();
            }
            Application.Run(form);
            if (instance != null) instance.Dispose();
            return 0;
        }
    }
}
