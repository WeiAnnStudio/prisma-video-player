// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace Prisma
{
    public sealed class ReviewMarker
    {
        public string Id = Guid.NewGuid().ToString("N");
        public long Frame;
        public string Name = "Nota", Note = "";
    }
    public sealed class ReviewDocument
    {
        public int Version = 1;
        public string MediaPath = "";
        public int FpsNumerator = 25, FpsDenominator = 1;
        public long DurationFrames;
        public int Width = 1920, Height = 1080, AudioChannels = 0, AudioRate = 48000;
        public List<ReviewMarker> Markers = new List<ReviewMarker>();
        // Same limits as the note editor; imported files cannot exceed them either.
        public const int MaxNameLength = 300, MaxNoteLength = 20000;
        [ScriptIgnore] public double Fps { get { return (double)FpsNumerator / FpsDenominator; } }
        public void SetFps(double fps)
        {
            foreach (int n in new int[] { 24000, 30000, 60000, 120000 })
                if (Math.Abs(fps - n / 1001.0) < .002) { FpsNumerator = n; FpsDenominator = 1001; return; }
            if (fps <= 0 || fps > 240 || Math.Abs(fps - Math.Round(fps)) > .002) throw new InvalidDataException("El vídeo no declara una velocidad de fotogramas estándar compatible con este intercambio XML.");
            FpsNumerator = (int)Math.Round(fps); FpsDenominator = 1;
        }
        public string Timecode(long frame)
        {
            int nominal = (int)Math.Round(Fps); frame = Math.Max(0, frame);
            return (frame / (nominal * 3600)).ToString("00") + ":" + (frame / (nominal * 60) % 60).ToString("00") + ":" + (frame / nominal % 60).ToString("00") + ":" + (frame % nominal).ToString("00");
        }
        public void Validate()
        {
            if (Version != 1 || FpsNumerator <= 0 || FpsDenominator <= 0 || Fps < 1 || Fps > 240 || DurationFrames < 0 || Markers == null || Markers.Count > 100000) throw new InvalidDataException("El documento de marcadores no es válido.");
            foreach (ReviewMarker m in Markers)
            {
                if (m == null || m.Frame < 0 || (DurationFrames > 0 && m.Frame >= DurationFrames)) throw new InvalidDataException("Hay marcadores fuera de la duración del vídeo.");
                if (string.IsNullOrEmpty(m.Id)) m.Id = Guid.NewGuid().ToString("N");
                m.Name = m.Name ?? "Nota"; m.Note = m.Note ?? "";
                if (m.Id.Length > 64 || m.Name.Length > MaxNameLength || m.Note.Length > MaxNoteLength) throw new InvalidDataException("Hay notas demasiado largas en el documento.");
            }
        }
        public static string StoragePath(string dataFolder, string media)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(media).ToUpperInvariant()))).Replace("-", "");
                return Path.Combine(dataFolder, "marcadores", key + ".json");
            }
        }
        public void Save(string path)
        {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temp = path + ".tmp"; File.WriteAllText(temp, new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        // Copy meant for other people: keeps the notes and the video's file name, but
        // not the full path, which would reveal the Windows user name and folder layout.
        public void SaveForSharing(string path)
        {
            ReviewDocument copy = new ReviewDocument { Version = Version, MediaPath = Path.GetFileName(MediaPath), FpsNumerator = FpsNumerator, FpsDenominator = FpsDenominator, DurationFrames = DurationFrames, Width = Width, Height = Height, AudioChannels = AudioChannels, AudioRate = AudioRate, Markers = Markers };
            copy.Save(path);
        }
        public static ReviewDocument Load(string path)
        {
            if (new FileInfo(path).Length > 16 * 1024 * 1024) throw new InvalidDataException("El documento de marcadores es demasiado grande.");
            ReviewDocument doc = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<ReviewDocument>(File.ReadAllText(path));
            if (doc == null) throw new InvalidDataException("Documento vacío."); doc.Validate(); return doc;
        }
        private XElement Rate()
        {
            return new XElement("rate", new XElement("timebase", (int)Math.Round(Fps)), new XElement("ntsc", FpsDenominator == 1001 ? "TRUE" : "FALSE"));
        }
        private static XElement MarkerXml(ReviewMarker m)
        {
            return new XElement("marker", new XElement("name", m.Name), new XElement("comment", m.Note), new XElement("in", m.Frame), new XElement("out", -1));
        }
        public void ExportXml(string path)
        {
            Validate(); if (DurationFrames <= 0 || !File.Exists(MediaPath)) throw new InvalidDataException("Abre un vídeo local antes de exportar.");
            string name = Path.GetFileName(MediaPath);
            XElement videoFormat = new XElement("samplecharacteristics", Rate(), new XElement("width", Width), new XElement("height", Height), new XElement("anamorphic", "FALSE"), new XElement("pixelaspectratio", "square"), new XElement("fielddominance", "none"));
            XElement media = new XElement("media", new XElement("video", new XElement(videoFormat)));
            if (AudioChannels > 0) media.Add(new XElement("audio", new XElement("samplecharacteristics", new XElement("depth", 16), new XElement("samplerate", AudioRate)), new XElement("channelcount", AudioChannels)));
            XElement file = new XElement("file", new XAttribute("id", "prisma-file"), new XElement("name", name), new XElement("pathurl", new Uri(Path.GetFullPath(MediaPath)).AbsoluteUri), Rate(), new XElement("duration", DurationFrames), new XElement("timecode", Rate(), new XElement("string", "00:00:00:00"), new XElement("frame", 0), new XElement("displayformat", "NDF")), media);
            XElement clip = new XElement("clipitem", new XAttribute("id", "prisma-video"), new XElement("name", name), new XElement("enabled", "TRUE"), new XElement("duration", DurationFrames), Rate(), new XElement("start", 0), new XElement("end", DurationFrames), new XElement("in", 0), new XElement("out", DurationFrames), file, new XElement("sourcetrack", new XElement("mediatype", "video"), new XElement("trackindex", 1)));
            XElement timelineMedia = new XElement("media", new XElement("video", new XElement("format", new XElement(videoFormat)), new XElement("track", clip)));
            if (AudioChannels > 0)
            {
                XElement audio = new XElement("audio", new XElement("numOutputChannels", AudioChannels), new XElement("format", new XElement("samplecharacteristics", new XElement("depth", 16), new XElement("samplerate", AudioRate))));
                for (int channel = 1; channel <= AudioChannels; channel++)
                {
                    string id = "prisma-audio-" + channel;
                    XElement ac = new XElement("clipitem", new XAttribute("id", id), new XElement("name", name), new XElement("enabled", "TRUE"), new XElement("duration", DurationFrames), Rate(), new XElement("start", 0), new XElement("end", DurationFrames), new XElement("in", 0), new XElement("out", DurationFrames), new XElement("file", new XAttribute("id", "prisma-file")), new XElement("sourcetrack", new XElement("mediatype", "audio"), new XElement("trackindex", channel)));
                    audio.Add(new XElement("track", ac));
                }
                timelineMedia.Add(audio);
            }
            XElement sequence = new XElement("sequence", new XAttribute("id", "prisma-review"), new XElement("name", Path.GetFileNameWithoutExtension(name) + " — Revisión Prisma"), new XElement("duration", DurationFrames), Rate(), new XElement("timecode", Rate(), new XElement("string", "00:00:00:00"), new XElement("frame", 0), new XElement("displayformat", "NDF")), timelineMedia);
            foreach (ReviewMarker marker in Markers.OrderBy(m => m.Frame)) sequence.Add(MarkerXml(marker));
            XDocument xml = new XDocument(new XDeclaration("1.0", "UTF-8", null), new XDocumentType("xmeml", null, null, null), new XElement("xmeml", new XAttribute("version", "5"), sequence));
            xml.Save(path);
        }
        // Import sequence markers from FCP 7 XML exported by either editor. Clip
        // markers are accepted for a single untrimmed clip, avoiding silent retiming.
        public static ReviewDocument ImportXml(string path)
        {
            XmlReaderSettings options = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 };
            XDocument xml; using (XmlReader reader = XmlReader.Create(path, options)) xml = XDocument.Load(reader);
            if (xml.Root == null || xml.Root.Name != "xmeml") throw new InvalidDataException("Se necesita XML de Final Cut Pro 7 (.xml), no FCPXML.");
            XElement[] sequences = xml.Descendants("sequence").Where(x => x.Element("rate") != null).ToArray();
            if (sequences.Length != 1) throw new InvalidDataException("Exporta una sola secuencia para importar sus notas.");
            XElement sequence = sequences[0], rate = sequence.Element("rate");
            int nominal = (int?)rate.Element("timebase") ?? 0; bool ntsc = string.Equals((string)rate.Element("ntsc"), "TRUE", StringComparison.OrdinalIgnoreCase);
            ReviewDocument result = new ReviewDocument { FpsNumerator = ntsc ? nominal * 1000 : nominal, FpsDenominator = ntsc ? 1001 : 1, DurationFrames = (long?)sequence.Element("duration") ?? 0 };
            XElement source = sequence.Descendants("file").FirstOrDefault(x => x.Element("pathurl") != null);
            if (source != null)
            {
                Uri uri; if (Uri.TryCreate((string)source.Element("pathurl"), UriKind.Absolute, out uri) && uri.IsFile) result.MediaPath = uri.LocalPath;
            }
            IEnumerable<XElement> markers = sequence.Elements("marker");
            if (!markers.Any())
            {
                XElement[] clips = sequence.Descendants("video").Elements("track").Elements("clipitem").ToArray();
                if (clips.Length == 1 && ((long?)clips[0].Element("in") ?? 0) == 0 && ((long?)clips[0].Element("start") ?? 0) == 0) markers = clips[0].Elements("marker");
            }
            foreach (XElement marker in markers)
            {
                long frame = (long?)marker.Element("in") ?? -1;
                if (frame < 0) throw new InvalidDataException("Marcador sin posición válida.");
                result.Markers.Add(new ReviewMarker { Frame = frame, Name = (string)marker.Element("name") ?? "Nota", Note = (string)marker.Element("comment") ?? "" });
            }
            result.Validate(); return result;
        }
        public void ExportCsv(string path)
        {
            StringBuilder csv = new StringBuilder("Fotograma,Código de tiempo,FPS,Nombre,Nota\r\n");
            foreach (ReviewMarker m in Markers.OrderBy(m => m.Frame)) csv.Append(m.Frame).Append(',').Append(Timecode(m.Frame)).Append(',').Append(Fps.ToString("0.########", CultureInfo.InvariantCulture)).Append(',').Append(Csv(m.Name)).Append(',').Append(Csv(m.Note)).Append("\r\n");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
        }
        // Spreadsheets run cells that start with = + - @ as formulas. An apostrophe
        // keeps a note received from someone else as plain text (CSV injection).
        internal static string Csv(string value)
        {
            if (value.Length > 0 && "=+-@\t\r\n".IndexOf(value[0]) >= 0) value = "'" + value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
    internal sealed class Shuttle
    {
        public double Rate;
        public double Press(int direction)
        {
            if (Rate == 0) Rate = direction;
            else if (Math.Sign(Rate) == direction) Rate = direction * Math.Min(16, Math.Abs(Rate) * 2);
            else if (Math.Abs(Rate) > 1) Rate /= 2;
            else Rate = direction;
            return Rate;
        }
    }
}
