// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import AVFoundation
import AVKit
import UniformTypeIdentifiers
import PrismaCore

final class PlayerWindowController: NSWindowController, NSWindowDelegate, NSMenuDelegate {
    let root = RootView(frame: NSRect(x: 0, y: 0, width: 1280, height: 760))
    let videoView = VideoView()
    lazy var stage = VideoStage(video: videoView)
    let player = AVPlayer()
    let notesPanel = NotesPanel(frame: .zero)
    let summary = OverlayLabel(frame: .zero)
    private let bar = FlippedView()
    private let seekBar = SeekBar()
    private let timeLabel = Theme.label("", size: 13)
    private let timecodeField = TimecodeField(code: "--:--:--:--")
    private lazy var zoomButton: FlatButton = FlatButton("Ajustar", symbol: "plus.magnifyingglass", style: .outlined, tip: "Tamaño de la imagen · Rueda o pellizco sobre el vídeo, ⌘+ / ⌘− · ⌘0 ajustar · ⌘1 al 100 %") { [weak self] in self?.showZoomMenu() }
    private let noticeLabel = Theme.label("", size: 12, color: Theme.muted)
    private let volume = NSSlider(value: 1, minValue: 0, maxValue: 1, target: nil, action: nil)
    private lazy var openButton: FlatButton = FlatButton("Abrir", symbol: "folder", style: .outlined, tip: "Abrir vídeos (⌘O)") { [weak self] in self?.chooseFiles(append: false) }
    private lazy var backButton: FlatButton = FlatButton("", symbol: "backward.frame.fill", tip: "Fotograma anterior (←)") { [weak self] in self?.step(-1) }
    private lazy var playButton: FlatButton = FlatButton("", symbol: "play.fill", tip: "Reproducir / pausar (Espacio)") { [weak self] in self?.togglePlay() }
    private lazy var forwardButton: FlatButton = FlatButton("", symbol: "forward.frame.fill", tip: "Fotograma siguiente (→)") { [weak self] in self?.step(1) }
    private lazy var notesButton: FlatButton = FlatButton("Notas  N", symbol: "text.bubble", style: .outlined, tip: "Mostrar notas de revisión (N)") { [weak self] in self?.toggleNotes() }
    private lazy var infoButton: FlatButton = FlatButton("", symbol: "info.circle", tip: "Información del vídeo (⌘I)") { [weak self] in self?.showInfo() }
    private let speaker = NSImageView()

    var playlist: [URL] = []
    var index = -1
    var info = MediaInfo()
    var review: ReviewDocument?
    var reviewURL: URL?
    var notesShown = false
    var summaryShown = false
    private var shuttle = Shuttle()
    private var reverseTimer: Timer?
    private var reverseSeeking = false
    private var timeObserver: Any?
    private var endObserver: NSObjectProtocol?
    private var statusObserver: NSKeyValueObservation?
    private var rateObserver: NSKeyValueObservation?
    private var loadToken = 0
    private var noticeClear: DispatchWorkItem?

    init() {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 1280, height: 760),
                              styleMask: [.titled, .closable, .miniaturizable, .resizable], backing: .buffered, defer: false)
        window.title = "Prisma"
        window.appearance = NSAppearance(named: .darkAqua)
        window.backgroundColor = Theme.background
        window.minSize = NSSize(width: 760, height: 460)
        window.collectionBehavior = [.fullScreenPrimary]
        window.isReleasedWhenClosed = false
        super.init(window: window)
        window.delegate = self
        window.contentView = root
        if !window.setFrameUsingName("PrismaPrincipal") { window.center() }
        window.setFrameAutosaveName("PrismaPrincipal")
        build()
    }
    required init?(coder: NSCoder) { fatalError() }

    // MARK: Layout

    private func build() {
        videoView.player = player
        videoView.controlsStyle = .none
        videoView.wantsLayer = true
        videoView.layer?.backgroundColor = Theme.video.cgColor
        stage.onDoubleClick = { [weak self] in self?.toggleFullscreen() }
        stage.onClick = { [weak self] in self?.window?.makeFirstResponder(self?.root) }
        stage.onZoomChanged = { [weak self] label in
            guard let self, self.zoomButton.title != label else { return }
            self.zoomButton.setLabel(label)
        }
        timecodeField.canEdit = { [weak self] in self?.review != nil }
        timecodeField.onCommit = { [weak self] text in self?.commitTimecode(text) }
        timecodeField.onCancel = { [weak self] in self?.endTimecodeEdit() }
        let tcMenu = NSMenu()
        tcMenu.addItem(BlockMenuItem(title: "Copiar código de tiempo", keyEquivalent: "") { [weak self] in self?.copyTimecode() })
        tcMenu.addItem(BlockMenuItem(title: "Ir a un código de tiempo…", keyEquivalent: "") { [weak self] in self?.beginTimecodeEdit() })
        timecodeField.menu = tcMenu
        notesPanel.currentTimecode = { [weak self] in
            guard let self, let review = self.review else { return "" }
            return review.timecode(self.currentFrame)
        }
        player.actionAtItemEnd = .pause
        player.preventsDisplaySleepDuringVideoPlayback = true
        let saved = UserDefaults.standard.object(forKey: "volumen") as? Double ?? 1
        player.volume = Float(saved); volume.doubleValue = saved
        volume.target = self; volume.action = #selector(volumeChanged)
        volume.controlSize = .small
        speaker.image = NSImage(systemSymbolName: "speaker.wave.2.fill", accessibilityDescription: "Volumen")
        speaker.contentTintColor = Theme.muted
        timeLabel.font = Theme.mono(13)
        bar.fill = Theme.panel
        seekBar.onSeek = { [weak self] fraction, final in self?.seek(fraction: fraction, exact: final) }
        summary.isHidden = true
        for v in [seekBar, openButton, backButton, playButton, forwardButton, timecodeField, timeLabel, noticeLabel, speaker, volume, zoomButton, notesButton, infoButton] as [NSView] { bar.addSubview(v) }
        for v in [stage, summary, bar, notesPanel] as [NSView] { root.addSubview(v) }
        root.onLayout = { [weak self] in self?.layoutPlayer() }
        root.onKey = { [weak self] event in self?.handleKey(event) ?? false }
        root.onDrop = { [weak self] urls in self?.open(urls, append: false) }

        notesPanel.isHidden = true
        notesPanel.onAdd = { [weak self] in self?.addMarker() }
        notesPanel.onJump = { [weak self] m in self?.pauseShuttle(); self?.seek(frame: m.frame) }
        notesPanel.onChanged = { [weak self] in self?.saveReview() }
        notesPanel.onEditingEnded = { [weak self] in self?.window?.makeFirstResponder(self?.root) }
        notesPanel.onImport = { [weak self] in self?.importNotes() }
        notesPanel.onExport = { [weak self] view in self?.showExportMenu(from: view) }
        notesPanel.onCloud = { [weak self] in self?.editCloudFolder() }
        notesPanel.onResolve = { [weak self] in self?.sendToResolve() }

        timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] _ in self?.updateTime() }
        rateObserver = player.observe(\.rate, options: [.new]) { [weak self] _, _ in DispatchQueue.main.async { self?.updatePlayButton() } }
        // The notes panel and the summary stay as the user left them.
        notesShown = UserDefaults.standard.bool(forKey: "mostrarNotas")
        notesButton.active = notesShown
        summaryShown = UserDefaults.standard.bool(forKey: "mostrarResumen")
        summary.isHidden = !summaryShown
        updateTime()
    }

    func layoutPlayer() {
        let b = root.bounds
        let panel: CGFloat = notesShown ? min(400, max(320, b.width * 0.3)) : 0
        let left = b.width - panel
        let compact = left < 920
        let barHeight: CGFloat = compact ? 120 : 92
        stage.frame = NSRect(x: 0, y: 0, width: left, height: b.height - barHeight)
        bar.frame = NSRect(x: 0, y: b.height - barHeight, width: left, height: barHeight)
        notesPanel.frame = NSRect(x: left, y: 0, width: panel, height: b.height)
        notesPanel.isHidden = !notesShown
        let w = bar.bounds.width
        let noticeWidth = noticeLabel.stringValue.isEmpty ? 0 : min(360, floor(w * 0.45))
        noticeLabel.frame = NSRect(x: w - 18 - noticeWidth, y: 14, width: noticeWidth, height: 17)
        let seekWidth = w - 36 - noticeWidth - (noticeWidth > 0 ? 12 : 0)
        seekBar.frame = NSRect(x: 18, y: 10, width: max(0, seekWidth), height: 24)
        var x: CGFloat = 16
        let y: CGFloat = 46
        openButton.frame = NSRect(x: x, y: y, width: 84, height: 32); x += 96
        backButton.frame = NSRect(x: x, y: y, width: 36, height: 32); x += 40
        playButton.frame = NSRect(x: x, y: y, width: 40, height: 32); x += 44
        forwardButton.frame = NSRect(x: x, y: y, width: 36, height: 32); x += 50
        var right = w - 16
        infoButton.frame = NSRect(x: right - 36, y: y, width: 36, height: 32); right -= 44
        notesButton.frame = NSRect(x: right - 104, y: y, width: 104, height: 32); right -= 116
        zoomButton.frame = NSRect(x: right - 96, y: y, width: 96, height: 32); right -= 108
        let timeRow = compact ? y + 36 : y
        if compact { right = w - 16; x = 16 }
        volume.frame = NSRect(x: right - 90, y: timeRow + 8, width: 90, height: 16); right -= 96
        speaker.frame = NSRect(x: right - 20, y: timeRow + 7, width: 20, height: 18); right -= 30
        timecodeField.frame = NSRect(x: x - 3, y: timeRow + 5, width: 106, height: 22)
        timeLabel.frame = NSRect(x: x + 112, y: timeRow + 7, width: max(0, right - x - 112), height: 18)
        layoutSummary()
    }

    private func layoutSummary() {
        guard summaryShown, !summary.isHidden else { return }
        let size = summary.fit(maxWidth: min(560, stage.frame.width - 40))
        summary.frame = NSRect(x: 20, y: 20, width: size.width, height: size.height)
    }

    // MARK: Opening

    static let blocked: Set<String> = ["m3u", "m3u8", "pls", "xspf", "asx", "cue", "webloc", "url", "inetloc"]

    static func isPlayable(_ url: URL) -> Bool {
        guard url.isFileURL else { return false }
        var dir: ObjCBool = false
        guard FileManager.default.fileExists(atPath: url.path, isDirectory: &dir), !dir.boolValue else { return false }
        let ext = url.pathExtension.lowercased()
        if blocked.contains(ext) { return false }
        guard let type = UTType(filenameExtension: ext) else { return false }
        return type.conforms(to: .audiovisualContent)
    }

    func chooseFiles(append: Bool) {
        let panel = NSOpenPanel()
        panel.title = append ? "Añadir vídeos a la lista" : "Abrir vídeos"
        panel.allowsMultipleSelection = true
        panel.canChooseDirectories = false
        panel.allowedContentTypes = [.audiovisualContent]
        guard panel.runModal() == .OK else { return }
        open(panel.urls, append: append)
    }

    // Replaces what is playing (one window), or adds to the playlist. Several files form a list.
    func open(_ urls: [URL], append: Bool) {
        let valid = urls.filter(PlayerWindowController.isPlayable)
        if valid.isEmpty {
            notice(urls.count == 1 ? "No se puede abrir «\(urls[0].lastPathComponent)»: no es un vídeo o audio compatible." : "No hay vídeos compatibles entre los archivos.", error: true)
            return
        }
        for url in valid { NSDocumentController.shared.noteNewRecentDocumentURL(url) }
        if append && !playlist.isEmpty {
            playlist += valid
            notice(valid.count == 1 ? "Añadido a la lista: \(valid[0].lastPathComponent)" : "\(valid.count) vídeos añadidos a la lista")
            return
        }
        playlist = valid
        play(at: 0)
        if valid.count > 1 { notice("Lista de reproducción: \(valid.count) vídeos") }
    }

    func play(at i: Int) {
        guard i >= 0, i < playlist.count else { return }
        notesPanel.commitEdit()
        index = i
        load(playlist[i])
    }

    private func load(_ url: URL) {
        loadToken += 1
        let token = loadToken
        pauseShuttle()
        let asset = AVURLAsset(url: url, options: [AVURLAssetPreferPreciseDurationAndTimingKey: true])
        let item = AVPlayerItem(asset: asset)
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in self?.itemEnded() }
        statusObserver = item.observe(\.status, options: [.new]) { [weak self] item, _ in
            guard item.status == .failed else { return }
            DispatchQueue.main.async { self?.notice("No se pudo reproducir: " + (item.error?.localizedDescription ?? "formato no compatible"), error: true) }
        }
        player.replaceCurrentItem(with: item)
        window?.title = url.lastPathComponent
        window?.representedURL = url
        review = nil; reviewURL = nil
        endTimecodeEdit()
        stage.reset()
        notesPanel.refresh(nil)
        seekBar.enabled = false; seekBar.markers = []
        Task { @MainActor in
            do {
                let media = try await MediaInfo.load(asset)
                guard token == self.loadToken else { return }
                self.info = media
                self.stage.naturalSize = CGSize(width: media.width, height: media.height)
                self.setUpReview(url, media)
                self.player.rate = 1
                self.updateSummary()
            } catch {
                guard token == self.loadToken else { return }
                self.notice("No se pudo abrir «\(url.lastPathComponent)»: \(error.localizedDescription)", error: true)
            }
        }
    }

    private func setUpReview(_ url: URL, _ media: MediaInfo) {
        let doc = ReviewDocument()
        doc.mediaPath = url.path
        doc.width = media.width; doc.height = media.height
        doc.audioChannels = media.audioChannels; doc.audioRate = media.audioRate
        do { try doc.setFps(media.fps) } catch {
            notesPanel.refresh(nil)
            notice("No se han detectado FPS constantes compatibles para las notas.", error: true)
            seekBar.enabled = media.duration > 0
            return
        }
        doc.durationFrames = max(1, Int64((media.duration * doc.fps).rounded()))
        let file = ReviewDocument.storageURL(for: url.path)
        if FileManager.default.fileExists(atPath: file.path) {
            do {
                let saved = try ReviewDocument.load(file)
                doc.syncFolder = saved.syncFolder; doc.syncHash = saved.syncHash
                if saved.fpsNumerator == doc.fpsNumerator && saved.fpsDenominator == doc.fpsDenominator && saved.durationFrames == doc.durationFrames {
                    doc.markers = saved.markers
                } else {
                    notice("El vídeo ha cambiado de duración o FPS. Las notas guardadas se conservan para importarlas manualmente.", error: true)
                }
            } catch {
                notice("No se pudieron leer las notas: " + error.localizedDescription, error: true)
            }
        }
        review = doc; reviewURL = file
        notesPanel.refresh(doc)
        seekBar.enabled = true
        updateMarkerTicks()
    }

    private func itemEnded() {
        pauseShuttle()
        if index + 1 < playlist.count { play(at: index + 1) }
    }

    // MARK: Playback

    var currentFrame: Int64 {
        guard let review else { return 0 }
        let t = player.currentTime().seconds
        guard t.isFinite else { return 0 }
        return max(0, min(review.durationFrames - 1, Int64((t * review.fps).rounded())))
    }

    func seek(frame: Int64) {
        guard let review else { return }
        let time = CMTime(value: frame * Int64(review.fpsDenominator), timescale: CMTimeScale(review.fpsNumerator))
        player.seek(to: time, toleranceBefore: .zero, toleranceAfter: .zero)
    }

    private func seek(fraction: Double, exact: Bool) {
        guard let item = player.currentItem else { return }
        let duration = item.duration.seconds
        guard duration.isFinite, duration > 0 else { return }
        pauseShuttle()
        if let review, exact {
            let target = min(review.durationFrames - 1, Int64((fraction * Double(review.durationFrames)).rounded(.down)))
            seek(frame: target)
            if !timecodeField.isTyping { timecodeField.stringValue = review.timecode(target) }
            return
        } else {
            let tolerance = exact ? CMTime.zero : CMTime(value: 1, timescale: 10)
            player.seek(to: CMTime(seconds: fraction * duration, preferredTimescale: 600), toleranceBefore: tolerance, toleranceAfter: tolerance)
        }
        updateTime()   // while dragging, the timecode shows the frame under the mouse at once
    }

    func seekRelative(_ seconds: Double) {
        pauseShuttle()
        let target = max(0, player.currentTime().seconds + seconds)
        player.seek(to: CMTime(seconds: target, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero)
    }

    func togglePlay() {
        guard let item = player.currentItem else { chooseFiles(append: false); return }
        if player.rate != 0 || reverseTimer != nil { pauseShuttle(); return }
        if item.duration.isNumeric && player.currentTime() >= item.duration - CMTime(value: 1, timescale: 30) {
            player.seek(to: .zero, toleranceBefore: .zero, toleranceAfter: .zero)
        }
        shuttle.rate = 0
        player.rate = 1
    }

    func step(_ direction: Int) {
        guard let item = player.currentItem else { return }
        pauseShuttle()
        item.step(byCount: direction)
    }

    func pauseShuttle() {
        shuttle.rate = 0
        reverseTimer?.invalidate(); reverseTimer = nil
        player.pause()
    }

    // J / L: doubles speed up to 16×; the opposite key slows down and reverses. Shift: half speed.
    func startShuttle(_ direction: Int, slow: Bool) {
        guard let item = player.currentItem else { return }
        let rate = slow ? Double(direction) * 0.5 : shuttle.press(direction)
        shuttle.rate = rate
        reverseTimer?.invalidate(); reverseTimer = nil
        if rate < 0 {
            let native = rate > -1 ? item.canPlaySlowReverse : (rate == -1 ? item.canPlayReverse : item.canPlayFastReverse)
            if native { player.rate = Float(rate) } else { player.pause(); startReverseTimer(rate) }
        } else {
            let native = rate < 1 ? item.canPlaySlowForward : (rate <= 2 ? true : item.canPlayFastForward)
            player.rate = native ? Float(rate) : 2
        }
        notice(rate < 0 ? "Retroceso \(speedText(rate))" : "Avance \(speedText(rate))")
    }

    private func speedText(_ rate: Double) -> String {
        let a = abs(rate)
        return a < 1 ? "½×" : "\(Int(a))×"
    }

    // Long-GOP files that AVFoundation cannot play backwards: exact seeks while paused.
    private func startReverseTimer(_ rate: Double) {
        let interval = 1.0 / 12
        reverseTimer = Timer.scheduledTimer(withTimeInterval: interval, repeats: true) { [weak self] _ in
            guard let self, !self.reverseSeeking else { return }
            let target = self.player.currentTime().seconds + rate * interval
            if target <= 0 { self.pauseShuttle(); self.player.seek(to: .zero); return }
            self.reverseSeeking = true
            self.player.seek(to: CMTime(seconds: target, preferredTimescale: 600), toleranceBefore: .zero, toleranceAfter: .zero) { _ in
                DispatchQueue.main.async { self.reverseSeeking = false }
            }
        }
    }

    func stop() { pauseShuttle(); player.seek(to: .zero, toleranceBefore: .zero, toleranceAfter: .zero) }

    func changeVolume(_ delta: Double) {
        volume.doubleValue = min(1, max(0, volume.doubleValue + delta))
        volumeChanged()
        notice("Volumen \(Int((volume.doubleValue * 100).rounded())) %")
    }

    @objc private func volumeChanged() {
        player.volume = Float(volume.doubleValue)
        UserDefaults.standard.set(volume.doubleValue, forKey: "volumen")
    }

    func toggleMute() {
        player.isMuted.toggle()
        speaker.image = NSImage(systemSymbolName: player.isMuted ? "speaker.slash.fill" : "speaker.wave.2.fill", accessibilityDescription: "Volumen")
        notice(player.isMuted ? "Silenciado" : "Sonido activado")
    }

    func toggleFullscreen() { window?.toggleFullScreen(nil) }

    private func updatePlayButton() {
        let playing = player.rate != 0 || reverseTimer != nil
        playButton.image = NSImage(systemSymbolName: playing ? "pause.fill" : "play.fill", accessibilityDescription: playing ? "Pausar" : "Reproducir")
    }

    private func clock(_ seconds: Double) -> String {
        guard seconds.isFinite, seconds >= 0 else { return "--:--" }
        let s = Int(seconds)
        return s >= 3600 ? String(format: "%d:%02d:%02d", s / 3600, s / 60 % 60, s % 60) : String(format: "%02d:%02d", s / 60, s % 60)
    }

    private func updateTime() {
        let now = player.currentTime().seconds
        let duration = player.currentItem?.duration.seconds ?? .nan
        if let review {
            let frame = seekBar.dragging ? min(review.durationFrames - 1, max(0, Int64((seekBar.progress * Double(review.durationFrames)).rounded(.down)))) : currentFrame
            if !timecodeField.isTyping { timecodeField.stringValue = review.timecode(frame) }
        } else if !timecodeField.isTyping {
            timecodeField.stringValue = "--:--:--:--"
        }
        timeLabel.stringValue = player.currentItem == nil ? "" : clock(now) + " / " + clock(duration)
        if duration.isFinite && duration > 0 && !seekBar.dragging { seekBar.progress = now / duration }
        if summaryShown { updateSummary() }
        updatePlayButton()
    }

    // MARK: Keyboard

    func handleKey(_ event: NSEvent) -> Bool {
        let mods = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        // ⌘= (and ⌘+ on keyboards where + needs Shift) zoom in, like ⌘+ in the menu.
        if mods == [.command] || mods == [.command, .shift] {
            let key = event.charactersIgnoringModifiers ?? ""
            if key == "=" || key == "+" { zoomStep(1); return true }
        }
        if mods.contains(.command) || mods.contains(.control) || mods.contains(.option) { return false }
        let shift = mods.contains(.shift)
        switch event.keyCode {
        case 49: togglePlay(); return true
        case 123: shift ? seekRelative(-5) : step(-1); return true
        case 124: shift ? seekRelative(5) : step(1); return true
        case 126: changeVolume(0.05); return true
        case 125: changeVolume(-0.05); return true
        case 115: pauseShuttle(); player.seek(to: .zero, toleranceBefore: .zero, toleranceAfter: .zero); return true
        case 53:
            if window?.styleMask.contains(.fullScreen) == true { toggleFullscreen(); return true }
            if summaryShown { toggleSummary(); return true }
            return false
        default: break
        }
        switch (event.charactersIgnoringModifiers ?? "").lowercased() {
        case "j": startShuttle(-1, slow: shift); return true
        case "k": pauseShuttle(); notice("Pausa"); return true
        case "l": startShuttle(1, slow: shift); return true
        case "i": toggleSummary(); return true
        case "m": addMarker(); return true
        case "n": toggleNotes(); return true
        case "s": stop(); return true
        case "f": toggleFullscreen(); return true
        case ".": step(1); return true
        case ",": step(-1); return true
        default: return false
        }
    }

    // MARK: Image size

    func showZoomMenu() {
        let menu = NSMenu()
        let fitItem = BlockMenuItem(title: "Ajustar a la ventana    ⌘0", keyEquivalent: "") { [weak self] in self?.fitVideo() }
        fitItem.state = stage.fit ? .on : .off
        menu.addItem(fitItem)
        for value in [0.5, 1, 2, 4] as [CGFloat] {
            let item = BlockMenuItem(title: VideoStage.percent(value) + (value == 1 ? " · píxel a píxel    ⌘1" : ""), keyEquivalent: "") { [weak self] in self?.zoom(to: value) }
            item.state = !stage.fit && abs(stage.scale - value) < 0.001 ? .on : .off
            menu.addItem(item)
        }
        menu.addItem(.separator())
        menu.addItem(BlockMenuItem(title: "Acercar    ⌘+", keyEquivalent: "") { [weak self] in self?.zoomStep(1) })
        menu.addItem(BlockMenuItem(title: "Alejar    ⌘−", keyEquivalent: "") { [weak self] in self?.zoomStep(-1) })
        menu.popUp(positioning: nil, at: NSPoint(x: 0, y: zoomButton.bounds.height + 4), in: zoomButton)
    }

    private func zoomReady() -> Bool {
        if stage.hasVideo { return true }
        notice("Abre un vídeo para cambiar el tamaño de la imagen.")
        return false
    }

    func zoomStep(_ steps: CGFloat) {
        guard zoomReady() else { return }
        stage.zoomStep(steps)
        notice(stage.fit ? "Imagen ajustada a la ventana" : "Tamaño de la imagen: " + stage.label)
    }

    func zoom(to value: CGFloat) {
        guard zoomReady() else { return }
        stage.zoom(to: value)
        notice("Tamaño de la imagen: " + stage.label)
    }

    func fitVideo() {
        guard zoomReady() else { return }
        stage.fitVideo()
        notice("Imagen ajustada a la ventana")
    }

    // MARK: Timecode

    func beginTimecodeEdit() {
        guard review != nil else { notice("Abre un vídeo para usar su código de tiempo."); return }
        window?.makeFirstResponder(timecodeField)
        notice("⌘C copia el código · escribe otro y pulsa ↩ para ir · Esc cancela")
    }

    func copyTimecode() {
        guard let review else { notice("Abre un vídeo para copiar su código de tiempo."); return }
        let code = review.timecode(currentFrame)
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(code, forType: .string)
        notice("Código de tiempo copiado: " + code)
    }

    private func commitTimecode(_ text: String) {
        guard let review else { endTimecodeEdit(); return }
        guard let frame = review.parseTimecode(text, current: currentFrame) else {
            notice("Código de tiempo no válido. Ejemplos: 00:00:03:10, 3:10, 310 o +10", error: true)
            timecodeField.currentEditor()?.selectAll(nil)
            return
        }
        endTimecodeEdit()
        pauseShuttle()
        seek(frame: frame)
        timecodeField.stringValue = review.timecode(frame)
        notice("Ir a " + review.timecode(frame))
    }

    func endTimecodeEdit() {
        guard timecodeField.isTyping else { return }
        window?.makeFirstResponder(root)
        if let review { timecodeField.stringValue = review.timecode(currentFrame) }
    }

    // MARK: Notes

    func toggleNotes() {
        notesShown.toggle()
        UserDefaults.standard.set(notesShown, forKey: "mostrarNotas")
        notesButton.active = notesShown
        layoutPlayer()
        if !notesShown { notesPanel.commitEdit(); window?.makeFirstResponder(root) }
    }

    func addMarker() {
        guard let review else { notice("Abre un vídeo para añadir notas."); return }
        pauseShuttle()
        if !notesShown { toggleNotes() }
        let frame = currentFrame
        notesPanel.beginEdit(ReviewMarker(frame: frame, name: "Nota \(review.markers.count + 1)"), isNew: true)
    }

    func saveReview() {
        guard let review, let reviewURL else { return }
        var delivered = "", failed = ""
        if !review.syncFolder.isEmpty {
            do { delivered = try SyncDelivery.write(review, to: review.syncFolder) } catch { failed = error.localizedDescription }
        }
        do {
            try review.save(reviewURL)
            if !failed.isEmpty { notice("Notas guardadas, pero no en la carpeta sincronizada: " + failed, error: true) }
            else { notice(delivered.isEmpty ? "Notas guardadas" : delivered) }
        } catch {
            notice("No se pudieron guardar las notas: \(error.localizedDescription). Exporta una copia.", error: true)
        }
        updateMarkerTicks()
        notesPanel.setCloud(SyncDelivery.label(review))
    }

    func updateMarkerTicks() {
        guard let review, review.durationFrames > 0 else { seekBar.markers = []; return }
        seekBar.markers = review.markers.map { Double($0.frame) / Double(review.durationFrames) }
    }

    // MARK: Summary and notices

    func toggleSummary() {
        summaryShown.toggle()
        UserDefaults.standard.set(summaryShown, forKey: "mostrarResumen")
        summary.isHidden = !summaryShown
        updateSummary()
    }

    func summaryText() -> String {
        guard let url = player.currentItem.flatMap({ ($0.asset as? AVURLAsset)?.url }) else { return "Ningún vídeo abierto" }
        var lines = [url.lastPathComponent]
        var line2 = info.codec.isEmpty ? "Códec no indicado" : info.codec
        if !info.depthLabel.isEmpty { line2 += " · " + info.depthLabel }
        if info.width > 0 { line2 += " · \(info.width) × \(info.height)" }
        lines.append(line2)
        var line3 = info.fps > 0 ? String(format: "%.3f fps", info.fps).replacingOccurrences(of: ".000", with: "") : "FPS no indicados"
        line3 += info.isHDR ? " · HDR " + MediaInfo.readable(info.transfer) : " · SDR"
        if !info.primaries.isEmpty { line3 += " · " + MediaInfo.readable(info.primaries) }
        lines.append(line3)
        if let review { lines.append("TC \(review.timecode(currentFrame)) · fotograma \(currentFrame) de \(review.durationFrames) · \(review.markers.count) notas") }
        if index >= 0 && playlist.count > 1 { lines.append("Lista: \(index + 1) de \(playlist.count)") }
        return lines.joined(separator: "\n")
    }

    func updateSummary() {
        guard summaryShown else { return }
        summary.text.stringValue = summaryText()
        layoutSummary()
    }

    func notice(_ text: String, error: Bool = false) {
        noticeLabel.stringValue = text
        noticeLabel.toolTip = text
        layoutPlayer()
        noticeLabel.textColor = error ? Theme.error : Theme.muted
        noticeClear?.cancel()
        let work = DispatchWorkItem { [weak self] in
            self?.noticeLabel.stringValue = ""
            self?.noticeLabel.toolTip = nil
            self?.layoutPlayer()
        }
        noticeClear = work
        DispatchQueue.main.asyncAfter(deadline: .now() + (error ? 8 : 4), execute: work)
    }

    // MARK: Playlist menu

    func menuNeedsUpdate(_ menu: NSMenu) {
        menu.removeAllItems()
        if playlist.isEmpty { menu.addItem(withTitle: "La lista está vacía", action: nil, keyEquivalent: ""); return }
        for (i, url) in playlist.enumerated() {
            let item = NSMenuItem(title: "\(i + 1). \(url.lastPathComponent)", action: #selector(playFromMenu(_:)), keyEquivalent: "")
            item.target = self; item.tag = i; item.state = i == index ? .on : .off
            menu.addItem(item)
        }
        menu.addItem(.separator())
        let clear = NSMenuItem(title: "Vaciar la lista (deja el vídeo actual)", action: #selector(clearPlaylist), keyEquivalent: "")
        clear.target = self
        menu.addItem(clear)
    }

    @objc private func playFromMenu(_ sender: NSMenuItem) { play(at: sender.tag) }

    @objc private func clearPlaylist() {
        guard index >= 0, index < playlist.count else { playlist = []; return }
        playlist = [playlist[index]]; index = 0
    }

    // MARK: Window

    func windowWillClose(_ notification: Notification) {
        notesPanel.commitEdit()
        player.pause()
    }

    func windowDidResize(_ notification: Notification) { layoutPlayer() }
    func windowDidEnterFullScreen(_ notification: Notification) { layoutPlayer() }
    func windowDidExitFullScreen(_ notification: Notification) { layoutPlayer() }
    func windowDidBecomeKey(_ notification: Notification) {
        if !notesPanel.isEditing { window?.makeFirstResponder(root) }
    }
}
#endif
