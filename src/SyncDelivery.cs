// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Prisma
{
    // Delivery to a folder that Google Drive (or Dropbox, OneDrive) keeps in sync on this
    // computer. This is the only way notes reach the cloud: there is no AI connection. Every time the notes change, Prisma writes "Ajustes <video>.docx" (one checkbox per
    // note) and "<video>.prisma.json" there, and the sync app uploads them. Prisma itself never
    // connects to the cloud. A document someone else edited is kept under another name, never lost.
    internal static class SyncDelivery
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
        private static readonly XNamespace MC = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        private static readonly DateTimeOffset FixedTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public static string Service(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return "";
            string f = folder.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (f.IndexOf("\\Mi unidad\\", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("\\My Drive\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.IndexOf("\\Unidades compartidas\\", StringComparison.OrdinalIgnoreCase) >= 0 || f.IndexOf("\\Shared drives\\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                f.IndexOf("\\Google Drive\\", StringComparison.OrdinalIgnoreCase) >= 0) return "Google Drive";
            if (f.IndexOf("\\Dropbox", StringComparison.OrdinalIgnoreCase) >= 0) return "Dropbox";
            foreach (string variable in new string[] { "OneDrive", "OneDriveCommercial", "OneDriveConsumer" })
            {
                string root = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(root) && f.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return "OneDrive";
            }
            if (f.IndexOf("\\OneDrive", StringComparison.OrdinalIgnoreCase) >= 0) return "OneDrive";
            return "Carpeta";
        }

        public static bool IsValidFolder(string folder)
        {
            try { return !string.IsNullOrWhiteSpace(folder) && folder.Length <= 1024 && Path.IsPathRooted(folder) && Path.GetFullPath(folder).Length > 3; }
            catch { return false; }
        }

        // Suggests the "My Drive" folder of Google Drive for desktop, if there is one.
        public static string SuggestedStart()
        {
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                    foreach (string name in new string[] { "Mi unidad", "My Drive" })
                    {
                        string candidate = Path.Combine(d.RootDirectory.FullName, name);
                        if (d.IsReady && Directory.Exists(candidate)) return candidate;
                    }
            }
            catch { }
            string dropbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox");
            if (Directory.Exists(dropbox)) return dropbox;
            string onedrive = Environment.GetEnvironmentVariable("OneDrive");
            return !string.IsNullOrEmpty(onedrive) && Directory.Exists(onedrive) ? onedrive : "";
        }

        public static string Label(ReviewDocument doc)
        {
            return doc == null || doc.SyncFolder.Length == 0 ? "" : Service(doc.SyncFolder);
        }

        public static string Title(ReviewDocument doc) { return "Ajustes " + Path.GetFileNameWithoutExtension(Path.GetFileName(doc.MediaPath)); }
        public static string SharingFileName(ReviewDocument doc) { return Path.GetFileNameWithoutExtension(Path.GetFileName(doc.MediaPath)) + ".prisma.json"; }

        public static string Line(ReviewDocument doc, ReviewMarker m)
        {
            string name = (m.Name ?? "").Trim(), note = Regex.Replace((m.Note ?? "").Trim(), @"\s*[\r\n]+\s*", " / ");
            bool generic = IsDefaultName(name);
            string body = generic ? note : note.Length == 0 ? name : name + ": " + note;
            return doc.Timecode(m.Frame) + " " + (body.Length == 0 ? "(sin texto)" : body);
        }

        // The title Prisma proposes for a new note ("Nota 3"), or none: it says nothing, so the
        // documents leave it out and keep only the timecode and the comment.
        public static bool IsDefaultName(string name)
        {
            return string.IsNullOrWhiteSpace(name) || Regex.IsMatch(name.Trim(), @"^nota(\s*(n\.?|n\u00ba|#)?\s*\d+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }


        public static string DocumentName(ReviewDocument doc) { return Title(doc) + ".docx"; }

        // Writes both files. Returns a short status for the status bar.
        public static string Write(ReviewDocument doc, string folder)
        {
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("No se encuentra la carpeta sincronizada. ¿Está abierta la aplicación de Google Drive, Dropbox u OneDrive?");
            string docx = Path.Combine(folder, DocumentName(doc));
            byte[] bytes = Docx(doc);
            string written = Hash(bytes), kept = "";
            if (File.Exists(docx))
            {
                string current = Hash(File.ReadAllBytes(docx));
                if (current != written && current != (doc.SyncHash ?? ""))
                {
                    // Not the last version Prisma wrote (someone edited it, or it was made elsewhere): keep it.
                    kept = Path.Combine(folder, Title(doc) + " (editado " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ").docx");
                    File.Move(docx, kept);
                }
            }
            if (!File.Exists(docx) || Hash(File.ReadAllBytes(docx)) != written) Atomic(docx, bytes);
            doc.SyncHash = written;
            byte[] json = new UTF8Encoding(false).GetBytes(doc.SharingJson());
            string jsonPath = Path.Combine(folder, SharingFileName(doc));
            if (!File.Exists(jsonPath) || !File.ReadAllBytes(jsonPath).SequenceEqual(json)) Atomic(jsonPath, json);
            return kept.Length > 0 ? "El documento tenía cambios de otra persona: se conservan en «" + Path.GetFileName(kept) + "»" : "Notas actualizadas en " + Service(folder);
        }

        private static void Atomic(string path, byte[] bytes)
        {
            string temp = path + ".prisma-tmp";
            File.WriteAllBytes(temp, bytes);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }

        private static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        private static XElement Run(string text, bool bold, bool italic, int halfPoints)
        {
            XElement props = new XElement(W + "rPr");
            if (bold) props.Add(new XElement(W + "b"));
            if (italic) props.Add(new XElement(W + "i"));
            if (halfPoints > 0) props.Add(new XElement(W + "sz", new XAttribute(W + "val", halfPoints)));
            return new XElement(W + "r", props, new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
        }

        // One unchecked checkbox per note. In Word it is a checkbox content control that can be
        // ticked with a click; other editors (Google Docs, Pages) show the same ☐ character.
        private static XElement CheckboxLine(int id, string text)
        {
            XElement box = new XElement(W + "sdt",
                new XElement(W + "sdtPr",
                    new XElement(W + "id", new XAttribute(W + "val", id)),
                    new XElement(W14 + "checkbox",
                        new XElement(W14 + "checked", new XAttribute(W14 + "val", 0)),
                        new XElement(W14 + "checkedState", new XAttribute(W14 + "val", "2612"), new XAttribute(W14 + "font", "MS Gothic")),
                        new XElement(W14 + "uncheckedState", new XAttribute(W14 + "val", "2610"), new XAttribute(W14 + "font", "MS Gothic")))),
                new XElement(W + "sdtContent",
                    new XElement(W + "r",
                        new XElement(W + "rPr", new XElement(W + "rFonts", new XAttribute(W + "ascii", "MS Gothic"), new XAttribute(W + "eastAsia", "MS Gothic"), new XAttribute(W + "hAnsi", "MS Gothic"), new XAttribute(W + "hint", "eastAsia"))),
                        new XElement(W + "t", "\u2610"))));
            return new XElement(W + "p",
                new XElement(W + "pPr", new XElement(W + "spacing", new XAttribute(W + "after", 80)), new XElement(W + "ind", new XAttribute(W + "left", 425), new XAttribute(W + "hanging", 425))),
                box,
                new XElement(W + "r", new XElement(W + "tab"), new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text)));
        }

        public static byte[] Docx(ReviewDocument doc)
        {
            XElement body = new XElement(W + "body");
            body.Add(new XElement(W + "p", Run(Title(doc), true, false, 28)));
            body.Add(new XElement(W + "p"));
            int id = 1;
            foreach (ReviewMarker m in doc.Markers.OrderBy(m => m.Frame)) body.Add(CheckboxLine(id++, Line(doc, m)));
            body.Add(new XElement(W + "p"));
            body.Add(new XElement(W + "p", Run("Notas de Prisma (" + doc.Fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " fps). Para cargarlas en Prisma y enviarlas a DaVinci Resolve, importa «" + SharingFileName(doc) + "» de esta carpeta. Prisma actualiza este documento al cambiar las notas; si lo editas aquí, tu versión se conserva con otro nombre.", false, true, 18)));
            body.Add(new XElement(W + "sectPr"));
            XDocument document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document",
                new XAttribute(XNamespace.Xmlns + "w", W), new XAttribute(XNamespace.Xmlns + "w14", W14), new XAttribute(XNamespace.Xmlns + "mc", MC), new XAttribute(MC + "Ignorable", "w14"), body));
            const string types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>";
            const string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";
            using (MemoryStream stream = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Entry(zip, "[Content_Types].xml", types);
                    Entry(zip, "_rels/.rels", rels);
                    Entry(zip, "word/document.xml", document.Declaration + document.ToString(SaveOptions.DisableFormatting));
                }
                return stream.ToArray();
            }
        }

        private static void Entry(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            entry.LastWriteTime = FixedTime; // Same notes, same bytes: the sync app has nothing to upload.
            using (Stream s = entry.Open()) { byte[] b = new UTF8Encoding(false).GetBytes(content); s.Write(b, 0, b.Length); }
        }
    }
}
