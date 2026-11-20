// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import AVKit

// Window content. It owns the keyboard (J/K/L, arrows, M...) and accepts dropped videos.
final class RootView: NSView {
    var onKey: ((NSEvent) -> Bool)?
    var onDrop: (([URL]) -> Void)?
    var onLayout: (() -> Void)?

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = Theme.background.cgColor
        registerForDraggedTypes([.fileURL])
    }
    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { true }
    override var isFlipped: Bool { true }

    override func keyDown(with event: NSEvent) {
        if onKey?(event) != true { super.keyDown(with: event) }
    }

    override func layout() { super.layout(); onLayout?() }

    override func draggingEntered(_ sender: NSDraggingInfo) -> NSDragOperation {
        urls(sender).isEmpty ? [] : .copy
    }

    override func performDragOperation(_ sender: NSDraggingInfo) -> Bool {
        let list = urls(sender)
        if list.isEmpty { return false }
        onDrop?(list)
        return true
    }

    private func urls(_ sender: NSDraggingInfo) -> [URL] {
        let options: [NSPasteboard.ReadingOptionKey: Any] = [.urlReadingFileURLsOnly: true]
        return sender.draggingPasteboard.readObjects(forClasses: [NSURL.self], options: options) as? [URL] ?? []
    }
}

// AVPlayerView without its own controls or keyboard handling: Prisma draws its controls and
// the keys go to RootView. AVPlayerView shows HDR (EDR) on displays that support it.
final class VideoView: AVPlayerView {
    var onDoubleClick: (() -> Void)?
    var onClick: (() -> Void)?
    override var acceptsFirstResponder: Bool { false }
    // Clicks go to Prisma, not to AVPlayerView's internal views.
    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 { onDoubleClick?() } else { onClick?() }
    }
    override func keyDown(with event: NSEvent) { nextResponder?.keyDown(with: event) }
}

// Timeline with the play position and a tick for every note.
final class SeekBar: NSView {
    var progress: Double = 0 { didSet { needsDisplay = true } }
    var markers: [Double] = [] { didSet { needsDisplay = true } }
    var onSeek: ((Double, Bool) -> Void)?   // fraction, final (mouse up)
    var enabled = false { didSet { needsDisplay = true } }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }

    override func draw(_ dirtyRect: NSRect) {
        let track = NSRect(x: 0, y: bounds.midY - 2, width: bounds.width, height: 4)
        Theme.border.setFill(); NSBezierPath(roundedRect: track, xRadius: 2, yRadius: 2).fill()
        guard enabled else { return }
        let x = CGFloat(min(1, max(0, progress))) * bounds.width
        Theme.accent.setFill(); NSBezierPath(roundedRect: NSRect(x: 0, y: track.minY, width: x, height: 4), xRadius: 2, yRadius: 2).fill()
        Theme.marker.setFill()
        for m in markers {
            let mx = CGFloat(m) * bounds.width
            NSBezierPath(rect: NSRect(x: mx - 1, y: bounds.midY - 7, width: 2, height: 14)).fill()
        }
        Theme.ink.setFill(); NSBezierPath(ovalIn: NSRect(x: x - 6, y: bounds.midY - 6, width: 12, height: 12)).fill()
    }

    private func fraction(_ event: NSEvent) -> Double {
        let p = convert(event.locationInWindow, from: nil)
        return Double(min(1, max(0, p.x / max(1, bounds.width))))
    }
    override func mouseDown(with event: NSEvent) { guard enabled else { return }; progress = fraction(event); onSeek?(progress, false) }
    override func mouseDragged(with event: NSEvent) { guard enabled else { return }; progress = fraction(event); onSeek?(progress, false) }
    override func mouseUp(with event: NSEvent) { guard enabled else { return }; progress = fraction(event); onSeek?(progress, true) }
}

// Rounded translucent box over the video (resumen, avisos).
final class OverlayLabel: NSView {
    let text = NSTextField(wrappingLabelWithString: "")
    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = Theme.rgb(10, 12, 15, 0.82).cgColor
        layer?.cornerRadius = 10
        layer?.borderWidth = 1
        layer?.borderColor = Theme.border.cgColor
        text.font = Theme.mono(12); text.textColor = Theme.ink; text.isSelectable = false
        addSubview(text)
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    func fit(maxWidth: CGFloat) -> NSSize {
        text.preferredMaxLayoutWidth = maxWidth - 28
        let size = text.intrinsicContentSize
        text.frame = NSRect(x: 14, y: 10, width: min(size.width, maxWidth - 28), height: size.height)
        return NSSize(width: min(size.width, maxWidth - 28) + 28, height: size.height + 20)
    }
}
#endif
