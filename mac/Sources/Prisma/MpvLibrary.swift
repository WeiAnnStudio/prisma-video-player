// SPDX-License-Identifier: GPL-3.0-or-later
#if canImport(AppKit)
import Foundation

// libmpv se carga en el momento (dlopen), no al compilar. Así el mismo Prisma (Apple Silicon + Intel)
// compila sin tener libmpv a mano, y en un Mac Intel, donde la libmpv incluida no sirve, simplemente
// no se encuentra y Prisma lo explica. Las librerías van en Prisma.app/Contents/Frameworks.
final class MpvLibrary {
    typealias Create = @convention(c) () -> OpaquePointer?
    typealias Initialize = @convention(c) (OpaquePointer?) -> Int32
    typealias SetOption = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?, UnsafePointer<CChar>?) -> Int32
    typealias Command = @convention(c) (OpaquePointer?, UnsafePointer<UnsafePointer<CChar>?>?) -> Int32
    typealias GetProperty = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?, Int32, UnsafeMutableRawPointer?) -> Int32
    typealias SetProperty = @convention(c) (OpaquePointer?, UnsafePointer<CChar>?, Int32, UnsafeMutableRawPointer?) -> Int32
    typealias WaitEvent = @convention(c) (OpaquePointer?, Double) -> UnsafeMutableRawPointer?
    typealias Destroy = @convention(c) (OpaquePointer?) -> Void
    typealias ErrorString = @convention(c) (Int32) -> UnsafePointer<CChar>?
    typealias RenderCreate = @convention(c) (UnsafeMutablePointer<OpaquePointer?>?, OpaquePointer?, UnsafeMutableRawPointer?) -> Int32
    typealias RenderRender = @convention(c) (OpaquePointer?, UnsafeMutableRawPointer?) -> Int32
    typealias RenderUpdate = @convention(c) (OpaquePointer?) -> UInt64
    typealias RenderCallback = @convention(c) (UnsafeMutableRawPointer?) -> Void
    typealias RenderSetCallback = @convention(c) (OpaquePointer?, RenderCallback?, UnsafeMutableRawPointer?) -> Void
    typealias RenderFree = @convention(c) (OpaquePointer?) -> Void

    // Valores de client.h y render.h (no cambian entre versiones de libmpv 2.x).
    enum Format { static let string: Int32 = 1, flag: Int32 = 3, double: Int32 = 5 }
    enum Event { static let endFile: Int32 = 7, fileLoaded: Int32 = 8, playbackRestart: Int32 = 21 }
    enum Param { static let apiType: Int32 = 1, swSize: Int32 = 17, swFormat: Int32 = 18, swStride: Int32 = 19, swPointer: Int32 = 20 }

    let create: Create, initialize: Initialize, setOption: SetOption, command: Command
    let getProperty: GetProperty, setProperty: SetProperty, waitEvent: WaitEvent
    let destroy: Destroy, errorString: ErrorString
    let renderCreate: RenderCreate, render: RenderRender, renderUpdate: RenderUpdate
    let renderSetCallback: RenderSetCallback, renderFree: RenderFree

    /// nil si libmpv no está o no es de la arquitectura de este Mac.
    static let shared = MpvLibrary()

    private init?() {
        guard let handle = MpvLibrary.open() else { return nil }
        func symbol<T>(_ name: String) -> T? {
            guard let pointer = dlsym(handle, name) else { return nil }
            return unsafeBitCast(pointer, to: T.self)
        }
        guard let create: Create = symbol("mpv_create"),
              let initialize: Initialize = symbol("mpv_initialize"),
              let setOption: SetOption = symbol("mpv_set_option_string"),
              let command: Command = symbol("mpv_command"),
              let getProperty: GetProperty = symbol("mpv_get_property"),
              let setProperty: SetProperty = symbol("mpv_set_property"),
              let waitEvent: WaitEvent = symbol("mpv_wait_event"),
              let destroy: Destroy = symbol("mpv_terminate_destroy"),
              let errorString: ErrorString = symbol("mpv_error_string"),
              let renderCreate: RenderCreate = symbol("mpv_render_context_create"),
              let render: RenderRender = symbol("mpv_render_context_render"),
              let renderUpdate: RenderUpdate = symbol("mpv_render_context_update"),
              let renderSetCallback: RenderSetCallback = symbol("mpv_render_context_set_update_callback"),
              let renderFree: RenderFree = symbol("mpv_render_context_free")
        else { return nil }
        self.create = create; self.initialize = initialize; self.setOption = setOption; self.command = command
        self.getProperty = getProperty; self.setProperty = setProperty; self.waitEvent = waitEvent
        self.destroy = destroy; self.errorString = errorString
        self.renderCreate = renderCreate; self.render = render; self.renderUpdate = renderUpdate
        self.renderSetCallback = renderSetCallback; self.renderFree = renderFree
    }

    private static func open() -> UnsafeMutableRawPointer? {
        guard let folder = Bundle.main.privateFrameworksURL else { return nil }
        for name in ["libmpv.2.dylib", "libmpv.dylib"] {
            if let handle = dlopen(folder.appendingPathComponent(name).path, RTLD_NOW | RTLD_LOCAL) { return handle }
        }
        return nil
    }

    func message(_ code: Int32) -> String { errorString(code).map { String(cString: $0) } ?? "error \(code)" }
}
#endif
