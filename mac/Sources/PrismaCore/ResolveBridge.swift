// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation

public enum ResolveEdition { case notInstalled, free, studio, unknown }

public struct ResolveInstall {
    public var edition: ResolveEdition = .notInstalled
    public var app = ""        // DaVinci Resolve.app
    public var fuscript = ""
}

// Sends review notes to the timeline open in DaVinci Resolve Studio. Prisma runs Resolve's own
// fuscript from inside the app bundle with a fixed Lua script (the same one as on Windows); the
// notes travel in a data file, so their text is never executed. Nothing goes over the network.
public enum ResolveBridge {
    public static let studioBundle = "com.blackmagic-design.DaVinciResolve"
    public static let freeBundle = "com.blackmagic-design.DaVinciResolveLite"

    public static func candidateApps(applications: String = "/Applications") -> [String] {
        var apps = [applications + "/DaVinci Resolve/DaVinci Resolve.app", applications + "/DaVinci Resolve Studio/DaVinci Resolve.app"]
        let fm = FileManager.default
        for folder in ((try? fm.contentsOfDirectory(atPath: applications)) ?? []).sorted() where folder.hasPrefix("DaVinci Resolve") {
            let base = applications + "/" + folder
            if folder.hasSuffix(".app") { apps.append(base); continue }
            for app in ((try? fm.contentsOfDirectory(atPath: base)) ?? []).sorted() where app.hasPrefix("DaVinci Resolve") && app.hasSuffix(".app") {
                apps.append(base + "/" + app)
            }
        }
        var seen = Set<String>()
        return apps.filter { seen.insert($0).inserted }
    }

    public static func detect(applications: String = "/Applications") -> ResolveInstall {
        var install = ResolveInstall()
        for app in candidateApps(applications: applications) {
            let fuscript = app + "/Contents/Libraries/Fusion/fuscript"
            guard FileManager.default.isExecutableFile(atPath: fuscript) else { continue }
            install.app = app; install.fuscript = fuscript
            let plist = URL(fileURLWithPath: app + "/Contents/Info.plist")
            let info = (try? Data(contentsOf: plist)).flatMap { try? PropertyListSerialization.propertyList(from: $0, format: nil) as? [String: Any] }
            let bundle = info?["CFBundleIdentifier"] as? String ?? ""
            // The free edition has its own bundle id; anything else is confirmed by Resolve itself.
            install.edition = bundle == freeBundle ? .free : .unknown
            if install.edition == .unknown { return install }
        }
        return install
    }

    public static func escape(_ text: String) -> String {
        text.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\t", with: "\\t").replacingOccurrences(of: "\r", with: "\\r").replacingOccurrences(of: "\n", with: "\\n")
    }

    public static func markersData(_ review: ReviewDocument) -> String {
        var out = "PRISMA-MARKERS 1\nfps\t\(review.fpsNumerator)\t\(review.fpsDenominator)\nvideo\t\(escape(review.mediaPath.portableFileName))\n"
        for m in review.markers { out += "m\t\(m.frame)\t\(escape(m.id))\t\(escape(m.name))\t\(escape(m.note))\n" }
        return out
    }

    // Writes the script and the notes into the work folder.
    public static func prepare(_ review: ReviewDocument, work: URL, script: String) throws {
        try FileManager.default.createDirectory(at: work, withIntermediateDirectories: true)
        try Data(script.utf8).write(to: work.appendingPathComponent("prisma_resolve.lua"), options: .atomic)
        try Data(markersData(review).utf8).write(to: work.appendingPathComponent("notas.txt"), options: .atomic)
        try? FileManager.default.removeItem(at: work.appendingPathComponent("resultado.txt"))
    }

    // Runs fuscript and returns the result fields (status, added, present, ...). Call off the main thread.
    public static func run(_ install: ResolveInstall, work: URL, timeout: TimeInterval = 30) -> [String: String] {
        let resultURL = work.appendingPathComponent("resultado.txt")
        let process = Process()
        process.executableURL = URL(fileURLWithPath: install.fuscript)
        process.arguments = [work.appendingPathComponent("prisma_resolve.lua").path]
        process.currentDirectoryURL = work
        var env = ProcessInfo.processInfo.environment
        env["PRISMA_MARKERS"] = work.appendingPathComponent("notas.txt").path
        env["PRISMA_RESULT"] = resultURL.path
        process.environment = env
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        let done = DispatchSemaphore(value: 0)
        process.terminationHandler = { _ in done.signal() }
        do { try process.run() } catch { return ["status": "ERROR", "message": error.localizedDescription] }
        if done.wait(timeout: .now() + timeout) == .timedOut { process.terminate(); return ["status": "TIMEOUT"] }
        return parseResult(resultURL)
    }

    public static func parseResult(_ url: URL) -> [String: String] {
        guard let text = try? String(contentsOf: url, encoding: .utf8) else { return ["status": "NO_OUTPUT"] }
        var result: [String: String] = [:]
        for line in text.components(separatedBy: "\n") {
            guard let tab = line.firstIndex(of: "\t"), tab != line.startIndex else { continue }
            result[String(line[..<tab])] = String(line[line.index(after: tab)...]).trimmingCharacters(in: CharacterSet(charactersIn: "\r"))
        }
        if result["status"] == nil { result["status"] = "NO_OUTPUT" }
        return result
    }
}
