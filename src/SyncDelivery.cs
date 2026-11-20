// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace Prisma
{
    // Automatic delivery to a folder that Google Drive, Dropbox or OneDrive keeps in sync on this
    // computer. Every time the notes change, Prisma writes "Ajustes <video>.docx" (one bullet per
    // note) and "<video>.prisma.json" there, and the sync app uploads them. Prisma itself never
    // connects to the cloud. A document someone else edited is kept under another name, never lost.
    internal static class SyncDelivery
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
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
            if (doc == null) return "";
            if (doc.SyncFolder.Length > 0) return Service(doc.SyncFolder) + " · auto";
            return CloudFolder.ServiceName(doc.CloudFolder);
        }

        public static string DocumentName(ReviewDocument doc) { return McpServer.Title(doc) + ".docx"; }

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
                    kept = Path.Combine(folder, McpServer.Title(doc) + " (editado " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ").docx");
                    File.Move(docx, kept);
                }
            }
            if (!File.Exists(docx) || Hash(File.ReadAllBytes(docx)) != written) Atomic(docx, bytes);
            doc.SyncHash = written;
            byte[] json = new UTF8Encoding(false).GetBytes(doc.SharingJson());
            string jsonPath = Path.Combine(folder, McpServer.SharingFileName(doc));
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

        public static byte[] Docx(ReviewDocument doc)
        {
            XElement body = new XElement(W + "body");
            body.Add(new XElement(W + "p", Run(McpServer.Title(doc), true, false, 28)));
            body.Add(new XElement(W + "p"));
            foreach (ReviewMarker m in doc.Markers.OrderBy(m => m.Frame))
                body.Add(new XElement(W + "p",
                    new XElement(W + "pPr", new XElement(W + "numPr", new XElement(W + "ilvl", new XAttribute(W + "val", 0)), new XElement(W + "numId", new XAttribute(W + "val", 1)))),
                    Run(McpServer.Line(doc, m), false, false, 0)));
            body.Add(new XElement(W + "p"));
            body.Add(new XElement(W + "p", Run("Notas de Prisma (" + doc.Fps.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " fps). Para cargarlas en Prisma y enviarlas a DaVinci Resolve, importa «" + McpServer.SharingFileName(doc) + "» de esta carpeta. Prisma actualiza este documento al cambiar las notas; si lo editas aquí, tu versión se conserva con otro nombre.", false, true, 18)));
            body.Add(new XElement(W + "sectPr"));
            XDocument document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "document", new XAttribute(XNamespace.Xmlns + "w", W), body));
            XDocument numbering = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), new XElement(W + "numbering", new XAttribute(XNamespace.Xmlns + "w", W),
                new XElement(W + "abstractNum", new XAttribute(W + "abstractNumId", 0),
                    new XElement(W + "lvl", new XAttribute(W + "ilvl", 0),
                        new XElement(W + "start", new XAttribute(W + "val", 1)), new XElement(W + "numFmt", new XAttribute(W + "val", "bullet")),
                        new XElement(W + "lvlText", new XAttribute(W + "val", "•")), new XElement(W + "lvlJc", new XAttribute(W + "val", "left")),
                        new XElement(W + "pPr", new XElement(W + "ind", new XAttribute(W + "left", 720), new XAttribute(W + "hanging", 360))))),
                new XElement(W + "num", new XAttribute(W + "numId", 1), new XElement(W + "abstractNumId", new XAttribute(W + "val", 0)))));
            const string types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>";
            const string rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>";
            const string docRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>";
            using (MemoryStream stream = new MemoryStream())
            {
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    Entry(zip, "[Content_Types].xml", types);
                    Entry(zip, "_rels/.rels", rels);
                    Entry(zip, "word/_rels/document.xml.rels", docRels);
                    Entry(zip, "word/document.xml", document.Declaration + document.ToString(SaveOptions.DisableFormatting));
                    Entry(zip, "word/numbering.xml", numbering.Declaration + numbering.ToString(SaveOptions.DisableFormatting));
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
