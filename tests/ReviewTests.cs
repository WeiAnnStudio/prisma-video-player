// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using Prisma;

internal static class ReviewTests
{
    private static int passed;
    private static void Check(bool value, string description) { if (!value) throw new Exception("FAIL " + description); Console.WriteLine("PASS " + description); passed++; }
    private static bool Wait(Func<bool> predicate, int milliseconds)
    {
        Stopwatch sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); if (predicate()) return true; Thread.Sleep(20); } return false;
    }
    private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    private static void Invoke(object target, string name, params object[] args) { target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
    private static void Key(PlayerForm form, Keys key) { Invoke(form, "ProcessCmdKey", new Message(), key); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Hardening.Apply();
            string root = args[0], output = Path.Combine(root, "tests", "exports"); Directory.CreateDirectory(output);
            ReviewDocument doc = new ReviewDocument { MediaPath = Path.Combine(root, "tests", "media", "prores-422-10bit.mov"), DurationFrames = 48, Width = 640, Height = 360, AudioChannels = 1 };
            doc.SetFps(24);
            doc.Markers.Add(new ReviewMarker { Frame = 0, Name = "Inicio", Note = "Revisar negros & contraste" });
            doc.Markers.Add(new ReviewMarker { Frame = 12, Name = "Piel áéíóú ñ", Note = "Bajar rojo <un poco>\nSegunda línea, con \"comillas\"." });
            doc.Markers.Add(new ReviewMarker { Frame = 47, Name = "Último fotograma", Note = "Comprobar salida" });
            string xml = Path.Combine(output, "revision-prisma.xml"); doc.ExportXml(xml);
            ReviewDocument imported = ReviewDocument.ImportXml(xml);
            Check(imported.Markers.Select(m => m.Frame).SequenceEqual(new long[] { 0, 12, 47 }), "XML conserva fotograma cero, intermedio y último");
            Check(imported.Markers[1].Note == doc.Markers[1].Note && imported.Markers[1].Name == doc.Markers[1].Name, "XML conserva Unicode, saltos de línea y caracteres especiales");
            Check(imported.MediaPath == doc.MediaPath && imported.Fps == 24, "XML enlaza el render y conserva FPS");
            doc.Save(Path.Combine(output, "revision-prisma.json"));
            Check(ReviewDocument.Load(Path.Combine(output, "revision-prisma.json")).Markers.Count == 3, "Persistencia JSON de las notas");
            doc.ExportCsv(Path.Combine(output, "revision-prisma.csv")); Check(File.ReadAllText(Path.Combine(output, "revision-prisma.csv")).Contains("\"\"comillas\"\""), "CSV escapa comillas y notas multilínea");
            doc.SetFps(24000.0 / 1001); doc.ExportXml(Path.Combine(output, "revision-23976.xml"));
            ReviewDocument fractional = ReviewDocument.ImportXml(Path.Combine(output, "revision-23976.xml")); Check(fractional.FpsNumerator == 24000 && fractional.FpsDenominator == 1001, "23,976 se conserva como 24000/1001");
            Check(doc.Timecode(24000) == "00:16:40:00", "Código de tiempo NDF usa base nominal de 24");
            string hostile = Path.Combine(output, "invalid.xml"); File.WriteAllText(hostile, "<!DOCTYPE xmeml [<!ENTITY data SYSTEM 'file:///C:/Windows/win.ini'>]><xmeml>&data;</xmeml>");
            bool rejected = false; try { ReviewDocument.ImportXml(hostile); } catch { rejected = true; } Check(rejected, "No se resuelven entidades externas de XML");
            ReviewDocument risky = new ReviewDocument { MediaPath = doc.MediaPath, DurationFrames = 48 }; risky.SetFps(24);
            risky.Markers.Add(new ReviewMarker { Frame = 1, Name = "=HYPERLINK(\"http://ejemplo.invalid\",\"clic\")", Note = "@SUM(1+1)" });
            risky.Markers.Add(new ReviewMarker { Frame = 2, Name = "-2 pasos", Note = "+cmd|' /C calc'!A0" });
            string riskyCsv = Path.Combine(output, "formulas.csv"); risky.ExportCsv(riskyCsv); string riskyText = File.ReadAllText(riskyCsv);
            Check(riskyText.Contains("\"'=HYPERLINK(") && riskyText.Contains("\"'@SUM(1+1)\"") && riskyText.Contains("\"'-2 pasos\"") && riskyText.Contains("\"'+cmd"), "CSV neutraliza fórmulas en las notas");
            string sharedJson = Path.Combine(output, "compartir.json"); risky.SaveForSharing(sharedJson); ReviewDocument shared = ReviewDocument.Load(sharedJson);
            Check(shared.MediaPath == Path.GetFileName(doc.MediaPath) && !Path.IsPathRooted(shared.MediaPath) && shared.Markers.Count == 2 && risky.MediaPath == doc.MediaPath, "JSON para compartir no incluye la ruta completa del equipo");
            ReviewDocument tooLong = new ReviewDocument { DurationFrames = 48 }; tooLong.Markers.Add(new ReviewMarker { Frame = 0, Note = new string('x', ReviewDocument.MaxNoteLength + 1) });
            bool lengthRejected = false; try { tooLong.Validate(); } catch (InvalidDataException) { lengthRejected = true; } Check(lengthRejected, "Rechaza notas importadas más largas que el editor");
            Shuttle shuttle = new Shuttle(); Check(shuttle.Press(1) == 1 && shuttle.Press(1) == 2 && shuttle.Press(1) == 4 && shuttle.Press(-1) == 2 && shuttle.Press(-1) == 1 && shuttle.Press(-1) == -1 && shuttle.Press(-1) == -2, "J/L aceleran, frenan y cambian de dirección");
            for (int i = 0; i < 12; i++) shuttle.Press(-1); Check(shuttle.Rate == -16, "Revisión limitada a 16×");
            Check(MpvEngine.PixelDepth("gbrp10le") == "10" && MpvEngine.PixelDepth("p010") == "10" && MpvEngine.PixelDepth("unknown") == "", "Profundidad de color no se inventa para formatos desconocidos");
            Application.EnableVisualStyles();
            using (PlayerForm form = new PlayerForm(new string[] { Path.Combine(root, "tests", "media", "h265-4K-10bit.mp4") }, Path.Combine(root, "tests", "review-ui", "preferencias.json")))
            {
                form.Show(); Check(Wait(delegate { return Field(form, "review") != null; }, 10000), "Carga interfaz de revisión con el vídeo");
                MpvEngine engine = (MpvEngine)Field(form, "engine"); Key(form, Keys.K); engine.Command("seek", "2", "absolute+exact");
                Check(Wait(delegate { return Math.Abs(engine.Number("time-pos") - 2) < .01 && !engine.Flag("seeking"); }, 5000), "Sitúa el cabezal en 2 segundos");
                Key(form, Keys.Right); bool stepped = Wait(delegate { return Math.Abs(engine.Number("time-pos") - (2 + 1.0 / 24)) < .01 && !engine.Flag("seeking"); }, 5000); Console.WriteLine("  Posición después de →: " + engine.Get("time-pos")); Check(stepped, "Flecha derecha avanza exactamente un fotograma");
                Key(form, Keys.Left); Check(Wait(delegate { return Math.Abs(engine.Number("time-pos") - 2) < .01; }, 5000), "Flecha izquierda retrocede exactamente un fotograma");
                Key(form, Keys.J); Check(Wait(delegate { return engine.Number("time-pos") < 1.6; }, 5000), "J revisa el vídeo hacia atrás");
                Key(form, Keys.K); double pausedAt = engine.Number("time-pos"); Wait(delegate { return false; }, 250); Check(Math.Abs(engine.Number("time-pos") - pausedAt) < .12 && engine.Flag("pause"), "K detiene el retroceso");
                Key(form, Keys.L); Key(form, Keys.L); Check(Math.Abs(engine.Number("speed") - 2) < .01 && !engine.Flag("pause"), "L dos veces reproduce a 2×"); Key(form, Keys.K);
                Key(form, Keys.I); Check((bool)Field(form, "summaryShown"), "I activa el resumen superpuesto"); Key(form, Keys.I); Check(!(bool)Field(form, "summaryShown"), "I vuelve a ocultar el resumen");
                Key(form, Keys.M); MarkerPanel panel = (MarkerPanel)Field(form, "notesPanel"); Check(panel.Editing, "M abre el editor de una nota");
                ((TextBox)Field(panel, "title")).Text = "Nota desde la interfaz"; ((TextBox)Field(panel, "note")).Text = "Revisar este fotograma. ñ & < >";
                panel.Hide(); Check(panel.Editing, "Ocultar el panel conserva la edición pendiente"); panel.CommitEdit(); panel.Show();
                ReviewDocument review = (ReviewDocument)Field(form, "review"); Check(review.Markers.Any(m => m.Name == "Nota desde la interfaz"), "Guarda una nota en el fotograma actual");
                Check(File.Exists((string)Field(form, "reviewFile")), "Persiste automáticamente los marcadores");
                form.Size = new System.Drawing.Size(1000, 680); form.Close();
            }
            Settings saved = Settings.Load(Path.Combine(root, "tests", "review-ui", "preferencias.json")); Check(saved.Width == 1000 && saved.Height == 680, "La ventana recuerda su tamaño de revisión");
            Console.WriteLine(passed + " comprobaciones de revisión correctas."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
