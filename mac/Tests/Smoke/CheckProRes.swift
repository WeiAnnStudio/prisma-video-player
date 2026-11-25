// SPDX-License-Identifier: GPL-3.0-or-later
import AVFoundation
import Foundation

@main
struct CheckProRes {
    static func main() async throws {
        let clip = URL(fileURLWithPath: CommandLine.arguments[1])
        let media = try await MediaInfo.load(AVURLAsset(url: clip))
        guard media.fourCC == "apch", media.depthLabel == "10 bits (perfil)" else {
            throw NSError(domain: "Prisma.Smoke", code: 1, userInfo: [
                NSLocalizedDescriptionKey: "El clip ProRes 422 HQ de 10 bits aparece como \(media.codec) · \(media.depthLabel)."
            ])
        }
        print("\(media.codec) · \(media.depthLabel)")
    }
}
