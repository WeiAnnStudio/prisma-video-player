// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit

// Same palette as Prisma for Windows.
enum Theme {
    static func rgb(_ r: CGFloat, _ g: CGFloat, _ b: CGFloat, _ a: CGFloat = 1) -> NSColor {
        NSColor(srgbRed: r / 255, green: g / 255, blue: b / 255, alpha: a)
    }
    static let background = rgb(16, 18, 22)
    static let panel = rgb(23, 26, 31)
    static let card = rgb(30, 34, 40)
    static let hover = rgb(40, 46, 53)
    static let border = rgb(47, 54, 62)
    static let ink = rgb(235, 239, 243)
    static let muted = rgb(148, 159, 172)
    static let accent = rgb(128, 224, 193)
    static let accentSoft = rgb(34, 57, 52)
    static let marker = rgb(245, 194, 104)
    static let error = rgb(255, 138, 128)
    static let video = rgb(10, 12, 15)

    static func label(_ text: String, size: CGFloat = 12, weight: NSFont.Weight = .regular, color: NSColor = Theme.ink) -> NSTextField {
        let l = NSTextField(labelWithString: text)
        l.font = .systemFont(ofSize: size, weight: weight)
        l.textColor = color
        l.lineBreakMode = .byTruncatingTail
        return l
    }

    static func mono(_ size: CGFloat) -> NSFont { .monospacedDigitSystemFont(ofSize: size, weight: .regular) }
}

// Flat button in Prisma's style: filled (accent), outlined or plain with an SF Symbol.
final class FlatButton: NSButton {
    enum Style { case accent, outlined, plain }
    var style: Style = .plain { didSet { needsDisplay = true } }
    var active = false { didSet { refreshColors(); needsDisplay = true } }
    private var hovering = false
    private var area: NSTrackingArea?
    private var handler: (() -> Void)?

    convenience init(_ title: String, symbol: String? = nil, style: Style = .plain, tip: String? = nil, action: @escaping () -> Void) {
        self.init(frame: .zero)
        self.title = title
        self.style = style
        self.handler = action
        isBordered = false
        bezelStyle = .regularSquare
        setButtonType(.momentaryChange)
        font = .systemFont(ofSize: 12, weight: .medium)
        toolTip = tip ?? (title.isEmpty ? nil : title)
        if let symbol, let image = NSImage(systemSymbolName: symbol, accessibilityDescription: tip ?? title) {
            self.image = image
            imagePosition = title.isEmpty ? .imageOnly : .imageLeading
            imageHugsTitle = true
        }
        target = self
        self.action = #selector(run)
        wantsLayer = true
        refreshColors()
    }

    @objc private func run() { handler?() }

    override var isEnabled: Bool { didSet { refreshColors(); needsDisplay = true } }
    override var acceptsFirstResponder: Bool { false }
    func setLabel(_ text: String) { title = text; refreshColors(); invalidateIntrinsicContentSize() }

    private func refreshColors() {
        let color: NSColor = style == .accent ? Theme.background : (active ? Theme.accent : Theme.ink)
        contentTintColor = isEnabled ? color : Theme.muted
        attributedTitle = NSAttributedString(string: title, attributes: [.foregroundColor: isEnabled ? color : Theme.muted, .font: font ?? NSFont.systemFont(ofSize: 12)])
    }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        if let area { removeTrackingArea(area) }
        let a = NSTrackingArea(rect: bounds, options: [.mouseEnteredAndExited, .activeInKeyWindow, .inVisibleRect], owner: self)
        addTrackingArea(a); area = a
    }
    override func mouseEntered(with event: NSEvent) { hovering = true; needsDisplay = true }
    override func mouseExited(with event: NSEvent) { hovering = false; needsDisplay = true }

    override func draw(_ dirtyRect: NSRect) {
        let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
        let path = NSBezierPath(roundedRect: rect, xRadius: 7, yRadius: 7)
        var fill: NSColor = .clear
        switch style {
        case .accent: fill = isEnabled ? (hovering ? Theme.rgb(156, 237, 210) : Theme.accent) : Theme.border
        case .outlined, .plain: fill = hovering && isEnabled ? Theme.hover : (active ? Theme.accentSoft : .clear)
        }
        fill.setFill(); path.fill()
        if style == .outlined { Theme.border.setStroke(); path.lineWidth = 1; path.stroke() }
        super.draw(dirtyRect)
    }

    override var intrinsicContentSize: NSSize {
        let s = super.intrinsicContentSize
        return NSSize(width: s.width + (title.isEmpty ? 14 : 24), height: max(30, s.height + 10))
    }
}
#endif
