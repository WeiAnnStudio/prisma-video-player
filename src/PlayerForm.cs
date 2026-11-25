// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prisma
{
    internal sealed partial class PlayerForm : Form
    {
        private MpvEngine engine;
        private readonly Settings settings;
        private readonly string settingsPath;
        private readonly string[] startupFiles;
        private readonly MenuStrip menu = new MenuStrip();
        private readonly Panel header = new Panel(), transport = new Panel(), footer = new Panel(), queue = new Panel();
        private readonly VideoSurface video = new VideoSurface();
        private readonly Label brand = LabelOf("P R I S M A", 13, Theme.Ink), fileName = LabelOf("Reproductor de vídeo", 9, Theme.Muted);
        private readonly Label status = LabelOf("Listo para abrir un vídeo", 9, Theme.Muted), format = LabelOf("", 9, Theme.Accent);
        private readonly Label time = LabelOf("00:00 / 00:00", 10, Theme.Ink);
        private readonly FlatButton open = new FlatButton("Abrir vídeo", "Abrir vídeo", true);
        private readonly FlatButton info = new FlatButton("Información", "Información del vídeo", false);
        private readonly FlatButton queueButton = new FlatButton("Lista", "Mostrar lista de reproducción", false);
        private readonly FlatButton play = new FlatButton("▶", "Reproducir o pausar", true);
        private readonly FlatButton previous = new FlatButton("|◀", "Archivo anterior", false), next = new FlatButton("▶|", "Archivo siguiente", false);
        private readonly FlatButton stop = new FlatButton("■", "Detener", false);
        private readonly FlatButton back = new FlatButton("‹|", "Fotograma anterior", false), forward = new FlatButton("|›", "Fotograma siguiente", false);
        private readonly FlatButton speed = new FlatButton("1×", "Velocidad de reproducción", false);
        private readonly FlatButton mute = new FlatButton("Sonido", "Silenciar o activar sonido", false);
        private readonly FlatButton full = new FlatButton("⛶", "Pantalla completa", false);
        private readonly SeekBar seek = new SeekBar(), volume = new SeekBar();
        private readonly ListBox playlist = new ListBox();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly ToolTip tips = new ToolTip();
        private ToolStripMenuItem recentMenu, audioMenu, subtitleMenu, hardwareItem, autoColorItem, sdrColorItem, repeatItem;
        private bool fullscreen, wasMaximized, updatingQueue, queueShown, closing, stopped;
        private Rectangle normalBounds;
        private double duration;
        private int tickCount, queueCount = -1;
        private long lastDrop = -1;
        private readonly object incomingLock = new object();
        private readonly List<string[]> incoming = new List<string[]>();
        private DateTime lastIncoming = DateTime.MinValue;
        private string queueSignature = "", lastError = "";
        private float dpiScale = 1;
        private DateTime statusUntil;

        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public PlayerForm(string[] files, string dataPath)
        {
            startupFiles = files; settingsPath = dataPath; settings = Settings.Load(dataPath);
            Text = "Prisma — Reproductor de vídeo";
            BackColor = Theme.Background; ForeColor = Theme.Ink; Font = new Font("Segoe UI", 10);
            AutoScaleMode = AutoScaleMode.None; KeyPreview = true; AllowDrop = true;
            using (Graphics g = CreateGraphics()) dpiScale = g.DpiX / 96f;
            MinimumSize = new Size(S(790), S(550));
            Rectangle area = Screen.FromControl(this).WorkingArea;
            bool hasSavedSize = File.Exists(dataPath);
            Size = hasSavedSize ? new Size(Math.Min(settings.Width, area.Width), Math.Min(settings.Height, area.Height)) : new Size(Math.Min(S(1100), (int)(area.Width * .8)), Math.Min(S(730), (int)(area.Height * .85)));
            StartPosition = FormStartPosition.Manual;
            Location = new Point(area.X + (area.Width - Width) / 2, area.Y + (area.Height - Height) / 2);
            Rectangle saved = new Rectangle(settings.X, settings.Y, Width, Height);
            if (settings.X != -1 && Screen.AllScreens.Any(delegate(Screen sc) { return sc.WorkingArea.Contains(new Rectangle(saved.X, saved.Y, Math.Min(saved.Width, S(200)), S(80))); })) Location = saved.Location;
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Prisma.ico");
            if (File.Exists(iconPath)) Icon = new Icon(iconPath);
            BuildMenus();
            menu.BackColor = Theme.Background; menu.ForeColor = Theme.Ink; menu.Renderer = new DarkRenderer(); menu.Dock = DockStyle.None; menu.Font = new Font("Segoe UI", 9);
            header.BackColor = Theme.Background; transport.BackColor = Theme.Panel; footer.BackColor = Theme.Background;
            brand.Font = new Font("Segoe UI Semibold", 13); time.Font = new Font("Consolas", 10);
            open.Icon = Symbol.Folder; info.Icon = Symbol.Info; notesButton.Icon = Symbol.Notes; queueButton.Icon = Symbol.List;
            info.Text = "Info"; notesButton.Text = "Notas";
            play.Icon = Symbol.Play; previous.Icon = Symbol.Previous; next.Icon = Symbol.Next; stop.Icon = Symbol.Stop;
            back.Icon = Symbol.FrameBack; forward.Icon = Symbol.FrameNext; mute.Icon = Symbol.Volume; full.Icon = Symbol.Fullscreen;
            foreach(FlatButton button in new FlatButton[]{play,previous,next,stop,back,forward,mute,full}) button.IconOnly=true;
            speed.Outlined = true;
            header.Paint += delegate(object sender, PaintEventArgs e) { using(Pen p=new Pen(Theme.Border)) e.Graphics.DrawLine(p,S(20),header.Height-1,header.Width-S(20),header.Height-1); };
            Controls.AddRange(new Control[] { menu, header, video, queue, transport, footer });
            header.Controls.AddRange(new Control[] { brand, fileName, open, info, queueButton });
            transport.Controls.AddRange(new Control[] { seek, previous, play, stop, next, back, forward, time, speed, mute, volume, full });
            footer.Controls.AddRange(new Control[] { status, format });
            queue.BackColor = Theme.Panel; queue.Visible = false;
            Label queueTitle = LabelOf("Lista de reproducción", 13, Theme.Ink); queueTitle.Font = new Font("Segoe UI Semibold",13); queueTitle.Dock = DockStyle.Top; queueTitle.Height = S(64); queueTitle.Padding = new Padding(S(18), 0, 0, 0);
            playlist.Dock = DockStyle.Fill; playlist.BackColor = Theme.Panel; playlist.ForeColor = Theme.Ink; playlist.BorderStyle = BorderStyle.None;
            Theme.DarkScrollbars(playlist);
            playlist.DrawMode = DrawMode.OwnerDrawFixed; playlist.ItemHeight = S(72); playlist.IntegralHeight = false; playlist.AccessibleName = "Lista de reproducción";
            playlist.DrawItem += DrawQueueItem;
            queue.Controls.Add(playlist); queue.Controls.Add(queueTitle);
            InitializeReview();
            InitializeView();
            notesPanel.SetScale(dpiScale);
            foreach (Control c in transport.Controls) if (c is FlatButton) tips.SetToolTip(c, c.AccessibleName);
            tips.SetToolTip(open, "Abrir uno o varios archivos (Ctrl+O)");
            tips.SetToolTip(info, "Códec, profundidad de color y decodificación (Ctrl+I)");
            tips.SetToolTip(notesButton, "Notas de revisión (Ctrl+M) · Añadir nota (M)");
            tips.SetToolTip(queueButton, "Lista de reproducción (Ctrl+L)");
            tips.SetToolTip(volume, "Volumen");
            volume.AccessibleName = "Volumen";
            open.Click += delegate { ChooseFiles(false); };
            info.Click += delegate { ShowInfo(); };
            queueButton.Click += delegate { ToggleQueue(); };
            play.Click += delegate { TogglePlay(); };
            stop.Click += delegate { StopPlayback(); };
            previous.Click += delegate { MovePlaylist(-1); };
            next.Click += delegate { MovePlaylist(1); };
            back.Click += delegate { StepFrame(-1); };
            forward.Click += delegate { StepFrame(1); };
            full.Click += delegate { ToggleFullscreen(); };
            mute.Click += delegate { if (engine != null) engine.Command("cycle", "mute"); };
            speed.Click += delegate { ShowSpeedMenu(); };
            volume.Fraction = settings.Volume / 100.0;
            volume.Seek += delegate(double value) { settings.Volume = (int)Math.Round(value * 100); if (engine != null) engine.Set("volume", settings.Volume.ToString()); };
            seek.Seek += delegate(double value) { if (engine != null && duration > 0) { engine.Command("seek", P(review != null ? FrameAt(value * duration) / review.Fps : value * duration), "absolute+exact"); stopped = false; } };
            seek.Scrub += ScrubTo;
            playlist.DoubleClick += delegate { if (!updatingQueue && playlist.SelectedIndex >= 0 && engine != null) { engine.Set("playlist-pos", playlist.SelectedIndex.ToString()); engine.Set("pause", "no"); stopped = false; } };
            video.DoubleClick += delegate { if (engine != null && !video.Empty) ToggleFullscreen(); else ChooseFiles(false); };
            DragEnter += OnDragEnter; DragDrop += OnDrop; video.AllowDrop = true; video.DragEnter += OnDragEnter; video.DragDrop += OnDrop;
            playlist.AllowDrop = true; playlist.DragEnter += OnDragEnter; playlist.DragDrop += delegate(object sender, DragEventArgs e) { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop), true); };
            Resize += delegate { LayoutPlayer(); };
            Shown += StartEngine;
            FormClosing += SaveAndClose;
            timer.Interval = 100; timer.Tick += Tick;
            LayoutPlayer();
        }

        private static Label LabelOf(string text, float size, Color color)
        {
            return new Label { Text = text, ForeColor = color, Font = new Font("Segoe UI", size), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, BackColor = Color.Transparent };
        }
        private int S(int value) { return (int)Math.Round(value * dpiScale); }
        private static string F(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
        private ToolStripMenuItem Item(string text, Action action)
        {
            ToolStripMenuItem result = new ToolStripMenuItem(text); result.Click += delegate { action(); }; return result;
        }
        private void BuildMenus()
        {
            ToolStripMenuItem file = new ToolStripMenuItem("Archivo");
            file.DropDownItems.Add(Item("Abrir vídeo…    Ctrl+O", delegate { ChooseFiles(false); }));
            file.DropDownItems.Add(Item("Añadir a la lista…    Ctrl+Mayús+O", delegate { ChooseFiles(true); }));
            recentMenu = new ToolStripMenuItem("Archivos recientes"); recentMenu.DropDownOpening += delegate { RefreshRecent(); };
            file.DropDownItems.Add(recentMenu);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item("Salir    Alt+F4", Close));
            ToolStripMenuItem playback = new ToolStripMenuItem("Reproducción");
            playback.DropDownItems.Add(Item("Reproducir / pausar    Espacio", TogglePlay));
            playback.DropDownItems.Add(Item("Detener    S", StopPlayback));
            playback.DropDownItems.Add(Item("Volver al principio    Inicio", delegate { if (engine != null) engine.Command("seek", "0", "absolute+exact"); }));
            playback.DropDownItems.Add(Item("Fotograma siguiente    →", delegate { StepFrame(1); }));
            playback.DropDownItems.Add(Item("Fotograma anterior    ←", delegate { StepFrame(-1); }));
            ToolStripMenuItem speeds = new ToolStripMenuItem("Velocidad"); AddSpeeds(speeds.DropDownItems); playback.DropDownItems.Add(speeds);
            repeatItem = Item("Repetir este vídeo", delegate { if (engine != null) { bool repeat = engine.Get("loop-file") != "inf"; engine.Set("loop-file", repeat ? "inf" : "no"); repeatItem.Checked = repeat; } }); playback.DropDownItems.Add(repeatItem);
            audioMenu = new ToolStripMenuItem("Audio"); audioMenu.DropDownOpening += delegate { BuildTracks(audioMenu, "audio"); };
            subtitleMenu = new ToolStripMenuItem("Subtítulos"); subtitleMenu.DropDownOpening += delegate { BuildTracks(subtitleMenu, "sub"); };
            // Placeholders make the dynamic menus open before any file has been loaded.
            audioMenu.DropDownItems.Add("Sin pistas"); subtitleMenu.DropDownItems.Add("Sin pistas"); recentMenu.DropDownItems.Add("Sin archivos recientes");
            ToolStripMenuItem view = new ToolStripMenuItem("Ver");
            view.DropDownItems.Add(Item("Pantalla completa    F11", ToggleFullscreen));
            ToolStripMenuItem size = new ToolStripMenuItem("Tamaño de la imagen"); size.DropDownOpening += delegate { size.DropDownItems.Clear(); AddZoomItems(size.DropDownItems); };
            size.DropDownItems.Add("Ajustar"); view.DropDownItems.Add(size);
            view.DropDownItems.Add(Item("Lista de reproducción    Ctrl+L", ToggleQueue));
            view.DropDownItems.Add(Item("Información del vídeo    Ctrl+I", ShowInfo));
            view.DropDownItems.Add(new ToolStripSeparator());
            hardwareItem = Item("Aceleración por hardware", delegate { if (engine != null && engine.Set("hwdec", settings.Hardware ? "no" : "auto-safe")) { settings.Hardware = !settings.Hardware; hardwareItem.Checked = settings.Hardware; } });
            hardwareItem.Checked = settings.Hardware; view.DropDownItems.Add(hardwareItem);
            ToolStripMenuItem color = new ToolStripMenuItem("Color y HDR");
            autoColorItem = Item("Automático según la pantalla", delegate { SetColor("auto"); });
            sdrColorItem = Item("Adaptar HDR a pantalla SDR", delegate { SetColor("sdr"); });
            autoColorItem.Checked = settings.ColorMode == "auto"; sdrColorItem.Checked = settings.ColorMode == "sdr";
            color.DropDownItems.Add(autoColorItem); color.DropDownItems.Add(sdrColorItem); view.DropDownItems.Add(color);
            ToolStripMenuItem help = new ToolStripMenuItem("Ayuda");
            help.DropDownItems.Add(Item("Atajos de teclado", delegate { ShowText("Atajos de teclado", "Ctrl+O   Abrir uno o varios vídeos\r\nCtrl+Mayús+O   Añadir a la lista\r\nEspacio   Reproducir / pausar\r\n← / →   Fotograma anterior / siguiente\r\nMayús+← / →   Retroceder / avanzar 5 segundos\r\nJ / K / L   Retroceder / pausar / avanzar\r\nRepite J o L para acelerar hasta 16×; la tecla contraria frena.\r\nMayús+J / L   Revisión a media velocidad\r\nI   Resumen sobre el vídeo\r\nCtrl+I   Información completa\r\nM   Añadir marcador con nota\r\nCtrl+M   Mostrar notas\r\nCtrl+Intro   Guardar nota en edición\r\n↑ / ↓   Volumen\r\nCtrl+Mayús+M   Silenciar\r\nInicio   Volver al principio\r\nS   Detener\r\nF o F11   Pantalla completa\r\nEsc   Salir de pantalla completa\r\nCtrl + / Ctrl −   Acercar / alejar la imagen (también con la rueda sobre el vídeo)\r\nCtrl+0   Ajustar la imagen a la ventana\r\nCtrl+1   Imagen al 100 % (píxel a píxel)\r\nArrastrar   Mover la imagen ampliada · Clic central: ajustar / 100 %\r\nClic en el código de tiempo   Copiarlo o escribir otro para saltar\r\nCtrl+G   Ir a un código de tiempo (00:00:03:10, 3:10, 310, +10)\r\nCtrl+Mayús+C   Copiar el código de tiempo\r\nCtrl+T   (editando una nota) Insertar el código de tiempo actual\r\nCtrl+L   Mostrar lista\r\n\r\nDoble clic sobre el vídeo: pantalla completa.\r\nEl retroceso continuo usa búsquedas precisas, sin audio, y puede ir a saltos con archivos exigentes."); }));
            help.DropDownItems.Add(Item("Acerca de Prisma", delegate { ShowText("Acerca de Prisma", "PRISMA  ·  Versión 0.5\r\nReproductor de vídeo para Windows, en español.\r\n\r\nPrisma no se conecta a internet ni envía datos. Solo abre los archivos que eliges tú.\r\n\r\nInterfaz propia con motor libmpv y FFmpeg.\r\nMotor: " + (engine == null ? "no disponible" : engine.Get("mpv-version")) + "\r\n\r\nCódigo abierto: GPL-3.0-o-posterior.\r\nSin garantía. Licencias y procedencia en la carpeta licencias.\r\n\r\nLa profundidad de color indicada corresponde al archivo. La salida final depende de Windows, la GPU, el cable y la pantalla.\r\n\r\nProRes estándar es compatible; ProRes RAW no está incluido."); }));
            menu.Items.AddRange(new ToolStripItem[] { file, playback, audioMenu, subtitleMenu, view, help }); MainMenuStrip = menu;
        }

        private void LayoutPlayer()
        {
            if (header == null || ClientSize.Width == 0) return;
            int w = ClientSize.Width, h = ClientSize.Height;
            int mh = fullscreen ? 0 : S(28), hh = fullscreen ? 0 : S(76), th = S(100), fh = S(28), gap = fullscreen ? 0 : S(12);
            menu.Visible = header.Visible = !fullscreen;
            menu.SetBounds(0, 0, w, mh); header.SetBounds(0, mh, w, hh);
            brand.SetBounds(S(24), S(10), S(180), S(28));
            fileName.SetBounds(S(24), S(38), Math.Max(0, w - S(480)), S(23));
            open.SetBounds(w - S(432), S(19), S(130), S(36));
            info.SetBounds(w - S(290), S(19), S(78), S(36));
            notesButton.SetBounds(w - S(202), S(19), S(90), S(36));
            queueButton.SetBounds(w - S(102), S(19), S(78), S(36));
            int qw = notesShown ? S(350) : queueShown ? S(300) : 0;
            video.SetBounds(gap, mh + hh + gap, Math.Max(1, w - qw - gap*2 - (qw>0 ? gap : 0)), Math.Max(1, h - mh - hh - th - fh - gap*2));
            queue.SetBounds(w - qw - gap, mh + hh + gap, qw, video.Height); queue.Visible = queueShown;
            notesPanel.SetBounds(w - qw - gap, mh + hh + gap, qw, video.Height); notesPanel.Visible = notesShown;
            notesButton.Active=notesShown; queueButton.Active=queueShown;
            transport.SetBounds(0, h - th - fh, w, th); footer.SetBounds(0, h - fh, w, fh);
            seek.SetBounds(S(16), S(4), w - S(32), S(28));
            int y = S(42), bh = S(36);
            previous.SetBounds(S(18), y, S(32), bh); play.SetBounds(S(58), y - S(4), S(48), S(44));
            next.SetBounds(S(114), y, S(32), bh); stop.SetBounds(S(152), y, S(32), bh);
            back.SetBounds(S(200), y, S(34), bh); forward.SetBounds(S(238), y, S(34), bh);
            time.SetBounds(S(292), y, Math.Max(S(140), w - S(652)), bh);
            if (timecodeBox.Visible) timecodeBox.SetBounds(time.Left, y + (bh - timecodeBox.Height) / 2, S(122), timecodeBox.Height);
            zoomButton.SetBounds(w - S(340), y, S(66), bh);
            speed.SetBounds(w - S(266), y, S(52), bh); mute.SetBounds(w - S(202), y, S(34), bh);
            volume.SetBounds(w - S(162), y + S(4), S(92), S(28)); full.SetBounds(w - S(58), y, S(36), bh);
            status.SetBounds(S(20), 0, w * 2 / 3 - S(20), fh); format.SetBounds(w * 2 / 3, 0, w / 3 - S(20), fh); format.TextAlign = ContentAlignment.MiddleRight;
            video.Invalidate();
            ApplyZoom();
        }

        private void StartEngine(object sender, EventArgs e)
        {
            int dark = 1;
            try { DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
            try
            {
                engine = new MpvEngine(video.Handle, AppDomain.CurrentDomain.BaseDirectory, settings.Hardware, settings.ColorMode, false);
                engine.Event += EngineEvent; engine.Warning += delegate(string message) { Notice(message, true); };
                engine.Set("volume", settings.Volume.ToString()); timer.Start();
                if (startupFiles.Length > 0) AddFiles(startupFiles, false);
                ProcessIncoming();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo iniciar el motor de vídeo. Mantén Prisma.exe y libmpv-2.dll en la misma carpeta.\r\n\r\nDetalle: " + ex.Message, "Prisma", MessageBoxButtons.OK, MessageBoxIcon.Error);
                status.Text = "Motor no disponible";
            }
        }
        private void EngineEvent(int id, string detail)
        {
            if (id == 6) { Notice("Abriendo vídeo…", false); lastError = ""; }
            if (id == 8)
            {
                stopped = false;
                fileName.Text = engine.Get("filename"); Text = fileName.Text + " — Prisma";
                video.Empty = !Enumerable.Range(0, (int)engine.Number("track-list/count")).Any(i => engine.Get("track-list/" + i + "/type") == "video");
                statusUntil = DateTime.MinValue; EndTimecodeEdit(); ResetZoom();
                video.Invalidate(); RefreshQueue(); UpdateMetadata();
                LoadReview();
            }
            if (id == 7 && detail.Length > 0)
            {
                lastError = detail; Notice(detail, true); video.Empty = true; video.Invalidate();
            }
        }
        private void Tick(object sender, EventArgs e)
        {
            if (engine == null || closing) return;
            engine.Pump();
            duration = engine.Number("duration"); double pos = engine.Number("time-pos");
            bool idle = engine.Flag("idle-active"), paused = engine.Flag("pause"), eof = engine.Flag("eof-reached");
            if (eof && duration > 0) pos = duration;
            if (!seek.Dragging) seek.Fraction = duration > 0 ? pos / duration : 0;
            seek.Enabled = duration > 0;
            time.Text = Clock(pos) + " / " + Clock(duration);
            play.Text = !idle && !paused && !eof ? "Ⅱ" : "▶";
            if (!volume.Dragging) volume.Fraction = engine.Number("volume") / 100;
            mute.Text = engine.Flag("mute") ? "Silencio" : "Sonido";
            speed.Text = F(engine.Number("speed")) + "×";
            if (DateTime.Now > statusUntil)
            {
                status.ForeColor = Theme.Muted;
                status.Text = lastError.Length > 0 ? lastError : idle ? "Listo para abrir un vídeo" : eof ? "Vídeo terminado · Pulsa reproducir para volver a verlo" : stopped ? "Detenido · Pulsa reproducir para comenzar" : paused ? "En pausa" : "Reproduciendo";
            }
            if (++tickCount % 10 == 0) { UpdateMetadata(); RefreshQueue(); }
            CheckDroppedFiles();
            CheckVideoSize();
            TickReview();
        }
        internal static string Clock(double seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(Math.Max(0, Math.Min(seconds, 3599999)));
            return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString("00") + ":" + t.Minutes.ToString("00") + ":" + t.Seconds.ToString("00") : ((int)t.TotalMinutes).ToString("00") + ":" + t.Seconds.ToString("00");
        }
        private void UpdateMetadata()
        {
            if (engine == null) return;
            string codec = engine.Get("video-format").ToUpperInvariant(), bits = engine.VideoDepth();
            if (codec.Length == 0) { format.Text = ""; return; }
            format.Text = codec + (bits.Length > 0 ? "  ·  " + bits + " bits" : "") + "  ·  " + engine.Get("video-params/w") + " × " + engine.Get("video-params/h");
        }
        private void Notice(string text, bool error)
        {
            status.Text = text; status.ForeColor = error ? Color.FromArgb(255, 166, 148) : Theme.Muted; statusUntil = DateTime.Now.AddSeconds(error ? 12 : 3);
        }
        private void ChooseFiles(bool append)
        {
            using (OpenFileDialog dialog = new OpenFileDialog { Title = append ? "Añadir vídeos a la lista" : "Abrir vídeo", Multiselect = true, Filter = "Vídeos|*.mp4;*.mkv;*.mov;*.mxf;*.avi;*.m4v;*.webm;*.ts;*.mts;*.m2ts;*.mpg;*.mpeg;*.wmv;*.vob;*.hevc;*.h265;*.h264|Todos los archivos|*.*" })
                if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames, append);
        }
        internal void AddFiles(string[] files, bool append)
        {
            if (engine == null || files == null) return;
            notesPanel.CommitEdit();
            string[] valid = files.Where(File.Exists).Select(Path.GetFullPath).ToArray();
            if (valid.Length == 0) { Notice("No se encontró ningún archivo que se pueda abrir.", true); return; }
            bool adding = append && engine.Number("playlist/count") > 0;
            foreach (string path in valid)
            {
                engine.Command("loadfile", path, adding ? "append" : "replace"); adding = true;
                settings.Recent.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase)); settings.Recent.Insert(0, path);
            }
            if (settings.Recent.Count > 12) settings.Recent.RemoveRange(12, settings.Recent.Count - 12);
            if (!append || engine.Flag("idle-active")) { engine.Set("pause", "no"); stopped = false; }
            if (valid.Length > 1) { queueShown = true; LayoutPlayer(); }
        }
        private void RefreshRecent()
        {
            recentMenu.DropDownItems.Clear();
            foreach (string path in settings.Recent.Where(File.Exists).Take(12))
            {
                string file = path; ToolStripMenuItem item = Item(Path.GetFileName(file).Replace("&", "&&"), delegate { AddFiles(new string[] { file }, false); }); item.ToolTipText = path; recentMenu.DropDownItems.Add(item);
            }
            if (recentMenu.DropDownItems.Count == 0) recentMenu.DropDownItems.Add(new ToolStripMenuItem("Todavía no hay archivos recientes") { Enabled = false });
            if (settings.Recent.Count > 0) { recentMenu.DropDownItems.Add(new ToolStripSeparator()); recentMenu.DropDownItems.Add(Item("Borrar lista de recientes", ClearRecent)); }
        }
        // Files from a second launch (double-clicking another video in Explorer). Called from any thread.
        public void OpenFromAnotherLaunch(string[] files)
        {
            lock (incomingLock) incoming.Add(files ?? new string[0]);
            if (IsHandleCreated && engine != null) BeginInvoke(new Action(ProcessIncoming));
        }
        private void ProcessIncoming()
        {
            List<string[]> batches;
            lock (incomingLock) { batches = new List<string[]>(incoming); incoming.Clear(); }
            if (batches.Count == 0 || engine == null) return;
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate(); BringToFront();
            foreach (string[] files in batches)
            {
                if (files.Length == 0) continue;
                // Several videos opened together from Explorer arrive as separate launches a moment
                // apart: the first replaces what was playing and the rest join it as a playlist.
                bool append = (DateTime.Now - lastIncoming).TotalSeconds < 2;
                AddFiles(files, append); lastIncoming = DateTime.Now;
            }
        }
        private void ClearRecent()
        {
            settings.Recent.Clear();
            try { settings.Save(settingsPath); Notice("Lista de archivos recientes borrada", false); }
            catch (Exception ex) { Notice("No se pudo borrar la lista: " + ex.Message, true); }
        }
        private static readonly string[] SubtitleExtensions = { ".srt", ".ass", ".ssa", ".vtt", ".sub", ".idx", ".sup", ".smi", ".lrc" };
        private static bool IsSubtitle(string path) { return SubtitleExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()); }
        // Once a video is shown, drops land on the engine's own window. The engine only
        // reports them; Prisma applies the same rules as the Open dialog: local files that
        // exist, never URLs or anything the engine would otherwise load on its own.
        private void CheckDroppedFiles()
        {
            if (!engine.OwnsDrops) return;
            try { HandleDrop(); }
            catch (Exception ex) { Notice("No se pudo abrir lo arrastrado: " + ex.Message, true); }
        }
        private void HandleDrop()
        {
            Dictionary<string, object> drop = engine.GetNode("dropped-files") as Dictionary<string, object>;
            object stamp, files, action;
            if (drop == null || !drop.TryGetValue("time", out stamp) || !(stamp is long) || (long)stamp == lastDrop) return;
            lastDrop = (long)stamp;
            List<object> list = drop.TryGetValue("files", out files) ? files as List<object> : null;
            string[] paths = list == null ? new string[0] : list.OfType<string>().Where(p => p.IndexOf("://", StringComparison.Ordinal) < 0).ToArray();
            bool append = drop.TryGetValue("action", out action) && (action as string) == "append";
            if (paths.Length > 0 && paths.All(IsSubtitle))
            {
                foreach (string path in paths.Where(File.Exists)) engine.Command("sub-add", Path.GetFullPath(path), "select");
                return;
            }
            AddFiles(paths, append);
        }
        private void OnDragEnter(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy; }
        private void OnDrop(object sender, DragEventArgs e) { AddFiles((string[])e.Data.GetData(DataFormats.FileDrop), false); }
        private void TogglePlay()
        {
            if (engine == null) return;
            if (shuttle.Rate < 0) { PauseShuttle(); return; }
            shuttle.Rate = 0; shuttleClock.Reset();
            if (engine.Flag("idle-active")) { ChooseFiles(false); return; }
            if (engine.Flag("eof-reached")) { engine.Command("seek", "0", "absolute+exact"); engine.Set("pause", "no"); }
            else engine.Command("cycle", "pause");
            stopped = false;
        }
        private void StopPlayback()
        {
            if (engine == null || engine.Flag("idle-active")) return;
            PauseShuttle();
            engine.Set("pause", "yes"); engine.Command("seek", "0", "absolute+exact"); stopped = true;
        }
        private void SeekRelative(int seconds) { if (engine != null && !engine.Flag("idle-active")) { engine.Command("seek", seconds.ToString(), "relative+exact"); stopped = false; } }
        private void MovePlaylist(int delta)
        {
            if (engine == null) return;
            int position = (int)engine.Number("playlist-pos"), count = (int)engine.Number("playlist/count");
            if (position + delta < 0 || position + delta >= count) return;
            engine.Set("playlist-pos", (position + delta).ToString()); engine.Set("pause", "no"); stopped = false;
        }
        private void ToggleQueue() { queueShown = !queueShown; if (queueShown) notesShown = false; LayoutPlayer(); }
        private void RefreshQueue()
        {
            int count = (int)engine.Number("playlist/count"), position = (int)engine.Number("playlist-pos");
            List<string> items = new List<string>();
            for (int i = 0; i < count; i++) items.Add(Path.GetFileName(engine.Get("playlist/" + i + "/filename")));
            string signature = string.Join("\n", items);
            updatingQueue = true;
            try
            {
                if (queueCount != count || signature != queueSignature)
                {
                    playlist.BeginUpdate(); playlist.Items.Clear(); foreach (string item in items) playlist.Items.Add(item); playlist.EndUpdate();
                    queueCount = count; queueSignature = signature;
                }
                if (position >= 0 && position < count && !playlist.Focused) playlist.SelectedIndex = position;
            }
            finally { updatingQueue = false; }
        }
        private void DrawQueueItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (Brush b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
            e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r=new Rectangle(e.Bounds.X+S(12),e.Bounds.Y+S(3),e.Bounds.Width-S(24),e.Bounds.Height-S(9));
            Theme.Box(e.Graphics,r,S(8),selected ? Theme.AccentSoft : Theme.Card,selected ? Color.FromArgb(72,122,106) : Theme.Border);
            TextRenderer.DrawText(e.Graphics,(e.Index+1).ToString("00"),Font,new Rectangle(r.X+S(10),r.Y,S(30),r.Height),selected ? Theme.Accent : Theme.Muted,TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics,playlist.Items[e.Index].ToString(),Font,new Rectangle(r.X+S(46),r.Y+S(10),r.Width-S(56),S(24)),Theme.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix);
            using(Font small=new Font("Segoe UI",8)) TextRenderer.DrawText(e.Graphics,"VÍDEO LOCAL",small,new Rectangle(r.X+S(47),r.Y+S(34),r.Width-S(56),S(18)),Theme.Muted);
        }
        private void AddSpeeds(ToolStripItemCollection items)
        {
            foreach (double v in new double[] { .25, .5, .75, 1, 1.25, 1.5, 2 })
            {
                double value = v; items.Add(Item(F(value) + "×", delegate { if (engine != null) engine.Set("speed", F(value)); }));
            }
        }
        private void ShowSpeedMenu()
        {
            ContextMenuStrip popup = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.Panel };
            AddSpeeds(popup.Items); popup.Closed += delegate { popup.Dispose(); }; popup.Show(speed, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }
        private void BuildTracks(ToolStripMenuItem target, string type)
        {
            target.DropDownItems.Clear();
            if (engine == null) return;
            string property = type == "audio" ? "aid" : "sid";
            ToolStripMenuItem disabled = Item(type == "audio" ? "Desactivar audio" : "Sin subtítulos", delegate { engine.Set(property, "no"); });
            disabled.Checked = engine.Get(property) == "no"; target.DropDownItems.Add(disabled);
            int count = (int)engine.Number("track-list/count");
            for (int i = 0; i < count; i++)
            {
                string prefix = "track-list/" + i + "/";
                if (engine.Get(prefix + "type") != type) continue;
                string id = engine.Get(prefix + "id"), title = engine.Get(prefix + "title"), lang = engine.Get(prefix + "lang"), codec = engine.Get(prefix + "codec");
                string label = "Pista " + id + (lang.Length > 0 ? " · " + lang : "") + (title.Length > 0 ? " · " + title : "") + (codec.Length > 0 ? " (" + codec + ")" : "");
                ToolStripMenuItem item = Item(label.Replace("&", "&&"), delegate { engine.Set(property, id); }); item.Checked = engine.Flag(prefix + "selected"); target.DropDownItems.Add(item);
            }
            if (type == "sub")
            {
                target.DropDownItems.Add(new ToolStripSeparator()); target.DropDownItems.Add(Item("Cargar subtítulos…", delegate
                {
                    using (OpenFileDialog dialog = new OpenFileDialog { Filter = "Subtítulos|*.srt;*.ass;*.ssa;*.vtt;*.sub|Todos los archivos|*.*" })
                        if (dialog.ShowDialog(this) == DialogResult.OK) engine.Command("sub-add", dialog.FileName, "select");
                }));
            }
        }
        private void SetColor(string mode)
        {
            if (engine != null && !engine.Set("target-trc", mode == "sdr" ? "bt.1886" : "auto")) return;
            settings.ColorMode = mode; autoColorItem.Checked = mode == "auto"; sdrColorItem.Checked = mode == "sdr";
            Notice(mode == "auto" ? "Color adaptado automáticamente a la pantalla" : "HDR adaptado a SDR", false);
        }
        private void ToggleFullscreen()
        {
            if (!fullscreen)
            {
                wasMaximized = WindowState == FormWindowState.Maximized; normalBounds = wasMaximized ? RestoreBounds : Bounds;
                WindowState = FormWindowState.Normal; fullscreen = true; FormBorderStyle = FormBorderStyle.None; Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                fullscreen = false; FormBorderStyle = FormBorderStyle.Sizable; Bounds = normalBounds;
                if (wasMaximized) WindowState = FormWindowState.Maximized;
            }
            LayoutPlayer();
        }
        private void ShowInfo()
        {
            if (engine == null || engine.Get("path").Length == 0) { Notice("Abre un vídeo para consultar su información.", false); return; }
            string hw = engine.Get("hwdec-current"), bits = engine.VideoDepth();
            string details = "ARCHIVO\r\n" + engine.Get("path") + "\r\n\r\n";
            details += "Vídeo: " + engine.Get("video-codec") + "\r\nResolución: " + engine.Get("video-params/w") + " × " + engine.Get("video-params/h") + "\r\n";
            details += "Profundidad del archivo: " + (bits.Length > 0 ? bits + " bits" : "No indicada") + "\r\nFormato de píxel: " + engine.Get("video-params/pixelformat") + "\r\n";
            details += "Fotogramas por segundo: " + F(engine.Number("container-fps")) + "\r\nPrimarias: " + engine.Get("video-params/primaries") + "\r\nTransferencia: " + engine.Get("video-params/gamma") + "\r\n";
            details += "Decodificación: " + (hw == "no" || hw.Length == 0 ? "CPU (software)" : hw + " (hardware)") + "\r\nFotogramas descartados por el renderizador: " + engine.Get("frame-drop-count") + "\r\n";
            details += "Audio: " + engine.Get("audio-codec-name") + "\r\nDuración: " + Clock(duration) + "\r\n\r\n";
            details += "SALIDA\r\nRenderizador: " + engine.Get("current-vo") + " / Direct3D 11\r\nModo: " + (settings.ColorMode == "sdr" ? "HDR adaptado a SDR" : "Automático según la pantalla") + "\r\n\r\n";
            details += "Los bits indicados pertenecen al archivo. La salida física de 10 bits requiere una pantalla y una configuración de Windows y GPU compatibles. Esta aplicación no mide la señal del monitor.\r\n\r\nMotor: " + engine.Get("mpv-version");
            ShowText("Información del vídeo", details);
        }
        private void ShowText(string title, string content)
        {
            using (Form dialog = new Form { Text = title + " — Prisma", Size = new Size(S(660), S(640)), MinimumSize = new Size(S(450), S(340)), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Ink, ShowInTaskbar = false, Icon = Icon, AutoScaleMode=AutoScaleMode.None })
            using (TextBox body = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Theme.Background, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 10), Text = content, Dock = DockStyle.Fill })
            {
                dialog.Padding = new Padding(S(24));
                Theme.DarkScrollbars(body);
                Label heading=LabelOf(title,18,Theme.Ink); heading.Font=new Font("Segoe UI Semibold",18); heading.Dock=DockStyle.Top; heading.Height=S(62); heading.TextAlign=ContentAlignment.TopLeft;
                SurfacePanel contentPanel=new SurfacePanel { Dock=DockStyle.Fill, Padding=new Padding(S(18)) }; contentPanel.Controls.Add(body);
                Panel buttons=new Panel { Dock=DockStyle.Bottom, Height=S(60) };
                FlatButton close=new FlatButton("Cerrar","Cerrar ventana",true) { DialogResult=DialogResult.Cancel, Size=new Size(S(100),S(36)), Anchor=AnchorStyles.Right|AnchorStyles.Bottom };
                buttons.Controls.Add(close); buttons.Resize += delegate { close.Location=new Point(buttons.Width-close.Width,buttons.Height-close.Height); };
                dialog.Controls.Add(contentPanel); dialog.Controls.Add(heading); dialog.Controls.Add(buttons); dialog.CancelButton=close;
                dialog.Shown += delegate { int dark = 1; try { DwmSetWindowAttribute(dialog.Handle, 20, ref dark, 4); } catch { } body.SelectionStart = 0; body.SelectionLength = 0; };
                dialog.KeyPreview = true; dialog.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) dialog.Close(); };
                dialog.ShowDialog(this);
            }
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            bool? typing = TimecodeKey(keyData);
            if (typing.HasValue) return typing.Value || base.ProcessCmdKey(ref msg, keyData);
            if (notesPanel.Editing && notesPanel.ContainsFocus) return base.ProcessCmdKey(ref msg, keyData);
            if (ViewKey(keyData)) return true;
            if (keyData == (Keys.Control | Keys.O)) { ChooseFiles(false); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.O)) { ChooseFiles(true); return true; }
            if (keyData == (Keys.Control | Keys.I)) { ShowInfo(); return true; }
            if (keyData == (Keys.Control | Keys.L)) { ToggleQueue(); return true; }
            if (keyData == (Keys.Control | Keys.M)) { ToggleNotes(); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.R)) { SendToResolve(); return true; }
            if (keyData == Keys.F11 || keyData == Keys.F) { ToggleFullscreen(); return true; }
            if (keyData == Keys.Escape && fullscreen) { ToggleFullscreen(); return true; }
            if (engine != null)
            {
                switch (keyData)
                {
                    case Keys.Space: TogglePlay(); return true;
                    case Keys.Left: StepFrame(-1); return true;
                    case Keys.Right: StepFrame(1); return true;
                    case Keys.Shift | Keys.Left: SeekRelative(-5); return true;
                    case Keys.Shift | Keys.Right: SeekRelative(5); return true;
                    case Keys.J: StartShuttle(-1, false); return true;
                    case Keys.K: PauseShuttle(); return true;
                    case Keys.L: StartShuttle(1, false); return true;
                    case Keys.Shift | Keys.J: StartShuttle(-1, true); return true;
                    case Keys.Shift | Keys.L: StartShuttle(1, true); return true;
                    case Keys.I: ToggleSummary(); return true;
                    case Keys.Up: ChangeVolume(5); return true;
                    case Keys.Down: ChangeVolume(-5); return true;
                    case Keys.M: AddMarker(); return true;
                    case Keys.Control | Keys.Shift | Keys.M: engine.Command("cycle", "mute"); return true;
                    case Keys.S: StopPlayback(); return true;
                    case Keys.Home: engine.Command("seek", "0", "absolute+exact"); return true;
                    case Keys.OemPeriod: StepFrame(1); return true;
                    case Keys.Oemcomma: StepFrame(-1); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private void ChangeVolume(int delta)
        {
            settings.Volume = (int)Math.Max(0, Math.Min(100, engine.Number("volume") + delta)); engine.Set("volume", settings.Volume.ToString());
        }
        private void SaveAndClose(object sender, FormClosingEventArgs e)
        {
            notesPanel.CommitEdit();
            closing = true; timer.Stop();
            Rectangle b = fullscreen ? normalBounds : WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            settings.X = b.X; settings.Y = b.Y; settings.Width = b.Width; settings.Height = b.Height;
            if (engine != null) settings.Volume = (int)Math.Max(0, Math.Min(100, engine.Number("volume")));
            try { settings.Save(settingsPath); }
            catch (Exception ex) { MessageBox.Show(this, "No se pudieron guardar las preferencias.\r\n" + ex.Message, "Prisma", MessageBoxButtons.OK, MessageBoxIcon.Information); }
            if (engine != null) { engine.Dispose(); engine = null; }
            tips.Dispose(); timer.Dispose();
        }
    }
}
