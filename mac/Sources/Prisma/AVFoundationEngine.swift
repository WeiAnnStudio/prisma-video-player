// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import AppKit
import AVFoundation

// El motor de Apple: ProRes, H.265, H.264 y el AV1 que el Mac decodifica con su chip.
// Es el mismo AVPlayer de siempre; aquí solo queda detrás del protocolo PlaybackEngine.
final class AVFoundationEngine: PlaybackEngine {
    private let player = AVPlayer()
    private let videoView = VideoView()
    private var timeObserver: Any?
    private var endObserver: NSObjectProtocol?
    private var statusObserver: NSKeyValueObservation?
    private var rateObserver: NSKeyValueObservation?

    var onTick: (() -> Void)?
    var onEnded: (() -> Void)?
    var onFailed: ((String) -> Void)?
    var onRateChanged: (() -> Void)?

    init() {
        videoView.player = player
        videoView.controlsStyle = .none
        videoView.wantsLayer = true
        videoView.layer?.backgroundColor = Theme.video.cgColor
        player.actionAtItemEnd = .pause
        player.preventsDisplaySleepDuringVideoPlayback = true
        timeObserver = player.addPeriodicTimeObserver(forInterval: CMTime(value: 1, timescale: 30), queue: .main) { [weak self] _ in self?.onTick?() }
        rateObserver = player.observe(\.rate, options: [.new]) { [weak self] _, _ in DispatchQueue.main.async { self?.onRateChanged?() } }
    }

    var view: NSView { videoView }
    var url: URL? { (player.currentItem?.asset as? AVURLAsset)?.url }
    var hasMedia: Bool { player.currentItem != nil }
    var seconds: Double { player.currentTime().seconds }
    var duration: Double { player.currentItem?.duration.seconds ?? .nan }
    var rate: Double {
        get { Double(player.rate) }
        set { player.rate = Float(newValue) }
    }
    var volume: Double {
        get { Double(player.volume) }
        set { player.volume = Float(newValue) }
    }
    var isMuted: Bool {
        get { player.isMuted }
        set { player.isMuted = newValue }
    }

    func load(_ url: URL) {
        let asset = AVURLAsset(url: url, options: [AVURLAssetPreferPreciseDurationAndTimingKey: true])
        let item = AVPlayerItem(asset: asset)
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in self?.onEnded?() }
        statusObserver = item.observe(\.status, options: [.new]) { [weak self] item, _ in
            guard item.status == .failed else { return }
            DispatchQueue.main.async { self?.onFailed?(item.error?.localizedDescription ?? "formato no compatible") }
        }
        player.replaceCurrentItem(with: item)
    }

    func unload() {
        player.pause()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = nil; statusObserver = nil
        player.replaceCurrentItem(with: nil)
    }

    func pause() { player.pause() }

    func seek(to time: CMTime, exact: Bool, completion: (() -> Void)?) {
        let tolerance = exact ? CMTime.zero : CMTime(value: 1, timescale: 10)
        guard let completion else {
            player.seek(to: time, toleranceBefore: tolerance, toleranceAfter: tolerance)
            return
        }
        player.seek(to: time, toleranceBefore: tolerance, toleranceAfter: tolerance) { _ in DispatchQueue.main.async(execute: completion) }
    }

    func step(_ direction: Int) { player.currentItem?.step(byCount: direction) }

    // Las mismas comprobaciones de siempre: qué velocidades sabe hacer AVFoundation sin ayuda.
    func supportsNative(rate: Double) -> Bool {
        guard let item = player.currentItem else { return false }
        if rate < 0 { return rate > -1 ? item.canPlaySlowReverse : (rate == -1 ? item.canPlayReverse : item.canPlayFastReverse) }
        return rate < 1 ? item.canPlaySlowForward : (rate <= 2 ? true : item.canPlayFastForward)
    }
}
#endif
