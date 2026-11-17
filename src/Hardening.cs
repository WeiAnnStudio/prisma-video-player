// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Prisma
{
    // Process-wide Windows hardening applied before libmpv is loaded.
    // Every step is best effort: an unsupported call never prevents playback.
    internal static class Hardening
    {
        private const uint SafeSearchMode = 0x00000001, SearchModePermanent = 0x00008000;
        private const int ImageLoadPolicy = 10;
        private const uint NoRemoteImages = 0x1, NoLowLabelImages = 0x2;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetDllDirectory(string path);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetSearchPathMode(uint flags);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetProcessMitigationPolicy(int policy, ref uint value, IntPtr length);

        public static bool Applied { get; private set; }

        public static void Apply()
        {
            // Removes the current folder from the DLL search order. Opening a video
            // from a folder that also contains a planted DLL can no longer load it,
            // including vendor decoder DLLs that hardware decoding looks up by name.
            try { SetDllDirectory(""); } catch { }
            try { SetSearchPathMode(SafeSearchMode | SearchModePermanent); } catch { }
            // DLLs from network shares are refused only when Prisma itself runs from a
            // local disk, so a portable copy on a NAS keeps working.
            uint policy = NoLowLabelImages;
            if (RunsFromLocalDisk()) policy |= NoRemoteImages;
            try { Applied = SetProcessMitigationPolicy(ImageLoadPolicy, ref policy, new IntPtr(4)); } catch { Applied = false; }
        }

        internal static bool RunsFromLocalDisk()
        {
            try
            {
                string root = Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory);
                if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal)) return false;
                DriveType type = new DriveInfo(root).DriveType;
                return type == DriveType.Fixed || type == DriveType.Removable;
            }
            catch { return false; }
        }
    }
}
