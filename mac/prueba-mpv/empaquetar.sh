#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Prueba de empaquetado: compila el mini programa de prueba contra el libmpv de Homebrew y
# copia dentro de una carpeta propia TODAS las librerías que necesita (con dylibbundler), igual
# que se hará con la app. Deja esta estructura, idéntica a la de una .app:
#   <salida>/MacOS/probar-libmpv        el programa
#   <salida>/Frameworks/*.dylib         libmpv, ffmpeg, dav1d... con rutas relativas
# Uso: empaquetar.sh <carpeta-de-salida>
set -euo pipefail
cd "$(dirname "$0")"

SALIDA="${1:?Falta la carpeta de salida}"
MPV="$(brew --prefix mpv)"
rm -rf "$SALIDA"
mkdir -p "$SALIDA/MacOS" "$SALIDA/Frameworks"

clang -O1 -o "$SALIDA/MacOS/probar-libmpv" probar-libmpv.c \
  -I"$MPV/include" -L"$MPV/lib" -lmpv -Wl,-rpath,"$MPV/lib"

# -p: así verá cada librería a las demás una vez dentro de la app (Contents/Frameworks).
dylibbundler -cd -of -b \
  -x "$SALIDA/MacOS/probar-libmpv" \
  -d "$SALIDA/Frameworks" \
  -p "@executable_path/../Frameworks/" \
  -s "$MPV/lib" -s "$(brew --prefix)/lib" | tee "$SALIDA/dylibbundler.log"

# Quita la ruta de Homebrew que el compilador dejó en el programa: ya no hace falta y,
# si se quedara, la prueba podría "hacer trampa" cargando librerías de fuera.
install_name_tool -delete_rpath "$MPV/lib" "$SALIDA/MacOS/probar-libmpv" 2>/dev/null || true

# Cambiar las rutas invalida las firmas: se firma de nuevo (firma local, sin cuenta de Apple).
for f in "$SALIDA"/Frameworks/*.dylib "$SALIDA/MacOS/probar-libmpv"; do
  codesign --force -s - "$f" 2>/dev/null
done
