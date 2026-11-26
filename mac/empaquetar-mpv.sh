#!/bin/bash
# SPDX-License-Identifier: GPL-3.0-or-later
# Reúne libmpv (el motor del AV1) con TODAS las librerías que necesita (ffmpeg, dav1d...) en una
# carpeta con la estructura de una .app, y escribe de dónde sale cada una. Necesita Homebrew con
# mpv y dylibbundler. Deja:
#   <salida>/Frameworks/*.dylib   las librerías, con rutas relativas a Contents/Frameworks
#   <salida>/procedencia.txt      qué versiones y licencias se incluyen
# Uso: empaquetar-mpv.sh <carpeta-de-salida>
set -euo pipefail
cd "$(dirname "$0")"

SALIDA="${1:?Falta la carpeta de salida}"
mkdir -p "$(dirname "$SALIDA")"
SALIDA="$(cd "$(dirname "$SALIDA")" && pwd)/$(basename "$SALIDA")"   # ruta absoluta: dylibbundler la guarda tal cual
MPV="$(brew --prefix mpv)"
rm -rf "$SALIDA"
mkdir -p "$SALIDA/MacOS" "$SALIDA/Frameworks"

# Sin -rpath: libmpv de Homebrew se identifica con su ruta completa y dylibbundler la reescribe.
clang -O1 -o "$SALIDA/MacOS/enlace" mpv-enlace.c -I"$MPV/include" -L"$MPV/lib" -lmpv

# -p: así verá cada librería a las demás una vez dentro de la app (Contents/Frameworks).
dylibbundler -cd -of -b \
  -x "$SALIDA/MacOS/enlace" \
  -d "$SALIDA/Frameworks" \
  -p "@executable_path/../Frameworks/" \
  -s "$MPV/lib" -s "$(brew --prefix)/lib" | tee "$SALIDA/dylibbundler.log"

# macOS 15 se niega a abrir una librería que tenga la misma ruta de búsqueda (LC_RPATH)
# repetida: "Library not loaded ... duplicate LC_RPATH". Se deja una sola copia.
quitar_rutas_repetidas() {
  local fichero="$1" ruta
  while ruta=$(otool -l "$fichero" | awk '/cmd LC_RPATH/{r=1} r && $1=="path"{print $2; r=0}' | sort | uniq -d | head -1); [ -n "$ruta" ]; do
    echo "Ruta repetida quitada en $(basename "$fichero"): $ruta"
    install_name_tool -delete_rpath "$ruta" "$fichero"
  done
}
for f in "$SALIDA"/Frameworks/*.dylib; do quitar_rutas_repetidas "$f"; done

# Cambiar las rutas invalida las firmas: se firma de nuevo (firma local, sin cuenta de Apple).
for f in "$SALIDA"/Frameworks/*.dylib; do codesign --force -s - "$f" 2>/dev/null; done

python3 - "$SALIDA" <<'PY'
import json, re, subprocess, sys
carpeta = sys.argv[1]
registro = open(f"{carpeta}/dylibbundler.log", errors="replace").read()
nombres = set(re.findall(r"/Cellar/([^/\s]+)/", registro)) | set(re.findall(r"/opt/(?!homebrew)([^/\s]+)/", registro))
lineas = []
for nombre in sorted(nombres):
    try:
        f = json.loads(subprocess.check_output(["brew", "info", "--json=v2", nombre], stderr=subprocess.DEVNULL))["formulae"][0]
    except Exception:
        continue
    lineas.append(f"- {f['name']} {f['versions']['stable']} — licencia: {f.get('license') or 'consulta el proyecto'} — {f['homepage']}\n"
                  f"  código fuente: {f['urls']['stable']['url']}")
cabecera = ("Motor de AV1 (solo Mac con chip Apple): libmpv con dav1d y FFmpeg, de Homebrew, sin modificar.\n"
            "Prisma lo usa únicamente para los vídeos AV1 que el Mac no decodifica por sí solo.\n"
            "Librerías incluidas en Prisma.app/Contents/Frameworks (cada una con su licencia; la de Prisma es GPL-3.0-o-posterior):\n")
open(f"{carpeta}/procedencia.txt", "w").write(cabecera + "\n".join(lineas) + "\n")
PY
rm -f "$SALIDA/MacOS/enlace"
echo "Librerías incluidas: $(ls "$SALIDA/Frameworks" | wc -l | tr -d ' ')"
