// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation
#if canImport(FoundationXML)
import FoundationXML
#endif

public final class ReviewMarker {
    public var id: String
    public var frame: Int64
    public var name: String
    public var note: String

    public init(id: String = ReviewMarker.newId(), frame: Int64, name: String = "Nota", note: String = "") {
        self.id = id; self.frame = frame; self.name = name; self.note = note
    }

    public static func newId() -> String { UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() }
}

// Review notes of one video. The JSON is the same as Prisma for Windows, so notes travel
// between both in either direction (.prisma.json, Importar / Exportar).
public final class ReviewDocument {
    public var version = 1
    public var mediaPath = ""
    public var syncFolder = ""       // folder on this Mac kept in sync by one of those apps
    public var syncHash = ""
    public var fpsNumerator = 25, fpsDenominator = 1
    public var durationFrames: Int64 = 0
    public var width = 1920, height = 1080, audioChannels = 0, audioRate = 48000
    public var markers: [ReviewMarker] = []

    public static let maxNameLength = 300, maxNoteLength = 20000

    public init() {}

    public var fps: Double { Double(fpsNumerator) / Double(fpsDenominator) }

    public func setFps(_ value: Double) throws {
        for n in [24000, 30000, 60000, 120000] where abs(value - Double(n) / 1001.0) < 0.002 {
            fpsNumerator = n; fpsDenominator = 1001; return
        }
        if value <= 0 || value > 240 || abs(value - value.rounded()) > 0.002 {
            throw PrismaError("El vídeo no declara una velocidad de fotogramas estándar compatible con este intercambio XML.")
        }
        fpsNumerator = Int(value.rounded()); fpsDenominator = 1
    }

    public func timecode(_ frame: Int64) -> String {
        let nominal = Int64(fps.rounded()), f = max(0, frame)
        return String(format: "%02lld:%02lld:%02lld:%02lld", f / (nominal * 3600), f / (nominal * 60) % 60, f / nominal % 60, f % nominal)
    }

    // Reads a timecode typed by the user, as in Premiere or DaVinci: "00:00:03:10", "3:10"
    // (seconds:frames), "1.00.00.00", or only digits read from the right ("310" = 00:00:03:10).
    // A leading + or - moves from the current frame ("+10" = ten frames later). The result is
    // clamped to the video. Returns nil for anything that is not a timecode.
    public func parseTimecode(_ text: String, current: Int64) -> Int64? {
        var s = text.trimmingCharacters(in: .whitespacesAndNewlines).replacingOccurrences(of: " ", with: "")
        guard !s.isEmpty, s.count <= 16 else { return nil }
        var sign: Int64 = 0
        if s.hasPrefix("+") || s.hasPrefix("-") { sign = s.hasPrefix("+") ? 1 : -1; s.removeFirst() }
        guard !s.isEmpty else { return nil }
        let separators = CharacterSet(charactersIn: ":;.,")
        var parts: [String]
        if s.rangeOfCharacter(from: separators) != nil {
            parts = s.components(separatedBy: separators)
        } else {
            guard s.count <= 8 else { return nil }
            let padded = Array(String(repeating: "0", count: 8 - s.count) + s)
            parts = stride(from: 0, to: 8, by: 2).map { String(padded[$0..<$0 + 2]) }
        }
        guard parts.count <= 4 else { return nil }
        var values: [Int64] = [0, 0, 0, 0]
        for (i, raw) in parts.reversed().enumerated() {
            let p = raw.isEmpty ? "0" : raw
            guard p.count <= 4, p.allSatisfy({ $0.isASCII && $0.isNumber }), let v = Int64(p) else { return nil }
            values[3 - i] = v
        }
        let nominal = Int64(fps.rounded())
        let typed = ((values[0] * 60 + values[1]) * 60 + values[2]) * nominal + values[3]
        let target = sign == 0 ? typed : current + sign * typed
        return max(0, min(max(0, durationFrames - 1), target))
    }

    public func validate() throws {
        if version != 1 || fpsNumerator <= 0 || fpsDenominator <= 0 || fps < 1 || fps > 240 || durationFrames < 0 || markers.count > 100_000 {
            throw PrismaError("El documento de marcadores no es válido.")
        }
        if !SyncDelivery.isValidFolder(syncFolder) { syncFolder = "" }
        if syncHash.count > 64 { syncHash = "" }
        for m in markers {
            if m.frame < 0 || (durationFrames > 0 && m.frame >= durationFrames) { throw PrismaError("Hay marcadores fuera de la duración del vídeo.") }
            if m.id.isEmpty { m.id = ReviewMarker.newId() }
            if m.id.count > 64 || m.name.count > ReviewDocument.maxNameLength || m.note.count > ReviewDocument.maxNoteLength {
                throw PrismaError("Hay notas demasiado largas en el documento.")
            }
        }
    }

    // ~/Library/Application Support/Prisma/marcadores/<SHA-256 of the full path>.json
    public static func storageURL(for media: String, in folder: URL = PrismaPaths.markersFolder) -> URL {
        let full = URL(fileURLWithPath: media).standardizedFileURL.path.uppercased()
        return folder.appendingPathComponent(SHA256.hex(Data(full.utf8)) + ".json")
    }

    // MARK: JSON

    public func json() -> String {
        var s = "{\"Version\":\(version),\"MediaPath\":\(JSON.string(mediaPath)),"
        s += "\"SyncFolder\":\(JSON.string(syncFolder)),\"SyncHash\":\(JSON.string(syncHash)),"
        s += "\"FpsNumerator\":\(fpsNumerator),\"FpsDenominator\":\(fpsDenominator),\"DurationFrames\":\(durationFrames),"
        s += "\"Width\":\(width),\"Height\":\(height),\"AudioChannels\":\(audioChannels),\"AudioRate\":\(audioRate),\"Markers\":["
        s += markers.map { "{\"Id\":\(JSON.string($0.id)),\"Frame\":\($0.frame),\"Name\":\(JSON.string($0.name)),\"Note\":\(JSON.string($0.note))}" }.joined(separator: ",")
        return s + "]}"
    }

    public static func parse(_ data: Data) throws -> ReviewDocument {
        if data.count > 16 * 1024 * 1024 { throw PrismaError("El documento de marcadores es demasiado grande.") }
        guard let root = try JSON.parse(data) as? [String: Any] else { throw PrismaError("Documento vacío.") }
        // Windows writes the keys exactly like this; accept other casing too.
        var keys: [String: Any] = [:]
        for (k, v) in root { keys[k.lowercased()] = v }
        let doc = ReviewDocument()
        func text(_ k: String) -> String { keys[k] as? String ?? "" }
        func int(_ k: String, _ fallback: Int) -> Int { JSON.int64(keys[k]).map { Int(clamping: $0) } ?? fallback }
        doc.version = int("version", 1)
        doc.mediaPath = text("mediapath")
        doc.syncFolder = text("syncfolder")
        doc.syncHash = text("synchash")
        doc.fpsNumerator = int("fpsnumerator", 25)
        doc.fpsDenominator = int("fpsdenominator", 1)
        doc.durationFrames = JSON.int64(keys["durationframes"]) ?? 0
        doc.width = int("width", 1920); doc.height = int("height", 1080)
        doc.audioChannels = int("audiochannels", 0); doc.audioRate = int("audiorate", 48000)
        if let list = keys["markers"] as? [Any] {
            for item in list {
                guard let raw = item as? [String: Any] else { throw PrismaError("Hay una nota con datos no válidos.") }
                var m: [String: Any] = [:]
                for (k, v) in raw { m[k.lowercased()] = v }
                guard let frame = JSON.int64(m["frame"]) else { throw PrismaError("Marcador sin posición válida.") }
                doc.markers.append(ReviewMarker(id: m["id"] as? String ?? "", frame: frame, name: m["name"] as? String ?? "Nota", note: m["note"] as? String ?? ""))
            }
        }
        try doc.validate()
        return doc
    }

    public static func load(_ url: URL) throws -> ReviewDocument {
        let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size] as? NSNumber)?.int64Value ?? 0
        if size > 16 * 1024 * 1024 { throw PrismaError("El documento de marcadores es demasiado grande.") }
        return try parse(Data(contentsOf: url))
    }

    public func save(_ url: URL) throws {
        try validate()
        try FileManager.default.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        try Data(json().utf8).write(to: url, options: .atomic)
    }

    // Copy meant for other people: keeps the notes and the video's file name, not the full
    // path (which would show the user name and folder layout) nor the cloud or sync folders.
    public func sharingCopy() -> ReviewDocument {
        let copy = ReviewDocument()
        copy.version = version; copy.mediaPath = mediaPath.portableFileName
        copy.fpsNumerator = fpsNumerator; copy.fpsDenominator = fpsDenominator; copy.durationFrames = durationFrames
        copy.width = width; copy.height = height; copy.audioChannels = audioChannels; copy.audioRate = audioRate
        copy.markers = markers.sorted { $0.frame < $1.frame }
        return copy
    }

    public func sharingJSON() throws -> String {
        let copy = sharingCopy(); try copy.validate(); return copy.json()
    }

    public func saveForSharing(_ url: URL) throws { try sharingCopy().save(url) }

    // MARK: CSV

    public func csv() -> String {
        var out = "Fotograma,Código de tiempo,FPS,Nombre,Nota\r\n"
        let rate = ReviewDocument.trim(fps, decimals: 8)
        for m in markers.sorted(by: { $0.frame < $1.frame }) {
            out += "\(m.frame),\(timecode(m.frame)),\(rate),\(ReviewDocument.csvCell(m.name)),\(ReviewDocument.csvCell(m.note))\r\n"
        }
        return out
    }

    public func exportCSV(_ url: URL) throws {
        var data = Data([0xEF, 0xBB, 0xBF]); data.append(Data(csv().utf8))
        try data.write(to: url, options: .atomic)
    }

    // Spreadsheets run cells that start with = + - @ as formulas: an apostrophe keeps them as text.
    public static func csvCell(_ value: String) -> String {
        var v = value
        if let first = v.unicodeScalars.first, "=+-@\t\r\n".unicodeScalars.contains(first) { v = "'" + v }
        return "\"" + v.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }

    static func trim(_ value: Double, decimals: Int) -> String {
        var s = String(format: "%.\(decimals)f", value)
        if s.contains(".") { while s.hasSuffix("0") { s.removeLast() }; if s.hasSuffix(".") { s.removeLast() } }
        return s
    }

    // MARK: FCP 7 XML (DaVinci Resolve, Premiere)

    private func rateXML(_ indent: String) -> String {
        "\(indent)<rate>\n\(indent)  <timebase>\(Int(fps.rounded()))</timebase>\n\(indent)  <ntsc>\(fpsDenominator == 1001 ? "TRUE" : "FALSE")</ntsc>\n\(indent)</rate>\n"
    }

    private static func x(_ s: String) -> String {
        s.replacingOccurrences(of: "&", with: "&amp;").replacingOccurrences(of: "<", with: "&lt;").replacingOccurrences(of: ">", with: "&gt;").replacingOccurrences(of: "\"", with: "&quot;")
    }

    public func exportXML(_ url: URL) throws {
        try validate()
        guard durationFrames > 0, FileManager.default.fileExists(atPath: mediaPath) else { throw PrismaError("Abre un vídeo local antes de exportar.") }
        try Data(xml().utf8).write(to: url, options: .atomic)
    }

    public func xml() -> String {
        let name = ReviewDocument.x(mediaPath.portableFileName)
        let pathURL = ReviewDocument.x(URL(fileURLWithPath: mediaPath).absoluteString)
        let d = durationFrames
        func sample(_ i: String) -> String {
            "\(i)<samplecharacteristics>\n" + rateXML(i + "  ") +
            "\(i)  <width>\(width)</width>\n\(i)  <height>\(height)</height>\n\(i)  <anamorphic>FALSE</anamorphic>\n\(i)  <pixelaspectratio>square</pixelaspectratio>\n\(i)  <fielddominance>none</fielddominance>\n\(i)</samplecharacteristics>\n"
        }
        func audioSample(_ i: String) -> String {
            "\(i)<samplecharacteristics>\n\(i)  <depth>16</depth>\n\(i)  <samplerate>\(audioRate)</samplerate>\n\(i)</samplecharacteristics>\n"
        }
        func tc(_ i: String) -> String {
            "\(i)<timecode>\n" + rateXML(i + "  ") + "\(i)  <string>00:00:00:00</string>\n\(i)  <frame>0</frame>\n\(i)  <displayformat>NDF</displayformat>\n\(i)</timecode>\n"
        }
        func clipHead(_ id: String, _ i: String) -> String {
            "\(i)<clipitem id=\"\(id)\">\n\(i)  <name>\(name)</name>\n\(i)  <enabled>TRUE</enabled>\n\(i)  <duration>\(d)</duration>\n" + rateXML(i + "  ") +
            "\(i)  <start>0</start>\n\(i)  <end>\(d)</end>\n\(i)  <in>0</in>\n\(i)  <out>\(d)</out>\n"
        }
        var s = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<!DOCTYPE xmeml>\n<xmeml version=\"5\">\n  <sequence id=\"prisma-review\">\n"
        s += "    <name>\(ReviewDocument.x(mediaPath.portableFileName.fileNameWithoutExtension)) — Revisión Prisma</name>\n    <duration>\(d)</duration>\n"
        s += rateXML("    ") + tc("    ") + "    <media>\n      <video>\n        <format>\n" + sample("          ") + "        </format>\n        <track>\n"
        s += clipHead("prisma-video", "          ")
        s += "            <file id=\"prisma-file\">\n              <name>\(name)</name>\n              <pathurl>\(pathURL)</pathurl>\n" + rateXML("              ")
        s += "              <duration>\(d)</duration>\n" + tc("              ") + "              <media>\n                <video>\n" + sample("                  ") + "                </video>\n"
        if audioChannels > 0 {
            s += "                <audio>\n" + audioSample("                  ") + "                  <channelcount>\(audioChannels)</channelcount>\n                </audio>\n"
        }
        s += "              </media>\n            </file>\n            <sourcetrack>\n              <mediatype>video</mediatype>\n              <trackindex>1</trackindex>\n            </sourcetrack>\n          </clipitem>\n        </track>\n      </video>\n"
        if audioChannels > 0 {
            s += "      <audio>\n        <numOutputChannels>\(audioChannels)</numOutputChannels>\n        <format>\n" + audioSample("          ") + "        </format>\n"
            for channel in 1...audioChannels {
                s += "        <track>\n" + clipHead("prisma-audio-\(channel)", "          ")
                s += "            <file id=\"prisma-file\" />\n            <sourcetrack>\n              <mediatype>audio</mediatype>\n              <trackindex>\(channel)</trackindex>\n            </sourcetrack>\n          </clipitem>\n        </track>\n"
            }
            s += "      </audio>\n"
        }
        s += "    </media>\n"
        for m in markers.sorted(by: { $0.frame < $1.frame }) {
            s += "    <marker>\n      <name>\(ReviewDocument.x(m.name))</name>\n      <comment>\(ReviewDocument.x(m.note))</comment>\n      <in>\(m.frame)</in>\n      <out>-1</out>\n    </marker>\n"
        }
        return s + "  </sequence>\n</xmeml>\n"
    }

    // Sequence markers from FCP 7 XML exported by Resolve or Premiere. Clip markers are
    // accepted for a single untrimmed clip only, so nothing is silently retimed.
    public static func importXML(_ data: Data) throws -> ReviewDocument {
        if data.count > 16 * 1024 * 1024 { throw PrismaError("El XML es demasiado grande.") }
        let xml: XMLDocument
        do { xml = try XMLDocument(data: data, options: [.nodeLoadExternalEntitiesNever]) }
        catch { throw PrismaError("El XML no es válido.") }
        guard let root = xml.rootElement(), root.name == "xmeml" else { throw PrismaError("Se necesita XML de Final Cut Pro 7 (.xml), no FCPXML.") }
        let sequences = descendants(root, "sequence").filter { $0.elements(forName: "rate").first != nil }
        guard sequences.count == 1 else { throw PrismaError("Exporta una sola secuencia para importar sus notas.") }
        let sequence = sequences[0], rate = sequence.elements(forName: "rate")[0]
        let nominal = Int(value(rate, "timebase") ?? "") ?? 0
        let ntsc = (value(rate, "ntsc") ?? "").uppercased() == "TRUE"
        let doc = ReviewDocument()
        doc.fpsNumerator = ntsc ? nominal * 1000 : nominal
        doc.fpsDenominator = ntsc ? 1001 : 1
        doc.durationFrames = Int64(value(sequence, "duration") ?? "") ?? 0
        if let file = descendants(sequence, "file").first(where: { $0.elements(forName: "pathurl").first != nil }),
           let path = value(file, "pathurl"), let url = URL(string: path), url.isFileURL {
            doc.mediaPath = url.path
        }
        var markers = sequence.elements(forName: "marker")
        if markers.isEmpty {
            let clips = sequence.elements(forName: "media").flatMap { $0.elements(forName: "video") }.flatMap { $0.elements(forName: "track") }.flatMap { $0.elements(forName: "clipitem") }
            if clips.count == 1, (Int64(value(clips[0], "in") ?? "0") ?? 0) == 0, (Int64(value(clips[0], "start") ?? "0") ?? 0) == 0 {
                markers = clips[0].elements(forName: "marker")
            }
        }
        for marker in markers {
            guard let frame = Int64(value(marker, "in") ?? ""), frame >= 0 else { throw PrismaError("Marcador sin posición válida.") }
            doc.markers.append(ReviewMarker(frame: frame, name: value(marker, "name") ?? "Nota", note: value(marker, "comment") ?? ""))
        }
        try doc.validate()
        return doc
    }

    private static func value(_ element: XMLElement, _ name: String) -> String? {
        element.elements(forName: name).first?.stringValue
    }

    private static func descendants(_ element: XMLElement, _ name: String) -> [XMLElement] {
        var found: [XMLElement] = []
        for child in element.children ?? [] {
            guard let e = child as? XMLElement else { continue }
            if e.name == name { found.append(e) }
            found += descendants(e, name)
        }
        return found
    }

    // Adds the notes of another document (import). Returns how many were new.
    @discardableResult
    public func merge(_ incoming: ReviewDocument) throws -> Int {
        if incoming.markers.isEmpty {
            throw PrismaError("El documento no contiene marcadores. Algunas exportaciones XML de los editores omiten las notas. Usa un XML que incluya marcadores o una copia JSON de Prisma.")
        }
        if incoming.fpsNumerator != fpsNumerator || incoming.fpsDenominator != fpsDenominator {
            throw PrismaError("Los FPS del documento no coinciden con los del vídeo. Exporta las notas con la misma velocidad de fotogramas para mantener su posición.")
        }
        if incoming.markers.contains(where: { $0.frame >= durationFrames }) { throw PrismaError("Hay notas fuera de la duración del vídeo abierto.") }
        var count = 0
        for m in incoming.markers where !markers.contains(where: { $0.frame == m.frame && $0.name == m.name && $0.note == m.note }) {
            markers.append(m); count += 1
        }
        return count
    }
}

// J / L: each press doubles the speed up to 16×, the opposite key slows down and then reverses.
public struct Shuttle {
    public var rate: Double = 0
    public init() {}
    public mutating func press(_ direction: Int) -> Double {
        let d = Double(direction)
        if rate == 0 { rate = d }
        else if (rate > 0 ? 1.0 : -1.0) == d { rate = d * min(16, abs(rate) * 2) }
        else if abs(rate) > 1 { rate /= 2 }
        else { rate = d }
        return rate
    }
}
