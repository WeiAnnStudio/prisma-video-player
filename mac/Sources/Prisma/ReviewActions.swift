// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import AVFoundation
import UniformTypeIdentifiers
import PrismaCore

extension PlayerWindowController {
    // MARK: Export / import

    func showExportMenu(from view: NSView) {
        let menu = NSMenu()
        for (title, selector) in [("XML para DaVinci Resolve / Premiere…", #selector(exportXMLAction)), ("Notas de Prisma (.prisma.json)…", #selector(exportJSONAction)), ("Documento CSV…", #selector(exportCSVAction))] {
            let item = NSMenuItem(title: title, action: selector, keyEquivalent: ""); item.target = self; menu.addItem(item)
        }
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: view.bounds.height + 4), in: view)
    }

    @objc func exportXMLAction() { export(kind: "xml") }
    @objc func exportJSONAction() { export(kind: "json") }
    @objc func exportCSVAction() { export(kind: "csv") }

    func export(kind: String) {
        guard let review else { notice("Abre un vídeo antes de exportar las notas."); return }
        notesPanel.commitEdit()
        let panel = NSSavePanel()
        let base = review.mediaPath.portableFileNameForUI
        switch kind {
        case "xml": panel.title = "Exportar XML con vídeo y marcadores"; panel.nameFieldStringValue = base + "-notas.xml"; panel.allowedContentTypes = [.xml]
        case "json": panel.title = "Exportar notas de Prisma"; panel.nameFieldStringValue = base + ".prisma.json"; panel.allowedContentTypes = [.json]
        default: panel.title = "Exportar notas en CSV"; panel.nameFieldStringValue = base + "-notas.csv"; panel.allowedContentTypes = [.commaSeparatedText]
        }
        panel.directoryURL = URL(fileURLWithPath: review.mediaPath).deletingLastPathComponent()
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            switch kind {
            case "xml": try review.exportXML(url)
            case "json": try review.saveForSharing(url)
            default: try review.exportCSV(url)
            }
            notice("Notas exportadas: " + url.lastPathComponent)
        } catch {
            Dialogs.ask("No se pudieron exportar las notas", error.localizedDescription, buttons: ["Cerrar"], warning: true)
        }
    }

    func importNotes() {
        guard let review else { notice("Abre el vídeo al que pertenecen las notas."); return }
        notesPanel.commitEdit()
        let panel = NSOpenPanel()
        panel.title = "Importar notas para el vídeo abierto"
        panel.allowedContentTypes = [.xml, .json]
        panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let data = try Data(contentsOf: url)
            let incoming = try (url.pathExtension.lowercased() == "xml" ? ReviewDocument.importXML(data) : ReviewDocument.parse(data))
            let theirs = incoming.mediaPath.portableFileName, ours = review.mediaPath.portableFileName
            if !theirs.isEmpty && theirs.lowercased() != ours.lowercased() {
                if Dialogs.ask("Importar notas", "El documento corresponde a «\(theirs)». ¿Añadir sus notas al vídeo abierto?", buttons: ["Añadir", "Cancelar"]) != 0 { return }
            }
            let added = try review.merge(incoming)
            saveReview()
            notesPanel.refresh(review)
            if !notesShown { toggleNotes() }
            notice("\(added) notas importadas")
        } catch {
            Dialogs.ask("No se pudieron importar las notas", error.localizedDescription, buttons: ["Cerrar"], warning: true)
        }
    }

    // MARK: Cloud folder

    func editCloudFolder() {
        guard let review else { notice("Abre un vídeo para asociarle una carpeta."); return }
        notesPanel.commitEdit()
        let video = review.mediaPath.portableFileNameForUI
        var typed = review.cloudFolder, problem = ""
        while true {
            let body = problem + "Dónde quieres las notas de «\(review.mediaPath.portableFileName)»:\n\n" +
                "AUTOMÁTICO · Carpeta del Mac: elige una carpeta que Google Drive, Dropbox u OneDrive sincronicen en este Mac. Cada vez que cambies una nota, Prisma escribe ahí «Ajustes \(video).docx» y «\(video).prisma.json», y la aplicación de la nube los sube. Sin IA y sin pedir nada.\n" +
                "Ahora: \(review.syncFolder.isEmpty ? "ninguna" : review.syncFolder)\n\n" +
                "CON IA · Enlace: pega abajo el enlace de una carpeta de Google Drive, Dropbox u OneDrive y pide a Claude o ChatGPT «Pasa a la nube las notas de \(video)». Crea un Google Doc nativo.\n\n" +
                "Prisma no se conecta a la nube en ningún caso: solo guarda la carpeta o el enlace junto a las notas."
            let field = Dialogs.field(typed)
            var buttons = ["Guardar enlace", "Elegir carpeta del Mac…", "Cancelar"]
            if !review.syncFolder.isEmpty || !review.cloudFolder.isEmpty { buttons.append("Quitar todo") }
            let choice = Dialogs.ask("Carpeta en la nube", body, buttons: buttons, accessory: field)
            typed = field.stringValue
            if choice == 2 { return }
            if choice == 3 { review.cloudFolder = ""; review.syncFolder = ""; review.syncHash = ""; break }
            if choice == 1 {
                let panel = NSOpenPanel()
                panel.title = "Carpeta sincronizada para las notas"
                panel.message = "Elige una carpeta que Google Drive, Dropbox u OneDrive sincronicen en este Mac."
                panel.canChooseDirectories = true; panel.canChooseFiles = false; panel.canCreateDirectories = true
                panel.prompt = "Usar esta carpeta"
                let start = review.syncFolder.isEmpty ? SyncDelivery.suggestedStart() : review.syncFolder
                if !start.isEmpty { panel.directoryURL = URL(fileURLWithPath: start, isDirectory: true) }
                guard panel.runModal() == .OK, let url = panel.url else { continue }
                if !SyncDelivery.isValidFolder(url.path) { problem = "Esa carpeta no sirve. Elige una carpeta, no la raíz de un disco.\n\n"; continue }
                if review.syncFolder != url.path { review.syncHash = "" }
                review.syncFolder = url.path
                if review.markers.isEmpty { notice("Carpeta guardada. Prisma escribirá ahí en cuanto añadas la primera nota.") }
                break
            }
            if typed.trimmingCharacters(in: .whitespaces).isEmpty { review.cloudFolder = ""; break }
            if let normalized = CloudFolder.normalize(typed) { review.cloudFolder = normalized.url; break }
            problem = "Ese enlace no es una carpeta de Google Drive, Dropbox ni OneDrive. Copia la dirección completa desde el navegador, por ejemplo https://drive.google.com/drive/folders/…\n\n"
        }
        saveReview()
        notesPanel.setCloud(SyncDelivery.label(review))
    }

    // MARK: DaVinci Resolve

    private func showXMLFallback(_ reason: String) {
        let body = reason + "\n\nLa solución es exportar un XML con el vídeo y las notas:\n\n" +
            "1. Pulsa «Exportar XML ahora» y guarda el archivo junto al vídeo.\n" +
            "2. En DaVinci Resolve, abre el proyecto y ve a Archivo > Importar > Línea de tiempo… (⇧⌘I).\n" +
            "3. Elige el XML y deja marcada la importación automática de los clips al panel multimedia.\n" +
            "4. Resolve crea una línea de tiempo nueva con el vídeo y cada nota como marcador. Si el clip sale sin conexión, vuelve a vincularlo con clic derecho > Relink.\n\n" +
            "Antes de importar, el proyecto debe tener la misma velocidad de fotogramas que el vídeo."
        if Dialogs.ask("Enviar a DaVinci Resolve", body, buttons: ["Exportar XML ahora", "Cerrar"]) == 0 { export(kind: "xml") }
    }

    func sendToResolve() {
        guard let review else { notice("Abre un vídeo antes de enviar sus notas."); return }
        notesPanel.commitEdit()
        if review.markers.isEmpty { notice("Este vídeo todavía no tiene notas."); return }
        var install = ResolveBridge.detect()
        if install.edition == .notInstalled { showXMLFallback("No se ha encontrado DaVinci Resolve en la carpeta Aplicaciones."); return }
        let running = NSWorkspace.shared.runningApplications.filter { ($0.bundleIdentifier ?? "").hasPrefix(ResolveBridge.studioBundle) }
        if running.isEmpty {
            Dialogs.message("Enviar a DaVinci Resolve", "Abre DaVinci Resolve con el proyecto y la línea de tiempo donde quieres las notas, y vuelve a pulsar «Enviar a Resolve».")
            return
        }
        if running.contains(where: { $0.bundleIdentifier == ResolveBridge.freeBundle }) { install.edition = .free }
        if install.edition == .free {
            showXMLFallback("Tienes la versión gratuita de DaVinci Resolve. Solo Resolve Studio permite que otra aplicación añada marcadores a la línea de tiempo abierta.")
            return
        }
        guard let scriptURL = Bundle.main.url(forResource: "prisma_resolve", withExtension: "lua"),
              let script = try? String(contentsOf: scriptURL, encoding: .utf8) else {
            notice("Falta el script de Resolve dentro de Prisma. Vuelve a descargar Prisma.", error: true); return
        }
        let work = PrismaPaths.dataFolder.appendingPathComponent("resolve", isDirectory: true)
        do { try ResolveBridge.prepare(review, work: work, script: script) }
        catch { notice("No se pudieron preparar las notas: \(error.localizedDescription)", error: true); return }
        notice("Enviando notas a DaVinci Resolve…")
        DispatchQueue.global(qos: .userInitiated).async {
            let result = ResolveBridge.run(install, work: work)
            DispatchQueue.main.async { self.showResolveResult(result) }
        }
    }

    private func showResolveResult(_ r: [String: String]) {
        let status = r["status"] ?? "", timeline = r["timeline"] ?? ""
        let title = "Enviar a DaVinci Resolve"
        func v(_ k: String) -> String { r[k] ?? "0" }
        switch status {
        case "OK":
            var summary = "\(v("added")) notas añadidas a la línea de tiempo «\(timeline)» como marcadores rojos."
            if v("present") != "0" { summary += "\n\(v("present")) ya estaban y no se han duplicado." }
            if v("moved") != "0" { summary += "\n\(v("moved")) se han movido al siguiente fotograma libre porque el suyo ya tenía un marcador." }
            if v("outside") != "0" { summary += "\n\(v("outside")) quedan después del final de la línea de tiempo y no se han añadido." }
            if v("failed") != "0" { summary += "\n\(v("failed")) no se han podido añadir." }
            notice("\(v("added")) notas enviadas a Resolve")
            Dialogs.message(title, summary + "\n\nLas posiciones se cuentan desde el primer fotograma de la línea de tiempo, como en el render revisado.")
        case "FPS":
            Dialogs.message(title, "La línea de tiempo «\(timeline)» va a \(r["timeline_fps"] ?? "?") fps y las notas a \(r["notes_fps"] ?? "?") fps. No se ha añadido ninguna nota.\n\nAbre en Resolve la línea de tiempo de este render, con su misma velocidad de fotogramas, y vuelve a enviarlas.")
        case "NO_PROJECT", "NO_TIMELINE":
            Dialogs.message(title, "DaVinci Resolve está abierto, pero no tiene ninguna línea de tiempo activa. Abre el proyecto y la línea de tiempo donde quieres las notas, y vuelve a enviarlas.")
        case "NOT_STUDIO":
            showXMLFallback("Tienes la versión gratuita de DaVinci Resolve. Solo Resolve Studio permite que otra aplicación añada marcadores a la línea de tiempo abierta.")
        default:
            var body = "Prisma no ha podido conectar con DaVinci Resolve.\n\n" +
                "En Resolve Studio, activa la conexión una sola vez: DaVinci Resolve > Ajustes (Preferencias) > Sistema > General > Scripting externo (External scripting using): Local. Guarda y reinicia Resolve.\n\n" +
                "Si tu Resolve es la versión gratuita, no admite esta conexión: exporta el XML e impórtalo como línea de tiempo."
            if let m = r["message"], !m.isEmpty { body += "\n\nDetalle: " + m }
            if Dialogs.ask(title, body, buttons: ["Cerrar", "Exportar XML"]) == 1 { export(kind: "xml") }
        }
    }

    // MARK: Claude / ChatGPT

    func connectAssistant(claude: Bool) {
        let app = claude ? "Claude" : "ChatGPT"
        guard let exe = Bundle.main.executablePath else { return }
        if exe.contains("/AppTranslocation/") || exe.hasPrefix("/Volumes/") {
            Dialogs.ask("Conectar con \(app)", "Antes de conectar, mueve Prisma a la carpeta Aplicaciones y ábrelo desde ahí. Ahora macOS lo está ejecutando desde una ubicación temporal, y \(app) no podría encontrarlo después.", buttons: ["Entendido"], warning: true)
            return
        }
        let file = claude ? AiConnect.claudeConfigFile : AiConnect.chatGptConfigFile
        let body = "Prisma se añadirá a \(app) como herramienta local de solo lectura. \(app) abrirá Prisma en segundo plano cuando necesite tus notas; no hace falta tener Prisma abierto ni ejecutar nada más.\n\n" +
            "No tienes que escribir contraseñas ni claves: la conexión ocurre dentro de tu Mac. Para guardar el documento en Google Drive, Dropbox u OneDrive, \(app) usa el conector de ese servicio que ya tengas activado en la propia app.\n\n" +
            "Se modificará este archivo de configuración (se guarda una copia del original):\n\(file.path)\n\nSi mueves Prisma a otra carpeta, vuelve a conectar."
        if Dialogs.ask("Conectar con \(app)", body, buttons: ["Conectar", "Cancelar"]) != 0 { return }
        do {
            let current = AiConnect.readOrEmpty(file)
            let merged: String
            if claude { merged = try AiConnect.mergeClaudeConfig(current, executable: exe) } else { merged = AiConnect.mergeChatGptConfig(current, executable: exe) }
            try AiConnect.writeWithBackup(file, merged)
        } catch {
            Dialogs.ask("Conectar con \(app)", "No se pudo modificar la configuración de \(app): \(error.localizedDescription)\n\nPuedes añadir el servidor a mano: nombre «prisma», tipo STDIO, comando:\n\(exe)\ncon el argumento --mcp", buttons: ["Cerrar"], warning: true)
            return
        }
        Dialogs.message("Conectado con \(app)", "Listo. Cierra \(app) por completo (⌘Q) y vuelve a abrirlo.\n\n" +
            "Después, en un chat nuevo, pide por ejemplo: «Pasa a la nube las notas de Corto 12». \(app) leerá las notas de Prisma y creará el documento «Ajustes Corto 12» en la carpeta que asociaste al vídeo.\n\n" +
            (claude ? "" : "En cuentas de empresa, el administrador puede tener desactivados los servidores locales. Si Prisma no aparece en Ajustes > MCP servers, consúltalo con él.\n\n") +
            "Asegúrate de tener activado en \(app) el conector de Google Drive, Dropbox u OneDrive, según dónde esté la carpeta.")
    }

    func showAssistantHelp() {
        Dialogs.text("Notas en la nube con IA",
            "1. En el panel de notas, pulsa «Carpeta en la nube» y pega el enlace de la carpeta de Google Drive, Dropbox u OneDrive para ese vídeo. Es el único paso manual.\n\n" +
            "2. Una sola vez: menú IA > Conectar con Claude o con ChatGPT, y reinicia esa app. No pide contraseñas: la app abre Prisma en segundo plano en tu Mac.\n\n" +
            "3. En esa app, activa el conector del servicio donde está la carpeta (Google Drive, Dropbox u OneDrive).\n\n" +
            "4. Pide en un chat: «Pasa a la nube las notas de <nombre del vídeo>». La IA crea el documento «Ajustes <vídeo>» con el título en negrita y una línea por nota con su código de tiempo. Si el documento ya existe, añade solo lo nuevo.\n\n" +
            "Qué ve la IA: el nombre del archivo (nunca la ruta de tu Mac), los FPS, la carpeta asociada y el texto de las notas. Prisma no puede borrar ni cambiar nada desde la IA.")
    }

    // MARK: Information

    func showInfo() {
        guard let url = player.currentItem.flatMap({ ($0.asset as? AVURLAsset)?.url }) else { notice("Abre un vídeo para consultar su información."); return }
        var d = "ARCHIVO\n\(url.path)\n\n"
        d += "Vídeo: \(info.codec.isEmpty ? "No indicado" : info.codec) (\(info.fourCC))\nResolución: \(info.width) × \(info.height)\n"
        d += "Profundidad del archivo: \(info.bits.isEmpty ? "No indicada" : info.bits + " bits")\n"
        d += "Fotogramas por segundo: \(String(format: "%.3f", info.fps))\n"
        d += "Primarias: \(MediaInfo.readable(info.primaries))\nTransferencia: \(MediaInfo.readable(info.transfer))\nMatriz: \(MediaInfo.readable(info.matrix))\n"
        d += "Rango dinámico: \(info.isHDR ? "HDR" : "SDR")\n"
        d += "Audio: \(info.audioCodec.isEmpty ? "Sin audio" : "\(info.audioCodec) · \(info.audioChannels) canales · \(info.audioRate) Hz")\n"
        d += "Duración: \(String(format: "%.2f", info.duration)) s\n\n"
        d += "SALIDA\nDecodificación: VideoToolbox (Apple), por hardware cuando el Mac lo admite.\n"
        d += "HDR: macOS lo muestra con rango extendido (EDR) en pantallas compatibles, como las Liquid Retina XDR; en las demás lo adapta a SDR.\n\n"
        d += "Los bits indicados pertenecen al archivo. La salida física depende de la pantalla y de los ajustes de macOS. Esta aplicación no mide la señal del monitor."
        Dialogs.text("Información del vídeo", d)
    }

    func showShortcuts() {
        Dialogs.text("Atajos de teclado",
            "⌘O   Abrir uno o varios vídeos\n⇧⌘O   Añadir a la lista\nEspacio   Reproducir / pausar\n← / →   Fotograma anterior / siguiente\n, / .   Fotograma anterior / siguiente\n⇧← / ⇧→   Retroceder / avanzar 5 segundos\n" +
            "J / K / L   Retroceder / pausar / avanzar\nRepite J o L para acelerar hasta 16×; la tecla contraria frena.\n⇧J / ⇧L   Revisión a media velocidad\nI   Resumen sobre el vídeo\n⌘I   Información completa\n" +
            "M   Añadir nota en este fotograma\nN   Mostrar notas\n⌘↩   Guardar nota en edición\nEsc   Cancelar edición / salir de pantalla completa\n↑ / ↓   Volumen\n⇧⌘M   Silenciar\nInicio   Volver al principio\nS   Detener\nF   Pantalla completa\n⇧⌘R   Enviar notas a DaVinci Resolve\n\n" +
            "Doble clic sobre el vídeo: pantalla completa.")
    }

    func showAbout() {
        let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.4"
        Dialogs.text("Acerca de Prisma",
            "PRISMA  ·  Versión \(version) para Mac\nReproductor y revisor de vídeo, en español.\n\n" +
            "Prisma no se conecta a internet ni envía datos. Solo abre los archivos que eliges tú.\n\n" +
            "En Mac usa el motor de vídeo de Apple (AVFoundation y VideoToolbox): ProRes, HEVC y H.264, incluidos 10 bits y HDR. Las notas son las mismas que en Prisma para Windows.\n\n" +
            "Código abierto: GPL-3.0-o-posterior. Sin garantía.\n\n" +
            "Esta versión no está firmada por Apple. La primera vez, macOS pide permiso en Ajustes del Sistema > Privacidad y seguridad > «Abrir igualmente».")
    }
}

extension String {
    var portableFileNameForUI: String {
        let name = self.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        return (name as NSString).deletingPathExtension
    }
}
#endif
