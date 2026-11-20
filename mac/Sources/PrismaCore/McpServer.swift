// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation

// Model Context Protocol server (stdio, JSON-RPC 2.0) started by Claude or ChatGPT desktop as
// "Prisma --mcp". It only reads the notes Prisma saved: it never writes, never opens network
// connections and never reveals full paths, only file names. Same tools as Prisma for Windows.
public final class McpServer {
    public static let instructions =
        "Prisma guarda notas de revisión de vídeo con código de tiempo. Para pasar las notas de un vídeo a su carpeta en la nube: " +
        "1) Llama a leer_revision con el nombre del vídeo (usa listar_revisiones si no lo sabes). " +
        "2) Si no tiene carpeta, pide al usuario que la indique en Prisma (Revisión > Carpeta en la nube) y no la inventes. " +
        "3) Con el conector del servicio de esa carpeta, crea dentro de ella un documento con el título exacto que te da la herramienta: en Google Drive un documento de Google Docs; en Dropbox u OneDrive un documento de Word. " +
        "4) Contenido: el título en negrita y debajo una lista con viñetas, una viñeta por línea de notas, en el mismo orden y copiada literalmente. No recalcules ni redondees los códigos de tiempo. " +
        "Al final del documento añade una línea en cursiva: «Para importar estas notas en Prisma: archivo <archivo_prisma> en esta carpeta». " +
        "5) Si ya existe un documento con ese título en la carpeta, añade solo las líneas que falten, sin duplicar ni borrar nada. " +
        "6) Guarda también en la misma carpeta el archivo de notas para Prisma: llama a json_revision y sube su resultado tal cual, sin cambiar ni un carácter, como archivo con el nombre de archivo_prisma y tipo application/json, sin convertirlo a documento. Si ya existe, sustitúyelo. Con ese archivo, otra persona importa las notas en su Prisma y las envía a su DaVinci Resolve. " +
        "Si el conector no permite subir archivos, dilo y pide al usuario que en Prisma use Exportar > JSON y suba ese archivo a la carpeta. " +
        "7) Responde con el enlace del documento y del archivo. Si el usuario no tiene conectado ese servicio en la app, díselo en lugar de guardar las notas en otro sitio."

    private static let versions = ["2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05"]
    private let folder: URL

    public init(markersFolder: URL = PrismaPaths.markersFolder) { folder = markersFolder }

    public static func run(markersFolder: URL = PrismaPaths.markersFolder) -> Int32 {
        let server = McpServer(markersFolder: markersFolder)
        while let line = readLine(strippingNewline: true) {
            if line.utf8.count > 1024 * 1024 { write(server.error(nil, -32600, "Mensaje demasiado grande.")); continue }
            if line.trimmingCharacters(in: .whitespaces).isEmpty { continue }
            if let reply = server.handle(line) { write(reply) }
        }
        return 0
    }

    private static func write(_ line: String) {
        FileHandle.standardOutput.write(Data((line + "\n").utf8))
    }

    // Returns the JSON reply, or nil for notifications.
    public func handle(_ line: String) -> String? {
        guard let parsed = try? JSON.parse(Data(line.utf8)) else { return error(nil, -32700, "JSON no válido.") }
        guard let request = parsed as? [String: Any] else { return error(nil, -32600, "Petición no válida.") }
        let id = request["id"], hasId = request.keys.contains("id")
        guard let method = request["method"] as? String else { return hasId ? error(id, -32600, "Falta el método.") : nil }
        let args = request["params"] as? [String: Any]
        if !hasId { return nil } // notifications/initialized, notifications/cancelled...
        do {
            switch method {
            case "initialize": return result(id, initialize(args))
            case "ping": return result(id, [String: Any]())
            case "tools/list": return result(id, ["tools": McpServer.tools()])
            case "tools/call": return result(id, try call(args))
            case "resources/list": return result(id, ["resources": [Any]()])
            case "prompts/list": return result(id, ["prompts": [Any]()])
            default: return error(id, -32601, "Método desconocido: " + method)
            }
        } catch {
            return self.error(id, -32603, error.localizedDescription)
        }
    }

    private func initialize(_ args: [String: Any]?) -> [String: Any] {
        let asked = args?["protocolVersion"] as? String
        return [
            "protocolVersion": asked.map { McpServer.versions.contains($0) ? $0 : McpServer.versions[1] } ?? McpServer.versions[1],
            "capabilities": ["tools": ["listChanged": false]],
            "serverInfo": ["name": "prisma", "title": "Prisma · notas de revisión", "version": "0.4.0"],
            "instructions": McpServer.instructions,
        ]
    }

    private static func tools() -> [Any] {
        let none: [String: Any] = ["type": "object", "properties": [String: Any]()]
        let video: [String: Any] = [
            "type": "object",
            "properties": ["video": ["type": "string", "description": "Nombre del vídeo (con o sin extensión) o el id que da listar_revisiones."]],
            "required": ["video"],
        ]
        let readOnly: [String: Any] = ["readOnlyHint": true, "openWorldHint": false]
        return [
            ["name": "listar_revisiones", "title": "Listar revisiones de Prisma", "description": "Lista los vídeos con notas de revisión en Prisma: nombre, número de notas, FPS, carpeta en la nube y fecha.", "inputSchema": none, "annotations": readOnly],
            ["name": "json_revision", "title": "Archivo de notas para Prisma", "description": "Devuelve el contenido exacto del archivo de notas de un vídeo para importarlo en otro Prisma (sin rutas del equipo). Súbelo sin modificar a la carpeta del vídeo con el nombre que indica leer_revision (archivo_prisma), como application/json y sin convertir.", "inputSchema": video, "annotations": readOnly],
            ["name": "leer_revision", "title": "Leer las notas de un vídeo", "description": "Devuelve las notas de un vídeo listas para un documento: título, carpeta en la nube y una línea por nota (código de tiempo y texto), en orden. " + instructions, "inputSchema": video, "annotations": readOnly],
        ]
    }

    private func call(_ args: [String: Any]?) throws -> [String: Any] {
        let name = args?["name"] as? String ?? ""
        let input = args?["arguments"] as? [String: Any]
        if name == "listar_revisiones" { return text(listText(), false) }
        if name == "leer_revision" || name == "json_revision" {
            let query = (input?["video"] as? String ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            if query.isEmpty { return text("Indica el nombre del vídeo.", true) }
            return try find(query, asJSON: name == "json_revision")
        }
        return text("Herramienta desconocida: " + name, true)
    }

    private struct Entry { let id: String; let video: String; let updated: Date; let doc: ReviewDocument }

    private func load() -> [Entry] {
        let fm = FileManager.default
        guard let files = try? fm.contentsOfDirectory(at: folder, includingPropertiesForKeys: [.contentModificationDateKey]) else { return [] }
        var entries: [Entry] = []
        for file in files.filter({ $0.pathExtension.lowercased() == "json" }).prefix(5000) {
            guard let doc = try? ReviewDocument.load(file) else { continue }
            let stem = file.deletingPathExtension().lastPathComponent
            let date = (try? file.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate) ?? Date.distantPast
            entries.append(Entry(id: String(stem.prefix(12)), video: doc.mediaPath.portableFileName, updated: date, doc: doc))
        }
        return entries.sorted { $0.updated > $1.updated }
    }

    private func listText() -> String {
        let entries = load()
        if entries.isEmpty { return "No hay vídeos con notas en Prisma." }
        let stamp = DateFormatter(); stamp.locale = Locale(identifier: "en_US_POSIX"); stamp.dateFormat = "yyyy-MM-dd HH:mm"
        var out = "Vídeos con notas en Prisma (más recientes primero):\n"
        for e in entries {
            let label = SyncDelivery.label(e.doc)
            out += "- \(e.video) · id \(e.id) · \(e.doc.markers.count) notas · \(ReviewDocument.trim(e.doc.fps, decimals: 3)) fps · carpeta: \(label.isEmpty ? "sin indicar" : label) · actualizado \(stamp.string(from: e.updated))\n"
        }
        return out
    }

    private func find(_ query: String, asJSON: Bool) throws -> [String: Any] {
        let entries = load()
        let plain = query.fileNameWithoutExtension
        func same(_ a: String, _ b: String) -> Bool { a.compare(b, options: [.caseInsensitive]) == .orderedSame }
        var found = entries.filter { same($0.id, query) || same($0.video, query) || same($0.video.fileNameWithoutExtension, plain) }
        if found.isEmpty { found = entries.filter { $0.video.range(of: plain, options: .caseInsensitive) != nil } }
        if found.isEmpty { return text("No hay notas de un vídeo llamado «\(query)». Usa listar_revisiones para ver los disponibles.", true) }
        if found.count > 1 {
            return text("Hay varios vídeos que coinciden: " + found.map { "\($0.video) (id \($0.id))" }.joined(separator: ", ") + ". Indica el nombre exacto o el id.", true)
        }
        return text(asJSON ? try found[0].doc.sharingJSON() : McpServer.document(found[0].doc), false)
    }

    public static func title(_ doc: ReviewDocument) -> String { "Ajustes " + doc.mediaPath.portableFileName.fileNameWithoutExtension }
    public static func sharingFileName(_ doc: ReviewDocument) -> String { doc.mediaPath.portableFileName.fileNameWithoutExtension + ".prisma.json" }

    public static func line(_ doc: ReviewDocument, _ m: ReviewMarker) -> String {
        let name = m.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let note = m.note.trimmingCharacters(in: .whitespacesAndNewlines)
            .replacingOccurrences(of: "\\s*[\\r\\n]+\\s*", with: " / ", options: .regularExpression)
        let generic = name.isEmpty || name.range(of: "^Nota( \\d+)?$", options: .regularExpression) != nil
        let body = generic ? note : (note.isEmpty ? name : name + ": " + note)
        return doc.timecode(m.frame) + " " + (body.isEmpty ? "(sin texto)" : body)
    }

    public static func document(_ doc: ReviewDocument) -> String {
        var out = "titulo: \(title(doc))\nvideo: \(doc.mediaPath.portableFileName)\n"
        out += "archivo_prisma: \(sharingFileName(doc)) (contenido con json_revision)\n"
        if !doc.cloudFolder.isEmpty { out += "carpeta: \(doc.cloudFolder) (\(CloudFolder.serviceName(doc.cloudFolder)))\n" }
        else { out += "carpeta: sin indicar. Pide al usuario que la indique en Prisma (Revisión > Carpeta en la nube).\n" }
        if !doc.syncFolder.isEmpty {
            let folderName = URL(fileURLWithPath: doc.syncFolder).lastPathComponent
            out += "entrega_automatica: Prisma ya guarda «\(SyncDelivery.documentName(doc))» y «\(sharingFileName(doc))» en una carpeta sincronizada con \(SyncDelivery.service(doc.syncFolder)) («\(folderName)»). No hace falta crearlos otra vez: díselo al usuario salvo que pida expresamente otro documento.\n"
        }
        out += "fps: \(doc.fpsNumerator)/\(doc.fpsDenominator) · código de tiempo HH:MM:SS:FF desde el inicio del vídeo\n"
        out += "notas (\(doc.markers.count)), una viñeta por línea:\n"
        for m in doc.markers.sorted(by: { $0.frame < $1.frame }) { out += line(doc, m) + "\n" }
        return out
    }

    private func text(_ value: String, _ isError: Bool) -> [String: Any] {
        ["content": [["type": "text", "text": value]], "isError": isError]
    }

    private func result(_ id: Any?, _ value: Any) -> String {
        JSON.serialize(["jsonrpc": "2.0", "id": id ?? NSNull(), "result": value])
    }

    func error(_ id: Any?, _ code: Int, _ message: String) -> String {
        JSON.serialize(["jsonrpc": "2.0", "id": id ?? NSNull(), "error": ["code": code, "message": message]])
    }
}
