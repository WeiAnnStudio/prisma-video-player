// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
            notesPanel.Cloud += EditCloudFolder; notesPanel.SendResolve += SendToResolve;
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
            notesMenu.DropDownItems.Add(new ToolStripSeparator());
            notesMenu.DropDownItems.Add(Item("Enviar notas a DaVinci Resolve    Ctrl+Mayús+R", SendToResolve));
            notesMenu.DropDownItems.Add(Item("Carpeta en la nube para este vídeo…", EditCloudFolder));
            ToolStripMenuItem ai = new ToolStripMenuItem("IA");
            ai.DropDownItems.Add(Item("Conectar con Claude (app de escritorio)…", delegate { ConnectAssistant(true); }));
            ai.DropDownItems.Add(Item("Conectar con ChatGPT (app de escritorio)…", delegate { ConnectAssistant(false); }));
            ai.DropDownItems.Add(new ToolStripSeparator());
            ai.DropDownItems.Add(Item("Cómo pasar las notas a la nube con IA", ShowAssistantHelp));
            menu.Items.Insert(3, ai);
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
                    review.CloudFolder = saved.CloudFolder; review.SyncFolder = saved.SyncFolder; review.SyncHash = saved.SyncHash;
                    if (saved.FpsNumerator == review.FpsNumerator && saved.FpsDenominator == review.FpsDenominator && saved.DurationFrames == review.DurationFrames) review.Markers = saved.Markers;
                    else Notice("El vídeo ha cambiado de duración o FPS. Las notas guardadas se conservan para importarlas manualmente.", true);
                }
                catch (Exception ex) { Notice("No se pudieron leer las notas: " + ex.Message, true); }
            }
            notesPanel.RefreshMarkers(review); notesPanel.SetCloud(CloudLabel()); UpdateMarkerTicks();
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
            string delivered = "", failed = "";
            if (review.SyncFolder.Length > 0)
            {
                try { delivered = SyncDelivery.Write(review, review.SyncFolder); }
                catch (Exception ex) { failed = ex.Message; }
            }
            try { review.Save(reviewFile); Notice(failed.Length > 0 ? "Notas guardadas, pero no en la carpeta sincronizada: " + failed : delivered.Length > 0 ? delivered : "Notas guardadas", failed.Length > 0); }
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
                        case ".json": review.SaveForSharing(dialog.FileName); break;
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

        private string CloudLabel() { return SyncDelivery.Label(review); }

        private void EditCloudFolder()
        {
            if (review == null) { Notice("Abre un vídeo para asociarle una carpeta.", false); return; }
            string typed = review.CloudFolder, video = Path.GetFileNameWithoutExtension(review.MediaPath), problem = "";
            while (true)
            {
                string body = problem + "Dónde quieres las notas de «" + Path.GetFileName(review.MediaPath) + "»:\r\n\r\n" +
                    "AUTOMÁTICO · Carpeta del equipo: elige una carpeta que Google Drive, Dropbox u OneDrive sincronicen en este PC (por ejemplo G:\\Mi unidad\\Revisiones). Cada vez que cambies una nota, Prisma escribe ahí «Ajustes " + video + ".docx» y «" + video + ".prisma.json», y la aplicación de la nube los sube. Sin IA y sin pedir nada.\r\n" +
                    "Ahora: " + (review.SyncFolder.Length > 0 ? review.SyncFolder : "ninguna") + "\r\n\r\n" +
                    "CON IA · Enlace: pega abajo el enlace de una carpeta de Google Drive, Dropbox u OneDrive y pide a Claude o ChatGPT «Pasa a la nube las notas de " + video + "». Crea un Google Doc nativo.\r\n" +
                    "Prisma no se conecta a la nube en ningún caso: solo guarda la carpeta o el enlace junto a las notas.";
                TextBox input = new TextBox { Text = typed, MaxLength = CloudFolder.MaxLength };
                Choice choice = Dialogs.Show(this, dpiScale, "Carpeta en la nube", body, "Guardar enlace", "Elegir carpeta del equipo…", "Cancelar", input,
                    review.SyncFolder.Length > 0 || review.CloudFolder.Length > 0 ? "Quitar todo" : null);
                if (choice == Choice.Cancel) return;
                typed = input.Tag as string ?? input.Text;
                if (choice == Choice.Extra) { review.CloudFolder = ""; review.SyncFolder = ""; review.SyncHash = ""; break; }
                if (choice == Choice.Alternative)
                {
                    using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Carpeta sincronizada con Google Drive, Dropbox u OneDrive para las notas de «" + Path.GetFileName(review.MediaPath) + "»", ShowNewFolderButton = true })
                    {
                        string start = review.SyncFolder.Length > 0 ? review.SyncFolder : SyncDelivery.SuggestedStart();
                        if (start.Length > 0 && Directory.Exists(start)) picker.SelectedPath = start;
                        if (picker.ShowDialog(this) != DialogResult.OK) continue;
                        if (!SyncDelivery.IsValidFolder(picker.SelectedPath)) { problem = "Esa carpeta no sirve. Elige una carpeta, no la raíz de un disco.\r\n\r\n"; continue; }
                        if (review.SyncFolder != picker.SelectedPath) review.SyncHash = "";
                        review.SyncFolder = picker.SelectedPath;
                    }
                    if (review.Markers.Count == 0) Notice("Carpeta guardada. Prisma escribirá ahí en cuanto añadas la primera nota.", false);
                    break;
                }
                if (typed.Trim().Length == 0) { review.CloudFolder = ""; break; }
                string url, service;
                if (CloudFolder.TryNormalize(typed, out url, out service)) { review.CloudFolder = url; break; }
                problem = "Ese enlace no es una carpeta de Google Drive, Dropbox ni OneDrive. Copia la dirección completa desde el navegador, por ejemplo https://drive.google.com/drive/folders/…\r\n\r\n";
            }
            SaveReview(); notesPanel.SetCloud(CloudLabel());
        }

        private void ShowXmlFallback(string reason)
        {
            string body = reason + "\r\n\r\nLa solución es exportar un XML con el vídeo y las notas:\r\n\r\n" +
                "1. Pulsa «Exportar XML ahora» y guarda el archivo junto al vídeo.\r\n" +
                "2. En DaVinci Resolve, abre el proyecto y ve a Archivo > Importar > Línea de tiempo… (Ctrl+Mayús+I).\r\n" +
                "3. Elige el XML y deja marcada la importación automática de los clips al panel multimedia.\r\n" +
                "4. Resolve crea una línea de tiempo nueva con el vídeo y cada nota como marcador. Si el clip sale sin conexión, vuelve a vincularlo con clic derecho > Relink.\r\n\r\n" +
                "Antes de importar, el proyecto debe tener la misma velocidad de fotogramas que el vídeo.";
            if (Dialogs.Show(this, dpiScale, "Enviar a DaVinci Resolve", body, "Exportar XML ahora", null, "Cerrar", null) == Choice.Accept) ExportMarkers();
        }

        private bool sendingToResolve;
        private void SendToResolve()
        {
            if (review == null || engine == null) { Notice("Abre un vídeo antes de enviar sus notas.", false); return; }
            if (sendingToResolve) return;
            notesPanel.CommitEdit();
            if (review.Markers.Count == 0) { Notice("Este vídeo todavía no tiene notas.", false); return; }
            ResolveInstall install = ResolveBridge.Detect();
            if (install.Edition == ResolveEdition.NotInstalled) { ShowXmlFallback("No se ha encontrado DaVinci Resolve en este equipo."); return; }
            // The edition is only known for sure once Resolve answers: the installer registers the
            // same name for both editions. A failed connection explains both cases (see default below).
            if (!ResolveBridge.IsRunning())
            {
                Dialogs.Show(this, dpiScale, "Enviar a DaVinci Resolve", "Abre DaVinci Resolve con el proyecto y la línea de tiempo donde quieres las notas, y vuelve a pulsar «Enviar a Resolve».", "Entendido", null, null, null);
                return;
            }
            string work = Path.Combine(Path.GetDirectoryName(settingsPath), "resolve");
            try { ResolveBridge.Prepare(review, work); }
            catch (Exception ex) { Notice("No se pudieron preparar las notas: " + ex.Message, true); return; }
            sendingToResolve = true; Notice("Enviando notas a DaVinci Resolve…", false); Cursor = Cursors.WaitCursor;
            Task.Factory.StartNew(() => ResolveBridge.Run(install, work, 30000)).ContinueWith(t =>
            {
                sendingToResolve = false; Cursor = Cursors.Default;
                ShowResolveResult(t.IsFaulted ? new Dictionary<string, string> { { "status", "ERROR" }, { "message", t.Exception.GetBaseException().Message } } : t.Result, install);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static string Value(Dictionary<string, string> r, string key) { string v; return r.TryGetValue(key, out v) ? v : ""; }

        private void ShowResolveResult(Dictionary<string, string> r, ResolveInstall install)
        {
            string status = Value(r, "status"), timeline = Value(r, "timeline");
            const string title = "Enviar a DaVinci Resolve";
            switch (status)
            {
                case "OK":
                    string summary = Value(r, "added") + " notas añadidas a la línea de tiempo «" + timeline + "» como marcadores rojos.";
                    if (Value(r, "present") != "0") summary += "\r\n" + Value(r, "present") + " ya estaban y no se han duplicado.";
                    if (Value(r, "moved") != "0") summary += "\r\n" + Value(r, "moved") + " se han movido al siguiente fotograma libre porque el suyo ya tenía un marcador.";
                    if (Value(r, "outside") != "0") summary += "\r\n" + Value(r, "outside") + " quedan después del final de la línea de tiempo y no se han añadido.";
                    if (Value(r, "failed") != "0") summary += "\r\n" + Value(r, "failed") + " no se han podido añadir.";
                    Notice(Value(r, "added") + " notas enviadas a Resolve", false);
                    Dialogs.Show(this, dpiScale, title, summary + "\r\n\r\nLas posiciones se cuentan desde el primer fotograma de la línea de tiempo, como en el render revisado.", "Cerrar", null, null, null);
                    break;
                case "FPS":
                    Dialogs.Show(this, dpiScale, title, "La línea de tiempo «" + timeline + "» va a " + Value(r, "timeline_fps") + " fps y las notas a " + Value(r, "notes_fps") + " fps. No se ha añadido ninguna nota.\r\n\r\nAbre en Resolve la línea de tiempo de este render, con su misma velocidad de fotogramas, y vuelve a enviarlas.", "Cerrar", null, null, null);
                    break;
                case "NO_PROJECT":
                case "NO_TIMELINE":
                    Dialogs.Show(this, dpiScale, title, "DaVinci Resolve está abierto, pero no tiene ninguna línea de tiempo activa. Abre el proyecto y la línea de tiempo donde quieres las notas, y vuelve a enviarlas.", "Cerrar", null, null, null);
                    break;
                case "NOT_STUDIO":
                    ShowXmlFallback("Tienes la versión gratuita de DaVinci Resolve. Solo Resolve Studio permite que otra aplicación añada marcadores a la línea de tiempo abierta.");
                    break;
                default:
                    string body = "Prisma no ha podido conectar con DaVinci Resolve.\r\n\r\n" +
                        "En Resolve Studio, activa la conexión una sola vez: DaVinci Resolve > Preferencias > Sistema > General > Scripting externo (External scripting using): Local. Guarda y reinicia Resolve.\r\n\r\n" +
                        "Si tu Resolve es la versión gratuita, no admite esta conexión: exporta el XML e impórtalo como línea de tiempo.";
                    if (Value(r, "message").Length > 0) body += "\r\n\r\nDetalle: " + Value(r, "message");
                    if (Dialogs.Show(this, dpiScale, title, body, "Cerrar", "Exportar XML", null, null) == Choice.Alternative) ExportMarkers();
                    break;
            }
        }

        private void ConnectAssistant(bool claude)
        {
            string app = claude ? "Claude" : "ChatGPT";
            string exe = Application.ExecutablePath;
            string body = "Prisma se añadirá a " + app + " como herramienta local de solo lectura. " + app + " abrirá Prisma en segundo plano cuando necesite tus notas; no hace falta tener Prisma abierto ni ejecutar nada más.\r\n\r\n" +
                "No tienes que escribir contraseñas ni claves: la conexión ocurre dentro de tu equipo. Para guardar el documento en Google Drive, Dropbox u OneDrive, " + app + " usa el conector de ese servicio que ya tengas activado en la propia app.\r\n\r\n" +
                "Se modificará este archivo de configuración (se guarda una copia del original):\r\n" + string.Join("\r\n", claude ? AiConnect.ClaudeConfigFiles().ToArray() : new string[] { AiConnect.ChatGptConfigFile() }) +
                "\r\n\r\nSi mueves la carpeta de Prisma, vuelve a conectar.";
            if (Dialogs.Show(this, dpiScale, "Conectar con " + app, body, "Conectar", null, "Cancelar", null) != Choice.Accept) return;
            try
            {
                if (claude) foreach (string file in AiConnect.ClaudeConfigFiles()) AiConnect.WriteWithBackup(file, AiConnect.MergeClaudeConfig(AiConnect.ReadOrEmpty(file), exe));
                else { string file = AiConnect.ChatGptConfigFile(); AiConnect.WriteWithBackup(file, AiConnect.MergeChatGptConfig(AiConnect.ReadOrEmpty(file), exe)); }
            }
            catch (Exception ex)
            {
                Dialogs.Show(this, dpiScale, "Conectar con " + app, "No se pudo modificar la configuración de " + app + ": " + ex.Message + "\r\n\r\nPuedes añadir el servidor a mano: nombre «prisma», tipo STDIO, comando:\r\n" + exe + "\r\ncon el argumento --mcp", "Cerrar", null, null, null);
                return;
            }
            Dialogs.Show(this, dpiScale, "Conectado con " + app, "Listo. Cierra " + app + " por completo (también desde la bandeja del sistema) y vuelve a abrirlo.\r\n\r\n" +
                "Después, en un chat nuevo, pide por ejemplo: «Pasa a la nube las notas de Corto 12». " + app + " leerá las notas de Prisma y creará el documento «Ajustes Corto 12» en la carpeta que asociaste al vídeo.\r\n\r\n" +
                (claude ? "" : "En cuentas de empresa, el administrador puede tener desactivados los servidores locales. Si Prisma no aparece en Ajustes > MCP servers, consúltalo con él.\r\n\r\n") +
                "Asegúrate de tener activado en " + app + " el conector de Google Drive, Dropbox u OneDrive, según dónde esté la carpeta.", "Cerrar", null, null, null);
        }

        private void ShowAssistantHelp()
        {
            ShowText("Notas en la nube con IA",
                "1. En el panel de notas, pulsa «Carpeta en la nube» y pega el enlace de la carpeta de Google Drive, Dropbox u OneDrive para ese vídeo. Es el único paso manual.\r\n\r\n" +
                "2. Una sola vez: menú IA > Conectar con Claude o con ChatGPT, y reinicia esa app. No pide contraseñas: la app abre Prisma en segundo plano en tu equipo.\r\n\r\n" +
                "3. En esa app, activa el conector del servicio donde está la carpeta (Google Drive, Dropbox u OneDrive).\r\n\r\n" +
                "4. Pide en un chat: «Pasa a la nube las notas de <nombre del vídeo>». La IA crea el documento «Ajustes <vídeo>» con el título en negrita y una línea por nota con su código de tiempo. Si el documento ya existe, añade solo lo nuevo.\r\n\r\n" +
                "Qué ve la IA: el nombre del archivo (nunca la ruta de tu equipo), los FPS, la carpeta asociada y el texto de las notas. Prisma no puede borrar ni cambiar nada desde la IA.");
        }
    }
}
