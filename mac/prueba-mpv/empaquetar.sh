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

# Sin -rpath: libmpv de Homebrew se identifica con su ruta completa y dylibbundler la reescribe.
clang -O1 -o "$SALIDA/MacOS/probar-libmpv" probar-libmpv.c \
  -I"$MPV/include" -L"$MPV/lib" -lmpv

# -p: así verá cada librería a las demás una vez dentro de la app (Contents/Frameworks).
dylibbundler -cd -of -b \
  -x "$SALIDA/MacOS/probar-libmpv" \
  -d "$SALIDA/Frameworks" \
  -p "@executable_path/../Frameworks/" \
  -s "$MPV/lib" -s "$(brew --prefix)/lib" | tee "$SALIDA/dylibbundler.log"

# macOS 15 se niega a abrir un programa o librería que tenga la misma ruta de búsqueda
# (LC_RPATH) repetida: "Library not loaded ... duplicate LC_RPATH". Se deja una sola copia.
quitar_rutas_repetidas() {
  local fichero="$1" ruta
  while ruta=$(otool -l "$fichero" | awk '/cmd LC_RPATH/{r=1} r && $1=="path"{print $2; r=0}' | sort | uniq -d | head -1); [ -n "$ruta" ]; do
    echo "Ruta repetida quitada en $(basename "$fichero"): $ruta"
    install_name_tool -delete_rpath "$ruta" "$fichero"
  done
}
for f in "$SALIDA"/Frameworks/*.dylib "$SALIDA/MacOS/probar-libmpv"; do
  quitar_rutas_repetidas "$f"
done

# Cambiar las rutas invalida las firmas: se firma de nuevo (firma local, sin cuenta de Apple).
for f in "$SALIDA"/Frameworks/*.dylib "$SALIDA/MacOS/probar-libmpv"; do
  codesign --force -s - "$f" 2>/dev/null
done
