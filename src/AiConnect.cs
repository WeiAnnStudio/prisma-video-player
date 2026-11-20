// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Prisma
{
    // Registers "Prisma.exe --mcp" as a local MCP server in Claude and ChatGPT desktop.
    // No account or key is involved: the app starts Prisma on this computer when it needs the notes,
    // and the cloud folder is reached with the connector the user already has in that app.
    internal static class AiConnect
    {
        public const string ServerName = "prisma";

        public static List<string> ClaudeConfigFiles()
        {
            List<string> files = new List<string>();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string classic = Path.Combine(roaming, "Claude");
            if (Directory.Exists(classic)) files.Add(Path.Combine(classic, "claude_desktop_config.json"));
            // Store (MSIX) installs keep a virtualised copy of AppData\Roaming.
            string packages = Path.Combine(local, "Packages");
            try
            {
                if (Directory.Exists(packages))
                    foreach (string package in Directory.GetDirectories(packages, "Claude_*"))
                    {
                        string folder = Path.Combine(package, "LocalCache", "Roaming", "Claude");
                        if (Directory.Exists(folder)) files.Add(Path.Combine(folder, "claude_desktop_config.json"));
                    }
            }
            catch { }
            if (files.Count == 0) files.Add(Path.Combine(classic, "claude_desktop_config.json"));
            return files;
        }

        public static string ChatGptConfigFile()
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrWhiteSpace(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            return Path.Combine(home, "config.toml");
        }

        // Adds or replaces mcpServers.prisma and keeps everything else in the file.
        public static string MergeClaudeConfig(string existing, string exePath)
        {
            JavaScriptSerializer json = new JavaScriptSerializer();
            Dictionary<string, object> root = string.IsNullOrWhiteSpace(existing) ? new Dictionary<string, object>() : json.DeserializeObject(existing) as Dictionary<string, object>;
            if (root == null) throw new InvalidDataException("La configuración de Claude no es un objeto JSON.");
            Dictionary<string, object> servers = root.ContainsKey("mcpServers") ? root["mcpServers"] as Dictionary<string, object> : null;
            if (servers == null) { servers = new Dictionary<string, object>(); root["mcpServers"] = servers; }
            servers[ServerName] = new Dictionary<string, object> { { "command", exePath }, { "args", new object[] { "--mcp" } } };
            return Indent(json.Serialize(root));
        }

        // Replaces the [mcp_servers.prisma] table (and its sub-tables) or appends it.
        public static string MergeChatGptConfig(string existing, string exePath)
        {
            List<string> lines = (existing ?? "").Replace("\r\n", "\n").Split('\n').ToList();
            List<string> kept = new List<string>();
            bool skipping = false;
            Regex header = new Regex(@"^\s*\[\[?\s*([^\]]+?)\s*\]\]?\s*(#.*)?$");
            foreach (string line in lines)
            {
                Match m = header.Match(line);
                if (m.Success)
                {
                    string table = m.Groups[1].Value.Replace("\"", "").Replace(" ", "");
                    skipping = table == "mcp_servers." + ServerName || table.StartsWith("mcp_servers." + ServerName + ".", StringComparison.Ordinal);
                }
                if (!skipping) kept.Add(line);
            }
            while (kept.Count > 0 && kept[kept.Count - 1].Trim().Length == 0) kept.RemoveAt(kept.Count - 1);
            if (kept.Count > 0) kept.Add("");
            kept.Add("[mcp_servers." + ServerName + "]");
            kept.Add("command = " + TomlString(exePath));
            kept.Add("args = [\"--mcp\"]");
            return string.Join("\r\n", kept.ToArray()) + "\r\n";
        }

        private static string TomlString(string value)
        {
            if (value.IndexOf('\'') < 0 && value.IndexOfAny(new char[] { '\r', '\n' }) < 0) return "'" + value + "'";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        // Writes the file atomically, keeping a one-time backup of the original.
        public static void WriteWithBackup(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string backup = path + ".antes-de-prisma";
            if (File.Exists(path) && !File.Exists(backup)) File.Copy(path, backup);
            string temp = path + ".tmp";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        public static string ReadOrEmpty(string path) { return File.Exists(path) ? File.ReadAllText(path) : ""; }

        // Minimal pretty printer so the config stays readable for people who edit it by hand.
        private static string Indent(string compact)
        {
            StringBuilder s = new StringBuilder(); int depth = 0; bool inString = false;
            for (int i = 0; i < compact.Length; i++)
            {
                char c = compact[i];
                if (inString) { s.Append(c); if (c == '\\' && i + 1 < compact.Length) s.Append(compact[++i]); else if (c == '"') inString = false; continue; }
                switch (c)
                {
                    case '"': inString = true; s.Append(c); break;
                    case '{': case '[':
                        if (i + 1 < compact.Length && (compact[i + 1] == '}' || compact[i + 1] == ']')) { s.Append(c).Append(compact[++i]); break; }
                        s.Append(c).Append('\n').Append(' ', ++depth * 2); break;
                    case '}': case ']': s.Append('\n').Append(' ', --depth * 2).Append(c); break;
                    case ',': s.Append(c).Append('\n').Append(' ', depth * 2); break;
                    case ':': s.Append(": "); break;
                    default: s.Append(c); break;
                }
            }
            return s.Append('\n').ToString();
        }
    }
}
