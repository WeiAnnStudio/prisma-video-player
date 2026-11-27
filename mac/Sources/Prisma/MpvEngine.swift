// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import CoreMedia

// El segundo motor: libmpv (con dav1d) para el AV1 que el Mac no decodifica. Todo se llama desde
// el hilo principal; libmpv solo avisa desde otro hilo de que hay fotograma nuevo.
final class MpvEngine: PlaybackEngine {
    private let lib: MpvLibrary
    private var handle: OpaquePointer?
    private var renderContext: OpaquePointer?
    private let picture = MpvPictureView()
    private var pump: Timer?
    private var loaded = false
    private var endedSent = false
    private var lastRate = 0.0
    private var seekWaiters: [() -> Void] = []
    private var keepAwake: NSObjectProtocol?
    private(set) var url: URL?

    var onTick: (() -> Void)?
    var onEnded: (() -> Void)?
    var onFailed: ((String) -> Void)?
    var onRateChanged: (() -> Void)?

    /// nil si libmpv no está disponible o no arranca.
    init?() {
        guard let lib = MpvLibrary.shared, let handle = lib.create() else { return nil }
        self.lib = lib
        self.handle = handle
        // Sin ajustes del usuario ni atajos de mpv: Prisma lleva sus propios controles. Sin decodificador
        // de hardware: dav1d (por software) es lo que se prueba y lo que da imágenes exactas.
        var options = ["config": "no", "vo": "libmpv", "hwdec": "no", "keep-open": "yes", "pause": "yes",
                       "input-default-bindings": "no", "input-vo-keyboard": "no", "input-media-keys": "no",
                       "terminal": "no"]
        // Ninguno de los extras de mpv escritos en Lua (estadísticas, consola, menús...): Prisma no los
        // usa y su Lua compila código al vuelo, cosa que macOS prohíbe en una app firmada como Prisma y
        // que la cerraba de golpe al abrir un AV1 (Prisma 0.6.0).
        for extra in MpvEngine.luaExtras { options[extra] = "no" }
        for (name, value) in options { _ = lib.setOption(handle, name, value) }
        guard lib.initialize(handle) >= 0, createRenderContext() else { lib.destroy(handle); self.handle = nil; return nil }
        picture.onResize = { [weak self] in self?.draw() }
        pump = Timer.scheduledTimer(withTimeInterval: 1.0 / 30, repeats: true) { [weak self] _ in self?.tick() }
    }

    deinit { shutdown() }

    // Opciones de mpv que cargan sus extras en Lua. "load-osd-console" es el nombre antiguo de
    // "load-console"; si una versión no conoce alguna, mpv la ignora.
    static let luaExtras = ["load-scripts", "osc", "ytdl", "load-stats-overlay", "load-console", "load-osd-console",
                            "load-auto-profiles", "load-select", "load-positioning", "load-commands", "load-context-menu"]

    // MARK: Imagen

    private func createRenderContext() -> Bool {
        var context: OpaquePointer?
        let type = strdup("sw")
        defer { free(type) }
        var params = [MpvRenderParam(type: MpvLibrary.Param.apiType, data: type), MpvRenderParam(type: 0, data: nil)]
        let result = params.withUnsafeMutableBytes { lib.renderCreate(&context, handle, $0.baseAddress) }
        guard result >= 0, let context else { return false }
        renderContext = context
        // libmpv avisa desde otro hilo cuando hay un fotograma nuevo; se pinta en el principal.
        lib.renderSetCallback(context, { pointer in
            guard let pointer else { return }
            let engine = Unmanaged<MpvEngine>.fromOpaque(pointer).takeUnretainedValue()
            DispatchQueue.main.async { engine.draw() }
        }, Unmanaged.passUnretained(self).toOpaque())
        return true
    }

    private func draw() {
        guard let renderContext, loaded else { return }
        _ = lib.renderUpdate(renderContext)
        let (width, height) = picture.pixelSize
        // Cada línea empieza en múltiplo de 64 bytes: libmpv convierte más rápido así.
        let rowBytes = (width * 4 + 63) / 64 * 64
        let buffer = UnsafeMutableRawPointer.allocate(byteCount: rowBytes * height, alignment: 64)
        let size = UnsafeMutablePointer<Int32>.allocate(capacity: 2)
        let stride = UnsafeMutablePointer<Int>.allocate(capacity: 1)
        let format = strdup("bgr0")
        defer { buffer.deallocate(); size.deallocate(); stride.deallocate(); free(format) }
        size[0] = Int32(width); size[1] = Int32(height)
        stride.pointee = rowBytes
        var params = [MpvRenderParam(type: MpvLibrary.Param.swSize, data: size),
                      MpvRenderParam(type: MpvLibrary.Param.swFormat, data: format),
                      MpvRenderParam(type: MpvLibrary.Param.swStride, data: stride),
                      MpvRenderParam(type: MpvLibrary.Param.swPointer, data: buffer),
                      MpvRenderParam(type: 0, data: nil)]
        let result = params.withUnsafeMutableBytes { lib.render(renderContext, $0.baseAddress) }
        // Se copia: libmpv reutilizará esta memoria para el fotograma siguiente.
        if result >= 0 { picture.show(Data(bytes: buffer, count: rowBytes * height), width: width, height: height, rowBytes: rowBytes) }
    }

    // MARK: Propiedades

    private func double(_ name: String) -> Double? {
        var value = 0.0
        return lib.getProperty(handle, name, MpvLibrary.Format.double, &value) >= 0 ? value : nil
    }

    private func flag(_ name: String) -> Bool? {
        var value: Int32 = 0
        return lib.getProperty(handle, name, MpvLibrary.Format.flag, &value) >= 0 ? value != 0 : nil
    }

    private func set(_ name: String, flag value: Bool) {
        var number: Int32 = value ? 1 : 0
        _ = lib.setProperty(handle, name, MpvLibrary.Format.flag, &number)
    }

    private func set(_ name: String, double value: Double) {
        var number = value
        _ = lib.setProperty(handle, name, MpvLibrary.Format.double, &number)
    }

    @discardableResult
    private func run(_ arguments: [String]) -> Int32 {
        var pointers: [UnsafePointer<CChar>?] = arguments.map { UnsafePointer<CChar>?(strdup($0)) }
        pointers.append(nil)                     // libmpv espera la lista terminada en nil
        defer { for pointer in pointers { free(UnsafeMutablePointer(mutating: pointer)) } }
        return lib.command(handle, pointers)
    }

    // MARK: PlaybackEngine

    var view: NSView { picture }
    var hasMedia: Bool { loaded }
    var seconds: Double { double("time-pos") ?? 0 }
    var duration: Double { double("duration") ?? .nan }

    var rate: Double {
        get { (flag("pause") ?? true) ? 0 : (double("speed") ?? 1) }
        set {
            if newValue == 0 { set("pause", flag: true); return }
            set("speed", double: newValue)
            set("pause", flag: false)
            endedSent = false
        }
    }

    var volume: Double {
        get { (double("volume") ?? 100) / 100 }
        set { set("volume", double: newValue * 100) }
    }

    var isMuted: Bool {
        get { flag("mute") ?? false }
        set { set("mute", flag: newValue) }
    }

    func load(_ url: URL) {
        self.url = url
        loaded = true
        endedSent = false
        set("pause", flag: true)
        if run(["loadfile", url.path, "replace"]) < 0 { loaded = false; onFailed?("libmpv no pudo abrir el archivo") }
    }

    func unload() {
        pause()
        run(["stop"])
        loaded = false
        url = nil
        seekWaiters.forEach { $0() }; seekWaiters = []
        endKeepAwake()
    }

    func pause() { set("pause", flag: true) }

    func seek(to time: CMTime, exact: Bool, completion: (() -> Void)?) {
        guard loaded else { completion?(); return }
        // +0,5 ms: cae dentro del fotograma pedido aunque la división de tiempos redondee hacia abajo.
        let target = max(0, time.seconds) + (exact ? 0.0005 : 0)
        if let completion { seekWaiters.append(completion) }
        endedSent = false
        run(["seek", String(format: "%.6f", target), exact ? "absolute+exact" : "absolute"])
    }

    func step(_ direction: Int) {
        guard loaded else { return }
        endedSent = false
        run([direction >= 0 ? "frame-step" : "frame-back-step"])
    }

    // libmpv reproduce hacia delante a cualquier velocidad razonable, pero no hacia atrás.
    func supportsNative(rate: Double) -> Bool { rate > 0 && rate <= 16 }

    // MARK: Avisos

    private func tick() {
        guard handle != nil else { return }
        drainEvents()
        guard loaded else { return }
        let now = rate
        if now != lastRate { lastRate = now; onRateChanged?(); updateKeepAwake(playing: now != 0) }
        if !endedSent, flag("eof-reached") == true { endedSent = true; onEnded?() }
        onTick?()
    }

    private func drainEvents() {
        while let event = lib.waitEvent(handle, 0) {
            let id = event.load(as: Int32.self)
            if id == 0 { break }                                     // MPV_EVENT_NONE: no queda nada
            if id == MpvLibrary.Event.playbackRestart {
                let waiting = seekWaiters; seekWaiters = []
                waiting.forEach { $0() }
                draw()
            } else if id == MpvLibrary.Event.fileLoaded {
                draw()
            } else if id == MpvLibrary.Event.endFile, let data = event.load(fromByteOffset: 16, as: UnsafeMutableRawPointer?.self) {
                let reason = data.load(as: Int32.self), error = data.load(fromByteOffset: 4, as: Int32.self)
                if reason == 4 { onFailed?(lib.message(error)) }       // 4 = el archivo falló al abrirse o decodificarse
            }
        }
    }

    // Mientras se reproduce, la pantalla no se apaga (lo que AVPlayer ya hacía solo).
    private func updateKeepAwake(playing: Bool) {
        if playing, keepAwake == nil {
            keepAwake = ProcessInfo.processInfo.beginActivity(options: .idleDisplaySleepDisabled, reason: "Reproduciendo vídeo")
        } else if !playing { endKeepAwake() }
    }

    private func endKeepAwake() {
        if let keepAwake { ProcessInfo.processInfo.endActivity(keepAwake) }
        keepAwake = nil
    }

    private func shutdown() {
        pump?.invalidate(); pump = nil
        endKeepAwake()
        if let renderContext { lib.renderSetCallback(renderContext, nil, nil); lib.renderFree(renderContext) }
        renderContext = nil
        if let handle { lib.destroy(handle) }
        handle = nil
    }
}

// Igual que mpv_render_param de render.h: un tipo y un puntero.
struct MpvRenderParam {
    var type: Int32
    var data: UnsafeMutableRawPointer?
}
#endif
