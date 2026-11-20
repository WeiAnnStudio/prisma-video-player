// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AVFoundation
import CoreMedia

// What Prisma knows about the open file, read once from AVFoundation.
struct MediaInfo {
    var codec = "", fourCC = "", bits = "", primaries = "", transfer = "", matrix = ""
    var width = 0, height = 0
    var fps: Double = 0
    var duration: Double = 0
    var audioCodec = "", audioChannels = 0, audioRate = 48000
    var isHDR: Bool { transfer.contains("2084") || transfer.uppercased().contains("HLG") }

    static func load(_ asset: AVURLAsset) async throws -> MediaInfo {
        var info = MediaInfo()
        info.duration = try await asset.load(.duration).seconds
        if let video = try await asset.loadTracks(withMediaType: .video).first {
            let (rate, size, transform, formats) = try await video.load(.nominalFrameRate, .naturalSize, .preferredTransform, .formatDescriptions)
            info.fps = Double(rate)
            let shown = size.applying(transform)
            info.width = Int(abs(shown.width).rounded()); info.height = Int(abs(shown.height).rounded())
            if let format = formats.first {
                let sub = CMFormatDescriptionGetMediaSubType(format)
                info.fourCC = fourCharacterCode(sub)
                info.codec = codecName(info.fourCC)
                let ext = { (key: CFString) -> Any? in CMFormatDescriptionGetExtension(format, extensionKey: key) }
                if let n = ext(kCMFormatDescriptionExtension_BitsPerComponent) as? NSNumber { info.bits = n.stringValue }
                else { info.bits = defaultBits(info.fourCC) }
                info.primaries = ext(kCMFormatDescriptionExtension_ColorPrimaries) as? String ?? ""
                info.transfer = ext(kCMFormatDescriptionExtension_TransferFunction) as? String ?? ""
                info.matrix = ext(kCMFormatDescriptionExtension_YCbCrMatrix) as? String ?? ""
            }
        }
        if let audio = try await asset.loadTracks(withMediaType: .audio).first {
            let formats = try await audio.load(.formatDescriptions)
            if let format = formats.first {
                info.audioCodec = codecName(fourCharacterCode(CMFormatDescriptionGetMediaSubType(format)))
                if let asbd = CMAudioFormatDescriptionGetStreamBasicDescription(format)?.pointee {
                    info.audioChannels = Int(asbd.mChannelsPerFrame)
                    info.audioRate = asbd.mSampleRate > 0 ? Int(asbd.mSampleRate.rounded()) : 48000
                }
            }
        }
        return info
    }

    static func fourCharacterCode(_ code: FourCharCode) -> String {
        let bytes = [UInt8((code >> 24) & 0xff), UInt8((code >> 16) & 0xff), UInt8((code >> 8) & 0xff), UInt8(code & 0xff)]
        return String(bytes: bytes, encoding: .macOSRoman)?.trimmingCharacters(in: .whitespaces) ?? "?"
    }

    static func codecName(_ code: String) -> String {
        switch code {
        case "apco": return "Apple ProRes 422 Proxy"
        case "apcs": return "Apple ProRes 422 LT"
        case "apcn": return "Apple ProRes 422"
        case "apch": return "Apple ProRes 422 HQ"
        case "ap4h": return "Apple ProRes 4444"
        case "ap4x": return "Apple ProRes 4444 XQ"
        case "aprn", "aprh": return "Apple ProRes RAW"
        case "hvc1", "hev1": return "H.265 / HEVC"
        case "dvh1", "dvhe": return "Dolby Vision (HEVC)"
        case "avc1", "avc3": return "H.264 / AVC"
        case "av01": return "AV1"
        case "mp4a": return "AAC"
        case "lpcm", "sowt", "twos", "in24", "in32", "fl32": return "PCM"
        case "ac-3": return "Dolby Digital"
        case "ec-3": return "Dolby Digital Plus"
        case "alac": return "Apple Lossless"
        default: return code
        }
    }

    static func defaultBits(_ code: String) -> String {
        switch code {
        case "apco", "apcs", "apcn", "apch": return "10"
        case "ap4h", "ap4x": return "12"
        default: return ""
        }
    }

    static func readable(_ value: String) -> String {
        switch value {
        case "ITU_R_709_2": return "BT.709"
        case "ITU_R_2020": return "BT.2020"
        case "P3_D65": return "Display P3"
        case "DCI_P3": return "DCI-P3"
        case "SMPTE_ST_2084_PQ": return "PQ (SMPTE ST 2084)"
        case "ITU_R_2100_HLG": return "HLG"
        case "": return "No indicado"
        default: return value.replacingOccurrences(of: "_", with: " ")
        }
    }
}
#endif
