// SPDX-License-Identifier: GPL-3.0-or-later
import Foundation
import PrismaCore

// Started by Claude or ChatGPT desktop: no window, read-only notes server over stdio.
if CommandLine.arguments.count == 2 && CommandLine.arguments[1] == "--mcp" {
    exit(McpServer.run())
}

#if canImport(AppKit)
PrismaApp.start()
#else
print("Prisma para Mac solo funciona en macOS.")
#endif
