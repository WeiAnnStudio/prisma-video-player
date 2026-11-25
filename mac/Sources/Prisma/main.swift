// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation
import PrismaCore

// Prisma 0.4 could be added to Claude or ChatGPT desktop, which start "Prisma --mcp". That
// connection no longer exists: answer nothing and quit instead of opening a window.
if CommandLine.arguments.contains("--mcp") {
    FileHandle.standardError.write(Data("Prisma ya no ofrece conexión con Claude ni ChatGPT. Quita «prisma» de la configuración de esa app.\n".utf8))
    exit(1)
}

#if canImport(AppKit)
PrismaApp.start()
#else
print("Prisma para Mac solo funciona en macOS.")
#endif
