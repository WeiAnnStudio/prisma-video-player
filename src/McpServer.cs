// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Prisma
{
    // Model Context Protocol server (stdio, JSON-RPC 2.0) started by Claude or ChatGPT desktop as
    // "Prisma.exe --mcp". It only reads the notes Prisma saved in datos/marcadores: it never writes,
    // never opens network connections and never reveals full paths, only file names.
    internal sealed class McpServer
    {
        public const string Instructions =
            "Prisma guarda notas de revisión de vídeo con código de tiempo. Para pasar las notas de un vídeo a su carpeta en la nube: " +
            "1) Llama a leer_revision con el nombre del vídeo (usa listar_revisiones si no lo sabes). " +
            "2) Si no tiene carpeta, pide al usuario que la indique en Prisma (Revisión > Carpeta en la nube) y no la inventes. " +
            "3) Con el conector del servicio de esa carpeta, crea dentro de ella un documento con el título exacto que te da la herramienta: en Google Drive un documento de Google Docs; en Dropbox u OneDrive un documento de Word. " +
            "4) Contenido: el título en negrita y debajo una lista con viñetas, una viñeta por línea de notas, en el mismo orden y copiada literalmente. No recalcules ni redondees los códigos de tiempo. " +
            "Al final del documento añade una línea en cursiva: «Para importar estas notas en Prisma: archivo <archivo_prisma> en esta carpeta». " +
            "5) Si ya existe un documento con ese título en la carpeta, añade solo las líneas que falten, sin duplicar ni borrar nada. " +
            "6) Guarda también en la misma carpeta el archivo de notas para Prisma: llama a json_revision y sube su resultado tal cual, sin cambiar ni un carácter, como archivo con el nombre de archivo_prisma y tipo application/json, sin convertirlo a documento. Si ya existe, sustitúyelo. Con ese archivo, otra persona importa las notas en su Prisma y las envía a su DaVinci Resolve. " +
            "Si el conector no permite subir archivos, dilo y pide al usuario que en Prisma use Exportar > JSON y suba ese archivo a la carpeta. " +
            "7) Responde con el enlace del documento y del archivo. Si el usuario no tiene conectado ese servicio en la app, díselo en lugar de guardar las notas en otro sitio.";

        private static readonly string[] Versions = { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
        private readonly string folder;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };

        public McpServer(string markersFolder) { folder = markersFolder; }

        public static int Run(string markersFolder)
        {
            McpServer server = new McpServer(markersFolder);
            UTF8Encoding utf8 = new UTF8Encoding(false);
            using (StreamReader input = new StreamReader(Console.OpenStandardInput(), utf8))
            using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true, NewLine = "\n" })
            {
                string line;
                while ((line = input.ReadLine()) != null)
                {
                    if (line.Length > 1024 * 1024) { output.WriteLine(server.Error(null, -32600, "Mensaje demasiado grande.")); continue; }
                    if (line.Trim().Length == 0) continue;
                    string reply = server.Handle(line);
                    if (reply != null) output.WriteLine(reply);
                }
            }
            return 0;
        }

        // Returns the JSON reply, or null for notifications.
        public string Handle(string line)
        {
            Dictionary<string, object> request;
            try { request = json.DeserializeObject(line) as Dictionary<string, object>; }
            catch { return Error(null, -32700, "JSON no válido."); }
            if (request == null) return Error(null, -32600, "Petición no válida.");
            object id; bool hasId = request.TryGetValue("id", out id);
            string method = request.ContainsKey("method") ? request["method"] as string : null;
            if (method == null) return hasId ? Error(id, -32600, "Falta el método.") : null;
            Dictionary<string, object> args = request.ContainsKey("params") ? request["params"] as Dictionary<string, object> : null;
            if (!hasId) return null; // notifications/initialized, notifications/cancelled...
            try
            {
                switch (method)
                {
                    case "initialize": return Result(id, Initialize(args));
                    case "ping": return Result(id, new Dictionary<string, object>());
                    case "tools/list": return Result(id, new Dictionary<string, object> { { "tools", Tools() } });
                    case "tools/call": return Result(id, Call(args));
                    case "resources/list": return Result(id, new Dictionary<string, object> { { "resources", new object[0] } });
                    case "prompts/list": return Result(id, new Dictionary<string, object> { { "prompts", new object[0] } });
                    default: return Error(id, -32601, "Método desconocido: " + method);
                }
            }
            catch (Exception ex) { return Error(id, -32603, ex.Message); }
        }

        private Dictionary<string, object> Initialize(Dictionary<string, object> args)
        {
            string asked = args != null && args.ContainsKey("protocolVersion") ? args["protocolVersion"] as string : null;
            return new Dictionary<string, object>
            {
                { "protocolVersion", asked != null && Versions.Contains(asked) ? asked : Versions[1] },
                { "capabilities", new Dictionary<string, object> { { "tools", new Dictionary<string, object> { { "listChanged", false } } } } },
                { "serverInfo", new Dictionary<string, object> { { "name", "prisma" }, { "title", "Prisma · notas de revisión" }, { "version", "0.4.0" } } },
                { "instructions", Instructions }
            };
        }

        private static object[] Tools()
        {
            Dictionary<string, object> none = new Dictionary<string, object> { { "type", "object" }, { "properties", new Dictionary<string, object>() } };
            Dictionary<string, object> video = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object> { { "video", new Dictionary<string, object> { { "type", "string" }, { "description", "Nombre del vídeo (con o sin extensión) o el id que da listar_revisiones." } } } } },
                { "required", new object[] { "video" } }
            };
            Dictionary<string, object> readOnly = new Dictionary<string, object> { { "readOnlyHint", true }, { "openWorldHint", false } };
            return new object[]
            {
                new Dictionary<string, object> { { "name", "listar_revisiones" }, { "title", "Listar revisiones de Prisma" }, { "description", "Lista los vídeos con notas de revisión en Prisma: nombre, número de notas, FPS, carpeta en la nube y fecha." }, { "inputSchema", none }, { "annotations", readOnly } },
                new Dictionary<string, object> { { "name", "json_revision" }, { "title", "Archivo de notas para Prisma" }, { "description", "Devuelve el contenido exacto del archivo de notas de un vídeo para importarlo en otro Prisma (sin rutas del equipo). Súbelo sin modificar a la carpeta del vídeo con el nombre que indica leer_revision (archivo_prisma), como application/json y sin convertir." }, { "inputSchema", video }, { "annotations", readOnly } },
                new Dictionary<string, object> { { "name", "leer_revision" }, { "title", "Leer las notas de un vídeo" }, { "description", "Devuelve las notas de un vídeo listas para un documento: título, carpeta en la nube y una línea por nota (código de tiempo y texto), en orden. " + Instructions }, { "inputSchema", video }, { "annotations", readOnly } }
            };
        }

        private Dictionary<string, object> Call(Dictionary<string, object> args)
        {
            string name = args != null && args.ContainsKey("name") ? args["name"] as string : null;
            Dictionary<string, object> input = args != null && args.ContainsKey("arguments") ? args["arguments"] as Dictionary<string, object> : null;
            if (name == "listar_revisiones") return Text(ListText(), false);
            if (name == "leer_revision" || name == "json_revision")
            {
                string query = input != null && input.ContainsKey("video") ? input["video"] as string : null;
                if (string.IsNullOrWhiteSpace(query)) return Text("Indica el nombre del vídeo.", true);
                return Find(query.Trim(), name == "json_revision");
            }
            return Text("Herramienta desconocida: " + name, true);
        }

        private sealed class Entry { public string Id, Video; public DateTime Updated; public ReviewDocument Doc; }

        private List<Entry> Load()
        {
            List<Entry> entries = new List<Entry>();
            if (!Directory.Exists(folder)) return entries;
            foreach (string file in Directory.GetFiles(folder, "*.json").Take(5000))
            {
                try
                {
                    ReviewDocument doc = ReviewDocument.Load(file);
                    entries.Add(new Entry { Id = Path.GetFileNameWithoutExtension(file).Substring(0, Math.Min(12, Path.GetFileNameWithoutExtension(file).Length)), Video = Path.GetFileName(doc.MediaPath), Updated = File.GetLastWriteTime(file), Doc = doc });
                }
                catch { }
            }
            return entries.OrderByDescending(e => e.Updated).ToList();
        }

        private string ListText()
        {
            List<Entry> entries = Load();
            if (entries.Count == 0) return "No hay vídeos con notas en Prisma.";
            StringBuilder text = new StringBuilder("Vídeos con notas en Prisma (más recientes primero):\n");
            foreach (Entry e in entries)
                text.Append("- ").Append(e.Video).Append(" · id ").Append(e.Id).Append(" · ").Append(e.Doc.Markers.Count).Append(" notas · ")
                    .Append(e.Doc.Fps.ToString("0.###", CultureInfo.InvariantCulture)).Append(" fps · carpeta: ")
                    .Append(SyncDelivery.Label(e.Doc).Length > 0 ? SyncDelivery.Label(e.Doc) : "sin indicar")
                    .Append(" · actualizado ").Append(e.Updated.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            return text.ToString();
        }

        private Dictionary<string, object> Find(string query, bool asJson)
        {
            List<Entry> entries = Load();
            string plain = Path.GetFileNameWithoutExtension(query);
            List<Entry> found = entries.Where(e => e.Id.Equals(query, StringComparison.OrdinalIgnoreCase) || e.Video.Equals(query, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(e.Video).Equals(plain, StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0) found = entries.Where(e => e.Video.IndexOf(plain, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (found.Count == 0) return Text("No hay notas de un vídeo llamado «" + query + "». Usa listar_revisiones para ver los disponibles.", true);
            if (found.Count > 1) return Text("Hay varios vídeos que coinciden: " + string.Join(", ", found.Select(e => e.Video + " (id " + e.Id + ")").ToArray()) + ". Indica el nombre exacto o el id.", true);
            return Text(asJson ? found[0].Doc.SharingJson() : Document(found[0].Doc), false);
        }

        public static string Title(ReviewDocument doc) { return "Ajustes " + Path.GetFileNameWithoutExtension(Path.GetFileName(doc.MediaPath)); }
        public static string SharingFileName(ReviewDocument doc) { return Path.GetFileNameWithoutExtension(Path.GetFileName(doc.MediaPath)) + ".prisma.json"; }

        public static string Line(ReviewDocument doc, ReviewMarker m)
        {
            string name = (m.Name ?? "").Trim(), note = Regex.Replace((m.Note ?? "").Trim(), @"\s*[\r\n]+\s*", " / ");
            bool generic = name.Length == 0 || Regex.IsMatch(name, @"^Nota( \d+)?$");
            string body = generic ? note : note.Length == 0 ? name : name + ": " + note;
            return doc.Timecode(m.Frame) + " " + (body.Length == 0 ? "(sin texto)" : body);
        }

        public static string Document(ReviewDocument doc)
        {
            StringBuilder text = new StringBuilder();
            text.Append("titulo: ").Append(Title(doc)).Append('\n');
            text.Append("video: ").Append(Path.GetFileName(doc.MediaPath)).Append('\n');
            text.Append("archivo_prisma: ").Append(SharingFileName(doc)).Append(" (contenido con json_revision)\n");
            if (doc.CloudFolder.Length > 0) text.Append("carpeta: ").Append(doc.CloudFolder).Append(" (").Append(CloudFolder.ServiceName(doc.CloudFolder)).Append(")\n");
            else text.Append("carpeta: sin indicar. Pide al usuario que la indique en Prisma (Revisión > Carpeta en la nube).\n");
            if (doc.SyncFolder.Length > 0) text.Append("entrega_automatica: Prisma ya guarda «").Append(SyncDelivery.DocumentName(doc)).Append("» y «").Append(SharingFileName(doc)).Append("» en una carpeta sincronizada con ").Append(SyncDelivery.Service(doc.SyncFolder)).Append(" («").Append(Path.GetFileName(doc.SyncFolder.TrimEnd('\\', '/'))).Append("»). No hace falta crearlos otra vez: díselo al usuario salvo que pida expresamente otro documento.\n");
            text.Append("fps: ").Append(doc.FpsNumerator).Append('/').Append(doc.FpsDenominator).Append(" · código de tiempo HH:MM:SS:FF desde el inicio del vídeo\n");
            text.Append("notas (").Append(doc.Markers.Count).Append("), una viñeta por línea:\n");
            foreach (ReviewMarker m in doc.Markers.OrderBy(m => m.Frame)) text.Append(Line(doc, m)).Append('\n');
            return text.ToString();
        }

        private static Dictionary<string, object> Text(string text, bool error)
        {
            return new Dictionary<string, object> { { "content", new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", text } } } }, { "isError", error } };
        }
        private string Result(object id, object result)
        {
            return json.Serialize(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "result", result } });
        }
        private string Error(object id, int code, string message)
        {
            return json.Serialize(new Dictionary<string, object> { { "jsonrpc", "2.0" }, { "id", id }, { "error", new Dictionary<string, object> { { "code", code }, { "message", message } } } });
        }
    }
}
