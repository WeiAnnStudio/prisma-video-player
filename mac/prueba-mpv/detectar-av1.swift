// SPDX-License-Identifier: GPL-3.0-or-later
// Pregunta al propio macOS si puede reproducir el AV1 de un clip. Es solo información para
// decidir cuándo la app debe pasar a libmpv; no hace fallar la prueba.
// Uso: swift detectar-av1.swift clip.mp4
import AVFoundation
import VideoToolbox

let ruta = CommandLine.arguments.dropFirst().first ?? ""
print("Decodificador AV1 por hardware (VideoToolbox): \(VTIsHardwareDecodeSupported(kCMVideoCodecType_AV1))")

let pistas = AVURLAsset(url: URL(fileURLWithPath: ruta)).tracks(withMediaType: .video)
if pistas.isEmpty { print("AVFoundation no encuentra ninguna pista de vídeo en el clip") }
for pista in pistas {
    print("Pista de vídeo en AVFoundation: reproducible=\(pista.isPlayable) decodificable=\(pista.isDecodable)")
}
