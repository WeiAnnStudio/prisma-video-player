// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit

// El código de tiempo del reproductor. Se lee como un texto; con un solo clic se abre una cajita
// hundida con borde de color (como en DaVinci o Premiere) con el código actual en gris de fondo.
// Se escribe encima: +200 o -30 para moverse, o un código completo; Intro salta, Esc cancela.
final class TimecodeField: NSTextField, NSTextFieldDelegate {
    var onCommit: ((String) -> Void)?
    var onCancel: (() -> Void)?
    var canEdit: (() -> Bool)?
    var isTyping: Bool { currentEditor() != nil }

    override class var cellClass: AnyClass? { get { TimecodeCell.self } set {} }

    convenience init(code: String) {
        self.init(string: code)
        isBordered = false; isBezeled = false; drawsBackground = false
        isEditable = true; isSelectable = true
        font = Theme.mono(13); textColor = Theme.ink
        focusRingType = .none
        usesSingleLineMode = true; lineBreakMode = .byClipping
        cell?.isScrollable = true
        wantsLayer = true
        layer?.cornerRadius = 5
        delegate = self
        toolTip = "Clic: escribir +200, -30 o un código para ir a él (⌘G) · ⌘⇧C copia el código"
    }

    override func resetCursorRects() { addCursorRect(bounds, cursor: NSCursor.iBeam) }

    // Un solo clic abre la cajita. Sin esto, el clic recolocaba el cursor y hacían falta varios.
    override func mouseDown(with event: NSEvent) {
        if currentEditor() == nil {
            guard canEdit?() ?? true else { return }
            window?.makeFirstResponder(self)
            return
        }
        super.mouseDown(with: event)
    }

    override func becomeFirstResponder() -> Bool {
        guard canEdit?() ?? true else { return false }
        // El código actual queda de fondo, en gris: lo que se escribe empieza en blanco.
        placeholderAttributedString = NSAttributedString(string: stringValue, attributes: [.foregroundColor: Theme.muted, .font: font ?? Theme.mono(13)])
        stringValue = ""
        showBox(true)
        return super.becomeFirstResponder()
    }

    func controlTextDidEndEditing(_ obj: Notification) {
        showBox(false)
        // Si se sale sin escribir (clic en otro sitio) y el vídeo está en pausa, nadie vuelve a
        // pintar el código: se recupera el que había.
        if stringValue.isEmpty { stringValue = placeholderAttributedString?.string ?? "" }
    }

    func control(_ control: NSControl, textView: NSTextView, doCommandBy selector: Selector) -> Bool {
        if selector == #selector(NSResponder.insertNewline(_:)) { onCommit?(stringValue); return true }
        if selector == #selector(NSResponder.cancelOperation(_:)) { onCancel?(); return true }
        return false
    }

    // La cajita: fondo más oscuro (el "hueco") y borde del color de Prisma.
    private func showBox(_ shown: Bool) {
        layer?.backgroundColor = shown ? Theme.background.cgColor : NSColor.clear.cgColor
        layer?.borderColor = Theme.accent.cgColor
        layer?.borderWidth = shown ? 1.5 : 0
    }
}

// Deja aire a los lados y centra el texto en vertical, para que no toque el borde de la cajita.
final class TimecodeCell: NSTextFieldCell {
    static let sideMargin: CGFloat = 8

    private func inner(_ rect: NSRect) -> NSRect {
        let height = font.map { ceil($0.ascender - $0.descender + $0.leading) } ?? rect.height
        let dy = max(0, (rect.height - height) / 2)
        return NSRect(x: rect.minX + TimecodeCell.sideMargin, y: rect.minY + dy,
                      width: max(0, rect.width - 2 * TimecodeCell.sideMargin), height: min(rect.height, height))
    }

    override func drawingRect(forBounds rect: NSRect) -> NSRect { super.drawingRect(forBounds: inner(rect)) }

    override func edit(withFrame rect: NSRect, in controlView: NSView, editor textObj: NSText, delegate: Any?, event: NSEvent?) {
        super.edit(withFrame: inner(rect), in: controlView, editor: textObj, delegate: delegate, event: event)
    }

    override func select(withFrame rect: NSRect, in controlView: NSView, editor textObj: NSText, delegate: Any?, start selStart: Int, length selLength: Int) {
        super.select(withFrame: inner(rect), in: controlView, editor: textObj, delegate: delegate, start: selStart, length: selLength)
    }
}
#endif
