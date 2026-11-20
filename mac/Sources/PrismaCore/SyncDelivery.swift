// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation

// Automatic delivery to a folder that Google Drive, Dropbox or OneDrive keeps in sync on this Mac.
// Every time the notes change, Prisma writes "Ajustes <video>.docx" (one bullet per note) and
// "<video>.prisma.json" there, and the sync app uploads them. Prisma itself never connects to
// the cloud. A document someone else edited is kept under another name, never lost.
public enum SyncDelivery {
    public static func service(_ folder: String) -> String {
        if folder.isEmpty { return "" }
        let f = folder.replacingOccurrences(of: "\\", with: "/").lowercased() + "/"
        for marker in ["/cloudstorage/googledrive", "/mi unidad/", "/my drive/", "/unidades compartidas/", "/shared drives/", "/google drive/"] where f.contains(marker) {
            return "Google Drive"
        }
        if f.contains("/cloudstorage/dropbox") || f.contains("/dropbox") { return "Dropbox" }
        if f.contains("/cloudstorage/onedrive") || f.contains("/onedrive") { return "OneDrive" }
        return "Carpeta"
    }

    // An absolute folder on this Mac, not the root of a disk. Windows paths from a shared file are rejected.
    public static func isValidFolder(_ folder: String) -> Bool {
        guard !folder.isEmpty, folder.count <= 1024, folder.hasPrefix("/") else { return false }
        let parts = URL(fileURLWithPath: folder).standardizedFileURL.pathComponents.filter { $0 != "/" }
        if parts.isEmpty { return false }
        if parts[0] == "Volumes" && parts.count < 3 { return false }
        return true
    }

    // Suggests the "My Drive" folder of Google Drive for desktop, or Dropbox / OneDrive.
    public static func suggestedStart() -> String {
        let fm = FileManager.default
        let home = URL(fileURLWithPath: NSHomeDirectory())
        let storage = home.appendingPathComponent("Library/CloudStorage")
        let entries = (try? fm.contentsOfDirectory(atPath: storage.path)) ?? []
        for entry in entries.sorted() where entry.hasPrefix("GoogleDrive") {
            for name in ["Mi unidad", "My Drive"] {
                let candidate = storage.appendingPathComponent(entry).appendingPathComponent(name)
                if fm.fileExists(atPath: candidate.path) { return candidate.path }
            }
        }
        for entry in entries.sorted() where entry.hasPrefix("Dropbox") || entry.hasPrefix("OneDrive") {
            return storage.appendingPathComponent(entry).path
        }
        let dropbox = home.appendingPathComponent("Dropbox")
        return fm.fileExists(atPath: dropbox.path) ? dropbox.path : ""
    }

    public static func label(_ doc: ReviewDocument?) -> String {
        guard let doc else { return "" }
        if !doc.syncFolder.isEmpty { return service(doc.syncFolder) + " · auto" }
        return CloudFolder.serviceName(doc.cloudFolder)
    }

    public static func documentName(_ doc: ReviewDocument) -> String { McpServer.title(doc) + ".docx" }

    // Writes both files. Returns a short status for the status bar.
    @discardableResult
    public static func write(_ doc: ReviewDocument, to folder: String, now: Date = Date()) throws -> String {
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: folder, isDirectory: &isDir), isDir.boolValue else {
            throw PrismaError("No se encuentra la carpeta sincronizada. ¿Está abierta la aplicación de Google Drive, Dropbox u OneDrive?")
        }
        let base = URL(fileURLWithPath: folder, isDirectory: true)
        let docx = base.appendingPathComponent(documentName(doc))
        let bytes = docxData(doc)
        let written = SHA256.hex(bytes)
        var kept = ""
        if let current = fm.contents(atPath: docx.path) {
            let hash = SHA256.hex(current)
            if hash != written && hash != doc.syncHash {
                // Not the last version Prisma wrote (someone edited it, or it was made elsewhere): keep it.
                let stamp = DateFormatter()
                stamp.locale = Locale(identifier: "en_US_POSIX"); stamp.dateFormat = "yyyy-MM-dd HHmm"
                kept = McpServer.title(doc) + " (editado " + stamp.string(from: now) + ").docx"
                var target = base.appendingPathComponent(kept), n = 2
                while fm.fileExists(atPath: target.path) {
                    kept = McpServer.title(doc) + " (editado " + stamp.string(from: now) + " \(n)).docx"
                    target = base.appendingPathComponent(kept); n += 1
                }
                try fm.moveItem(at: docx, to: target)
            }
        }
        if fm.contents(atPath: docx.path).map(SHA256.hex) != written { try bytes.write(to: docx, options: .atomic) }
        doc.syncHash = written
        let json = Data(try doc.sharingJSON().utf8)
        let jsonURL = base.appendingPathComponent(McpServer.sharingFileName(doc))
        if fm.contents(atPath: jsonURL.path) != json { try json.write(to: jsonURL, options: .atomic) }
        return kept.isEmpty ? "Notas actualizadas en " + service(folder) : "El documento tenía cambios de otra persona: se conservan en «" + kept + "»"
    }

    // MARK: Minimal .docx (WordprocessingML in a zip). Same notes, same bytes.

    private static func x(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    private static func run(_ text: String, bold: Bool = false, italic: Bool = false, halfPoints: Int = 0) -> String {
        var props = ""
        if bold { props += "<w:b />" }
        if italic { props += "<w:i />" }
        if halfPoints > 0 { props += "<w:sz w:val=\"\(halfPoints)\" />" }
        return "<w:r><w:rPr>\(props)</w:rPr><w:t xml:space=\"preserve\">\(x(text))</w:t></w:r>"
    }

    public static func documentXML(_ doc: ReviewDocument) -> String {
        var body = "<w:p>" + run(McpServer.title(doc), bold: true, halfPoints: 28) + "</w:p><w:p />"
        for m in doc.markers.sorted(by: { $0.frame < $1.frame }) {
            body += "<w:p><w:pPr><w:numPr><w:ilvl w:val=\"0\" /><w:numId w:val=\"1\" /></w:numPr></w:pPr>" + run(McpServer.line(doc, m)) + "</w:p>"
        }
        let footer = "Notas de Prisma (" + ReviewDocument.trim(doc.fps, decimals: 3) + " fps). Para cargarlas en Prisma y enviarlas a DaVinci Resolve, importa «" + McpServer.sharingFileName(doc) + "» de esta carpeta. Prisma actualiza este documento al cambiar las notas; si lo editas aquí, tu versión se conserva con otro nombre."
        body += "<w:p /><w:p>" + run(footer, italic: true, halfPoints: 18) + "</w:p><w:sectPr />"
        return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" + body + "</w:body></w:document>"
    }

    public static func docxData(_ doc: ReviewDocument) -> Data {
        let types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/numbering.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml\"/></Types>"
        let rels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>"
        let docRels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering\" Target=\"numbering.xml\"/></Relationships>"
        let numbering = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:numbering xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:abstractNum w:abstractNumId=\"0\"><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\" /><w:numFmt w:val=\"bullet\" /><w:lvlText w:val=\"•\" /><w:lvlJc w:val=\"left\" /><w:pPr><w:ind w:left=\"720\" w:hanging=\"360\" /></w:pPr></w:lvl></w:abstractNum><w:num w:numId=\"1\"><w:abstractNumId w:val=\"0\" /></w:num></w:numbering>"
        return Zip.store([
            ("[Content_Types].xml", Data(types.utf8)),
            ("_rels/.rels", Data(rels.utf8)),
            ("word/_rels/document.xml.rels", Data(docRels.utf8)),
            ("word/document.xml", Data(documentXML(doc).utf8)),
            ("word/numbering.xml", Data(numbering.utf8)),
        ])
    }
}

// Zip archive with stored (uncompressed) entries and a fixed date. Enough for .docx.
public enum Zip {
    private static let table: [UInt32] = (0..<256).map { i -> UInt32 in
        var c = UInt32(i)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1 }
        return c
    }

    public static func crc32(_ data: Data) -> UInt32 {
        var c: UInt32 = 0xFFFFFFFF
        for b in data { c = table[Int((c ^ UInt32(b)) & 0xff)] ^ (c >> 8) }
        return c ^ 0xFFFFFFFF
    }

    public static func store(_ entries: [(String, Data)]) -> Data {
        // 2026-01-01 00:00 in MS-DOS format.
        let time: UInt16 = 0, date: UInt16 = UInt16((2026 - 1980) << 9 | 1 << 5 | 1)
        var out = Data(), central = Data()
        func u16(_ v: UInt16, _ d: inout Data) { d.append(UInt8(v & 0xff)); d.append(UInt8(v >> 8)) }
        func u32(_ v: UInt32, _ d: inout Data) { for i in 0..<4 { d.append(UInt8((v >> (8 * UInt32(i))) & 0xff)) } }
        for (name, data) in entries {
            let nameBytes = Data(name.utf8), crc = crc32(data), offset = UInt32(out.count), size = UInt32(data.count)
            u32(0x04034b50, &out); u16(20, &out); u16(0x0800, &out); u16(0, &out); u16(time, &out); u16(date, &out)
            u32(crc, &out); u32(size, &out); u32(size, &out); u16(UInt16(nameBytes.count), &out); u16(0, &out)
            out.append(nameBytes); out.append(data)
            u32(0x02014b50, &central); u16(20, &central); u16(20, &central); u16(0x0800, &central); u16(0, &central)
            u16(time, &central); u16(date, &central); u32(crc, &central); u32(size, &central); u32(size, &central)
            u16(UInt16(nameBytes.count), &central); u16(0, &central); u16(0, &central); u16(0, &central); u16(0, &central)
            u32(0, &central); u32(offset, &central); central.append(nameBytes)
        }
        let centralOffset = UInt32(out.count)
        out.append(central)
        u32(0x06054b50, &out); u16(0, &out); u16(0, &out); u16(UInt16(entries.count), &out); u16(UInt16(entries.count), &out)
        u32(UInt32(central.count), &out); u32(centralOffset, &out); u16(0, &out)
        return out
    }
}
