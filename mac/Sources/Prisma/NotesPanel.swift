// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import PrismaCore

class FlippedView: NSView {
    override var isFlipped: Bool { true }
    var fill: NSColor? { didSet { wantsLayer = true; layer?.backgroundColor = fill?.cgColor } }
}

private final class MarkerCell: NSTableCellView {
    let code = Theme.label("", size: 12, color: Theme.accent)
    let name = Theme.label("", size: 13, weight: .semibold)
    let note = NSTextField(wrappingLabelWithString: "")

    override init(frame: NSRect) {
        super.init(frame: frame)
        code.font = Theme.mono(12)
        note.font = .systemFont(ofSize: 12); note.textColor = Theme.muted
        note.maximumNumberOfLines = 2; note.lineBreakMode = .byTruncatingTail
        note.cell?.truncatesLastVisibleLine = true
        for v in [code, name, note] { addSubview(v) }
    }
    required init?(coder: NSCoder) { fatalError() }
    override var isFlipped: Bool { true }
    override func layout() {
        super.layout()
        let w = bounds.width - 24
        code.frame = NSRect(x: 12, y: 8, width: 110, height: 16)
        name.frame = NSRect(x: 124, y: 7, width: max(10, w - 112), height: 18)
        note.frame = NSRect(x: 12, y: 28, width: w, height: 34)
    }
}

private final class MarkerRow: NSTableRowView {
    override func drawBackground(in dirtyRect: NSRect) {
        Theme.panel.setFill(); bounds.fill()
        let card = bounds.insetBy(dx: 8, dy: 4)
        (isSelected ? Theme.accentSoft : Theme.card).setFill()
        NSBezierPath(roundedRect: card, xRadius: 8, yRadius: 8).fill()
    }
    override func drawSelection(in dirtyRect: NSRect) {}
}

// Review notes beside the video: list, editor and the team actions (cloud, Resolve, import, export).
final class NotesPanel: FlippedView, NSTableViewDataSource, NSTableViewDelegate {
    var onAdd: (() -> Void)?
    var onImport: (() -> Void)?
    var onExport: ((NSView) -> Void)?
    var onCloud: (() -> Void)?
    var onResolve: (() -> Void)?
    var onChanged: (() -> Void)?
    var onJump: ((ReviewMarker) -> Void)?
    var onEditingEnded: (() -> Void)?
    /// Timecode where the player is now, for "+ TC actual" (⌘T) in the editor.
    var currentTimecode: (() -> String)?

    private(set) var doc: ReviewDocument?
    private var sorted: [ReviewMarker] = []
    private(set) var editing: ReviewMarker?
    private var editingIsNew = false

    private let heading = Theme.label("Notas", size: 18, weight: .semibold)
    private let subtitle = Theme.label("Revisión con código de tiempo", size: 12, color: Theme.muted)
    private let count = Theme.label("0", size: 12, weight: .semibold, color: Theme.accent)
    private lazy var add: FlatButton = FlatButton("Añadir nota", symbol: "plus", style: .accent, tip: "Añadir nota en este fotograma (M)") { [weak self] in self?.onAdd?() }
    private let table = NSTableView()
    private let scrollView = NSScrollView()
    private let empty = NSTextField(wrappingLabelWithString: "")
    private let hint = Theme.label("Clic: ir a la nota · Doble clic: editar", size: 11, color: Theme.muted)
    private lazy var cloud: FlatButton = FlatButton("Carpeta de Drive", symbol: "folder", style: .outlined, tip: "Carpeta de Google Drive sincronizada para las notas de este vídeo") { [weak self] in self?.onCloud?() }
    private lazy var resolve: FlatButton = FlatButton("Enviar a Resolve", symbol: "paperplane", style: .outlined, tip: "Enviar las notas a la línea de tiempo abierta en DaVinci Resolve (⇧⌘R)") { [weak self] in self?.onResolve?() }
    private lazy var importButton: FlatButton = FlatButton("Importar", symbol: "square.and.arrow.down", style: .outlined, tip: "Importar notas (XML / JSON)") { [weak self] in self?.onImport?() }
    private lazy var exportButton: FlatButton = FlatButton("Exportar", symbol: "square.and.arrow.up", style: .outlined, tip: "Exportar notas (XML / JSON / CSV)") { [weak self] in
        guard let self else { return }
        self.onExport?(self.exportButton)
    }

    // Editor
    private let editor = FlippedView()
    private let editorTitle = Theme.label("Nueva nota", size: 18, weight: .semibold)
    private let editorCode = Theme.label("", size: 13, color: Theme.accent)
    private let nameLabel = Theme.label("Título", size: 12, color: Theme.muted)
    private let nameField = NSTextField(string: "")
    private let noteLabel = Theme.label("Nota", size: 12, color: Theme.muted)
    private let noteScroll = NSTextView.scrollableTextView()
    private var noteView: NSTextView { noteScroll.documentView as! NSTextView }
    private lazy var saveButton: FlatButton = FlatButton("Guardar", symbol: "checkmark", style: .accent, tip: "Guardar nota (⌘↩)") { [weak self] in self?.commitEdit() }
    private lazy var cancelButton: FlatButton = FlatButton("Cancelar", style: .outlined, tip: "Cancelar (Esc)") { [weak self] in self?.cancelEdit() }
    private lazy var insertCode: FlatButton = FlatButton("+ TC actual", style: .outlined, tip: "Insertar en la nota el código de tiempo actual del reproductor (⌘T)") { [weak self] in self?.insertTimecode() }
    private lazy var deleteButton: FlatButton = FlatButton("Eliminar", symbol: "trash", style: .plain, tip: "Eliminar esta nota") { [weak self] in self?.deleteEditing() }

    override init(frame: NSRect) {
        super.init(frame: frame)
        fill = Theme.panel
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("nota"))
        table.addTableColumn(column)
        table.headerView = nil
        table.rowHeight = 74
        table.intercellSpacing = .zero
        table.backgroundColor = Theme.panel
        table.dataSource = self; table.delegate = self
        table.refusesFirstResponder = true   // J/K/L and the arrows stay with the player
        table.target = self
        table.action = #selector(rowClicked)
        table.doubleAction = #selector(rowDoubleClicked)
        table.selectionHighlightStyle = .regular
        scrollView.documentView = table
        scrollView.hasVerticalScroller = true
        scrollView.drawsBackground = false
        scrollView.borderType = .noBorder
        scrollView.scrollerStyle = .overlay
        empty.alignment = .center; empty.textColor = Theme.muted; empty.font = .systemFont(ofSize: 13)

        nameField.font = .systemFont(ofSize: 13); nameField.textColor = Theme.ink
        nameField.drawsBackground = true; nameField.backgroundColor = Theme.background
        nameField.isBezeled = true; nameField.bezelStyle = .roundedBezel
        noteView.isRichText = false; noteView.font = .systemFont(ofSize: 13); noteView.textColor = Theme.ink
        noteView.backgroundColor = Theme.background; noteView.insertionPointColor = Theme.accent
        noteView.allowsUndo = true; noteView.textContainerInset = NSSize(width: 6, height: 8)
        noteView.isAutomaticQuoteSubstitutionEnabled = false; noteView.isAutomaticDashSubstitutionEnabled = false
        noteScroll.borderType = .noBorder; noteScroll.drawsBackground = true; noteScroll.backgroundColor = Theme.background
        noteScroll.wantsLayer = true; noteScroll.layer?.cornerRadius = 8
        editorCode.font = Theme.mono(13)
        saveButton.keyEquivalent = "\r"; saveButton.keyEquivalentModifierMask = [.command]
        cancelButton.keyEquivalent = "\u{1b}"
        insertCode.keyEquivalent = "t"; insertCode.keyEquivalentModifierMask = [.command]
        editor.isHidden = true
        for v in [editorTitle, editorCode, nameLabel, nameField, noteLabel, insertCode, noteScroll, saveButton, cancelButton, deleteButton] as [NSView] { editor.addSubview(v) }
        for v in [heading, subtitle, count, add, scrollView, empty, hint, cloud, resolve, importButton, exportButton, editor] as [NSView] { addSubview(v) }
        refresh(nil)
    }
    required init?(coder: NSCoder) { fatalError() }

    override func layout() {
        super.layout()
        let w = bounds.width, h = bounds.height, pad: CGFloat = 18, inner = w - pad * 2
        heading.frame = NSRect(x: pad, y: 18, width: inner - 50, height: 26)
        count.frame = NSRect(x: w - pad - 40, y: 22, width: 40, height: 18); count.alignment = .right
        subtitle.frame = NSRect(x: pad, y: 46, width: inner, height: 16)
        add.frame = NSRect(x: pad, y: 72, width: inner, height: 34)
        let half = (inner - 10) / 2
        importButton.frame = NSRect(x: pad, y: h - 46, width: half, height: 32)
        exportButton.frame = NSRect(x: pad + half + 10, y: h - 46, width: half, height: 32)
        cloud.frame = NSRect(x: pad, y: h - 86, width: half, height: 32)
        resolve.frame = NSRect(x: pad + half + 10, y: h - 86, width: half, height: 32)
        hint.frame = NSRect(x: pad, y: h - 112, width: inner, height: 16)
        scrollView.frame = NSRect(x: 0, y: 116, width: w, height: max(40, h - 116 - 120))
        empty.frame = NSRect(x: pad, y: 150, width: inner, height: 90)
        editor.frame = bounds
        editorTitle.frame = NSRect(x: pad, y: 18, width: inner, height: 26)
        editorCode.frame = NSRect(x: pad, y: 48, width: inner, height: 18)
        nameLabel.frame = NSRect(x: pad, y: 80, width: inner, height: 16)
        nameField.frame = NSRect(x: pad, y: 100, width: inner, height: 26)
        noteLabel.frame = NSRect(x: pad, y: 138, width: max(0, inner - 112), height: 16)
        insertCode.frame = NSRect(x: pad + inner - 104, y: 131, width: 104, height: 24)
        noteScroll.frame = NSRect(x: pad, y: 158, width: inner, height: max(80, h - 158 - 66))
        let third = (inner - 16) / 3
        deleteButton.frame = NSRect(x: pad, y: h - 48, width: third, height: 32)
        cancelButton.frame = NSRect(x: pad + third + 8, y: h - 48, width: third, height: 32)
        saveButton.frame = NSRect(x: pad + (third + 8) * 2, y: h - 48, width: third, height: 32)
    }

    func refresh(_ document: ReviewDocument?) {
        doc = document
        sorted = (document?.markers ?? []).sorted { $0.frame < $1.frame }
        table.reloadData()
        count.stringValue = "\(sorted.count)"
        let has = document != nil
        for b in [add, importButton, exportButton, cloud, resolve] { b.isEnabled = has }
        setCloud(SyncDelivery.label(document))
        empty.stringValue = has ? "Tu revisión empieza aquí\n\nPulsa M sobre un fotograma\npara añadir tu primera nota." : "Todo listo para revisar\n\nAbre un vídeo para añadir\nnotas a sus fotogramas."
        empty.isHidden = !sorted.isEmpty
        hint.isHidden = sorted.isEmpty
    }

    func setCloud(_ service: String) {
        cloud.setLabel(service.isEmpty ? "Carpeta de Drive" : service)
        cloud.active = !service.isEmpty
    }

    // MARK: Table

    func numberOfRows(in tableView: NSTableView) -> Int { sorted.count }

    func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? { MarkerRow() }

    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
        let id = NSUserInterfaceItemIdentifier("celda")
        let cell = tableView.makeView(withIdentifier: id, owner: nil) as? MarkerCell ?? {
            let c = MarkerCell(frame: .zero); c.identifier = id; return c
        }()
        let m = sorted[row]
        cell.code.stringValue = doc?.timecode(m.frame) ?? ""
        cell.name.stringValue = m.name
        cell.note.stringValue = m.note.isEmpty ? "Sin texto" : m.note
        return cell
    }

    @objc private func rowClicked() {
        let row = table.clickedRow
        if row >= 0 && row < sorted.count { onJump?(sorted[row]) }
    }

    @objc private func rowDoubleClicked() {
        let row = table.clickedRow
        if row >= 0 && row < sorted.count { onJump?(sorted[row]); beginEdit(sorted[row], isNew: false) }
    }

    // MARK: Editor

    var isEditing: Bool { editing != nil }

    func beginEdit(_ marker: ReviewMarker, isNew: Bool) {
        guard let doc else { return }
        editing = marker; editingIsNew = isNew
        editorTitle.stringValue = isNew ? "Nueva nota" : "Editar nota"
        editorCode.stringValue = doc.timecode(marker.frame) + "  ·  fotograma \(marker.frame)"
        nameField.stringValue = marker.name
        noteView.string = marker.note
        deleteButton.isHidden = isNew
        editor.isHidden = false
        for v in [heading, subtitle, count, add, scrollView, empty, hint, cloud, resolve, importButton, exportButton] as [NSView] { v.isHidden = true }
        window?.makeFirstResponder(noteView)
        noteView.selectAll(nil)
    }

    func commitEdit() {
        guard let marker = editing, let doc else { return }
        let name = nameField.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
        marker.name = String((name.isEmpty ? "Nota" : name).prefix(ReviewDocument.maxNameLength))
        marker.note = String(noteView.string.prefix(ReviewDocument.maxNoteLength))
        if editingIsNew { doc.markers.append(marker) }
        endEdit()
        onChanged?()
    }

    func cancelEdit() { if editing != nil { endEdit() } }

    private func deleteEditing() {
        guard let marker = editing, let doc else { return }
        doc.markers.removeAll { $0 === marker }
        endEdit()
        onChanged?()
    }

    // Writes the player's current timecode where the cursor is in the note (at the end while
    // the whole note is still selected), so a note can say "move this cut to 00:00:03:10".
    private func insertTimecode() {
        guard editing != nil, var code = currentTimecode?(), !code.isEmpty else { return }
        let text = noteView.string as NSString
        var range = noteView.selectedRange()
        if range.location == NSNotFound || range.location > text.length || (range.length == text.length && text.length > 0) {
            range = NSRange(location: text.length, length: 0)
        }
        if range.location > 0, range.length == 0 {
            let previous = text.substring(with: NSRange(location: range.location - 1, length: 1))
            if previous.rangeOfCharacter(from: .whitespacesAndNewlines) == nil { code = " " + code }
        }
        window?.makeFirstResponder(noteView)
        noteView.setSelectedRange(range)
        noteView.insertText(code + " ", replacementRange: range)
    }

    private func endEdit() {
        editing = nil
        editor.isHidden = true
        for v in [heading, subtitle, count, add, scrollView, cloud, resolve, importButton, exportButton] as [NSView] { v.isHidden = false }
        refresh(doc)
        onEditingEnded?()
    }
}
#endif
