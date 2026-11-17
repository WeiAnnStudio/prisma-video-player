// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Prisma
{
    internal sealed class MpvEngine : IDisposable
    {
        private IntPtr handle;
        public event Action<int, string> Event;
        public event Action<string> Warning;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeEvent { public int Id, Error; public ulong Reply; public IntPtr Data; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ClientMessage { public int Count; public IntPtr Args; }
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_create();
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_initialize(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_terminate_destroy(IntPtr ctx);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_option_string(IntPtr ctx, byte[] name, byte[] value);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_set_property_string(IntPtr ctx, byte[] name, byte[] value);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_get_property_string(IntPtr ctx, byte[] name);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int mpv_command_async(IntPtr ctx, ulong data, IntPtr[] args);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_wait_event(IntPtr ctx, double timeout);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr mpv_error_string(int error);
        [DllImport("libmpv-2.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mpv_free(IntPtr data);

        private static byte[] Utf8(string text) { return Encoding.UTF8.GetBytes(text + "\0"); }
        private static string ReadUtf8(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero) return "";
            int count = 0;
            while (Marshal.ReadByte(ptr, count) != 0) count++;
            byte[] bytes = new byte[count];
            Marshal.Copy(ptr, bytes, 0, count);
            return Encoding.UTF8.GetString(bytes);
        }

        public MpvEngine(IntPtr videoWindow, string basePath, bool hardware, string colorMode, bool headless)
        {
            handle = mpv_create();
            if (handle == IntPtr.Zero) throw new InvalidOperationException("No se pudo crear el motor de vídeo.");
            try
            {
                Option("config", "no");
                Option("terminal", "no");
                Option("idle", "yes");
                Option("keep-open", "yes");
                Option("keep-open-pause", "yes");
                Option("osc", "no");
                Option("osd-level", "0");
                Option("osd-font-size", "22");
                Option("osd-align-x", "left");
                Option("osd-align-y", "top");
                Option("osd-margin-x", "24");
                Option("osd-margin-y", "24");
                Option("input-default-bindings", "no");
                Option("ytdl", "no");
                Option("load-scripts", "no");
                Option("audio-display", "no");
                Option("hwdec", hardware ? "auto-safe" : "no");
                if (headless)
                {
                    Option("vo", "null");
                    Option("ao", "null");
                }
                else
                {
                    Option("wid", videoWindow.ToInt64().ToString(CultureInfo.InvariantCulture));
                    Option("vo", "gpu-next");
                    Option("gpu-api", "d3d11");
                    Option("gpu-context", "d3d11");
                    Option("d3d11-output-format", "auto");
                    Option("target-colorspace-hint", "auto");
                    if (colorMode == "sdr") Option("target-trc", "bt.1886");
                    // The host owns keyboard and mouse input. Enabling mpv input as
                    // well forwards the same keystroke twice in an embedded HWND.
                    Option("input-vo-keyboard", "no");
                    Option("input-cursor", "no");
                }
                Check(mpv_initialize(handle));
            }
            catch { Dispose(); throw; }
        }

        private static void Check(int result)
        {
            if (result < 0) throw new InvalidOperationException(ReadUtf8(mpv_error_string(result)));
        }
        private void Option(string name, string value) { Check(mpv_set_option_string(handle, Utf8(name), Utf8(value))); }
        public bool Set(string name, string value)
        {
            if (handle == IntPtr.Zero) return false;
            int result = mpv_set_property_string(handle, Utf8(name), Utf8(value));
            if (result < 0 && Warning != null) Warning("No se pudo cambiar " + name + ": " + ReadUtf8(mpv_error_string(result)));
            return result >= 0;
        }
        public string Get(string name)
        {
            if (handle == IntPtr.Zero) return "";
            IntPtr result = mpv_get_property_string(handle, Utf8(name));
            try { return ReadUtf8(result); }
            finally { if (result != IntPtr.Zero) mpv_free(result); }
        }
        public double Number(string name)
        {
            double value;
            return double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value) ? value : 0;
        }
        public bool Flag(string name) { return Get(name) == "yes"; }
        // mpv exposes pixel formats, not a generic colordepth property. Prefer the
        // demuxed video format so a 16-bit hardware surface is not reported as a 16-bit source.
        public string VideoDepth()
        {
            for (int i = 0; i < (int)Number("track-list/count"); i++)
            {
                string prefix = "track-list/" + i + "/";
                if (Get(prefix + "type") == "video" && Flag(prefix + "selected"))
                {
                    string depth = PixelDepth(Get(prefix + "format-name"));
                    if (depth.Length > 0) return depth;
                }
            }
            string decoded = PixelDepth(Get("video-dec-params/pixelformat"));
            return decoded.Length > 0 ? decoded : PixelDepth(Get("video-dec-params/hw-pixelformat"));
        }
        internal static string PixelDepth(string format)
        {
            format = format.ToLowerInvariant();
            Match planar = Regex.Match(format, @"^(?:yuva?\d{3}p|gbr(?:a)?p|gray)(9|10|12|14|16)(?:le|be)?$");
            if (planar.Success) return planar.Groups[1].Value;
            Match packed = Regex.Match(format, @"^p[024](10|12|16)(?:le|be)?$");
            if (packed.Success) return packed.Groups[1].Value;
            if (Regex.IsMatch(format, @"^(?:yuvj?\d{3}p|yuva\d{3}p|nv12|nv21|rgb24|bgr24|rgba|bgra|argb|abgr|0rgb|rgb0|0bgr|bgr0|gbrp|gbrap|gray|gray8|yuyv422|uyvy422)$")) return "8";
            if (Regex.IsMatch(format, @"^(?:rgb48|bgr48|rgba64|bgra64)(?:le|be)?$")) return "16";
            if (Regex.IsMatch(format, @"^(?:x2rgb10|x2bgr10)(?:le|be)?$")) return "10";
            return "";
        }
        public void Command(params string[] args)
        {
            if (handle == IntPtr.Zero) return;
            IntPtr[] native = new IntPtr[args.Length + 1];
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    byte[] bytes = Utf8(args[i]);
                    native[i] = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, native[i], bytes.Length);
                }
                Check(mpv_command_async(handle, 0, native));
            }
            finally { foreach (IntPtr ptr in native) if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr); }
        }
        // Events are consumed on the UI thread; all native event memory is copied before the next poll.
        public void Pump()
        {
            for (int i = 0; i < 100 && handle != IntPtr.Zero; i++)
            {
                NativeEvent ev = (NativeEvent)Marshal.PtrToStructure(mpv_wait_event(handle, 0), typeof(NativeEvent));
                if (ev.Id == 0) break;
                string detail = "";
                if (ev.Id == 7 && ev.Data != IntPtr.Zero)
                {
                    int reason = Marshal.ReadInt32(ev.Data);
                    int error = Marshal.ReadInt32(ev.Data, 4);
                    if (reason == 4) detail = "No se pudo reproducir el archivo: " + ReadUtf8(mpv_error_string(error));
                }
                if (ev.Id == 16 && ev.Data != IntPtr.Zero)
                {
                    ClientMessage message = (ClientMessage)Marshal.PtrToStructure(ev.Data, typeof(ClientMessage));
                    if (message.Count > 0) detail = ReadUtf8(Marshal.ReadIntPtr(message.Args));
                }
                if (ev.Id == 5 && ev.Error < 0 && Warning != null)
                    Warning("La acción no se pudo completar: " + ReadUtf8(mpv_error_string(ev.Error)));
                if (Event != null) Event(ev.Id, detail);
            }
        }
        public void Dispose()
        {
            if (handle != IntPtr.Zero) { IntPtr old = handle; handle = IntPtr.Zero; mpv_terminate_destroy(old); }
        }
    }
}
