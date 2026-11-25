// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prisma
{
    internal static class Theme
    {
        [DllImport("uxtheme.dll", CharSet=CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr handle,string app,string classes);
        public static void DarkScrollbars(Control control)
        {
            // Windows 10/11 theme hint; unsupported themes retain the native scrollbar.
            control.HandleCreated += delegate { try { SetWindowTheme(control.Handle,"DarkMode_Explorer",null); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } };
        }
        public static readonly Color Background = Color.FromArgb(16, 18, 22);
        public static readonly Color Panel = Color.FromArgb(23, 26, 31);
        public static readonly Color Card = Color.FromArgb(30, 34, 40);
        public static readonly Color Hover = Color.FromArgb(40, 46, 53);
        public static readonly Color Border = Color.FromArgb(47, 54, 62);
        public static readonly Color Ink = Color.FromArgb(235, 239, 243);
        public static readonly Color Muted = Color.FromArgb(148, 159, 172);
        public static readonly Color Accent = Color.FromArgb(128, 224, 193);
        public static readonly Color AccentSoft = Color.FromArgb(34, 57, 52);
        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure();
            return path;
        }
        public static void Box(Graphics g, RectangleF bounds, float radius, Color fill, Color border)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            using (GraphicsPath path = Round(bounds, radius))
            {
                using (Brush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (border != Color.Empty) using (Pen pen = new Pen(border)) g.DrawPath(pen, path);
            }
        }
    }
    internal enum Symbol { None, Folder, Notes, List, Info, Plus, Edit, Import, Export, Check, Close, Play, Pause, Stop, Previous, Next, FrameBack, FrameNext, Volume, Muted, Fullscreen }
    internal static class Icons
    {
        public static void Draw(Graphics g, Symbol icon, RectangleF rect, Color color)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(rect.X, rect.Y); g.ScaleTransform(rect.Width / 24f, rect.Height / 24f);
            using (Pen p = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (Brush b = new SolidBrush(color))
            {
                switch (icon)
                {
                    case Symbol.Folder: g.DrawLines(p, new PointF[] { new PointF(3,19),new PointF(3,6),new PointF(9,6),new PointF(11,9),new PointF(21,9),new PointF(21,19),new PointF(3,19) }); break;
                    case Symbol.Notes: g.DrawLines(p, new PointF[] { new PointF(5,3),new PointF(19,3),new PointF(19,16),new PointF(14,21),new PointF(5,21),new PointF(5,3) }); g.DrawLines(p,new PointF[]{new PointF(14,21),new PointF(14,16),new PointF(19,16)}); g.DrawLine(p,8,8,16,8); g.DrawLine(p,8,12,14,12); break;
                    case Symbol.List: for (int y=6;y<=18;y+=6) { g.FillEllipse(b,3,y-1,2,2); g.DrawLine(p,9,y,21,y); } break;
                    case Symbol.Info: g.DrawEllipse(p,3,3,18,18); g.DrawLine(p,12,11,12,17); g.FillEllipse(b,11,6,2,2); break;
                    case Symbol.Plus: g.DrawLine(p,5,12,19,12); g.DrawLine(p,12,5,12,19); break;
                    case Symbol.Edit: g.DrawLines(p,new PointF[]{new PointF(4,20),new PointF(6,14),new PointF(17,3),new PointF(21,7),new PointF(10,18),new PointF(4,20)}); g.DrawLine(p,14,6,18,10); break;
                    case Symbol.Import: case Symbol.Export:
                        g.DrawLines(p,new PointF[]{new PointF(4,16),new PointF(4,21),new PointF(20,21),new PointF(20,16)});
                        g.DrawLine(p,12,3,12,15);
                        if(icon==Symbol.Import) g.DrawLines(p,new PointF[]{new PointF(7,10),new PointF(12,15),new PointF(17,10)});
                        else g.DrawLines(p,new PointF[]{new PointF(7,8),new PointF(12,3),new PointF(17,8)}); break;
                    case Symbol.Check: g.DrawLines(p,new PointF[]{new PointF(5,12),new PointF(10,17),new PointF(20,6)}); break;
                    case Symbol.Close: g.DrawLine(p,6,6,18,18); g.DrawLine(p,6,18,18,6); break;
                    case Symbol.Play: g.FillPolygon(b,new PointF[]{new PointF(8,4),new PointF(8,20),new PointF(20,12)}); break;
                    case Symbol.Pause: g.FillRectangle(b,6,5,4,14); g.FillRectangle(b,14,5,4,14); break;
                    case Symbol.Stop: Theme.Box(g,new RectangleF(6,6,12,12),2,color,Color.Empty); break;
                    case Symbol.Previous: case Symbol.FrameBack:
                        g.DrawLine(p,5,5,5,19); g.DrawLines(p,new PointF[]{new PointF(18,5),new PointF(9,12),new PointF(18,19)});
                        if(icon==Symbol.Previous) g.DrawLine(p,18,5,18,19); break;
                    case Symbol.Next: case Symbol.FrameNext:
                        g.DrawLine(p,19,5,19,19); g.DrawLines(p,new PointF[]{new PointF(6,5),new PointF(15,12),new PointF(6,19)});
                        if(icon==Symbol.Next) g.DrawLine(p,6,5,6,19); break;
                    case Symbol.Volume: case Symbol.Muted:
                        g.DrawPolygon(p,new PointF[]{new PointF(3,9),new PointF(7,9),new PointF(12,5),new PointF(12,19),new PointF(7,15),new PointF(3,15)});
                        if(icon==Symbol.Volume) { g.DrawArc(p,9,6,11,12,-60,120); g.DrawArc(p,8,3,15,18,-50,100); }
                        else { g.DrawLine(p,17,9,22,15); g.DrawLine(p,17,15,22,9); } break;
                    case Symbol.Fullscreen:
                        g.DrawLines(p,new PointF[]{new PointF(9,4),new PointF(4,4),new PointF(4,9)}); g.DrawLines(p,new PointF[]{new PointF(15,4),new PointF(20,4),new PointF(20,9)});
                        g.DrawLines(p,new PointF[]{new PointF(4,15),new PointF(4,20),new PointF(9,20)}); g.DrawLines(p,new PointF[]{new PointF(15,20),new PointF(20,20),new PointF(20,15)}); break;
                }
            }
            g.Restore(state);
        }
    }
    internal sealed class SurfacePanel : Panel
    {
        public bool Highlight;
        public SurfacePanel() { DoubleBuffered = true; BackColor = Theme.Background; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? Theme.Panel : Parent.BackColor);
            Theme.Box(e.Graphics,new RectangleF(.5f,.5f,Width-1,Height-1),8*e.Graphics.DpiX/96,BackColor,Highlight ? Theme.Accent : Theme.Border);
        }
    }
    internal sealed class FlatButton : Button
    {
        public bool Accent;
        public Symbol Icon;
        public bool IconOnly, Outlined;
        private bool hovered, pressed, active;
        public bool Active { get { return active; } set { if(active != value) { active = value; Invalidate(); } } }
        public FlatButton(string text, string accessible, bool accent)
        {
            Text = text; AccessibleName = accessible; Accent = accent;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            BackColor = accent ? Theme.Accent : Theme.Panel;
            ForeColor = accent ? Theme.Background : Theme.Ink;
            FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(148, 241, 205) : Theme.Hover;
            FlatAppearance.MouseDownBackColor = Theme.Border;
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI", 9.5f);
            UseVisualStyleBackColor = false;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered=true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered=pressed=false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed=true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed=false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g=e.Graphics; float s=g.DpiX/96f; g.SmoothingMode=SmoothingMode.AntiAlias;
            Color parent=Parent==null ? Theme.Panel : Parent.BackColor; g.Clear(parent);
            Color fill=Accent ? Theme.Accent : active ? Theme.AccentSoft : Outlined ? Theme.Card : parent;
            if(hovered && Enabled) fill=Accent ? Color.FromArgb(156,237,210) : Theme.Hover;
            if(pressed && Enabled) fill=Accent ? Color.FromArgb(105,200,170) : Theme.Border;
            Color ink=!Enabled ? Theme.Muted : Accent ? Theme.Background : active ? Theme.Accent : Theme.Ink;
            Color border=Focused && ShowFocusCues ? Theme.Accent : Outlined ? Theme.Border : Color.Empty;
            Theme.Box(g,new RectangleF(.5f,.5f,Width-1,Height-1),8*s,fill,border);
            Symbol icon=Icon;
            // Keep the dynamic playback text as the accessible button label.
            if(IconOnly && Icon==Symbol.Play && Text=="Ⅱ") icon=Symbol.Pause;
            if(IconOnly && Icon==Symbol.Volume && Text=="Silencio") icon=Symbol.Muted;
            if(icon!=Symbol.None)
            {
                float size=20*s, gap=7*s;
                float tw=IconOnly ? 0 : TextRenderer.MeasureText(Text,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
                float x=IconOnly ? (Width-size)/2 : (Width-size-gap-tw)/2;
                Icons.Draw(g,icon,new RectangleF(x,(Height-size)/2,size,size),ink);
                if(!IconOnly) TextRenderer.DrawText(g,Text,Font,new Rectangle((int)(x+size+gap),0,(int)tw+2,Height),ink,TextFormatFlags.NoPadding|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
            }
            else TextRenderer.DrawText(g,Text,Font,ClientRectangle,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        }
    }
    internal sealed class SeekBar : Control
    {
        private double fraction;
        public bool Dragging { get; private set; }
        public event Action<double> Seek;
        // While the mouse drags the bar: the position under it, so the image and the timecode follow.
        public event Action<double> Scrub;
        public double[] Markers = new double[0];
        public double Fraction { get { return fraction; } set { fraction = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public SeekBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand; TabStop = true; AccessibleName = "Posición de reproducción. Flechas para avanzar o retroceder";
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            int y = Height / 2;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float s=e.Graphics.DpiX/96f, inset=8*s;
            using (Pen rail = new Pen(Theme.Border, 3*s) { StartCap=LineCap.Round, EndCap=LineCap.Round }) e.Graphics.DrawLine(rail, inset, y, Math.Max(inset,Width-inset), y);
            float x = inset + (float)fraction * Math.Max(0,Width - 2*inset);
            using (Pen fill = new Pen(Enabled ? Theme.Accent : Theme.Muted, 3*s) { StartCap=LineCap.Round, EndCap=LineCap.Round }) e.Graphics.DrawLine(fill, inset, y, x, y);
            if (Enabled) using (Brush b = new SolidBrush(Theme.Accent)) e.Graphics.FillEllipse(b, x - 4*s, y - 4*s, 8*s, 8*s);
            using (Brush b = new SolidBrush(Color.FromArgb(245, 194, 104))) foreach (double marker in Markers)
            {
                float mx = inset + (float)marker * (Width - 2*inset); e.Graphics.FillPolygon(b, new PointF[] { new PointF(mx - 3*s, y - 10*s), new PointF(mx + 3*s, y - 10*s), new PointF(mx, y - 5*s) });
            }
            if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, Theme.Muted, BackColor);
        }
        private void UpdateMouse(int x) { float s; using(Graphics g=CreateGraphics()) s=g.DpiX/96f; Fraction = (x - 8.0*s) / Math.Max(1, Width - 16*s); if (Dragging && Scrub != null) Scrub(fraction); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            Focus(); Dragging = true; Capture = true; UpdateMouse(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (Dragging) UpdateMouse(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!Dragging) return;
            Dragging = false; UpdateMouse(e.X); Capture = false;
            if (Seek != null) Seek(fraction);
        }
    }
    internal sealed class VideoSurface : Panel
    {
        public bool Empty = true;
        public VideoSurface() { BackColor = Color.FromArgb(10, 12, 15); DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Empty) return;
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f, cx = Width / 2f, cy = Height / 2f - 62 * s;
            using (Pen ring = new Pen(Color.FromArgb(25, 32, 34), 1))
            {
                g.DrawEllipse(ring, cx - 62 * s, cy - 62 * s, 124 * s, 124 * s);
                g.DrawEllipse(ring, cx - 85 * s, cy - 85 * s, 170 * s, 170 * s);
            }
            Theme.Box(g,new RectangleF(cx-35*s,cy-35*s,70*s,70*s),18*s,Theme.AccentSoft,Color.FromArgb(46,80,69));
            Icons.Draw(g,Symbol.Play,new RectangleF(cx-18*s,cy-20*s,40*s,40*s),Theme.Accent);
            using (Font title = new Font("Segoe UI", Width < 500*s ? 15 : 20, FontStyle.Regular))
            using (Font body = new Font("Segoe UI", 10))
            using (Brush ink = new SolidBrush(Theme.Ink))
            using (Brush muted = new SolidBrush(Theme.Muted))
            using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("Cada fotograma cuenta.", title, ink, new PointF(cx, cy + 97 * s), center);
                g.DrawString("Arrastra tu vídeo aquí para empezar", body, muted, new PointF(cx, cy + 138 * s), center);
                using(Font small=new Font("Segoe UI",8.5f)) g.DrawString("H.265 / HEVC   ·   ProRes   ·   H.264   ·   10 bits", small, muted, new PointF(cx, cy + 180 * s), center);
            }
        }
    }
    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = Theme.Ink; base.OnRenderItemText(e); }
        private sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
            public override Color MenuItemSelected { get { return Theme.Hover; } }
            public override Color MenuItemBorder { get { return Theme.Border; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
            public override Color MenuItemPressedGradientBegin { get { return Theme.Hover; } }
            public override Color MenuItemPressedGradientEnd { get { return Theme.Hover; } }
        }
    }
}
