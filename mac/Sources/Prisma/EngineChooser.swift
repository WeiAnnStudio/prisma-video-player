// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AVFoundation
import VideoToolbox

enum EngineDecision {
    case apple
    case mpv
    case unsupported(String)
}

// Decide qué motor reproduce cada vídeo. Solo el AV1 que el Mac no decodifica pasa a libmpv;
// todo lo demás (ProRes, H.265, H.264…) sigue en el motor de Apple, que da fotogramas exactos.
enum EngineChooser {
    // Para pruebas: PRISMA_MOTOR=mpv fuerza libmpv con cualquier vídeo (los Mac de las pruebas
    // automáticas decodifican AV1 por su cuenta y, sin esto, nunca usarían libmpv).
    private static var forced: Bool { ProcessInfo.processInfo.environment["PRISMA_MOTOR"] == "mpv" }

    static func decide(_ asset: AVURLAsset, _ media: MediaInfo) async -> EngineDecision {
        if forced { return MpvLibrary.shared != nil ? .mpv : .unsupported(missingMessage) }
        guard media.fourCC == "av01" else { return .apple }
        // Los chips M3 y posteriores traen decodificador de AV1: ahí AVFoundation basta.
        if VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1) { return .apple }
        if MpvLibrary.shared != nil { return .mpv }
        // Sin libmpv (Mac Intel): solo seguimos con Apple si macOS dice que puede decodificarlo.
        let tracks = (try? await asset.loadTracks(withMediaType: .video)) ?? []
        var decodable = false
        if let track = tracks.first { decodable = (try? await track.load(.isDecodable)) ?? false }
        return decodable ? .apple : .unsupported(missingMessage)
    }

    private static var missingMessage: String {
        #if arch(x86_64)
        return "Este Mac (procesador Intel) no puede reproducir vídeo AV1. Hace falta un Mac con chip Apple (M1 o posterior). Convierte el vídeo a H.265 o ProRes."
        #else
        return "No se pudo cargar el motor AV1 que lleva Prisma. Vuelve a descargar Prisma."
        #endif
    }
}
#endif
