// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Reflection;
[assembly: AssemblyTitle("Prisma")]
[assembly: AssemblyDescription("Reproductor de vídeo para Windows")]
[assembly: AssemblyVersion("0.3.1.0")]

namespace Prisma
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
        [STAThread]
        private static void Main(string[] args)
        {
            Hardening.Apply();
            SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e) { MessageBox.Show("No se pudo completar la acción.\r\n\r\n" + e.Exception.Message, "Prisma", MessageBoxButtons.OK, MessageBoxIcon.Error); };
            string settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos", "preferencias.json");
            if (args.Contains("--test-session")) settings = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "datos-prueba", "preferencias.json");
            Application.Run(new PlayerForm(args.Where(a => a != "--test-session").ToArray(), settings));
        }
    }
}
