// SPDX-License-Identifier: GPL-3.0-or-later
import XCTest
@testable import PrismaCore

final class PrismaCoreTests: XCTestCase {
    var temp: URL!

    override func setUpWithError() throws {
        temp = FileManager.default.temporaryDirectory.appendingPathComponent("prisma-tests-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: temp, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: temp) }

    func sample(_ media: String = "/Users/ana/Renders/Corto 12.mov") -> ReviewDocument {
        let doc = ReviewDocument()
        doc.mediaPath = media; doc.durationFrames = 20000
        try! doc.setFps(25)
        doc.markers.append(ReviewMarker(frame: 3659, name: "Nota 1", note: "se ha colado una \"n\" después de \"carro\""))
        doc.markers.append(ReviewMarker(frame: 6401, name: "Sonido", note: "limpiamos este sonido\r\nsegunda línea"))
        doc.markers.append(ReviewMarker(frame: 100, name: "Música", note: ""))
        return doc
    }

    func testSha256() {
        XCTAssertEqual(SHA256.hex(Data()), "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")
        XCTAssertEqual(SHA256.hex(Data("abc".utf8)), "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")
        XCTAssertEqual(SHA256.hex(Data(String(repeating: "a", count: 1000).utf8)), "41EDECE42D63E8D9BF515A9BA6932E1C20CBC9F5A5D134645ADB5DB1B9737EA3")
    }

    func testFpsAndTimecode() throws {
        let doc = ReviewDocument()
        try doc.setFps(23.976); XCTAssertEqual(doc.fpsNumerator, 24000); XCTAssertEqual(doc.fpsDenominator, 1001)
        try doc.setFps(29.97); XCTAssertEqual(doc.fpsNumerator, 30000)
        try doc.setFps(25); XCTAssertEqual(doc.fpsNumerator, 25); XCTAssertEqual(doc.fpsDenominator, 1)
        XCTAssertThrowsError(try doc.setFps(27.3))
        XCTAssertEqual(doc.timecode(3659), "00:02:26:09")
        try doc.setFps(23.976); XCTAssertEqual(doc.timecode(86400), "01:00:00:00")
    }

    func testTypedTimecode() throws {
        let doc = ReviewDocument(); try doc.setFps(25); doc.durationFrames = 20000
        XCTAssertEqual(doc.parseTimecode("00:00:03:10", current: 0), 85)
        XCTAssertEqual(doc.parseTimecode("3:10", current: 0), 85)
        XCTAssertEqual(doc.parseTimecode("310", current: 0), 85)
        XCTAssertEqual(doc.parseTimecode("1.00.00", current: 0), 1500)
        XCTAssertEqual(doc.parseTimecode("+10", current: 100), 110)
        XCTAssertEqual(doc.parseTimecode("-1:00", current: 100), 75)
        XCTAssertEqual(doc.parseTimecode("99:00:00:00", current: 0), 19999)
        XCTAssertEqual(doc.parseTimecode("-500", current: 10), 0)
        XCTAssertNil(doc.parseTimecode("abc", current: 0))
        XCTAssertNil(doc.parseTimecode("", current: 0))
        XCTAssertNil(doc.parseTimecode("1:2:3:4:5", current: 0))
        try doc.setFps(23.976); doc.durationFrames = 200000
        XCTAssertEqual(doc.parseTimecode("01:00:00:00", current: 0), 86400)
    }

    func testDefaultNoteTitles() {
        for name in ["Nota", "Nota 1", "nota 12", "NOTA 3", "Nota #4", "Nota nº 5", "Nota3", "  Nota 2  ", ""] { XCTAssertTrue(SyncDelivery.isDefaultName(name), name) }
        for name in ["Notan", "Nota importante", "Música", "Nota 1 audio", "Notas"] { XCTAssertFalse(SyncDelivery.isDefaultName(name), name) }
    }

    // A file exactly as Prisma for Windows writes it (JavaScriptSerializer).
    func testReadsWindowsJson() throws {
        let windows = "{\"Version\":1,\"MediaPath\":\"Corto 12.mov\",\"CloudFolder\":\"\",\"SyncFolder\":\"G:\\\\Mi unidad\\\\Revisiones\",\"SyncHash\":\"\",\"FpsNumerator\":24000,\"FpsDenominator\":1001,\"DurationFrames\":20000,\"Width\":3840,\"Height\":2160,\"AudioChannels\":2,\"AudioRate\":48000,\"Markers\":[{\"Id\":\"0f8fad5bd9cb469fa16570867728950e\",\"Frame\":100,\"Name\":\"M\\u00fasica\",\"Note\":\"\\u003cok\\u003e\"}]}"
        let doc = try ReviewDocument.parse(Data(windows.utf8))
        XCTAssertEqual(doc.fpsNumerator, 24000); XCTAssertEqual(doc.width, 3840); XCTAssertEqual(doc.markers.count, 1)
        XCTAssertEqual(doc.markers[0].name, "Música"); XCTAssertEqual(doc.markers[0].note, "<ok>")
        XCTAssertEqual(doc.syncFolder, "", "Una carpeta de Windows no se usa en el Mac")
    }

    func testJsonRoundTripAndSharing() throws {
        let doc = sample()
        let url = temp.appendingPathComponent("notas.json")
        try doc.save(url)
        let back = try ReviewDocument.load(url)
        XCTAssertEqual(back.markers.count, 3); XCTAssertEqual(back.markers[0].note, doc.markers[0].note)
        XCTAssertTrue(doc.json().hasPrefix("{\"Version\":1,\"MediaPath\":"), "Mismo orden de campos que Windows")
        let shared = try doc.sharingJSON()
        XCTAssertFalse(shared.contains("/Users/ana")); XCTAssertFalse(shared.contains("drive.google.com"))
        let remote = try ReviewDocument.parse(Data(shared.utf8))
        XCTAssertEqual(remote.mediaPath, "Corto 12.mov"); XCTAssertEqual(remote.markers.first?.frame, 100)
        let windowsPath = ReviewDocument(); windowsPath.mediaPath = "E:\\Renders\\Corto 12.mov"
        XCTAssertEqual(windowsPath.sharingCopy().mediaPath, "Corto 12.mov")
    }

    func testValidation() {
        let doc = sample(); doc.markers.append(ReviewMarker(frame: 20000))
        XCTAssertThrowsError(try doc.validate())
        let long = sample(); long.markers[0].note = String(repeating: "x", count: 20001)
        XCTAssertThrowsError(try long.validate())
        // Notes saved by 0.4 kept a cloud link; it is ignored now and dropped on the next save.
        let old = try? ReviewDocument.parse(Data("{\"Version\":1,\"MediaPath\":\"a.mov\",\"CloudFolder\":\"https://drive.google.com/drive/folders/1AbC\",\"FpsNumerator\":25,\"FpsDenominator\":1,\"DurationFrames\":100,\"Markers\":[]}".utf8))
        XCTAssertNotNil(old); XCTAssertFalse(old?.json().contains("CloudFolder") ?? true)
        XCTAssertThrowsError(try ReviewDocument.parse(Data("{no".utf8)))
    }

    func testStorageIsPerVideo() {
        let a = ReviewDocument.storageURL(for: "/Users/ana/a.mov", in: temp), b = ReviewDocument.storageURL(for: "/Users/ana/b.mov", in: temp)
        XCTAssertNotEqual(a, b); XCTAssertEqual(a, ReviewDocument.storageURL(for: "/Users/ana/./a.mov", in: temp))
        XCTAssertEqual(a.lastPathComponent.count, 64 + 5)
    }

    func testCsvNeutralisesFormulas() {
        XCTAssertEqual(ReviewDocument.csvCell("=1+1"), "\"'=1+1\"")
        XCTAssertEqual(ReviewDocument.csvCell("a \"b\""), "\"a \"\"b\"\"\"")
        let csv = sample().csv()
        XCTAssertTrue(csv.hasPrefix("Fotograma,Código de tiempo,FPS,Nombre,Nota\r\n100,00:00:04:00,25,"))
    }

    func testXmlRoundTrip() throws {
        let doc = sample(); doc.audioChannels = 2
        let xml = doc.xml()
        XCTAssertTrue(xml.contains("<pathurl>file:///Users/ana/Renders/Corto%2012.mov</pathurl>"))
        XCTAssertTrue(xml.contains("<comment>se ha colado una &quot;n&quot; después de &quot;carro&quot;</comment>"))
        let back = try ReviewDocument.importXML(Data(xml.utf8))
        XCTAssertEqual(back.fpsNumerator, 25); XCTAssertEqual(back.durationFrames, 20000); XCTAssertEqual(back.markers.count, 3)
        XCTAssertEqual(back.mediaPath, "/Users/ana/Renders/Corto 12.mov")
        XCTAssertEqual(back.markers.first { $0.frame == 3659 }?.note, doc.markers[0].note)
        XCTAssertThrowsError(try ReviewDocument.importXML(Data("<fcpxml version=\"1.9\"/>".utf8)))
    }

    func testMergeImport() throws {
        let doc = sample(), incoming = sample()
        incoming.markers.append(ReviewMarker(frame: 9000, name: "Nueva", note: "otra"))
        XCTAssertEqual(try doc.merge(incoming), 1)
        let other = sample(); try other.setFps(24)
        XCTAssertThrowsError(try doc.merge(other))
    }

    func testShuttle() {
        var s = Shuttle()
        XCTAssertEqual([s.press(1), s.press(1), s.press(1), s.press(-1), s.press(-1), s.press(-1), s.press(-1)], [1, 2, 4, 2, 1, -1, -2])
    }

    func testDocumentLines() {
        let doc = sample()
        XCTAssertEqual(SyncDelivery.title(doc), "Ajustes Corto 12")
        XCTAssertEqual(SyncDelivery.line(doc, doc.markers[0]), "00:02:26:09 se ha colado una \"n\" después de \"carro\"")
        XCTAssertEqual(SyncDelivery.line(doc, doc.markers[1]), "00:04:16:01 Sonido: limpiamos este sonido / segunda línea")
        XCTAssertEqual(SyncDelivery.line(doc, doc.markers[2]), "00:00:04:00 Música")
        XCTAssertFalse(SyncDelivery.documentXML(doc).contains("/Users/ana"))
    }

    func testSyncDelivery() throws {
        XCTAssertEqual(SyncDelivery.service("/Users/ana/Library/CloudStorage/GoogleDrive-ana@x.com/Mi unidad/Revisiones"), "Google Drive")
        XCTAssertEqual(SyncDelivery.service("/Users/ana/Library/CloudStorage/Dropbox/Clientes"), "Dropbox")
        XCTAssertEqual(SyncDelivery.service("/Users/ana/Library/CloudStorage/OneDrive-Personal/Notas"), "OneDrive")
        XCTAssertEqual(SyncDelivery.service("/Users/ana/Proyectos"), "Carpeta")
        XCTAssertFalse(SyncDelivery.isValidFolder("/")); XCTAssertFalse(SyncDelivery.isValidFolder("relativa"))
        XCTAssertFalse(SyncDelivery.isValidFolder("G:\\Mi unidad")); XCTAssertFalse(SyncDelivery.isValidFolder("/Volumes/Disco"))
        XCTAssertTrue(SyncDelivery.isValidFolder("/Volumes/Disco/Notas"))

        let folder = temp.appendingPathComponent("Mi unidad/Revisiones")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let doc = sample(); doc.syncFolder = folder.path
        let first = try SyncDelivery.write(doc, to: folder.path)
        XCTAssertTrue(first.contains("Google Drive"))
        let docx = folder.appendingPathComponent("Ajustes Corto 12.docx"), json = folder.appendingPathComponent("Corto 12.prisma.json")
        let bytes = try Data(contentsOf: docx)
        XCTAssertEqual(Array(bytes.prefix(4)), [0x50, 0x4B, 0x03, 0x04])
        XCTAssertTrue(String(decoding: bytes, as: UTF8.self).contains("00:02:26:09 se ha colado una"))
        let xml = SyncDelivery.documentXML(doc)
        XCTAssertTrue(xml.contains("<w14:checkbox>"), "Una casilla por nota")
        XCTAssertFalse(xml.contains("numId"), "Sin viñetas")
        XCTAssertFalse(xml.contains("Nota 1"), "El título propuesto no pasa al documento")
        XCTAssertEqual(try ReviewDocument.load(json).markers.count, 3)
        let firstDate = try FileManager.default.attributesOfItem(atPath: docx.path)[.modificationDate] as! Date
        Thread.sleep(forTimeInterval: 1.1)
        try SyncDelivery.write(doc, to: folder.path)
        XCTAssertEqual(try FileManager.default.attributesOfItem(atPath: docx.path)[.modificationDate] as! Date, firstDate, "Sin cambios no reescribe")
        try Data("editado en Google Docs".utf8).write(to: docx)
        doc.markers.append(ReviewMarker(frame: 9000, name: "Nueva", note: "otra nota"))
        let kept = try SyncDelivery.write(doc, to: folder.path)
        XCTAssertTrue(kept.contains("se conservan"))
        let edited = try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasPrefix("Ajustes Corto 12 (editado ") }
        XCTAssertEqual(edited.count, 1)
        XCTAssertEqual(try String(contentsOf: folder.appendingPathComponent(edited[0]), encoding: .utf8), "editado en Google Docs")
        XCTAssertEqual(try ReviewDocument.load(json).markers.count, 4)
        XCTAssertFalse(try doc.sharingJSON().contains("Mi unidad"))
    }

    func testZipIsReadable() throws {
        let zip = Zip.store([("a.txt", Data("hola".utf8))])
        XCTAssertEqual(Zip.crc32(Data("123456789".utf8)), 0xCBF43926)
        XCTAssertEqual(Array(zip.suffix(22).prefix(4)), [0x50, 0x4B, 0x05, 0x06])
    }

    func testResolveData() throws {
        let doc = sample()
        let data = ResolveBridge.markersData(doc).components(separatedBy: "\n")
        XCTAssertEqual(data[0], "PRISMA-MARKERS 1"); XCTAssertEqual(data[1], "fps\t25\t1"); XCTAssertEqual(data[2], "video\tCorto 12.mov")
        XCTAssertEqual(data.filter { $0.hasPrefix("m\t") }.count, 3)
        XCTAssertTrue(ResolveBridge.markersData(doc).contains("limpiamos este sonido\\r\\nsegunda línea"))
        XCTAssertFalse(ResolveBridge.markersData(doc).contains("/Users/ana"))
        XCTAssertEqual(ResolveBridge.escape("a\\b\tc\nd"), "a\\\\b\\tc\\nd")
        let result = temp.appendingPathComponent("resultado.txt")
        try Data("status\tOK\ntimeline\tMontaje\nadded\t3\n".utf8).write(to: result)
        XCTAssertEqual(ResolveBridge.parseResult(result), ["status": "OK", "timeline": "Montaje", "added": "3"])
        XCTAssertEqual(ResolveBridge.parseResult(temp.appendingPathComponent("no")), ["status": "NO_OUTPUT"])
        XCTAssertEqual(ResolveBridge.detect(applications: temp.path).edition, .notInstalled)
    }
}
