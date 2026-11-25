// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit

enum PrismaApp {
    static func start() {
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.delegate = delegate
        app.setActivationPolicy(.regular)
        withExtendedLifetime(delegate) { app.run() }
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var controller: PlayerWindowController!
    private var pending: [URL] = []
    private var launched = false

    func applicationWillFinishLaunching(_ notification: Notification) {
        NSApp.appearance = NSAppearance(named: .darkAqua)
        controller = PlayerWindowController()
        NSApp.mainMenu = buildMenu()
    }

    func applicationDidFinishLaunching(_ notification: Notification) {
        controller.showWindow(nil)
        controller.window?.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        launched = true
        let fromArguments = CommandLine.arguments.dropFirst().filter { !$0.hasPrefix("-") }.map { URL(fileURLWithPath: $0) }
        let files = pending + fromArguments
        pending = []
        if !files.isEmpty { controller.open(files, append: false) }
    }

    // Videos opened from Finder (double click, "Abrir con", dragging onto the Dock icon).
    // macOS keeps a single Prisma: the new video replaces the current one; several form a list.
    func application(_ application: NSApplication, open urls: [URL]) {
        if launched { controller.open(urls, append: false); controller.window?.makeKeyAndOrderFront(nil) }
        else { pending += urls }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
    func applicationSupportsSecureRestorableState(_ app: NSApplication) -> Bool { true }

    // MARK: Menus

    private func item(_ title: String, _ key: String = "", _ mods: NSEvent.ModifierFlags = [.command], _ action: @escaping () -> Void) -> NSMenuItem {
        let item = BlockMenuItem(title: title, keyEquivalent: key, action: action)
        item.keyEquivalentModifierMask = mods
        return item
    }

    private func submenu(_ title: String, _ items: [NSMenuItem]) -> NSMenuItem {
        let top = NSMenuItem(title: title, action: nil, keyEquivalent: "")
        let menu = NSMenu(title: title)
        for i in items { menu.addItem(i) }
        top.submenu = menu
        return top
    }

    private func buildMenu() -> NSMenu {
        let c = controller!
        let main = NSMenu()

        main.addItem(submenu("Prisma", [
            item("Acerca de Prisma") { c.showAbout() },
            .separator(),
            NSMenuItem(title: "Ocultar Prisma", action: #selector(NSApplication.hide(_:)), keyEquivalent: "h"),
            {
                let i = NSMenuItem(title: "Ocultar otros", action: #selector(NSApplication.hideOtherApplications(_:)), keyEquivalent: "h")
                i.keyEquivalentModifierMask = [.command, .option]; return i
            }(),
            NSMenuItem(title: "Mostrar todo", action: #selector(NSApplication.unhideAllApplications(_:)), keyEquivalent: ""),
            .separator(),
            NSMenuItem(title: "Salir de Prisma", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q"),
        ]))

        let recent = NSMenuItem(title: "Abrir reciente", action: nil, keyEquivalent: "")
        let recentMenu = NSMenu(title: "Abrir reciente")
        recentMenu.addItem(NSMenuItem(title: "Borrar menú", action: #selector(NSDocumentController.clearRecentDocuments(_:)), keyEquivalent: ""))
        recent.submenu = recentMenu
        main.addItem(submenu("Archivo", [
            item("Abrir vídeo…", "o") { c.chooseFiles(append: false) },
            item("Añadir a la lista…", "o", [.command, .shift]) { c.chooseFiles(append: true) },
            recent,
            .separator(),
            NSMenuItem(title: "Cerrar ventana", action: #selector(NSWindow.performClose(_:)), keyEquivalent: "w"),
        ]))

        main.addItem(submenu("Edición", [
            NSMenuItem(title: "Deshacer", action: Selector(("undo:")), keyEquivalent: "z"),
            {
                let i = NSMenuItem(title: "Rehacer", action: Selector(("redo:")), keyEquivalent: "z")
                i.keyEquivalentModifierMask = [.command, .shift]; return i
            }(),
            .separator(),
            NSMenuItem(title: "Cortar", action: #selector(NSText.cut(_:)), keyEquivalent: "x"),
            NSMenuItem(title: "Copiar", action: #selector(NSText.copy(_:)), keyEquivalent: "c"),
            NSMenuItem(title: "Pegar", action: #selector(NSText.paste(_:)), keyEquivalent: "v"),
            NSMenuItem(title: "Seleccionar todo", action: #selector(NSText.selectAll(_:)), keyEquivalent: "a"),
        ]))

        let playlist = NSMenuItem(title: "Lista de reproducción", action: nil, keyEquivalent: "")
        let playlistMenu = NSMenu(title: "Lista de reproducción")
        playlistMenu.delegate = c
        playlist.submenu = playlistMenu
        main.addItem(submenu("Reproducción", [
            item("Reproducir / pausar    Espacio") { c.togglePlay() },
            item("Fotograma siguiente    →") { c.step(1) },
            item("Fotograma anterior    ←") { c.step(-1) },
            item("Avanzar 5 segundos    ⇧→") { c.seekRelative(5) },
            item("Retroceder 5 segundos    ⇧←") { c.seekRelative(-5) },
            .separator(),
            item("Retroceder / acelerar    J") { c.startShuttle(-1, slow: false) },
            item("Pausar    K") { c.pauseShuttle() },
            item("Avanzar / acelerar    L") { c.startShuttle(1, slow: false) },
            item("Media velocidad hacia atrás    ⇧J") { c.startShuttle(-1, slow: true) },
            item("Media velocidad    ⇧L") { c.startShuttle(1, slow: true) },
            item("Volver al principio    Inicio") { c.pauseShuttle(); c.seek(frame: 0) },
            item("Detener    S") { c.stop() },
            .separator(),
            item("Subir volumen    ↑") { c.changeVolume(0.05) },
            item("Bajar volumen    ↓") { c.changeVolume(-0.05) },
            item("Silenciar", "m", [.command, .shift]) { c.toggleMute() },
            .separator(),
            item("Siguiente de la lista", "]", [.command]) { c.play(at: c.index + 1) },
            item("Anterior de la lista", "[", [.command]) { c.play(at: c.index - 1) },
            playlist,
        ]))

        main.addItem(submenu("Revisión", [
            item("Añadir nota en este fotograma    M") { c.addMarker() },
            item("Mostrar notas    N") { c.toggleNotes() },
            item("Importar notas (XML / JSON)…") { c.importNotes() },
            item("Exportar XML para DaVinci Resolve / Premiere…") { c.export(kind: "xml") },
            item("Exportar notas de Prisma (.prisma.json)…") { c.export(kind: "json") },
            item("Exportar CSV…") { c.export(kind: "csv") },
            .separator(),
            item("Enviar notas a DaVinci Resolve", "r", [.command, .shift]) { c.sendToResolve() },
            .separator(),
            item("Ir a un código de tiempo…", "g") { c.beginTimecodeEdit() },
            item("Copiar código de tiempo", "c", [.command, .shift]) { c.copyTimecode() },
            item("Carpeta de Google Drive para este vídeo…") { c.editCloudFolder() },
        ]))

        main.addItem(submenu("Visualización", [
            item("Resumen sobre el vídeo    I") { c.toggleSummary() },
            item("Información del vídeo", "i") { c.showInfo() },
            item("Pantalla completa", "f", [.command, .control]) { c.toggleFullscreen() },
            .separator(),
            item("Acercar la imagen", "+") { c.zoomStep(1) },
            item("Alejar la imagen", "-") { c.zoomStep(-1) },
            item("Ajustar la imagen a la ventana", "0") { c.fitVideo() },
            item("Imagen al 100 % (píxel a píxel)", "1") { c.zoom(to: 1) },
        ]))

        let window = submenu("Ventana", [
            NSMenuItem(title: "Minimizar", action: #selector(NSWindow.performMiniaturize(_:)), keyEquivalent: "m"),
            NSMenuItem(title: "Zoom", action: #selector(NSWindow.performZoom(_:)), keyEquivalent: ""),
        ])
        main.addItem(window)
        NSApp.windowsMenu = window.submenu

        main.addItem(submenu("Ayuda", [
            item("Atajos de teclado") { c.showShortcuts() },
        ]))
        return main
    }
}

// Menu item that runs a closure, so menus don't need one @objc method per command.
final class BlockMenuItem: NSMenuItem {
    private let block: () -> Void
    init(title: String, keyEquivalent: String, action: @escaping () -> Void) {
        block = action
        super.init(title: title, action: #selector(run), keyEquivalent: keyEquivalent)
        target = self
    }
    required init(coder: NSCoder) { fatalError() }
    @objc private func run() { block() }
}
#endif
