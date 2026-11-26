// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import CoreMedia

// Lo que Prisma necesita de un motor de reproducción. Hay dos: el de Apple (AVFoundation) para
// casi todo, y libmpv solo para el AV1 que el Mac no sabe decodificar. Botones, barra de tiempo,
// notas, zoom y código de tiempo hablan siempre con este protocolo, así que se comportan igual.
protocol PlaybackEngine: AnyObject {
    /// La vista donde se ve el vídeo (va dentro de VideoStage).
    var view: NSView { get }
    var url: URL? { get }
    var hasMedia: Bool { get }
    var seconds: Double { get }
    /// NaN si todavía no se conoce.
    var duration: Double { get }
    /// 0 = en pausa; 1 = velocidad normal; negativo = hacia atrás.
    var rate: Double { get set }
    /// De 0 a 1.
    var volume: Double { get set }
    var isMuted: Bool { get set }

    var onTick: (() -> Void)? { get set }
    var onEnded: (() -> Void)? { get set }
    var onFailed: ((String) -> Void)? { get set }
    var onRateChanged: (() -> Void)? { get set }

    func load(_ url: URL)
    /// Para la reproducción y suelta el vídeo.
    func unload()
    func pause()
    /// exact: cae justo en ese instante (fotograma exacto); si no, vale uno cercano (rápido, para arrastrar).
    func seek(to time: CMTime, exact: Bool, completion: (() -> Void)?)
    func step(_ direction: Int)
    /// ¿Puede el motor reproducir a esa velocidad por sí solo? Si no, Prisma lo simula con saltos exactos.
    func supportsNative(rate: Double) -> Bool
}
#endif
