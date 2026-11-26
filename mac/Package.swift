// swift-tools-version:5.9
// SPDX-License-Identifier: GPL-3.0-or-later
import PackageDescription

let package = Package(
    name: "Prisma",
    // macOS 15: las librerías de libmpv (AV1) que lleva la app lo exigen.
    platforms: [.macOS("15.0")],
    products: [.library(name: "PrismaCore", targets: ["PrismaCore"])],
    targets: [
        // Formats, Resolve and synced-folder delivery: no UI, also builds and tests on Linux.
        .target(name: "PrismaCore", path: "Sources/PrismaCore"),
        // The Mac app (AppKit + AVFoundation). Only builds on macOS.
        .executableTarget(name: "Prisma", dependencies: ["PrismaCore"], path: "Sources/Prisma"),
        .testTarget(name: "PrismaCoreTests", dependencies: ["PrismaCore"], path: "Tests/PrismaCoreTests"),
    ]
)
