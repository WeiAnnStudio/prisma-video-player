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
        doc.mediaPath = media; doc.durationFrames = 20000; doc.cloudFolder = "https://drive.google.com/drive/folders/1AbC"
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
        XCTAssertEqual(back.cloudFolder, doc.cloudFolder)
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
        let bad = sample(); bad.cloudFolder = "https://evil.example/x"
        XCTAssertNoThrow(try bad.validate()); XCTAssertEqual(bad.cloudFolder, "")
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

    func testCloudFolders() {
        XCTAssertEqual(CloudFolder.normalize("https://drive.google.com/drive/folders/1AbC?usp=sharing")?.service, "Google Drive")
        XCTAssertEqual(CloudFolder.normalize("www.dropbox.com/scl/fo/abc/xyz?dl=0")?.url.hasPrefix("https://www.dropbox.com/"), true)
        XCTAssertEqual(CloudFolder.normalize("https://1drv.ms/f/s!Abc")?.service, "OneDrive")
        XCTAssertEqual(CloudFolder.normalize("https://contoso-my.sharepoint.com/:f:/g/personal/x")?.service, "OneDrive")
        for bad in ["http://drive.google.com/drive/folders/1", "https://drive.google.com.evil.example/x", "https://user:pass@drive.google.com/x",
                    "file:///C:/x", "https://sharepoint.com/x", "javascript:alert(1)", "https://drive.google.com:8443/x"] {
            XCTAssertNil(CloudFolder.normalize(bad), bad)
        }
    }

    func testMcpDocumentAndTools() throws {
        let doc = sample()
        let text = McpServer.document(doc)
        XCTAssertTrue(text.contains("titulo: Ajustes Corto 12"))
        XCTAssertTrue(text.contains("00:02:26:09 se ha colado una \"n\" después de \"carro\""))
        XCTAssertTrue(text.contains("00:04:16:01 Sonido: limpiamos este sonido / segunda línea"))
        XCTAssertTrue(text.contains("00:00:04:00 Música"))
        XCTAssertLessThan(text.range(of: "00:00:04:00")!.lowerBound, text.range(of: "00:02:26:09")!.lowerBound)
        XCTAssertFalse(text.contains("/Users/ana"))

        let markers = temp.appendingPathComponent("marcadores")
        try doc.save(ReviewDocument.storageURL(for: doc.mediaPath, in: markers))
        let mcp = McpServer(markersFolder: markers)
        let initReply = mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}")!
        XCTAssertTrue(initReply.contains("\"protocolVersion\":\"2025-06-18\"") && initReply.contains("\"instructions\""))
        XCTAssertNil(mcp.handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}"))
        let tools = mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}")!
        XCTAssertTrue(tools.contains("listar_revisiones") && tools.contains("leer_revision") && tools.contains("readOnlyHint"))
        let listed = mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"listar_revisiones\",\"arguments\":{}}}")!
        XCTAssertTrue(listed.contains("Corto 12.mov") && listed.contains("Google Drive") && !listed.contains("/Users/ana"))
        let read = mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"method\":\"tools/call\",\"params\":{\"name\":\"leer_revision\",\"arguments\":{\"video\":\"corto 12\"}}}")!
        XCTAssertTrue(read.contains("Ajustes Corto 12") && read.contains("00:02:26:09") && read.contains("\"id\":\"x\""))
        XCTAssertTrue(read.contains("archivo_prisma: Corto 12.prisma.json"))
        let jsonReply = mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\",\"params\":{\"name\":\"json_revision\",\"arguments\":{\"video\":\"Corto 12.mov\"}}}")!
        let parsed = try JSON.parse(Data(jsonReply.utf8)) as! [String: Any]
        let content = ((parsed["result"] as! [String: Any])["content"] as! [Any])[0] as! [String: Any]
        let remote = try ReviewDocument.parse(Data((content["text"] as! String).utf8))
        XCTAssertEqual(remote.markers.count, 3); XCTAssertEqual(remote.mediaPath, "Corto 12.mov"); XCTAssertEqual(remote.cloudFolder, "")
        XCTAssertTrue(mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"leer_revision\",\"arguments\":{\"video\":\"no existe\"}}}")!.contains("\"isError\":true"))
        XCTAssertTrue(mcp.handle("{no es json")!.contains("-32700"))
        XCTAssertTrue(mcp.handle("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"borrar\"}")!.contains("-32601"))
    }

    func testAiConfigMerges() throws {
        let claude = try AiConnect.mergeClaudeConfig("{\"preferences\":{\"theme\":\"dark\"},\"mcpServers\":{\"otro\":{\"command\":\"x\"}}}", executable: "/Applications/Prisma.app/Contents/MacOS/Prisma")
        let root = try JSON.parse(Data(claude.utf8)) as! [String: Any]
        let servers = root["mcpServers"] as! [String: Any]
        XCTAssertNotNil(root["preferences"]); XCTAssertNotNil(servers["otro"])
        XCTAssertEqual((servers["prisma"] as! [String: Any])["command"] as? String, "/Applications/Prisma.app/Contents/MacOS/Prisma")
        XCTAssertThrowsError(try AiConnect.mergeClaudeConfig("[1]", executable: "x"))
        let toml = AiConnect.mergeChatGptConfig("model = \"gpt\"\n\n[mcp_servers.prisma]\ncommand = 'viejo'\n[mcp_servers.prisma.env]\nA = \"1\"\n\n[mcp_servers.otro]\ncommand = \"x\"\n", executable: "/Applications/Prisma.app/Contents/MacOS/Prisma")
        XCTAssertTrue(toml.contains("model = \"gpt\"") && toml.contains("[mcp_servers.otro]") && !toml.contains("viejo") && !toml.contains("[mcp_servers.prisma.env]"))
        XCTAssertTrue(toml.contains("command = '/Applications/Prisma.app/Contents/MacOS/Prisma'"))
        XCTAssertEqual(AiConnect.mergeChatGptConfig(toml, executable: "/Applications/Prisma.app/Contents/MacOS/Prisma"), toml)
        XCTAssertTrue(AiConnect.mergeChatGptConfig("", executable: "/Users/it's/Prisma").contains("command = \"/Users/it's/Prisma\""))
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
        XCTAssertTrue(McpServer.document(doc).contains("entrega_automatica") && McpServer.document(doc).contains("«Revisiones»") && !McpServer.document(doc).contains(temp.path))
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
