// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation

// Registers "Prisma --mcp" as a local MCP server in Claude and ChatGPT for Mac.
// No account or key is involved: the app starts Prisma on this Mac when it needs the notes,
// and the cloud folder is reached with the connector the user already has in that app.
public enum AiConnect {
    public static let serverName = "prisma"

    public static var claudeConfigFile: URL {
        URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent("Library/Application Support/Claude/claude_desktop_config.json")
    }

    public static var chatGptConfigFile: URL {
        let custom = ProcessInfo.processInfo.environment["CODEX_HOME"] ?? ""
        let home = custom.isEmpty ? URL(fileURLWithPath: NSHomeDirectory()).appendingPathComponent(".codex") : URL(fileURLWithPath: custom)
        return home.appendingPathComponent("config.toml")
    }

    // Adds or replaces mcpServers.prisma and keeps everything else in the file.
    public static func mergeClaudeConfig(_ existing: String, executable: String) throws -> String {
        var root: [String: Any] = [:]
        if !existing.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            guard let parsed = try? JSON.parse(Data(existing.utf8)) as? [String: Any] else {
                throw PrismaError("La configuración de Claude no es un objeto JSON.")
            }
            root = parsed
        }
        var servers = root["mcpServers"] as? [String: Any] ?? [:]
        servers[serverName] = ["command": executable, "args": ["--mcp"]]
        root["mcpServers"] = servers
        return JSON.serialize(root, pretty: true) + "\n"
    }

    // Replaces the [mcp_servers.prisma] table (and its sub-tables) or appends it.
    public static func mergeChatGptConfig(_ existing: String, executable: String) -> String {
        let lines = existing.replacingOccurrences(of: "\r\n", with: "\n").components(separatedBy: "\n")
        var kept: [String] = []
        var skipping = false
        let header = try! NSRegularExpression(pattern: "^\\s*\\[\\[?\\s*([^\\]]+?)\\s*\\]\\]?\\s*(#.*)?$")
        for line in lines {
            let range = NSRange(line.startIndex..., in: line)
            if let match = header.firstMatch(in: line, range: range), let r = Range(match.range(at: 1), in: line) {
                let table = line[r].replacingOccurrences(of: "\"", with: "").replacingOccurrences(of: " ", with: "")
                skipping = table == "mcp_servers." + serverName || table.hasPrefix("mcp_servers." + serverName + ".")
            }
            if !skipping { kept.append(line) }
        }
        while let last = kept.last, last.trimmingCharacters(in: .whitespaces).isEmpty { kept.removeLast() }
        if !kept.isEmpty { kept.append("") }
        kept.append("[mcp_servers.\(serverName)]")
        kept.append("command = " + tomlString(executable))
        kept.append("args = [\"--mcp\"]")
        return kept.joined(separator: "\n") + "\n"
    }

    static func tomlString(_ value: String) -> String {
        if !value.contains("'") && !value.contains("\n") && !value.contains("\r") { return "'" + value + "'" }
        return "\"" + value.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"") + "\""
    }

    // Writes the file atomically, keeping a one-time backup of the original.
    public static func writeWithBackup(_ url: URL, _ content: String) throws {
        let fm = FileManager.default
        try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
        let backup = url.appendingPathExtension("antes-de-prisma")
        if fm.fileExists(atPath: url.path) && !fm.fileExists(atPath: backup.path) { try fm.copyItem(at: url, to: backup) }
        try Data(content.utf8).write(to: url, options: .atomic)
    }

    public static func readOrEmpty(_ url: URL) -> String { (try? String(contentsOf: url, encoding: .utf8)) ?? "" }
}
