// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace Prisma
{
    internal sealed class MarkerPanel : UserControl
    {
        private readonly ListBox list = new ListBox();
        private readonly Label heading = Label("Notas de revisión", 14, Theme.Ink), at = Label("", 10, Theme.Accent);
        private readonly Label subtitle = Label("Ideas y ajustes, fotograma a fotograma.", 9, Theme.Muted);
        private readonly Label count = Label("0", 10, Theme.Accent), section = Label("MARCADORES", 8, Theme.Muted);
        private readonly Label hint = Label("Doble clic en una nota para ir al fotograma", 8, Theme.Muted);
        private readonly Label empty = Label("", 10, Theme.Muted);
        private readonly Panel editor = new Panel(), editorScroll = new Panel(), fields = new Panel();
        private readonly Label editorHeading = Label("Nueva nota", 14, Theme.Ink), frame = Label("", 9, Theme.Muted);
        private readonly Label titleLabel = Label("TÍTULO", 8, Theme.Muted), noteLabel = Label("COMENTARIO", 8, Theme.Muted);
        private readonly Label saveHint = Label("Ctrl + Intro para guardar · Esc para cancelar", 8, Theme.Muted);
        private readonly SurfacePanel titleBox = new SurfacePanel(), noteBox = new SurfacePanel();
        private readonly TextBox title = new TextBox(), note = new TextBox();
        private readonly FlatButton save = new FlatButton("Guardar nota", "Guardar nota", true), cancel = new FlatButton("Cancelar", "Cancelar edición", false);
        private readonly FlatButton add = new FlatButton("Nueva nota", "Añadir nota en este fotograma (M)", true);
        private readonly FlatButton edit = new FlatButton("Editar", "Editar nota seleccionada", false);
        private readonly FlatButton import = new FlatButton("Importar", "Importar marcadores", false), export = new FlatButton("Exportar", "Exportar marcadores", false);
        private readonly Font codeFont = new Font("Consolas", 9.5f), nameFont = new Font("Segoe UI Semibold", 10), bodyFont = new Font("Segoe UI", 9.5f);
        private ReviewDocument document;
        private ReviewMarker pending;
        private float scale = 1;
        private bool editing;
        public bool Editing { get { return editing; } }
        public event Action Add, Import, Export;
        public event Action<ReviewMarker> Jump;
        public event Action Changed;
        private static Label Label(string text, float size, Color color)
        {
            return new Label { Text=text, Font=new Font("Segoe UI",size), ForeColor=color, BackColor=Color.Transparent, AutoEllipsis=true, TextAlign=ContentAlignment.MiddleLeft };
        }
        private int S(int n) { return (int)Math.Round(n*scale); }
        public MarkerPanel()
        {
            AutoScaleMode=AutoScaleMode.None; DoubleBuffered=true; BackColor=Theme.Panel; ForeColor=Theme.Ink;
            heading.Font=new Font("Segoe UI Semibold",14); editorHeading.Font=new Font("Segoe UI Semibold",14);
            count.TextAlign=ContentAlignment.MiddleCenter; count.BackColor=Theme.AccentSoft;
            add.Icon=Symbol.Plus; edit.Icon=Symbol.Edit; import.Icon=Symbol.Import; export.Icon=Symbol.Export;
            import.Outlined=export.Outlined=true; save.Icon=Symbol.Check;
            add.Click += delegate { if(Add!=null) Add(); };
            edit.Click += delegate { if(Selected!=null) BeginEdit(Selected); };
            import.Click += delegate { if(Import!=null) Import(); };
            export.Click += delegate { if(Export!=null) Export(); };
            list.BackColor=Theme.Panel; list.ForeColor=Theme.Ink; list.BorderStyle=BorderStyle.None;
            Theme.DarkScrollbars(list); Theme.DarkScrollbars(note); Theme.DarkScrollbars(editorScroll);
            list.DrawMode=DrawMode.OwnerDrawFixed; list.IntegralHeight=false; list.AccessibleName="Marcadores de revisión";
            list.FormattingEnabled=true; list.Format += delegate(object sender, ListControlConvertEventArgs e) { ReviewMarker m=e.ListItem as ReviewMarker; if(m!=null) e.Value=(document==null ? "" : document.Timecode(m.Frame)+" · ")+m.Name; };
            list.DrawItem += DrawMarker;
            list.SelectedIndexChanged += delegate { edit.Enabled=Selected!=null; };
            list.DoubleClick += delegate { if(Selected!=null && Jump!=null) Jump(Selected); };
            list.KeyDown += delegate(object sender, KeyEventArgs e) { if(e.KeyCode==Keys.Enter && Selected!=null && Jump!=null) { Jump(Selected); e.Handled=true; } };
            list.MouseDown += delegate(object sender, MouseEventArgs e) { if(e.Button==MouseButtons.Right) list.SelectedIndex=list.IndexFromPoint(e.Location); };
            ContextMenuStrip context=new ContextMenuStrip { Renderer=new DarkRenderer() };
            context.Items.Add("Ir al fotograma",null,delegate { if(Selected!=null && Jump!=null) Jump(Selected); });
            context.Items.Add("Editar nota",null,delegate { if(Selected!=null) BeginEdit(Selected); });
            context.Items.Add(new ToolStripSeparator());
            context.Items.Add("Eliminar nota",null,delegate { if(Selected!=null) { document.Markers.Remove(Selected); if(Changed!=null) Changed(); RefreshMarkers(document); } });
            list.ContextMenuStrip=context;
            empty.TextAlign=ContentAlignment.MiddleCenter;
            editor.BackColor=Theme.Panel; editor.Visible=false; editorScroll.AutoScroll=true;
            at.Font=new Font("Consolas",11); at.BackColor=Theme.AccentSoft; at.TextAlign=ContentAlignment.MiddleCenter;
            ConfigureInput(title,"Título de la nota",false,300); ConfigureInput(note,"Texto de la nota",true,20000);
            titleBox.Controls.Add(title); noteBox.Controls.Add(note);
            title.Enter += delegate { titleBox.Highlight=true; titleBox.Invalidate(); }; title.Leave += delegate { titleBox.Highlight=false; titleBox.Invalidate(); };
            note.Enter += delegate { noteBox.Highlight=true; noteBox.Invalidate(); }; note.Leave += delegate { noteBox.Highlight=false; noteBox.Invalidate(); };
            fields.Controls.AddRange(new Control[]{at,frame,titleLabel,titleBox,noteLabel,noteBox}); editorScroll.Controls.Add(fields);
            editor.Controls.AddRange(new Control[]{editorHeading,editorScroll,saveHint,save,cancel});
            save.Click += delegate { SaveEdit(); }; cancel.Click += delegate { CancelEdit(); };
            Controls.AddRange(new Control[]{heading,subtitle,count,add,section,edit,list,empty,hint,import,export,editor});
            Resize += delegate { Arrange(); };
            SetScale(1); RefreshMarkers(null);
        }
        private void ConfigureInput(TextBox input,string accessible,bool multiline,int max)
        {
            input.BackColor=Theme.Background; input.ForeColor=Theme.Ink; input.BorderStyle=BorderStyle.None;
            input.Font=new Font("Segoe UI",10); input.AccessibleName=accessible; input.MaxLength=max; input.Multiline=multiline;
            input.AcceptsReturn=multiline; input.ScrollBars=multiline ? ScrollBars.Vertical : ScrollBars.None;
        }
        public void SetScale(float value) { scale=value; Arrange(); }
        private void Arrange()
        {
            int pad=S(18), w=Math.Max(S(200),ClientSize.Width-2*pad), h=ClientSize.Height;
            bool compact=h<S(350);
            int listTop=compact ? S(102) : S(132), bottomSpace=compact ? S(64) : S(82);
            list.ItemHeight=Math.Min(255,S(compact ? 108 : 126));
            heading.SetBounds(pad,S(16),w-S(42),S(30)); count.SetBounds(pad+w-S(32),S(21),S(32),S(24));
            subtitle.SetBounds(pad,S(47),w,S(22)); subtitle.Visible=!compact && !editing;
            add.SetBounds(pad,S(compact ? 56 : 80),w-S(84),S(36));
            section.Visible=false; edit.SetBounds(pad+w-S(78),add.Top,S(78),S(36));
            int bottom=Math.Max(listTop,h-bottomSpace);
            list.SetBounds(pad,listTop,w,Math.Max(0,bottom-listTop)); empty.Bounds=list.Bounds;
            if(compact) list.ItemHeight=Math.Min(S(108),Math.Max(S(88),list.Height));
            hint.SetBounds(pad,h-S(75),w,S(20));
            hint.Visible=!compact && !editing;
            int half=(w-S(10))/2; import.SetBounds(pad,h-S(46),half,S(34)); export.SetBounds(pad+half+S(10),h-S(46),w-half-S(10),S(34));
            editor.Bounds=ClientRectangle;
            editorHeading.SetBounds(pad,S(16),w,S(34));
            editorScroll.SetBounds(pad,S(60),w,Math.Max(S(40),h-S(146)));
            int fieldWidth=Math.Max(S(180),w-SystemInformation.VerticalScrollBarWidth);
            fields.SetBounds(0,0,fieldWidth,Math.Max(S(300),editorScroll.Height));
            at.SetBounds(0,0,S(134),S(34)); frame.SetBounds(S(146),0,Math.Max(1,fieldWidth-S(146)),S(34));
            titleLabel.SetBounds(0,S(53),fieldWidth,S(20)); titleBox.SetBounds(0,S(79),fieldWidth,S(40));
            title.SetBounds(S(12),S(10),Math.Max(1,fieldWidth-S(24)),S(22));
            noteLabel.SetBounds(0,S(137),fieldWidth,S(20)); noteBox.SetBounds(0,S(163),fieldWidth,Math.Max(S(130),fields.Height-S(170)));
            note.SetBounds(S(12),S(12),Math.Max(1,fieldWidth-S(24)),Math.Max(1,noteBox.Height-S(24)));
            saveHint.SetBounds(pad,h-S(77),w,S(20)); cancel.SetBounds(pad,h-S(47),S(94),S(35)); save.SetBounds(pad+S(104),h-S(47),Math.Max(1,w-S(104)),S(35));
            Invalidate(); list.Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using(Pen p=new Pen(Theme.Border)) { e.Graphics.DrawLine(p,0,0,0,Height); if(!editing) { int bottom=Height<S(350) ? S(56) : S(86); e.Graphics.DrawLine(p,S(18),Height-bottom,Width-S(18),Height-bottom); } }
        }
        private ReviewMarker Selected { get { return list.SelectedItem as ReviewMarker; } }
        public void RefreshMarkers(ReviewDocument doc)
        {
            document=doc; string id=Selected==null ? "" : Selected.Id;
            list.BeginUpdate(); list.Items.Clear();
            if(doc!=null) foreach(ReviewMarker marker in doc.Markers.OrderBy(m=>m.Frame)) list.Items.Add(marker);
            list.EndUpdate();
            for(int i=0;i<list.Items.Count;i++) if(((ReviewMarker)list.Items[i]).Id==id) list.SelectedIndex=i;
            if(list.SelectedIndex<0 && list.Items.Count>0) list.SelectedIndex=0;
            count.Text=list.Items.Count.ToString(); edit.Enabled=Selected!=null;
            add.Enabled=import.Enabled=export.Enabled=doc!=null; empty.Visible=list.Items.Count==0 && !editing;
            empty.Text=doc==null ? "Todo listo para revisar\n\nAbre un vídeo para añadir\nnotas a sus fotogramas." : "Tu revisión empieza aquí\n\nPulsa M sobre un fotograma\npara añadir tu primera nota.";
        }
        private void SetEditing(bool value)
        {
            editing=value;
            foreach(Control c in Controls) if(c!=editor) c.Visible=!value;
            empty.Visible=!value && list.Items.Count==0;
            editor.Visible=value; if(value) editor.BringToFront(); Arrange(); Invalidate();
        }
        public void CancelEdit() { pending=null; SetEditing(false); list.Focus(); }
        public void CommitEdit() { if(Editing) SaveEdit(); }
        public void BeginEdit(ReviewMarker marker)
        {
            if(document==null || marker==null) return;
            if(Editing) SaveEdit();
            pending=marker; editorHeading.Text=document.Markers.Contains(marker) ? "Editar nota" : "Nueva nota";
            at.Text=document.Timecode(marker.Frame); frame.Text="Fotograma "+marker.Frame;
            title.Text=marker.Name; note.Text=marker.Note; editorScroll.AutoScrollPosition=Point.Empty;
            SetEditing(true); title.Focus(); title.SelectAll();
        }
        private void SaveEdit()
        {
            if(pending==null || document==null) return;
            string id=pending.Id;
            pending.Name=string.IsNullOrWhiteSpace(title.Text) ? "Nota" : title.Text.Trim(); pending.Note=note.Text;
            if(!document.Markers.Contains(pending)) document.Markers.Add(pending);
            pending=null; SetEditing(false);
            if(Changed!=null) Changed(); RefreshMarkers(document);
            for(int i=0;i<list.Items.Count;i++) if(((ReviewMarker)list.Items[i]).Id==id) list.SelectedIndex=i;
            list.Focus();
        }
        protected override bool ProcessCmdKey(ref Message msg,Keys keys)
        {
            if(Editing && keys==(Keys.Control|Keys.Enter)) { SaveEdit(); return true; }
            if(Editing && keys==Keys.Escape) { CancelEdit(); return true; }
            return base.ProcessCmdKey(ref msg,keys);
        }
        private void DrawMarker(object sender,DrawItemEventArgs e)
        {
            if(e.Index<0 || document==null) return;
            ReviewMarker marker=(ReviewMarker)list.Items[e.Index]; bool selected=(e.State&DrawItemState.Selected)!=0;
            Graphics g=e.Graphics; g.SmoothingMode=SmoothingMode.AntiAlias;
            using(Brush b=new SolidBrush(Theme.Panel)) g.FillRectangle(b,e.Bounds);
            bool compact=e.Bounds.Height<S(118);
            Rectangle card=new Rectangle(e.Bounds.X+1,e.Bounds.Y+1,e.Bounds.Width-3,e.Bounds.Height-S(compact ? 6 : 10));
            Theme.Box(g,card,S(9),selected ? Color.FromArgb(32,43,43) : Theme.Card,selected ? Color.FromArgb(72,122,106) : Theme.Border);
            Rectangle chip=new Rectangle(card.X+S(12),card.Y+S(compact ? 8 : 12),S(108),S(23));
            Theme.Box(g,chip,S(4),Theme.AccentSoft,Color.Empty);
            TextRenderer.DrawText(g,document.Timecode(marker.Frame),codeFont,chip,Theme.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPadding);
            Rectangle number=new Rectangle(chip.Right+S(8),chip.Y,Math.Max(0,card.Right-chip.Right-S(20)),chip.Height);
            TextRenderer.DrawText(g,(e.Index+1).ToString("00"),bodyFont,number,Theme.Muted,TextFormatFlags.Right|TextFormatFlags.VerticalCenter);
            Rectangle r=new Rectangle(card.X+S(12),card.Y+S(compact ? 36 : 43),card.Width-S(24),S(23));
            TextRenderer.DrawText(g,marker.Name,nameFont,r,Theme.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine);
            r.Y=card.Y+S(compact ? 62 : 70); r.Height=Math.Max(S(18),card.Bottom-r.Y-S(8));
            TextRenderer.DrawText(g,string.IsNullOrWhiteSpace(marker.Note) ? "Sin comentario" : marker.Note.Replace("\r", " ").Replace("\n", " "),bodyFont,r,Theme.Muted,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|(compact ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak));
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing) { codeFont.Dispose(); nameFont.Dispose(); bodyFont.Dispose(); if(list.ContextMenuStrip!=null) list.ContextMenuStrip.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
