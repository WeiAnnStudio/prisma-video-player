// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Prisma
{
    internal enum Choice { Accept, Alternative, Cancel, Extra }

    // Dark dialogs with Prisma's look: a message with up to three buttons, optionally with a text field.
    internal static class Dialogs
    {
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public static Choice Show(Form owner, float scale, string title, string body, string accept, string alternative, string cancel, TextBox input, string extra = null)
        {
            Func<int, int> S = n => (int)Math.Round(n * scale);
            using (Form dialog = new Form { Text = title + " — Prisma", ClientSize = new Size(S(640), S(input == null ? 330 : 470)), MinimumSize = new Size(S(420), S(260)), StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Panel, ForeColor = Theme.Ink, ShowInTaskbar = false, Icon = owner.Icon, AutoScaleMode = AutoScaleMode.None, MaximizeBox = false, MinimizeBox = false, KeyPreview = true })
            {
                Choice choice = Choice.Cancel;
                dialog.Padding = new Padding(S(24), S(18), S(24), S(18));
                Label heading = new Label { Text = title, Font = new Font("Segoe UI Semibold", 15), Dock = DockStyle.Top, Height = S(46), ForeColor = Theme.Ink, AutoEllipsis = true };
                TextBox text = new TextBox { Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = new Font("Segoe UI", 10), Text = body, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false };
                Theme.DarkScrollbars(text);
                Panel buttons = new Panel { Dock = DockStyle.Bottom, Height = S(52) };
                Panel field = null;
                if (input != null)
                {
                    field = new SurfacePanel { Dock = DockStyle.Bottom, Height = S(42) };
                    input.BorderStyle = BorderStyle.None; input.BackColor = Theme.Background; input.ForeColor = Theme.Ink; input.Font = new Font("Segoe UI", 10);
                    field.Controls.Add(input);
                    field.Resize += delegate { input.SetBounds(S(10), S(11), Math.Max(1, field.Width - S(20)), S(22)); };
                }
                FlatButton ok = new FlatButton(accept, accept, true);
                ok.Click += delegate { choice = Choice.Accept; dialog.Close(); };
                FlatButton other = alternative == null ? null : new FlatButton(alternative, alternative, false) { Outlined = true };
                if (other != null) other.Click += delegate { choice = Choice.Alternative; dialog.Close(); };
                FlatButton more = extra == null ? null : new FlatButton(extra, extra, false);
                if (more != null) more.Click += delegate { choice = Choice.Extra; dialog.Close(); };
                FlatButton close = cancel == null ? null : new FlatButton(cancel, cancel, false);
                if (close != null) close.Click += delegate { choice = Choice.Cancel; dialog.Close(); };
                buttons.Resize += delegate
                {
                    int x = buttons.Width, h = S(36), y = buttons.Height - h;
                    foreach (FlatButton b in new FlatButton[] { ok, other, close })
                    {
                        if (b == null) continue;
                        int w = Math.Max(S(96), TextRenderer.MeasureText(b.Text, b.Font).Width + S(32));
                        x -= w; b.SetBounds(x, y, w, h); x -= S(10);
                    }
                    if (more != null) more.SetBounds(0, y, Math.Max(S(96), TextRenderer.MeasureText(more.Text, more.Font).Width + S(32)), h);
                };
                buttons.Controls.Add(ok); if (other != null) buttons.Controls.Add(other); if (close != null) buttons.Controls.Add(close); if (more != null) buttons.Controls.Add(more);
                dialog.Controls.Add(text);
                if (field != null) { dialog.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = S(14) }); dialog.Controls.Add(field); }
                dialog.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = S(14) });
                dialog.Controls.Add(buttons);
                dialog.Controls.Add(heading);
                dialog.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (e.KeyCode == Keys.Escape) { choice = Choice.Cancel; dialog.Close(); }
                    if (e.KeyCode == Keys.Enter && input != null && input.Focused) { choice = Choice.Accept; dialog.Close(); e.SuppressKeyPress = true; }
                };
                dialog.Shown += delegate
                {
                    int dark = 1; try { DwmSetWindowAttribute(dialog.Handle, 20, ref dark, 4); } catch { }
                    text.SelectionLength = 0;
                    if (input != null) { input.Focus(); input.SelectAll(); } else ok.Focus();
                };
                dialog.FormClosing += delegate { if (input != null) input.Tag = input.Text; };
                dialog.ShowDialog(owner);
                return choice;
            }
        }
    }
}
