// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Prisma
{
    internal sealed partial class PlayerForm
    {
        private readonly MarkerPanel notesPanel = new MarkerPanel();
        private readonly FlatButton notesButton = new FlatButton("Notas  M", "Mostrar notas de revisión", false);
        private readonly Shuttle shuttle = new Shuttle();
        private readonly Stopwatch shuttleClock = new Stopwatch();
        private double shuttleStart;
        private bool notesShown, summaryShown;
        private ReviewDocument review;
        private string reviewFile = "";
        private void InitializeReview()
        {
            Controls.Add(notesPanel); notesPanel.Visible = false; header.Controls.Add(notesButton);
            notesButton.Click += delegate { ToggleNotes(); };
            notesPanel.Add += AddMarker; notesPanel.Import += ImportMarkers; notesPanel.Export += ExportMarkers;
            notesPanel.Jump += delegate(ReviewMarker m) { PauseShuttle(); engine.Command("seek", F(m.Frame / review.Fps), "absolute+exact"); };
            notesPanel.Changed += SaveReview;
            ToolStripMenuItem notesMenu = new ToolStripMenuItem("Revisión");
            notesMenu.DropDownItems.Add(Item("Fotograma anterior    ←", delegate { StepFrame(-1); }));
            notesMenu.DropDownItems.Add(Item("Fotograma siguiente    →", delegate { StepFrame(1); }));
            notesMenu.DropDownItems.Add(Item("Retroceder / acelerar    J", delegate { StartShuttle(-1, false); }));
            notesMenu.DropDownItems.Add(Item("Pausar    K", PauseShuttle));
            notesMenu.DropDownItems.Add(Item("Avanzar / acelerar    L", delegate { StartShuttle(1, false); }));
            notesMenu.DropDownItems.Add(Item("Resumen sobre el vídeo    I", ToggleSummary));
            notesMenu.DropDownItems.Add(new ToolStripSeparator());
            notesMenu.DropDownItems.Add(Item("Añadir nota en este fotograma    M", AddMarker));
            notesMenu.DropDownItems.Add(Item("Mostrar notas    Ctrl+M", ToggleNotes));
            notesMenu.DropDownItems.Add(Item("Importar notas (XML / JSON)…", ImportMarkers));
            notesMenu.DropDownItems.Add(Item("Exportar notas (XML / JSON / CSV)…", ExportMarkers));
            menu.Items.Insert(2, notesMenu);
        }
        private void LoadReview()
        {
            shuttle.Rate = 0; shuttleClock.Reset(); engine.Set("speed", "1"); notesPanel.CancelEdit();
            review = new ReviewDocument { MediaPath = engine.Get("path"), Width = (int)engine.Number("width"), Height = (int)engine.Number("height") };
            try { review.SetFps(engine.Number("container-fps")); }
            catch { review = null; reviewFile = ""; notesPanel.RefreshMarkers(null); Notice("No se han detectado FPS constantes compatibles para las notas.", true); return; }
            review.DurationFrames = Math.Max(1, (long)Math.Round(engine.Number("duration") * review.Fps));
            int tracks = (int)engine.Number("track-list/count");
            for (int i = 0; i < tracks; i++) if (engine.Get("track-list/" + i + "/type") == "audio" && engine.Flag("track-list/" + i + "/selected"))
            {
                review.AudioChannels = (int)engine.Number("track-list/" + i + "/demux-channel-count");
                review.AudioRate = (int)engine.Number("track-list/" + i + "/demux-samplerate");
                if (review.AudioRate <= 0) review.AudioRate = 48000;
            }
            reviewFile = ReviewDocument.StoragePath(Path.GetDirectoryName(settingsPath), review.MediaPath);
            if (File.Exists(reviewFile))
            {
                try
                {
                    ReviewDocument saved = ReviewDocument.Load(reviewFile);
                    if (saved.FpsNumerator == review.FpsNumerator && saved.FpsDenominator == review.FpsDenominator && saved.DurationFrames == review.DurationFrames) review.Markers = saved.Markers;
                    else Notice("El vídeo ha cambiado de duración o FPS. Las notas guardadas se conservan para importarlas manualmente.", true);
                }
                catch (Exception ex) { Notice("No se pudieron leer las notas: " + ex.Message, true); }
            }
            notesPanel.RefreshMarkers(review); UpdateMarkerTicks();
        }
        private void ToggleNotes()
        {
            notesShown = !notesShown; if (notesShown) queueShown = false; LayoutPlayer();
        }
        private void AddMarker()
        {
            if (review == null || engine == null) { Notice("Abre un vídeo para añadir notas.", false); return; }
            PauseShuttle(); notesShown = true; queueShown = false; LayoutPlayer();
            long frame = Math.Max(0, Math.Min(review.DurationFrames - 1, (long)Math.Round(engine.Number("time-pos") * review.Fps)));
            notesPanel.BeginEdit(new ReviewMarker { Frame = frame, Name = "Nota " + (review.Markers.Count + 1) });
        }
        private void SaveReview()
        {
            if (review == null) return;
            try { review.Save(reviewFile); Notice("Notas guardadas", false); }
            catch (Exception ex) { Notice("No se pudieron guardar las notas: " + ex.Message + ". Exporta una copia.", true); }
            UpdateMarkerTicks();
        }
        private void UpdateMarkerTicks()
        {
            seek.Markers = review == null || review.DurationFrames == 0 ? new double[0] : review.Markers.Select(m => (double)m.Frame / review.DurationFrames).ToArray(); seek.Invalidate();
        }
        private void ExportMarkers()
        {
            if (review == null) { Notice("Abre un vídeo antes de exportar las notas.", false); return; }
            notesPanel.CommitEdit();
            using (SaveFileDialog dialog = new SaveFileDialog { Title = "Exportar notas de revisión", FileName = Path.GetFileNameWithoutExtension(review.MediaPath) + "-notas.xml", Filter = "DaVinci / Premiere · XML con vídeo y marcadores|*.xml|Prisma · Notas editables|*.json|Documento de notas · CSV|*.csv", DefaultExt = "xml" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    switch (Path.GetExtension(dialog.FileName).ToLowerInvariant())
                    {
                        case ".xml": review.ExportXml(dialog.FileName); break;
                        case ".json": review.Save(dialog.FileName); break;
                        case ".csv": review.ExportCsv(dialog.FileName); break;
                        default: throw new InvalidDataException("Usa la extensión XML, JSON o CSV.");
                    }
                    Notice("Notas exportadas: " + Path.GetFileName(dialog.FileName), false);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudieron exportar las notas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void ImportMarkers()
        {
            if (review == null) { Notice("Abre el vídeo al que pertenecen las notas.", false); return; }
            notesPanel.CommitEdit();
            using (OpenFileDialog dialog = new OpenFileDialog { Title = "Importar notas para el vídeo abierto", Filter = "Notas · XML de Final Cut Pro 7 / JSON de Prisma|*.xml;*.json" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ReviewDocument incoming = Path.GetExtension(dialog.FileName).Equals(".xml", StringComparison.OrdinalIgnoreCase) ? ReviewDocument.ImportXml(dialog.FileName) : ReviewDocument.Load(dialog.FileName);
                    if (incoming.Markers.Count == 0) throw new InvalidDataException("El documento no contiene marcadores. Algunas exportaciones XML de los editores omiten las notas. Usa un XML que incluya marcadores o una copia JSON de Prisma.");
                    if (incoming.FpsNumerator != review.FpsNumerator || incoming.FpsDenominator != review.FpsDenominator) throw new InvalidDataException("Los FPS del documento no coinciden con los del vídeo. Exporta las notas con la misma velocidad de fotogramas para mantener su posición.");
                    if (incoming.Markers.Any(m => m.Frame >= review.DurationFrames)) throw new InvalidDataException("Hay notas fuera de la duración del vídeo abierto.");
                    if (incoming.MediaPath.Length > 0 && !string.Equals(Path.GetFileName(incoming.MediaPath), Path.GetFileName(review.MediaPath), StringComparison.OrdinalIgnoreCase) && MessageBox.Show(this, "El documento corresponde a " + Path.GetFileName(incoming.MediaPath) + ". ¿Añadir sus notas al vídeo abierto?", "Importar notas", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    int count = 0;
                    foreach (ReviewMarker marker in incoming.Markers)
                        if (!review.Markers.Any(m => m.Frame == marker.Frame && m.Name == marker.Name && m.Note == marker.Note)) { review.Markers.Add(marker); count++; }
                    SaveReview(); notesPanel.RefreshMarkers(review); notesShown = true; queueShown = false; LayoutPlayer(); Notice(count + " notas importadas", false);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "No se pudieron importar las notas", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
        private void StepFrame(int direction)
        {
            if (engine == null || engine.Flag("idle-active")) return;
            PauseShuttle(); engine.Command(direction < 0 ? "frame-back-step" : "frame-step"); stopped = false;
        }
        private void PauseShuttle()
        {
            shuttle.Rate = 0; shuttleClock.Reset();
            if (engine != null) { engine.Set("pause", "yes"); engine.Set("speed", "1"); }
        }
        private void StartShuttle(int direction, bool slow)
        {
            if (engine == null || engine.Flag("idle-active")) return;
            double rate = slow ? direction * .5 : shuttle.Press(direction); shuttle.Rate = rate; stopped = false;
            if (rate < 0)
            {
                // Reverse review uses bounded, exact seeks while paused. This keeps
                // hardware decoding and avoids buffering whole 4K/HEVC GOPs in RAM.
                engine.Set("pause", "yes"); engine.Set("speed", "1"); shuttleStart = engine.Number("time-pos");
                shuttleClock.Restart();
            }
            else
            {
                shuttleClock.Reset(); engine.Set("speed", F(rate));
                if (engine.Flag("eof-reached")) engine.Command("seek", "0", "absolute+exact"); engine.Set("pause", "no");
            }
        }
        private void TickReview()
        {
            if (shuttle.Rate < 0 && shuttleClock.IsRunning && !engine.Flag("seeking"))
            {
                double target = Math.Max(0, shuttleStart + shuttleClock.Elapsed.TotalSeconds * shuttle.Rate);
                engine.Command("seek", F(target), "absolute+exact"); if (target <= 0) PauseShuttle();
            }
            if (shuttle.Rate < 0) { status.Text = "Revisión hacia atrás  " + F(shuttle.Rate) + "×"; speed.Text = F(shuttle.Rate) + "×"; play.Text = "Ⅱ"; }
            if (review != null)
            {
                long frame = Math.Max(0, Math.Min(review.DurationFrames - 1, (long)Math.Round(engine.Number("time-pos") * review.Fps)));
                time.Text = review.Timecode(frame); tips.SetToolTip(time, "Fotograma " + frame + " · Duración " + Clock(duration) + " · " + F(review.Fps) + " fps (NDF)");
            }
            if (summaryShown && tickCount % 10 == 0) UpdateSummary();
        }
        private void ToggleSummary()
        {
            summaryShown = !summaryShown;
            if (summaryShown) UpdateSummary(); else if (engine != null) engine.Command("show-text", "", "0");
        }
        private void UpdateSummary()
        {
            if (engine == null || engine.Get("path").Length == 0) return;
            string hw = engine.Get("hwdec-current");
            string summary = engine.Get("filename") + "\n" + engine.Get("video-format").ToUpperInvariant() + " · " + engine.VideoDepth() + " bits · " + engine.Get("width") + " × " + engine.Get("height") + "\n" + F(engine.Number("container-fps")) + " fps · " + engine.Get("video-params/primaries") + " · " + engine.Get("video-params/gamma") + "\n" + (hw == "no" || hw.Length == 0 ? "Decodificación: CPU" : "Decodificación: GPU / " + hw) + "\nI: ocultar   ·   Ctrl+I: información completa";
            engine.Command("show-text", summary, "3000");
        }
    }
}
