// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Prisma
{
    internal enum ResolveEdition { NotInstalled, Free, Studio, Unknown }

    internal sealed class ResolveInstall
    {
        public ResolveEdition Edition = ResolveEdition.NotInstalled;
        public string Name = "", Folder = "", Fuscript = "";
    }

    // Sends review notes to the timeline open in DaVinci Resolve Studio. Prisma runs Resolve's
    // own fuscript.exe from its installation folder with a fixed Lua script; the notes travel in
    // a data file, so their text is never executed. Nothing is sent over the network.
    internal static class ResolveBridge
    {
        private static readonly Regex ProductName = new Regex(@"^DaVinci Resolve( Studio)?( [\d.]+.*)?$", RegexOptions.IgnoreCase);

        public static ResolveInstall Detect()
        {
            ResolveInstall install = new ResolveInstall();
            try
            {
                using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (uninstall != null)
                        foreach (string id in uninstall.GetSubKeyNames())
                            using (RegistryKey app = uninstall.OpenSubKey(id))
                            {
                                string name = app == null ? null : app.GetValue("DisplayName") as string;
                                if (name == null || !ProductName.IsMatch(name.Trim())) continue;
                                string folder = app.GetValue("InstallLocation") as string ?? "";
                                // Both editions register as "DaVinci Resolve": this only locates the install.
                                if (install.Name.Length == 0) { install.Name = name.Trim(); install.Folder = folder.Trim().Trim('"'); }
                            }
                }
            }
            catch { }
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!IsLocalFolder(install.Folder) || !File.Exists(Path.Combine(install.Folder, "fuscript.exe")))
                install.Folder = Path.Combine(programFiles, "Blackmagic Design", "DaVinci Resolve");
            string fuscript = Path.Combine(install.Folder, "fuscript.exe");
            if (!File.Exists(fuscript)) { install.Edition = ResolveEdition.NotInstalled; return install; }
            install.Fuscript = fuscript;
            install.Edition = ResolveEdition.Unknown;
            {
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(Path.Combine(install.Folder, "Resolve.exe"));
                    string product = (info.ProductName ?? "") + " " + (info.FileDescription ?? "");
                    if (product.IndexOf("Studio", StringComparison.OrdinalIgnoreCase) >= 0) install.Edition = ResolveEdition.Studio;
                }
                catch { }
            }
            return install;
        }

        private static bool IsLocalFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && Path.IsPathRooted(folder) && !folder.StartsWith(@"\\", StringComparison.Ordinal);
        }

        public static bool IsRunning()
        {
            Process[] found = Process.GetProcessesByName("Resolve");
            foreach (Process p in found) p.Dispose();
            return found.Length > 0;
        }

        internal static string Escape(string text)
        {
            return (text ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        public static void WriteMarkers(ReviewDocument review, string path)
        {
            StringBuilder data = new StringBuilder("PRISMA-MARKERS 1\n");
            data.Append("fps\t").Append(review.FpsNumerator).Append('\t').Append(review.FpsDenominator).Append('\n');
            data.Append("video\t").Append(Escape(Path.GetFileName(review.MediaPath))).Append('\n');
            foreach (ReviewMarker m in review.Markers)
                data.Append("m\t").Append(m.Frame).Append('\t').Append(Escape(m.Id)).Append('\t').Append(Escape(m.Name)).Append('\t').Append(Escape(m.Note)).Append('\n');
            File.WriteAllText(path, data.ToString(), new UTF8Encoding(false));
        }

        public static string ScriptSource()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Prisma.prisma_resolve.lua"))
            {
                if (stream == null) throw new InvalidOperationException("Falta el script de Resolve dentro de Prisma.");
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
            }
        }

        // Writes the script and the notes into workFolder. Runs on the UI thread.
        public static void Prepare(ReviewDocument review, string workFolder)
        {
            Directory.CreateDirectory(workFolder);
            File.WriteAllText(Path.Combine(workFolder, "prisma_resolve.lua"), ScriptSource(), new UTF8Encoding(false));
            WriteMarkers(review, Path.Combine(workFolder, "notas.txt"));
            string result = Path.Combine(workFolder, "resultado.txt");
            if (File.Exists(result)) File.Delete(result);
        }

        // Runs fuscript and returns the result fields (status, added, present, ...). Safe on a worker thread.
        public static Dictionary<string, string> Run(ResolveInstall install, string workFolder, int timeoutMs)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
            string resultPath = Path.Combine(workFolder, "resultado.txt");
            ProcessStartInfo start = new ProcessStartInfo(install.Fuscript, "\"" + Path.Combine(workFolder, "prisma_resolve.lua") + "\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = workFolder,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["PRISMA_MARKERS"] = Path.Combine(workFolder, "notas.txt");
            start.EnvironmentVariables["PRISMA_RESULT"] = resultPath;
            try
            {
                using (Process process = Process.Start(start))
                {
                    process.OutputDataReceived += delegate { }; process.ErrorDataReceived += delegate { };
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if (!process.WaitForExit(timeoutMs))
                    {
                        try { process.Kill(); } catch { }
                        result["status"] = "TIMEOUT"; return result;
                    }
                }
            }
            catch (Exception ex) { result["status"] = "ERROR"; result["message"] = ex.Message; return result; }
            if (!File.Exists(resultPath)) { result["status"] = "NO_OUTPUT"; return result; }
            foreach (string line in File.ReadAllLines(resultPath, Encoding.UTF8))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) result[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
            if (!result.ContainsKey("status")) result["status"] = "NO_OUTPUT";
            return result;
        }
    }
}
