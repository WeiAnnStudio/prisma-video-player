#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Builds Prisma.app (Apple Silicon + Intel) and Prisma-<version>-mac.zip. Runs on macOS 15+ with Xcode
# and Homebrew (mpv, dylibbundler): libmpv plays the AV1 that the Mac cannot decode (Apple Silicon only).
# The app is signed ad hoc (no Apple account): the first time, macOS asks for permission in
# System Settings > Privacy & Security > "Open Anyway".
set -euo pipefail
cd "$(dirname "$0")"
VERSION="${PRISMA_VERSION:-0.6.0}"
OUT=build
APP="$OUT/Prisma/Prisma.app"

rm -rf "$OUT"
swift build -c release --arch arm64 --arch x86_64
BIN="$(swift build -c release --arch arm64 --arch x86_64 --show-bin-path)/Prisma"
lipo -info "$BIN"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/Prisma"
cp ../src/prisma_resolve.lua "$APP/Contents/Resources/prisma_resolve.lua"

# libmpv and its libraries (ffmpeg, dav1d...) into Contents/Frameworks, with their provenance.
bash empaquetar-mpv.sh "$OUT/mpv"
mkdir -p "$APP/Contents/Frameworks"
cp "$OUT/mpv/Frameworks/"*.dylib "$APP/Contents/Frameworks/"

# Icon from mac/AppIcon.png (1024 px), drawn by tools/make-mac-icon.py if it is not there.
if [ ! -f AppIcon.png ]; then
  python3 -m venv "$OUT/venv"
  "$OUT/venv/bin/pip" install --quiet pillow
  "$OUT/venv/bin/python" ../tools/make-mac-icon.py
fi
ICONSET="$OUT/AppIcon.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z $size $size AppIcon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z $double $double AppIcon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>es</string>
  <key>CFBundleName</key><string>Prisma</string>
  <key>CFBundleDisplayName</key><string>Prisma</string>
  <key>CFBundleIdentifier</key><string>io.github.weiannstudio.prisma</string>
  <key>CFBundleExecutable</key><string>Prisma</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundleShortVersionString</key><string>${VERSION}</string>
  <key>CFBundleVersion</key><string>${VERSION}</string>
  <key>LSMinimumSystemVersion</key><string>15.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.video</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHumanReadableCopyright</key><string>GPL-3.0-o-posterior</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Vídeo</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key>
      <array>
        <string>public.movie</string>
        <string>com.apple.quicktime-movie</string>
        <string>public.mpeg-4</string>
        <string>public.audiovisual-content</string>
      </array>
    </dict>
  </array>
</dict>
</plist>
PLIST
plutil -lint "$APP/Contents/Info.plist"

# Ad hoc signature with the hardened runtime (required on Apple Silicon; no Apple account).
codesign --force --deep --options runtime --entitlements Prisma.entitlements --timestamp=none --sign - "$APP"
codesign --verify --strict --verbose=2 "$APP"

# Smoke test: an old Claude / ChatGPT entry ("Prisma --mcp") ends at once, without a window.
if "$APP/Contents/MacOS/Prisma" --mcp </dev/null 2>/dev/null; then echo "--mcp debería terminar con error"; exit 1; fi
echo "--mcp ya no abre nada: OK"

cp LEEME-Mac.txt "$OUT/Prisma/LEEME.txt"
mkdir -p "$OUT/Prisma/licencias"
cp ../LICENSE.txt "$OUT/Prisma/licencias/Prisma-GPL-3.0.txt"
cp "$OUT/mpv/procedencia.txt" "$OUT/Prisma/licencias/PROCEDENCIA-Mac.txt"
ditto -c -k --sequesterRsrc --keepParent "$OUT/Prisma" "$OUT/Prisma-$VERSION-mac.zip"
shasum -a 256 "$OUT/Prisma-$VERSION-mac.zip" "$APP/Contents/MacOS/Prisma" | tee "$OUT/SHA256.txt"
echo "Tamaño del zip: $(du -h "$OUT/Prisma-$VERSION-mac.zip" | cut -f1)"
