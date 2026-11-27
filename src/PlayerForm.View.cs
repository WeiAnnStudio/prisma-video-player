// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prisma
{
    // Image size (fit, 100 %, zoom with the wheel or Ctrl + / Ctrl -, drag to move) and the
    // timecode of the player, which can be clicked to copy it or to type one and jump there.
    internal sealed partial class PlayerForm : IMessageFilter
    {
        private const int WheelMessage = 0x020A;
        private readonly FlatButton zoomButton = new FlatButton("Ajustar", "Tamaño de la imagen", false);
        private readonly TextBox timecodeBox = new TextBox();
        // La cajita del código de tiempo: un hueco más oscuro con borde de color, como en DaVinci o Premiere.
        private readonly SurfacePanel timecodeFrame = new SurfacePanel { Highlight = true };
        private const int SetCueBanner = 0x1501;   // EM_SETCUEBANNER: texto gris de fondo en un cuadro vacío
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, string lParam);
        private readonly ContextMenuStrip timecodeMenu = new ContextMenuStrip { Renderer = new DarkRenderer() };
        // zoomScale: screen pixels per video pixel (1 = 100 %, pixel for pixel). Pan: fraction
        // of the displayed video, as libmpv's video-pan-x / video-pan-y.
        private bool zoomFit = true, panning;
        private double zoomScale = 1, panX, panY, panStartX, panStartY;
        private int zoomW, zoomH;
        private Point panStart;

        private void InitializeView()
        {
            zoomButton.Outlined = true;
            transport.Controls.Add(zoomButton); transport.Controls.Add(timecodeFrame); timecodeFrame.Controls.Add(timecodeBox);
            tips.SetToolTip(zoomButton, "Tamaño de la imagen · Rueda del ratón sobre el vídeo, Ctrl + / Ctrl − · Ctrl+0 ajustar · Ctrl+1 al 100 %");
            zoomButton.Click += delegate { ShowZoomMenu(); };
            timecodeFrame.Visible = false; timecodeBox.BorderStyle = BorderStyle.None; timecodeBox.Font = new Font("Consolas", 11);
            timecodeBox.BackColor = Theme.Background; timecodeBox.ForeColor = Theme.Ink; timecodeBox.MaxLength = 16; timecodeBox.AccessibleName = "Código de tiempo";
            timecodeBox.Leave += delegate { EndTimecodeEdit(); };
            time.Cursor = Cursors.IBeam; time.AccessibleName = "Código de tiempo. Clic para escribir +200, -30 u otro código y saltar";
            time.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) BeginTimecodeEdit(); };
            timecodeMenu.Items.Add(Item("Copiar código de tiempo    Ctrl+Mayús+C", CopyTimecode));
            timecodeMenu.Items.Add(Item("Ir a un código de tiempo…    Ctrl+G", BeginTimecodeEdit));
            time.ContextMenuStrip = timecodeMenu;
            video.MouseDown += VideoMouseDown; video.MouseMove += VideoMouseMove; video.MouseUp += VideoMouseUp;
            video.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Middle) { if (zoomFit) ZoomTo(1, null, false); else FitVideo(); } };
            notesPanel.CurrentTimecode = delegate { return review == null || engine == null ? "" : review.Timecode(CurrentFrame()); };
            Application.AddMessageFilter(this);
            FormClosed += delegate { Application.RemoveMessageFilter(this); };
        }

        private void AddZoomItems(ToolStripItemCollection items)
        {
            ToolStripMenuItem fit = Item("Ajustar a la ventana    Ctrl+0", FitVideo); fit.Checked = zoomFit; items.Add(fit);
            foreach (double v in new double[] { .5, 1, 2, 4 })
            {
                double value = v;
                ToolStripMenuItem item = Item(Percent(value) + (value == 1 ? " · píxel a píxel    Ctrl+1" : ""), delegate { ZoomTo(value, null, false); });
                item.Checked = !zoomFit && Math.Abs(zoomScale - value) < .001; items.Add(item);
            }
            items.Add(new ToolStripSeparator());
            items.Add(Item("Acercar    Ctrl + / rueda", delegate { ZoomStep(1, null); }));
            items.Add(Item("Alejar    Ctrl − / rueda", delegate { ZoomStep(-1, null); }));
        }

        private void ShowZoomMenu()
        {
            ContextMenuStrip popup = new ContextMenuStrip { Renderer = new DarkRenderer(), BackColor = Theme.Panel };
            AddZoomItems(popup.Items); popup.Closed += delegate { BeginInvoke(new Action(popup.Dispose)); };
            popup.Show(zoomButton, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
        }

        private static string Percent(double scale) { return Math.Round(scale * 100).ToString(CultureInfo.InvariantCulture) + " %"; }
        private static string P(double value) { return value.ToString("0.######", CultureInfo.InvariantCulture); }
        private double FitScale() { return zoomW <= 0 || zoomH <= 0 ? 1 : Math.Min((double)video.Width / zoomW, (double)video.Height / zoomH); }
        private double CurrentScale() { return zoomFit ? FitScale() : zoomScale; }

        // Called on every tick: the image size is only known once the first frame is decoded.
        private void CheckVideoSize()
        {
            int w = (int)engine.Number("video-params/dw"), h = (int)engine.Number("video-params/dh");
            if (w != zoomW || h != zoomH) { zoomW = w; zoomH = h; ApplyZoom(); }
        }

        private void FitVideo() { zoomFit = true; panX = panY = 0; ApplyZoom(); if (zoomW > 0) Notice("Imagen ajustada a la ventana", false); }

        private void ResetZoom() { zoomFit = true; panX = panY = 0; zoomW = zoomH = 0; ApplyZoom(); }

        private void ClampPan()
        {
            double s = CurrentScale(), dw = Math.Max(1, zoomW * s), dh = Math.Max(1, zoomH * s);
            double mx = Math.Max(0, (dw - video.Width) / 2) / dw, my = Math.Max(0, (dh - video.Height) / 2) / dh;
            panX = Math.Max(-mx, Math.Min(mx, panX)); panY = Math.Max(-my, Math.Min(my, panY));
        }

        private void ApplyZoom()
        {
            zoomButton.Text = zoomFit ? "Ajustar" : Percent(zoomScale);
            if (engine == null) return;
            if (zoomFit || zoomW <= 0)
            {
                engine.Set("video-zoom", "0"); engine.Set("video-pan-x", "0"); engine.Set("video-pan-y", "0");
            }
            else
            {
                ClampPan();
                engine.Set("video-zoom", P(Math.Log(zoomScale / FitScale(), 2)));
                engine.Set("video-pan-x", P(panX)); engine.Set("video-pan-y", P(panY));
            }
            video.Cursor = !zoomFit && (zoomW * zoomScale > video.Width + 1 || zoomH * zoomScale > video.Height + 1) ? Cursors.SizeAll : Cursors.Default;
        }

        // Zooms keeping the point under the mouse (anchor, in video coordinates) where it is.
        private void ZoomTo(double scale, Point? anchor, bool quiet)
        {
            if (engine == null || video.Empty || zoomW <= 0) { Notice("Abre un vídeo para cambiar el tamaño de la imagen.", false); return; }
            scale = Math.Max(.1, Math.Min(16, scale));
            double old = CurrentScale(), cx = 0, cy = 0;
            if (anchor.HasValue) { cx = anchor.Value.X - video.Width / 2.0; cy = anchor.Value.Y - video.Height / 2.0; }
            double ox = zoomW * old, oy = zoomH * old, nx = zoomW * scale, ny = zoomH * scale;
            double u = (cx - panX * ox) / ox, v = (cy - panY * oy) / oy;
            panX = cx / nx - u; panY = cy / ny - v;
            zoomFit = false; zoomScale = scale;
            ApplyZoom();
            if (!quiet) Notice("Tamaño de la imagen: " + Percent(scale), false);
        }

        // One step is a quarter of a doubling. Crossing "fit" or 100 % stops there once.
        private void ZoomStep(double steps, Point? anchor)
        {
            if (engine == null || video.Empty || zoomW <= 0) return;
            double old = CurrentScale(), next = old * Math.Pow(2, steps / 4), fit = FitScale();
            if ((old < fit - 1e-6 && next > fit + 1e-6) || (old > fit + 1e-6 && next < fit - 1e-6)) { FitVideo(); return; }
            if ((old < 1 - 1e-6 && next > 1 + 1e-6) || (old > 1 + 1e-6 && next < 1 - 1e-6)) next = 1;
            ZoomTo(next, anchor, false);
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WheelMessage || engine == null || video.Empty || !video.Visible || !ContainsFocus) return false;
            Point p = video.PointToClient(Cursor.Position);
            if (!video.ClientRectangle.Contains(p)) return false;
            int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
            ZoomStep(delta / 120.0, p);
            return true;
        }

        private void VideoMouseDown(object sender, MouseEventArgs e)
        {
            EndTimecodeEdit();
            if (e.Button != MouseButtons.Left || zoomFit || video.Cursor != Cursors.SizeAll) return;
            panning = true; panStart = e.Location; panStartX = panX; panStartY = panY; video.Capture = true;
        }
        private void VideoMouseMove(object sender, MouseEventArgs e)
        {
            if (!panning) return;
            double s = CurrentScale();
            panX = panStartX + (e.X - panStart.X) / Math.Max(1, zoomW * s); panY = panStartY + (e.Y - panStart.Y) / Math.Max(1, zoomH * s);
            ClampPan(); engine.Set("video-pan-x", P(panX)); engine.Set("video-pan-y", P(panY));
        }
        private void VideoMouseUp(object sender, MouseEventArgs e) { if (panning) { panning = false; video.Capture = false; } }

        // --- Timecode ---

        private long CurrentFrame()
        {
            if (review == null || engine == null) return 0;
            return Math.Max(0, Math.Min(review.DurationFrames - 1, (long)Math.Round(engine.Number("time-pos") * review.Fps)));
        }

        private long FrameAt(double seconds)
        {
            return review == null ? 0 : Math.Max(0, Math.Min(review.DurationFrames - 1, (long)Math.Floor(seconds * review.Fps + 1e-6)));
        }

        // Dragging the timeline: the timecode shows the frame under the mouse at once, and the
        // image follows with exact seeks, one at a time so a heavy 4K file does not pile them up.
        private void ScrubTo(double fraction)
        {
            if (engine == null || duration <= 0) return;
            double target = fraction * duration;
            if (review != null) time.Text = review.Timecode(FrameAt(target)); else time.Text = Clock(target) + " / " + Clock(duration);
            StopReverseShuttle(); stopped = false;
            if (!engine.Flag("seeking")) engine.Command("seek", P(review != null ? FrameAt(target) / review.Fps : target), "absolute+exact");
        }

        private void CopyTimecode()
        {
            if (review == null || engine == null) { Notice("Abre un vídeo para copiar su código de tiempo.", false); return; }
            string tc = review.Timecode(CurrentFrame());
            try { Clipboard.SetText(tc); Notice("Código de tiempo copiado: " + tc, false); }
            catch (Exception ex) { Notice("No se pudo copiar: " + ex.Message, true); }
        }

        private void BeginTimecodeEdit()
        {
            if (review == null || engine == null) { Notice("Abre un vídeo para usar su código de tiempo.", false); return; }
            // El código actual queda de fondo, en gris: lo que se escribe (+200, -30...) empieza en blanco.
            timecodeBox.Text = "";
            SendMessage(timecodeBox.Handle, SetCueBanner, (IntPtr)1, review.Timecode(CurrentFrame()));
            PlaceTimecodeBox(time.Top, time.Height);
            time.Visible = false; timecodeFrame.Visible = true; timecodeFrame.BringToFront();
            timecodeBox.Focus();
            Notice("Escribe +200, -30 o un código y pulsa Intro · Esc cancela · Ctrl+Mayús+C copia el código", false);
        }

        // Coloca la cajita sobre el código de tiempo, con el texto centrado y aire a los lados.
        private void PlaceTimecodeBox(int top, int height)
        {
            int boxHeight = timecodeBox.Height + S(10);
            timecodeFrame.SetBounds(time.Left - S(6), top + (height - boxHeight) / 2, S(134), boxHeight);
            timecodeBox.SetBounds(S(9), (boxHeight - timecodeBox.Height) / 2, timecodeFrame.Width - S(18), timecodeBox.Height);
        }

        private void EndTimecodeEdit()
        {
            if (!timecodeFrame.Visible) return;
            timecodeFrame.Visible = false; time.Visible = true;
            if (timecodeBox.Focused || ActiveControl == timecodeBox) ActiveControl = null;
        }

        private void CommitTimecode()
        {
            long frame;
            if (review == null || engine == null || timecodeBox.Text.Trim().Length == 0) { EndTimecodeEdit(); return; }
            if (!review.TryParseTimecode(timecodeBox.Text, CurrentFrame(), out frame))
            {
                Notice("Código de tiempo no válido. Ejemplos: 00:00:03:10, 3:10, 310 o +10", true);
                timecodeBox.SelectAll(); return;
            }
            EndTimecodeEdit();
            StopReverseShuttle(); engine.Command("seek", P(frame / review.Fps), "absolute+exact"); stopped = false;
            Notice("Ir a " + review.Timecode(frame), false);
        }

        // Keys while the timecode is being typed. Returns null when the key is not handled here.
        private bool? TimecodeKey(Keys keyData)
        {
            if (!timecodeBox.Focused) return null;
            if (keyData == Keys.Enter) { CommitTimecode(); return true; }
            if (keyData == Keys.Escape) { EndTimecodeEdit(); return true; }
            if (keyData == (Keys.Control | Keys.A)) { timecodeBox.SelectAll(); return true; }
            return false;
        }

        // Zoom and timecode shortcuts of the main window.
        private bool ViewKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.Oemplus: case Keys.Control | Keys.Add: case Keys.Control | Keys.Shift | Keys.Oemplus: ZoomStep(1, null); return true;
                case Keys.Control | Keys.OemMinus: case Keys.Control | Keys.Subtract: ZoomStep(-1, null); return true;
                case Keys.Control | Keys.D0: case Keys.Control | Keys.NumPad0: FitVideo(); return true;
                case Keys.Control | Keys.D1: case Keys.Control | Keys.NumPad1: ZoomTo(1, null, false); return true;
                case Keys.Control | Keys.G: BeginTimecodeEdit(); return true;
                case Keys.Control | Keys.Shift | Keys.C: CopyTimecode(); return true;
            }
            return false;
        }
    }
}
