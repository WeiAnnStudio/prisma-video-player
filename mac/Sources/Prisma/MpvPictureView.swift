// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit

// La vista donde libmpv deja cada fotograma. libmpv lo pinta en memoria (sin OpenGL) y aquí se
// convierte en la imagen de la capa. Es 8 bits por color: basta para revisar un AV1; el vídeo de
// 10 bits se decodifica entero, solo la imagen en pantalla se reduce a 8 bits.
final class MpvPictureView: NSView {
    /// Se pide un fotograma nuevo cuando cambia el tamaño de la vista.
    var onResize: (() -> Void)?
    // Más allá de este tamaño (en píxeles) la imagen se estira: con zoom enorme no se pide memoria sin fin.
    private static let maxSide: CGFloat = 4096

    override init(frame: NSRect) {
        super.init(frame: frame)
        wantsLayer = true
        layer?.backgroundColor = Theme.video.cgColor
        layer?.contentsGravity = .resize
    }
    required init?(coder: NSCoder) { fatalError() }

    override var acceptsFirstResponder: Bool { false }
    override func hitTest(_ point: NSPoint) -> NSView? { nil }
    override func keyDown(with event: NSEvent) { nextResponder?.keyDown(with: event) }
    override func layout() { super.layout(); onResize?() }

    /// Tamaño en píxeles reales que hay que dibujar ahora (pantallas retina incluidas).
    var pixelSize: (width: Int, height: Int) {
        let scale = window?.backingScaleFactor ?? 2
        let w = min(MpvPictureView.maxSide, max(2, (bounds.width * scale).rounded()))
        let h = min(MpvPictureView.maxSide, max(2, (bounds.height * scale).rounded()))
        return (Int(w), Int(h))
    }

    /// Muestra un fotograma bgr0 de width × height píxeles, con rowBytes bytes por línea.
    func show(_ pixels: Data, width: Int, height: Int, rowBytes: Int) {
        guard let provider = CGDataProvider(data: pixels as CFData),
              let space = CGColorSpace(name: CGColorSpace.sRGB),
              let image = CGImage(width: width, height: height, bitsPerComponent: 8, bitsPerPixel: 32, bytesPerRow: rowBytes,
                                  space: space,
                                  bitmapInfo: CGBitmapInfo(rawValue: CGBitmapInfo.byteOrder32Little.rawValue | CGImageAlphaInfo.noneSkipFirst.rawValue),
                                  provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent)
        else { return }
        CATransaction.begin()
        CATransaction.setDisableActions(true)
        layer?.contents = image
        CATransaction.commit()
    }
}
#endif
