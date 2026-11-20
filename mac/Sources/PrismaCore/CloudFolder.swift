// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation

// A shared cloud folder where an assistant (Claude, ChatGPT) files the review notes.
// Only https links to Google Drive, Dropbox and OneDrive/SharePoint are accepted.
public enum CloudFolder {
    public static let maxLength = 2048

    public static func normalize(_ input: String) -> (url: String, service: String)? {
        var text = input.trimmingCharacters(in: .whitespacesAndNewlines)
        if text.isEmpty || text.count > maxLength || text.rangeOfCharacter(from: CharacterSet(charactersIn: " \t\r\n\"<>")) != nil { return nil }
        let lower = text.lowercased()
        if !lower.hasPrefix("http://") && !lower.hasPrefix("https://") { text = "https://" + text }
        guard let components = URLComponents(string: text), components.scheme?.lowercased() == "https",
              components.user == nil, components.password == nil, components.port == nil || components.port == 443,
              let host = components.host?.lowercased(), let url = components.url else { return nil }
        let service = serviceOf(host)
        return service.isEmpty ? nil : (url.absoluteString, service)
    }

    public static func serviceName(_ url: String) -> String { normalize(url)?.service ?? "" }

    static func serviceOf(_ host: String) -> String {
        if host == "drive.google.com" || host == "docs.google.com" { return "Google Drive" }
        if host == "dropbox.com" || host == "www.dropbox.com" { return "Dropbox" }
        if host == "onedrive.live.com" || host == "1drv.ms" || host == "onedrive.cloud.microsoft" { return "OneDrive" }
        if host.hasSuffix(".sharepoint.com") && host.count > ".sharepoint.com".count { return "OneDrive" }
        return ""
    }
}
