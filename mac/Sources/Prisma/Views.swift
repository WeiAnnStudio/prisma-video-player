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
// Mouse events go to the VideoStage that holds it.
final class VideoView: AVPlayerView {
    override var acceptsFirstResponder: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func keyDown(with event: NSEvent) { nextResponder?.keyDown(with: event) }
}

// Holds the video and sets the size of the image: fitted to the view by default, or zoomed
// (mouse wheel, pinch, ⌘+ / ⌘−, the zoom button) and moved by dragging when it is larger.
final class VideoStage: NSView {
    private(set) var video: NSView
    var onClick: (() -> Void)?
    var onDoubleClick: (() -> Void)?
    var onZoomChanged: ((String) -> Void)?
    /// Size of the video in pixels, as it is displayed (rotation applied). Zero: no video.
    var naturalSize: CGSize = .zero { didSet { if oldValue != naturalSize { layoutVideo() } } }
    private(set) var fit = true
    /// Screen pixels per video pixel when not fitted (1 = 100 %, pixel for pixel).
    private(set) var scale: CGFloat = 1
    private var pan = CGPoint.zero            // offset of the video centre, in points
    private var dragStart: NSPoint?
    private var panStart = CGPoint.zero
    static let maxPixels: CGFloat = 16000     // largest view AVPlayerView is asked to draw

    init(video: NSView) {
        self.video = video
        super.init(frame: .zero)
        wantsLayer = true
        layer?.masksToBounds = true
        layer?.backgroundColor = Theme.video.cgColor
        addSubview(video)
    }
    required init?(coder: NSCoder) { fatalError() }

    /// Cambia la vista del vídeo cuando se cambia de motor de reproducción.
    func setVideo(_ view: NSView) {
        guard view !== video else { return }
        video.removeFromSuperview()
        video = view
        addSubview(view)
        layoutVideo()
    }

    override var isFlipped: Bool { true }
    override var acceptsFirstResponder: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { frame.contains(point) ? self : nil }
    override func layout() { super.layout(); layoutVideo() }
    override func viewDidChangeBackingProperties() { super.viewDidChangeBackingProperties(); layoutVideo() }

    private var pixelsPerPoint: CGFloat { window?.backingScaleFactor ?? 2 }
    var hasVideo: Bool { naturalSize.width > 0 && naturalSize.height > 0 }
    var fitScale: CGFloat {
        guard hasVideo, bounds.width > 0, bounds.height > 0 else { return 1 }
        return min(bounds.width / naturalSize.width, bounds.height / naturalSize.height) * pixelsPerPoint
    }
    var currentScale: CGFloat { fit ? fitScale : scale }
    var label: String { fit ? "Ajustar" : VideoStage.percent(scale) }
    static func percent(_ s: CGFloat) -> String { "\(Int((s * 100).rounded())) %" }
    private var displayed: CGSize { CGSize(width: naturalSize.width * currentScale / pixelsPerPoint, height: naturalSize.height * currentScale / pixelsPerPoint) }
    private var movable: Bool { !fit && (displayed.width > bounds.width + 1 || displayed.height > bounds.height + 1) }

    func layoutVideo() {
        if fit || !hasVideo {
            pan = .zero
            video.frame = bounds
        } else {
            let size = displayed
            let mx = max(0, (size.width - bounds.width) / 2), my = max(0, (size.height - bounds.height) / 2)
            pan = CGPoint(x: min(mx, max(-mx, pan.x)), y: min(my, max(-my, pan.y)))
            video.frame = NSRect(x: bounds.midX - size.width / 2 + pan.x, y: bounds.midY - size.height / 2 + pan.y, width: size.width, height: size.height)
        }
        window?.invalidateCursorRects(for: self)
        onZoomChanged?(label)
    }

    override func resetCursorRects() {
        if movable { addCursorRect(bounds, cursor: dragStart == nil ? NSCursor.openHand : NSCursor.closedHand) }
    }

    func reset() { fit = true; pan = .zero; naturalSize = .zero; layoutVideo() }
    func fitVideo() { fit = true; pan = .zero; layoutVideo() }

    /// Zooms keeping the point under the mouse (anchor, in this view) where it is.
    func zoom(to newScale: CGFloat, anchor: NSPoint? = nil) {
        guard hasVideo else { return }
        let limit = VideoStage.maxPixels * pixelsPerPoint / max(naturalSize.width, naturalSize.height)
        let target = min(max(0.1, newScale), min(16, limit))
        let p = anchor ?? NSPoint(x: bounds.midX, y: bounds.midY)
        let old = displayed
        let centre = CGPoint(x: bounds.midX + pan.x, y: bounds.midY + pan.y)
        let u = (p.x - centre.x) / max(1, old.width), v = (p.y - centre.y) / max(1, old.height)
        fit = false; scale = target
        let now = displayed
        pan = CGPoint(x: p.x - bounds.midX - u * now.width, y: p.y - bounds.midY - v * now.height)
        layoutVideo()
    }

    /// One step is a quarter of a doubling. Crossing "fit" or 100 % stops there once.
    func zoomStep(_ steps: CGFloat, anchor: NSPoint? = nil) {
        guard hasVideo, steps != 0 else { return }
        let old = currentScale, fitValue = fitScale
        var next = old * pow(2, steps / 4)
        if (old < fitValue - 1e-6 && next > fitValue + 1e-6) || (old > fitValue + 1e-6 && next < fitValue - 1e-6) { fitVideo(); return }
        if (old < 1 - 1e-6 && next > 1 + 1e-6) || (old > 1 + 1e-6 && next < 1 - 1e-6) { next = 1 }
        zoom(to: next, anchor: anchor)
    }

    func toggleFitAndActualSize(anchor: NSPoint? = nil) { if fit { zoom(to: 1, anchor: anchor) } else { fitVideo() } }

    // MARK: Mouse

    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 { dragStart = nil; onDoubleClick?(); return }
        onClick?()
        if movable { dragStart = convert(event.locationInWindow, from: nil); panStart = pan; window?.invalidateCursorRects(for: self) }
    }
    override func mouseDragged(with event: NSEvent) {
        guard let start = dragStart else { return }
        let p = convert(event.locationInWindow, from: nil)
        pan = CGPoint(x: panStart.x + p.x - start.x, y: panStart.y + p.y - start.y)
        layoutVideo()
    }
    override func mouseUp(with event: NSEvent) { if dragStart != nil { dragStart = nil; window?.invalidateCursorRects(for: self) } }
    override func otherMouseDown(with event: NSEvent) { toggleFitAndActualSize(anchor: convert(event.locationInWindow, from: nil)) }

    // A mouse wheel (or ⌘ + trackpad scroll) zooms. A plain trackpad scroll moves a zoomed image.
    override func scrollWheel(with event: NSEvent) {
        guard hasVideo else { super.scrollWheel(with: event); return }
        let anchor = convert(event.locationInWindow, from: nil)
        if event.hasPreciseScrollingDeltas && !event.modifierFlags.contains(.command) {
            guard movable else { return }
            pan = CGPoint(x: pan.x + event.scrollingDeltaX, y: pan.y + event.scrollingDeltaY)
            layoutVideo(); return
        }
        var dy = event.scrollingDeltaY
        if event.isDirectionInvertedFromDevice { dy = -dy }
        if dy == 0 { return }
        zoomStep(event.hasPreciseScrollingDeltas ? dy / 40 : (dy > 0 ? 1 : -1), anchor: anchor)
    }
    override func magnify(with event: NSEvent) {
        guard hasVideo else { return }
        zoom(to: currentScale * (1 + event.magnification), anchor: convert(event.locationInWindow, from: nil))
    }
    override func smartMagnify(with event: NSEvent) { toggleFitAndActualSize(anchor: convert(event.locationInWindow, from: nil)) }
}

// The player's timecode. It reads like a label; a click makes it editable, with the code
// selected so ⌘C copies it, and typing another code and pressing Return jumps there.
final class TimecodeField: NSTextField, NSTextFieldDelegate {
    var onCommit: ((String) -> Void)?
    var onCancel: (() -> Void)?
    var canEdit: (() -> Bool)?
    var isTyping: Bool { currentEditor() != nil }

    convenience init(code: String) {
        self.init(string: code)
        isBordered = false; isBezeled = false; drawsBackground = false
        isEditable = true; isSelectable = true
        font = Theme.mono(13); textColor = Theme.ink; backgroundColor = Theme.card
        focusRingType = .none
        usesSingleLineMode = true; lineBreakMode = .byClipping
        cell?.isScrollable = true
        delegate = self
        toolTip = "Clic: copiar el código de tiempo o escribir otro para ir a él (⌘G)"
    }

    override func resetCursorRects() { addCursorRect(bounds, cursor: NSCursor.iBeam) }

    // Un solo clic basta: entra en edición con todo el código seleccionado.
    // Sin esto, el clic recolocaba el cursor y deshacía la selección (hacían falta varios clics).
    override func mouseDown(with event: NSEvent) {
        if currentEditor() == nil {
            guard canEdit?() ?? true else { return }
            window?.makeFirstResponder(self)
            currentEditor()?.selectAll(nil)
            return
        }
        super.mouseDown(with: event)
    }

    override func becomeFirstResponder() -> Bool {
        guard canEdit?() ?? true else { return false }
        let ok = super.becomeFirstResponder()
        if ok {
            drawsBackground = true
            DispatchQueue.main.async { [weak self] in self?.currentEditor()?.selectAll(nil) }
        }
        return ok
    }

    func controlTextDidEndEditing(_ obj: Notification) { drawsBackground = false }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if selector == #selector(NSResponder.insertNewline(_:)) { onCommit?(stringValue); return true }
        if selector == #selector(NSResponder.cancelOperation(_:)) { onCancel?(); return true }
        return false
    }
}

// Timeline with the play position and a tick for every note.
final class SeekBar: NSView {
    var progress: Double = 0 { didSet { needsDisplay = true } }
    var markers: [Double] = [] { didSet { needsDisplay = true } }
    var onSeek: ((Double, Bool) -> Void)?   // fraction, final (mouse up)
    var enabled = false { didSet { needsDisplay = true } }
    private(set) var dragging = false

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
    override func mouseDown(with event: NSEvent) { guard enabled else { return }; dragging = true; progress = fraction(event); onSeek?(progress, false) }
    override func mouseDragged(with event: NSEvent) { guard enabled else { return }; progress = fraction(event); onSeek?(progress, false) }
    override func mouseUp(with event: NSEvent) { guard enabled else { return }; dragging = false; progress = fraction(event); onSeek?(progress, true) }
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
