// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Prisma;

internal static class EngineTests
{
    private static int passed;
    private static string error = "";
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name + (error.Length > 0 ? " / " + error : ""));
        Console.WriteLine("PASS: " + name); passed++;
    }
    private static bool Wait(MpvEngine engine, Func<bool> condition, int timeout)
    {
        Stopwatch clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeout)
        {
            engine.Pump(); if (condition()) return true; Thread.Sleep(20);
        }
        engine.Pump(); return condition();
    }
    private static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(args[0]), media = Path.Combine(root, "tests", "media");
            using (MpvEngine engine = new MpvEngine(IntPtr.Zero, Path.Combine(root, "Prisma"), false, "auto", true))
            {
                bool loaded = false;
                engine.Event += delegate(int id, string detail) { if (id == 8) loaded = true; if (id == 7 && detail.Length > 0) error = detail; };
                engine.Warning += delegate(string message) { error = message; };
                Console.WriteLine("Motor: " + engine.Get("mpv-version"));
                foreach (string file in Directory.GetFiles(media).Where(f => (f.EndsWith(".mp4") || f.EndsWith(".mov")) && !f.EndsWith("corrupto.mp4")))
                {
                    loaded = false; error = "";
                    engine.Command("loadfile", file, "replace"); engine.Set("pause", "no");
                    Check(Wait(engine, delegate { return loaded || error.Length > 0; }, 8000) && loaded && error.Length == 0, "Abrir " + Path.GetFileName(file));
                    Check(Wait(engine, delegate { return engine.Number("time-pos") > .15; }, 4000), "Decodifica y avanza");
                    string bits = engine.VideoDepth();
                    Console.WriteLine("  " + engine.Get("video-format") + " / " + engine.Get("video-params/pixelformat") + " / " + bits + " bits");
                    int expected = file.Contains("8bit") ? 8 : file.Contains("4444") ? 12 : 10;
                    Check(bits == expected.ToString(), "Profundidad de color " + expected + " bits");
                    engine.Set("pause", "yes"); Thread.Sleep(80);
                    double before = engine.Number("time-pos"); Thread.Sleep(150); engine.Pump();
                    Check(Math.Abs(engine.Number("time-pos") - before) < .08, "Pausa mantiene la posición");
                    engine.Command("seek", "0.8", "absolute+exact");
                    Check(Wait(engine, delegate { return Math.Abs(engine.Number("time-pos") - .8) < .15; }, 3000), "Búsqueda exacta");
                    engine.Set("pause", "no");
                    Check(Wait(engine, delegate { return engine.Flag("eof-reached"); }, (int)Math.Max(8000, engine.Number("duration") * 2000)), "Llega al final");
                    Check(Wait(engine, delegate { return !engine.Flag("idle-active") && engine.Flag("pause"); }, 2000), "Permanece abierto en el último fotograma (idle=" + engine.Get("idle-active") + ", pause=" + engine.Get("pause") + ")");
                    engine.Command("seek", "0", "absolute+exact"); engine.Set("pause", "no");
                    Check(Wait(engine, delegate { return !engine.Flag("eof-reached") && engine.Number("time-pos") > .1 && engine.Number("time-pos") < 1; }, 3000), "Vuelve a reproducir desde el principio");
                    engine.Set("pause", "yes");
                }
                error = ""; loaded = false;
                engine.Command("loadfile", Path.Combine(media, "h264-8bit.mp4"), "replace");
                engine.Command("loadfile", Path.Combine(media, "h265-10bit.mp4"), "append");
                Check(Wait(engine, delegate { return loaded && engine.Number("playlist/count") == 2; }, 4000), "Lista con dos archivos");
                engine.Set("speed", "2"); engine.Set("pause", "no");
                Check(Wait(engine, delegate { return engine.Number("playlist-pos") == 1 && engine.Flag("eof-reached"); }, 6000), "Avanza al siguiente archivo y conserva el final");
                engine.Set("speed", "1");
                engine.Command("sub-add", Path.Combine(media, "subtitulos.srt"), "select");
                Check(Wait(engine, delegate { return engine.Number("track-list/count") == 3; }, 3000), "Carga subtítulos externos");
                Check(engine.Set("volume", "35") && Math.Abs(engine.Number("volume") - 35) < .1, "Cambia volumen");
                Check(engine.Set("mute", "yes") && engine.Flag("mute"), "Silencia audio");
                error = ""; engine.Command("loadfile", Path.Combine(media, "corrupto.mp4"), "replace");
                Check(Wait(engine, delegate { return error.Length > 0; }, 4000), "Informa de archivo dañado");
                loaded = false; error = ""; engine.Command("loadfile", Path.Combine(media, "prores-422-10bit.mov"), "replace"); engine.Set("pause", "no");
                Check(Wait(engine, delegate { return loaded && engine.Number("time-pos") > .1; }, 4000), "Se recupera después de un archivo dañado");
            }
            string settingsPath = Path.Combine(root, "tests", "settings-test.json");
            Settings settings = new Settings { Width = 1234, Height = 789, X = 43, Y = 54, Volume = 37, Hardware = false, ColorMode = "sdr" };
            settings.Recent.Add("C:\\Vídeos\\mañana.mov"); settings.Save(settingsPath);
            Settings loadedSettings = Settings.Load(settingsPath);
            Check(loadedSettings.Width == 1234 && loadedSettings.Height == 789 && loadedSettings.X == 43 && loadedSettings.Volume == 37 && !loadedSettings.Hardware && loadedSettings.ColorMode == "sdr" && loadedSettings.Recent[0] == settings.Recent[0], "Recuerda tamaño, posición, volumen y preferencias Unicode");
            settings.Volume = 54; settings.Save(settingsPath); Check(Settings.Load(settingsPath).Volume == 54, "Actualización atómica de preferencias");
            File.WriteAllText(settingsPath, "invalid-json"); Check(Settings.Load(settingsPath).Volume == 80, "Recupera valores predeterminados ante preferencias dañadas");
            File.WriteAllText(settingsPath, "{\"Volume\":999,\"Width\":-1,\"Height\":999999,\"Recent\":null,\"ColorMode\":\"invalid\"}");
            Settings clamped = Settings.Load(settingsPath);
            Check(clamped.Volume == 100 && clamped.Width == 780 && clamped.Height == 4320 && clamped.Recent != null && clamped.ColorMode == "auto", "Limita preferencias inválidas");
            Console.WriteLine("\n" + passed + " comprobaciones correctas."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
