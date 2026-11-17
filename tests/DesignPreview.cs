// SPDX-License-Identifier: GPL-3.0-or-later
// Manual visual fixture. Uses only isolated test data. F6: compact/normal, F7: edit.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Prisma;
internal static class DesignPreview
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();
    private static object Field(object target,string name) { return target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target); }
    private static void Invoke(object target,string name) { target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,null); }
    [STAThread] private static void Main(string[] args)
    {
        SetProcessDPIAware(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string root=args[0];
        using(PlayerForm form=new PlayerForm(new string[]{Path.Combine(root,"tests","media","prores-422-10bit.mov")},Path.Combine(root,"tests","design","datos-prueba","preferencias.json")))
        using(Timer ready=new Timer { Interval=200 })
        {
            float scale; using(Graphics g=form.CreateGraphics()) scale=g.DpiX/96;
            form.Size=new Size((int)(1100*scale),(int)(800*scale));
            ready.Tick += delegate {
                ReviewDocument doc=(ReviewDocument)Field(form,"review"); if(doc==null) return; ready.Stop();
                ((MpvEngine)Field(form,"engine")).Set("pause","yes");
                doc.Markers.Clear();
                doc.Markers.Add(new ReviewMarker {Frame=0,Name="Ajustar el contraste",Note="Recuperar detalle en las sombras sin perder el negro de referencia."});
                doc.Markers.Add(new ReviewMarker {Frame=12,Name="Continuidad de color",Note="Igualar la temperatura con el plano anterior. Revisar los tonos de piel."});
                doc.Markers.Add(new ReviewMarker {Frame=24,Name="Revisar el cierre",Note="Dejar un poco más de aire antes del corte final."});
                ((MarkerPanel)Field(form,"notesPanel")).RefreshMarkers(doc); Invoke(form,"UpdateMarkerTicks"); Invoke(form,"ToggleNotes");
                form.Text="Prisma — Vista de diseño";
            };
            form.KeyDown += delegate(object sender,KeyEventArgs e) {
                if(e.KeyCode==Keys.F6) { form.Size=form.Width>900*scale ? form.MinimumSize : new Size((int)(1100*scale),(int)(800*scale)); e.Handled=true; }
                if(e.KeyCode==Keys.F7) { MarkerPanel panel=(MarkerPanel)Field(form,"notesPanel"); if(panel.Editing) panel.CancelEdit(); else panel.BeginEdit(((ReviewDocument)Field(form,"review")).Markers[0]); e.Handled=true; }
            };
            form.Shown += delegate { ready.Start(); }; Application.Run(form);
        }
    }
}
