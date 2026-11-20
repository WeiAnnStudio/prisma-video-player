// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit

enum Dialogs {
    // Shows an alert and returns the index of the pressed button (0 = first).
    @discardableResult
    static func ask(_ title: String, _ text: String, buttons: [String], accessory: NSView? = nil, warning: Bool = false) -> Int {
        let alert = NSAlert()
        alert.messageText = title
        alert.informativeText = text
        alert.alertStyle = warning ? .warning : .informational
        for b in buttons { alert.addButton(withTitle: b) }
        if let accessory { alert.accessoryView = accessory }
        alert.window.appearance = NSAppearance(named: .darkAqua)
        if let field = accessory as? NSTextField { alert.window.initialFirstResponder = field }
        let response = alert.runModal()
        return response.rawValue - NSApplication.ModalResponse.alertFirstButtonReturn.rawValue
    }

    static func message(_ title: String, _ text: String) { ask(title, text, buttons: ["Cerrar"]) }

    // Long text (information, shortcuts, help) in a scrollable, selectable box.
    static func text(_ title: String, _ body: String) {
        let scroll = NSTextView.scrollableTextView()
        scroll.frame = NSRect(x: 0, y: 0, width: 560, height: 360)
        scroll.borderType = .noBorder
        let view = scroll.documentView as! NSTextView
        view.isEditable = false
        view.isRichText = false
        view.font = .systemFont(ofSize: 13)
        view.textColor = Theme.ink
        view.backgroundColor = Theme.background
        view.textContainerInset = NSSize(width: 10, height: 10)
        view.string = body
        ask(title, "", buttons: ["Cerrar"], accessory: scroll)
    }

    static func field(_ value: String, width: CGFloat = 460) -> NSTextField {
        let f = NSTextField(string: value)
        f.frame = NSRect(x: 0, y: 0, width: width, height: 24)
        f.placeholderString = "https://drive.google.com/drive/folders/…"
        return f
    }
}
#endif
