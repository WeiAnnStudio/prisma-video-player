#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Prueba del motor AV1: abre la app ya compilada con un clip AV1 de 10 bits, obligándola a usar
# libmpv (PRISMA_MOTOR=mpv, porque el Mac de pruebas decodifica AV1 por su cuenta), y comprueba
# que se ve la imagen y que las librerías salen de dentro de la app, no de Homebrew.
set -euo pipefail
cd "$(dirname "$0")"
APP="$PWD/build/Prisma/Prisma.app"
CLIP="$PWD/build/prueba-av1.mp4"

command -v ffmpeg >/dev/null || brew install --quiet ffmpeg >/dev/null
# Fondo magenta liso: es un color que la interfaz de Prisma no usa, así se reconoce el vídeo en la captura.
for codec in libsvtav1 libaom-av1; do
  ffmpeg -loglevel error -y -f lavfi -i color=c=magenta:size=1280x720:rate=24 -f lavfi -i sine=frequency=440:sample_rate=48000 \
    -t 6 -c:v "$codec" -pix_fmt yuv420p10le -c:a aac "$CLIP" && break
done
[ -s "$CLIP" ] || { echo "No se pudo crear el clip AV1 de prueba"; exit 1; }

PRISMA_MOTOR=mpv "$APP/Contents/MacOS/Prisma" "$CLIP" >build/av1.log 2>&1 &
PID=$!
sleep 8
kill -0 "$PID" 2>/dev/null || { echo "Prisma se ha cerrado al abrir el AV1"; cat build/av1.log; exit 1; }

# Qué librerías tiene cargadas: libmpv sí, y todas desde la app.
lsof -p "$PID" 2>/dev/null | awk '{print $NF}' | grep '\.dylib$' | sort -u > build/av1-librerias.txt
grep -q "Prisma.app/Contents/Frameworks/libmpv" build/av1-librerias.txt || { echo "libmpv no está cargada desde la app"; cat build/av1-librerias.txt; exit 1; }
if grep -E "^/opt/homebrew|^/usr/local/(Cellar|opt|lib)" build/av1-librerias.txt; then echo "Prisma está usando librerías de Homebrew en lugar de las suyas"; exit 1; fi
echo "Librerías cargadas desde la app: $(grep -c 'Prisma.app/Contents/Frameworks' build/av1-librerias.txt)"

screencapture -x build/captura-av1.png
kill "$PID" 2>/dev/null || true
sips -Z 640 -s format bmp build/captura-av1.png --out build/captura-av1.bmp >/dev/null
python3 Tests/Smoke/contar-magenta.py build/captura-av1.bmp
