// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Prisma
{
    public sealed class Settings
    {
        public int X = -1, Y = -1, Width = 1100, Height = 730;
        public int Volume = 80;
        public bool Hardware = true;
        public string ColorMode = "auto";
        public List<string> Recent = new List<string>();
        public static Settings Load(string path)
        {
            try
            {
                Settings result = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(path));
                if (result == null) return new Settings();
                result.Volume = Math.Max(0, Math.Min(100, result.Volume));
                result.Width = Math.Max(780, Math.Min(7680, result.Width));
                result.Height = Math.Max(500, Math.Min(4320, result.Height));
                if (result.ColorMode != "sdr") result.ColorMode = "auto";
                if (result.Recent == null) result.Recent = new List<string>();
                return result;
            }
            catch { return new Settings(); }
        }
        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, new JavaScriptSerializer().Serialize(this));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }
}
