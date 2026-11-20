// SPDX-License-Identifier: GPL-3.0-or-later
using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Prisma;

internal static class ReviewTests
{
    private static int passed;
    private static void Check(bool value, string description) { if (!value) throw new Exception("FAIL " + description); Console.WriteLine("PASS " + description); passed++; }
    private static bool Wait(Func<bool> predicate, int milliseconds)
    {
        Stopwatch sw = Stopwatch.StartNew(); while (sw.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); if (predicate()) return true; Thread.Sleep(20); } return false;
    }
    private static object Field(object target, string name) { return target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target); }
    private static void Invoke(object target, string name, params object[] args) { target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(target, args); }
    private static void Key(PlayerForm form, Keys key) { Invoke(form, "ProcessCmdKey", new Message(), key); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Hardening.Apply();
            string root = args[0], output = Path.Combine(root, "tests", "exports"); Directory.CreateDirectory(output);
            ReviewDocument doc = new ReviewDocument { MediaPath = Path.Combine(root, "tests", "media", "prores-422-10bit.mov"), DurationFrames = 48, Width = 640, Height = 360, AudioChannels = 1 };
            doc.SetFps(24);
            doc.Markers.Add(new ReviewMarker { Frame = 0, Name = "Inicio", Note = "Revisar negros & contraste" });
            doc.Markers.Add(new ReviewMarker { Frame = 12, Name = "Piel áéíóú ñ", Note = "Bajar rojo <un poco>\nSegunda línea, con \"comillas\"." });
            doc.Markers.Add(new ReviewMarker { Frame = 47, Name = "Último fotograma", Note = "Comprobar salida" });
            string xml = Path.Combine(output, "revision-prisma.xml"); doc.ExportXml(xml);
            ReviewDocument imported = ReviewDocument.ImportXml(xml);
            Check(imported.Markers.Select(m => m.Frame).SequenceEqual(new long[] { 0, 12, 47 }), "XML conserva fotograma cero, intermedio y último");
            Check(imported.Markers[1].Note == doc.Markers[1].Note && imported.Markers[1].Name == doc.Markers[1].Name, "XML conserva Unicode, saltos de línea y caracteres especiales");
            Check(imported.MediaPath == doc.MediaPath && imported.Fps == 24, "XML enlaza el render y conserva FPS");
            doc.Save(Path.Combine(output, "revision-prisma.json"));
            Check(ReviewDocument.Load(Path.Combine(output, "revision-prisma.json")).Markers.Count == 3, "Persistencia JSON de las notas");
            doc.ExportCsv(Path.Combine(output, "revision-prisma.csv")); Check(File.ReadAllText(Path.Combine(output, "revision-prisma.csv")).Contains("\"\"comillas\"\""), "CSV escapa comillas y notas multilínea");
            doc.SetFps(24000.0 / 1001); doc.ExportXml(Path.Combine(output, "revision-23976.xml"));
            ReviewDocument fractional = ReviewDocument.ImportXml(Path.Combine(output, "revision-23976.xml")); Check(fractional.FpsNumerator == 24000 && fractional.FpsDenominator == 1001, "23,976 se conserva como 24000/1001");
            Check(doc.Timecode(24000) == "00:16:40:00", "Código de tiempo NDF usa base nominal de 24");
            string hostile = Path.Combine(output, "invalid.xml"); File.WriteAllText(hostile, "<!DOCTYPE xmeml [<!ENTITY data SYSTEM 'file:///C:/Windows/win.ini'>]><xmeml>&data;</xmeml>");
            bool rejected = false; try { ReviewDocument.ImportXml(hostile); } catch { rejected = true; } Check(rejected, "No se resuelven entidades externas de XML");
            ReviewDocument risky = new ReviewDocument { MediaPath = doc.MediaPath, DurationFrames = 48 }; risky.SetFps(24);
            risky.Markers.Add(new ReviewMarker { Frame = 1, Name = "=HYPERLINK(\"http://ejemplo.invalid\",\"clic\")", Note = "@SUM(1+1)" });
            risky.Markers.Add(new ReviewMarker { Frame = 2, Name = "-2 pasos", Note = "+cmd|' /C calc'!A0" });
            string riskyCsv = Path.Combine(output, "formulas.csv"); risky.ExportCsv(riskyCsv); string riskyText = File.ReadAllText(riskyCsv);
            Check(riskyText.Contains("\"'=HYPERLINK(") && riskyText.Contains("\"'@SUM(1+1)\"") && riskyText.Contains("\"'-2 pasos\"") && riskyText.Contains("\"'+cmd"), "CSV neutraliza fórmulas en las notas");
            string sharedJson = Path.Combine(output, "compartir.json"); risky.SaveForSharing(sharedJson); ReviewDocument shared = ReviewDocument.Load(sharedJson);
            Check(shared.MediaPath == Path.GetFileName(doc.MediaPath) && !Path.IsPathRooted(shared.MediaPath) && shared.Markers.Count == 2 && risky.MediaPath == doc.MediaPath, "JSON para compartir no incluye la ruta completa del equipo");
            ReviewDocument tooLong = new ReviewDocument { DurationFrames = 48 }; tooLong.Markers.Add(new ReviewMarker { Frame = 0, Note = new string('x', ReviewDocument.MaxNoteLength + 1) });
            bool lengthRejected = false; try { tooLong.Validate(); } catch (InvalidDataException) { lengthRejected = true; } Check(lengthRejected, "Rechaza notas importadas más largas que el editor");
            // 0.4: carpeta en la nube, envío a Resolve, servidor MCP, conexión con asistentes e instancia única.
            string url, service;
            Check(CloudFolder.TryNormalize("https://drive.google.com/drive/folders/1AbC?usp=sharing", out url, out service) && service == "Google Drive"
                && CloudFolder.TryNormalize("www.dropbox.com/scl/fo/abc/xyz?dl=0", out url, out service) && service == "Dropbox" && url.StartsWith("https://www.dropbox.com/")
                && CloudFolder.TryNormalize("https://1drv.ms/f/s!Abc", out url, out service) && service == "OneDrive"
                && CloudFolder.TryNormalize("https://contoso-my.sharepoint.com/:f:/g/personal/x", out url, out service) && service == "OneDrive", "Acepta carpetas de Google Drive, Dropbox y OneDrive");
            Check(!CloudFolder.TryNormalize("http://drive.google.com/drive/folders/1", out url, out service) && !CloudFolder.TryNormalize("https://drive.google.com.evil.example/x", out url, out service)
                && !CloudFolder.TryNormalize("https://user:pass@drive.google.com/x", out url, out service) && !CloudFolder.TryNormalize("file:///C:/x", out url, out service)
                && !CloudFolder.TryNormalize("https://sharepoint.com/x", out url, out service) && !CloudFolder.TryNormalize("javascript:alert(1)", out url, out service), "Rechaza enlaces que no son de esos servicios o no usan https");
            ReviewDocument cloud = new ReviewDocument { MediaPath = Path.Combine(root, "tests", "media", "Corto 12.mov"), DurationFrames = 20000, CloudFolder = "https://drive.google.com/drive/folders/1AbC" }; cloud.SetFps(25);
            cloud.Markers.Add(new ReviewMarker { Frame = 3659, Name = "Nota 1", Note = "se ha colado una \"n\" después de \"carro\"" });
            cloud.Markers.Add(new ReviewMarker { Frame = 6401, Name = "Sonido", Note = "limpiamos este sonido\r\nsegunda línea" });
            cloud.Markers.Add(new ReviewMarker { Frame = 100, Name = "Música", Note = "" });
            string cloudJson = Path.Combine(output, "nube.json"); cloud.Save(cloudJson);
            Check(ReviewDocument.Load(cloudJson).CloudFolder == cloud.CloudFolder, "La carpeta en la nube se guarda con las notas del vídeo");
            cloud.SaveForSharing(sharedJson); Check(ReviewDocument.Load(sharedJson).CloudFolder == "", "El JSON para compartir no incluye la carpeta en la nube");
            File.WriteAllText(cloudJson, File.ReadAllText(cloudJson).Replace("https://drive.google.com/drive/folders/1AbC", "https://evil.example/x"));
            Check(ReviewDocument.Load(cloudJson).CloudFolder == "", "Una carpeta no válida en un archivo importado se descarta");
            string aiDoc = McpServer.Document(cloud);
            Check(aiDoc.Contains("titulo: Ajustes Corto 12") && aiDoc.Contains("00:02:26:09 se ha colado una \"n\" después de \"carro\"") && aiDoc.Contains("00:04:16:01 Sonido: limpiamos este sonido / segunda línea") && aiDoc.Contains("00:00:04:00 Música")
                && aiDoc.IndexOf("00:00:04:00", StringComparison.Ordinal) < aiDoc.IndexOf("00:02:26:09", StringComparison.Ordinal) && !aiDoc.Contains(root), "El documento para la IA tiene el formato de Ajustes, en orden y sin rutas del equipo");
            string markersDir = Path.Combine(output, "mcp-marcadores"); Directory.CreateDirectory(markersDir); cloud.CloudFolder = "https://drive.google.com/drive/folders/1AbC";
            cloud.Save(Path.Combine(markersDir, "0123456789abcdef.json"));
            McpServer mcp = new McpServer(markersDir); JavaScriptSerializer js = new JavaScriptSerializer();
            Dictionary<string, object> init = (Dictionary<string, object>)js.DeserializeObject(mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}"));
            Check(((Dictionary<string, object>)init["result"])["protocolVersion"].ToString() == "2025-06-18" && ((Dictionary<string, object>)init["result"]).ContainsKey("instructions"), "MCP: inicia la sesión y entrega las instrucciones");
            Check(mcp.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") == null, "MCP: no responde a las notificaciones");
            string tools = mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}");
            Check(tools.Contains("listar_revisiones") && tools.Contains("leer_revision") && tools.Contains("readOnlyHint"), "MCP: publica herramientas de solo lectura");
            string listed = mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"listar_revisiones\",\"arguments\":{}}}");
            Check(listed.Contains("Corto 12.mov") && listed.Contains("Google Drive") && !listed.Contains(js.Serialize(root).Trim('"')), "MCP: lista los vídeos sin revelar rutas");
            string read = mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"method\":\"tools/call\",\"params\":{\"name\":\"leer_revision\",\"arguments\":{\"video\":\"corto 12\"}}}");
            Check(read.Contains("Ajustes Corto 12") && read.Contains("00:02:26:09") && read.Contains("\"id\":\"x\""), "MCP: lee las notas de un vídeo por su nombre");
            Check(read.Contains("archivo_prisma: Corto 12.prisma.json"), "MCP: indica el nombre del archivo de notas que acompaña al documento");
            Dictionary<string, object> jsonCall = (Dictionary<string, object>)js.DeserializeObject(mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"json_revision\",\"arguments\":{\"video\":\"Corto 12.mov\"}}}"));
            string sharedText = (string)((Dictionary<string, object>)((object[])((Dictionary<string, object>)jsonCall["result"])["content"])[0])["text"];
            string remoteJson = Path.Combine(output, "Corto 12.prisma.json"); File.WriteAllText(remoteJson, sharedText);
            ReviewDocument remote = ReviewDocument.Load(remoteJson);
            Check(remote.Markers.Count == 3 && remote.MediaPath == "Corto 12.mov" && remote.CloudFolder == "" && !sharedText.Contains(js.Serialize(root).Trim('"')) && remote.Markers[0].Frame == 100, "MCP: el archivo de notas se importa en otro Prisma, ordenado y sin rutas");
            Check(mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"leer_revision\",\"arguments\":{\"video\":\"no existe\"}}}").Contains("\"isError\":true")
                && mcp.Handle("{no es json").Contains("-32700") && mcp.Handle("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"borrar\"}").Contains("-32601"), "MCP: responde con errores claros a peticiones no válidas");
            string claude = AiConnect.MergeClaudeConfig("{\"preferences\":{\"theme\":\"dark\"},\"mcpServers\":{\"otro\":{\"command\":\"x.exe\"}}}", @"C:\Apps\Prisma\Prisma.exe");
            Dictionary<string, object> claudeRoot = (Dictionary<string, object>)js.DeserializeObject(claude); Dictionary<string, object> servers = (Dictionary<string, object>)claudeRoot["mcpServers"];
            Check(claudeRoot.ContainsKey("preferences") && servers.ContainsKey("otro") && ((Dictionary<string, object>)servers["prisma"])["command"].ToString() == @"C:\Apps\Prisma\Prisma.exe"
                && AiConnect.MergeClaudeConfig(claude, @"D:\Prisma.exe").Contains(@"D:\\Prisma.exe") && !AiConnect.MergeClaudeConfig(claude, @"D:\Prisma.exe").Contains(@"C:\\Apps"), "Claude: añade Prisma sin tocar el resto de la configuración");
            string toml = AiConnect.MergeChatGptConfig("model = \"gpt\"\r\n\r\n[mcp_servers.prisma]\r\ncommand = 'viejo.exe'\r\n[mcp_servers.prisma.env]\r\nA = \"1\"\r\n\r\n[mcp_servers.otro]\r\ncommand = \"x\"\r\n", @"C:\Apps\Prisma\Prisma.exe");
            Check(toml.Contains("model = \"gpt\"") && toml.Contains("[mcp_servers.otro]") && !toml.Contains("viejo.exe") && !toml.Contains("[mcp_servers.prisma.env]") && toml.Contains("command = 'C:\\Apps\\Prisma\\Prisma.exe'")
                && AiConnect.MergeChatGptConfig(toml, @"C:\Apps\Prisma\Prisma.exe") == toml && AiConnect.MergeChatGptConfig("", @"C:\it's\Prisma.exe").Contains("command = \"C:\\\\it's\\\\Prisma.exe\""), "ChatGPT: añade o sustituye Prisma sin tocar el resto de la configuración");
            string syncDir = Path.Combine(output, "Mi unidad", "Revisiones"); if (Directory.Exists(syncDir)) Directory.Delete(syncDir, true); Directory.CreateDirectory(syncDir);
            Check(SyncDelivery.Service(syncDir) == "Google Drive" && SyncDelivery.Service(@"C:\Users\x\Dropbox\Clientes") == "Dropbox" && SyncDelivery.Service(@"D:\Proyectos") == "Carpeta" && !SyncDelivery.IsValidFolder(@"C:\") && !SyncDelivery.IsValidFolder("relativa"), "Carpeta sincronizada: reconoce Google Drive, Dropbox y carpetas normales");
            cloud.SyncFolder = syncDir; cloud.SyncHash = "";
            string firstStatus = SyncDelivery.Write(cloud, syncDir);
            string docxPath = Path.Combine(syncDir, "Ajustes Corto 12.docx"), jsonPath = Path.Combine(syncDir, "Corto 12.prisma.json");
            string documentXml;
            using (System.IO.Compression.ZipArchive zip = new System.IO.Compression.ZipArchive(File.OpenRead(docxPath)))
            using (StreamReader reader = new StreamReader(zip.GetEntry("word/document.xml").Open())) documentXml = reader.ReadToEnd();
            Check(File.Exists(jsonPath) && documentXml.Contains("Ajustes Corto 12") && documentXml.Contains("00:02:26:09 se ha colado una") && documentXml.Contains("<w:numId w:val=\"1\" />") && documentXml.Contains("<w:b />") && ReviewDocument.Load(jsonPath).Markers.Count == 3 && firstStatus.Contains("Google Drive"), "Carpeta sincronizada: escribe el .docx con viñetas y el .prisma.json");
            DateTime firstWrite = File.GetLastWriteTimeUtc(docxPath); Thread.Sleep(50); SyncDelivery.Write(cloud, syncDir);
            Check(File.GetLastWriteTimeUtc(docxPath) == firstWrite, "Carpeta sincronizada: sin cambios no reescribe, así la nube no vuelve a subir nada");
            File.WriteAllText(docxPath, "editado en Google Docs"); cloud.Markers.Add(new ReviewMarker { Frame = 9000, Name = "Nueva", Note = "otra nota" });
            string keptStatus = SyncDelivery.Write(cloud, syncDir);
            Check(keptStatus.Contains("se conservan") && Directory.GetFiles(syncDir, "Ajustes Corto 12 (editado *.docx").Length == 1 && File.ReadAllText(Directory.GetFiles(syncDir, "Ajustes Corto 12 (editado *.docx")[0]) == "editado en Google Docs" && ReviewDocument.Load(jsonPath).Markers.Count == 4, "Carpeta sincronizada: si alguien editó el documento, su versión se conserva");
            cloud.Markers.RemoveAt(3);
            string syncSaved = Path.Combine(output, "sync.json"); cloud.Save(syncSaved); ReviewDocument syncLoaded = ReviewDocument.Load(syncSaved);
            Check(syncLoaded.SyncFolder == syncDir && syncLoaded.SyncHash.Length == 64 && cloud.SharingCopy().SyncFolder == "" && !cloud.SharingJson().Contains("Mi unidad"), "Carpeta sincronizada: se recuerda por vídeo y no viaja en los archivos compartidos");
            Check(McpServer.Document(cloud).Contains("entrega_automatica") && McpServer.Document(cloud).Contains("«Revisiones»") && !McpServer.Document(cloud).Contains(output), "MCP: avisa de la entrega automática sin revelar la ruta");
            cloud.SyncFolder = ""; cloud.SyncHash = "";
            string resolveData = Path.Combine(output, "resolve-notas.txt"); ResolveBridge.WriteMarkers(cloud, resolveData);
            string[] resolveLines = File.ReadAllText(resolveData).Split('\n');
            Check(resolveLines[0] == "PRISMA-MARKERS 1" && resolveLines[1] == "fps\t25\t1" && resolveLines.Count(l => l.StartsWith("m\t")) == 3 && File.ReadAllText(resolveData).Contains("limpiamos este sonido\\r\\nsegunda línea") && !File.ReadAllText(resolveData).Contains(root), "Resolve: las notas viajan como datos, sin rutas del equipo");
            Check(ResolveBridge.ScriptSource().Contains("GetMarkerByCustomData") && ResolveBridge.Escape("a\\b\tc\nd") == "a\\\\b\\tc\\nd", "Resolve: el script va dentro de Prisma y el texto se escapa");
            Check(SingleInstance.Decode("C:\\a.mov\r\nD:\\b.mp4\n\n").Length == 2, "Instancia única: separa las rutas recibidas");
            string prismaExe = Path.Combine(root, "Prisma", "Prisma.exe");
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && File.Exists(prismaExe))
            {
                string mcpCopy = Path.Combine(root, "tests", "mcp"); Directory.CreateDirectory(Path.Combine(mcpCopy, "datos", "marcadores"));
                File.Copy(prismaExe, Path.Combine(mcpCopy, "Prisma.exe"), true);
                foreach (string f in Directory.GetFiles(markersDir)) File.Copy(f, Path.Combine(mcpCopy, "datos", "marcadores", Path.GetFileName(f)), true);
                ProcessStartInfo start = new ProcessStartInfo(Path.Combine(mcpCopy, "Prisma.exe"), "--mcp") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = new System.Text.UTF8Encoding(false) };
                using (Process server = Process.Start(start))
                {
                    StreamWriter input = new StreamWriter(server.StandardInput.BaseStream, new System.Text.UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                    input.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}");
                    string first = server.StandardOutput.ReadLine();
                    input.WriteLine("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
                    input.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"leer_revision\",\"arguments\":{\"video\":\"Corto 12\"}}}");
                    string second = server.StandardOutput.ReadLine(); input.Close();
                    Check(server.WaitForExit(5000) && first.Contains("\"serverInfo\"") && second.Contains("00:04:16:01 Sonido: limpiamos este sonido"), "Prisma.exe --mcp responde por la entrada y salida estándar sin abrir ventanas");
                }
            }
            Shuttle shuttle = new Shuttle(); Check(shuttle.Press(1) == 1 && shuttle.Press(1) == 2 && shuttle.Press(1) == 4 && shuttle.Press(-1) == 2 && shuttle.Press(-1) == 1 && shuttle.Press(-1) == -1 && shuttle.Press(-1) == -2, "J/L aceleran, frenan y cambian de dirección");
            for (int i = 0; i < 12; i++) shuttle.Press(-1); Check(shuttle.Rate == -16, "Revisión limitada a 16×");
            Check(MpvEngine.PixelDepth("gbrp10le") == "10" && MpvEngine.PixelDepth("p010") == "10" && MpvEngine.PixelDepth("unknown") == "", "Profundidad de color no se inventa para formatos desconocidos");
            Application.EnableVisualStyles();
            using (PlayerForm form = new PlayerForm(new string[] { Path.Combine(root, "tests", "media", "h265-4K-10bit.mp4") }, Path.Combine(root, "tests", "review-ui", "preferencias.json")))
            {
                form.Show(); Check(Wait(delegate { return Field(form, "review") != null; }, 10000), "Carga interfaz de revisión con el vídeo");
                MpvEngine engine = (MpvEngine)Field(form, "engine"); Key(form, Keys.K); engine.Command("seek", "2", "absolute+exact");
                Check(Wait(delegate { return Math.Abs(engine.Number("time-pos") - 2) < .01 && !engine.Flag("seeking"); }, 5000), "Sitúa el cabezal en 2 segundos");
                Key(form, Keys.Right); bool stepped = Wait(delegate { return Math.Abs(engine.Number("time-pos") - (2 + 1.0 / 24)) < .01 && !engine.Flag("seeking"); }, 5000); Console.WriteLine("  Posición después de →: " + engine.Get("time-pos")); Check(stepped, "Flecha derecha avanza exactamente un fotograma");
                Key(form, Keys.Left); Check(Wait(delegate { return Math.Abs(engine.Number("time-pos") - 2) < .01; }, 5000), "Flecha izquierda retrocede exactamente un fotograma");
                Key(form, Keys.J); Check(Wait(delegate { return engine.Number("time-pos") < 1.6; }, 5000), "J revisa el vídeo hacia atrás");
                Key(form, Keys.K); double pausedAt = engine.Number("time-pos"); Wait(delegate { return false; }, 250); Check(Math.Abs(engine.Number("time-pos") - pausedAt) < .12 && engine.Flag("pause"), "K detiene el retroceso");
                Key(form, Keys.L); Key(form, Keys.L); Check(Math.Abs(engine.Number("speed") - 2) < .01 && !engine.Flag("pause"), "L dos veces reproduce a 2×"); Key(form, Keys.K);
                Key(form, Keys.I); Check((bool)Field(form, "summaryShown"), "I activa el resumen superpuesto"); Key(form, Keys.I); Check(!(bool)Field(form, "summaryShown"), "I vuelve a ocultar el resumen");
                Key(form, Keys.M); MarkerPanel panel = (MarkerPanel)Field(form, "notesPanel"); Check(panel.Editing, "M abre el editor de una nota");
                ((TextBox)Field(panel, "title")).Text = "Nota desde la interfaz"; ((TextBox)Field(panel, "note")).Text = "Revisar este fotograma. ñ & < >";
                panel.Hide(); Check(panel.Editing, "Ocultar el panel conserva la edición pendiente"); panel.CommitEdit(); panel.Show();
                ReviewDocument review = (ReviewDocument)Field(form, "review"); Check(review.Markers.Any(m => m.Name == "Nota desde la interfaz"), "Guarda una nota en el fotograma actual");
                Check(File.Exists((string)Field(form, "reviewFile")), "Persiste automáticamente los marcadores");
                form.Size = new System.Drawing.Size(1000, 680); form.Close();
            }
            Settings saved = Settings.Load(Path.Combine(root, "tests", "review-ui", "preferencias.json")); Check(saved.Width == 1000 && saved.Height == 680, "La ventana recuerda su tamaño de revisión");
            Console.WriteLine(passed + " comprobaciones de revisión correctas."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
