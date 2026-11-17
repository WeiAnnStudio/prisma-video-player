// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using Prisma;

internal static class RenderTests
{
    private static bool Wait(MpvEngine engine, Func<bool> predicate, int timeout)
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeout) { Application.DoEvents(); engine.Pump(); if (predicate()) return true; Thread.Sleep(20); }
        return false;
    }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Hardening.Apply();
            Application.EnableVisualStyles();
            using (Form form = new Form { Text = "Prisma — Prueba del renderizador", Size = new Size(960, 600), StartPosition = FormStartPosition.CenterScreen })
            using (Panel surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black })
            {
                form.Controls.Add(surface); form.Show(); Application.DoEvents();
                using (MpvEngine engine = new MpvEngine(surface.Handle, Path.Combine(args[0], "Prisma"), true, "auto", false))
                {
                    engine.Set("mute", "yes");
                    string error = "";
                    engine.Event += delegate(int id, string detail) { if (id == 7 && detail.Length > 0) error = detail; };
                    foreach (string name in new string[] { "h264-8bit.mp4", "h264-10bit.mp4", "h265-4K-10bit.mp4", "h265-422-10bit.mp4", "prores-422-10bit.mov", "prores-4444.mov", "hdr10.mp4" })
                    {
                        error = ""; bool loaded = false;
                        Action<int, string> listener = delegate(int id, string detail) { if (id == 8) loaded = true; };
                        engine.Event += listener;
                        engine.Command("loadfile", Path.Combine(args[0], "tests", "media", name), "replace"); engine.Set("pause", "no");
                        if (!Wait(engine, delegate { return loaded && engine.Number("time-pos") > .2 && engine.Get("current-vo") == "gpu-next"; }, 15000)) throw new Exception("No renderiza " + name + " " + error);
                        if (!Wait(engine, delegate { return engine.Flag("eof-reached") && engine.Flag("pause"); }, 15000)) throw new Exception("No mantiene el final " + name + " " + error);
                        Console.WriteLine("PASS " + name + " | " + engine.VideoDepth() + " bits | " + engine.Get("hwdec-current") + " | " + engine.Get("current-vo") + " | descartados: " + engine.Get("frame-drop-count"));
                        if (name == "hdr10.mp4")
                        {
                            if (!engine.Set("target-trc", "bt.1886")) throw new Exception("No cambia a SDR");
                            engine.Command("seek", "0", "absolute+exact"); engine.Set("pause", "no");
                            if (!Wait(engine, delegate { return !engine.Flag("eof-reached") && engine.Number("time-pos") > .2; }, 5000)) throw new Exception("No reproduce HDR adaptado a SDR");
                            Console.WriteLine("PASS HDR adaptado a SDR");
                        }
                        engine.Event -= listener;
                    }
                }
                form.Close();
            }
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
