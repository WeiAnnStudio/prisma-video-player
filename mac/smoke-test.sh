#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Opens the built Prisma.app with a 10-bit ProRes test clip and three notes, checks that the MCP
# mode sees them, and prints a screenshot (JPEG, base64) to the log so the interface can be reviewed.
set -euo pipefail
cd "$(dirname "$0")"
APP="build/Prisma/Prisma.app"
CLIP="$PWD/build/prueba.mov"

command -v ffmpeg >/dev/null || brew install --quiet ffmpeg >/dev/null
ffmpeg -loglevel error -y -f lavfi -i testsrc2=size=1920x1080:rate=25 -f lavfi -i sine=frequency=440:sample_rate=48000 \
  -t 10 -c:v prores_ks -profile:v 3 -pix_fmt yuv422p10le -c:a pcm_s16le "$CLIP"

DATA="$HOME/Library/Application Support/Prisma/marcadores"
mkdir -p "$DATA"
KEY="$(python3 -c 'import hashlib,os,sys; print(hashlib.sha256(os.path.abspath(sys.argv[1]).upper().encode()).hexdigest().upper())' "$CLIP")"
cat > "$DATA/$KEY.json" <<JSON
{"Version":1,"MediaPath":"$CLIP","CloudFolder":"https://drive.google.com/drive/folders/prueba","SyncFolder":"","SyncHash":"","FpsNumerator":25,"FpsDenominator":1,"DurationFrames":250,"Width":1920,"Height":1080,"AudioChannels":1,"AudioRate":48000,"Markers":[{"Id":"a1","Frame":50,"Name":"Color","Note":"Subir un punto la exposición del plano"},{"Id":"a2","Frame":125,"Name":"Sonido","Note":"Limpiar el ruido de fondo"},{"Id":"a3","Frame":200,"Name":"Nota 3","Note":"Revisar el rótulo final"}]}
JSON

# MCP: Claude / ChatGPT would see this video and its notes.
REPLY="$(printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"leer_revision","arguments":{"video":"prueba"}}}' | "$APP/Contents/MacOS/Prisma" --mcp)"
echo "$REPLY" | grep -q '00:00:05:00 Sonido: Limpiar el ruido de fondo' || { echo "MCP no lee las notas: $REPLY"; exit 1; }
echo "MCP lee las notas guardadas por la app"

defaults write io.github.weiannstudio.prisma mostrarNotas -bool true
defaults write io.github.weiannstudio.prisma mostrarResumen -bool true
open -n "$APP" --args "$CLIP"
sleep 9
pgrep -x Prisma >/dev/null || { echo "Prisma se ha cerrado al abrir el vídeo"; exit 1; }
screencapture -x build/captura.png
pkill -x Prisma || true
sips -Z 1600 -s format jpeg -s formatOptions 70 build/captura.png --out build/captura.jpg >/dev/null
echo "CAPTURA-INICIO"
base64 -i build/captura.jpg | fold -w 4000
echo "CAPTURA-FIN"
