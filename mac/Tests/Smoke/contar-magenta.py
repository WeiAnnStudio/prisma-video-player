# SPDX-License-Identifier: GPL-3.0-or-later
# Cuenta los píxeles magenta de una captura (BMP de 24 o 32 bits). Sale con error si hay pocos:
# eso significa que el vídeo AV1 no se está viendo en la ventana.
import struct
import sys

datos = open(sys.argv[1], "rb").read()
inicio = struct.unpack_from("<I", datos, 10)[0]
ancho, alto, _, bits = struct.unpack_from("<iiHH", datos, 18)
bytes_pixel = bits // 8
fila = (ancho * bytes_pixel + 3) // 4 * 4
magenta = 0
for y in range(abs(alto)):
    base = inicio + y * fila
    for x in range(ancho):
        b, g, r = datos[base + x * bytes_pixel: base + x * bytes_pixel + 3]
        if r > 200 and b > 200 and g < 70:
            magenta += 1
total = ancho * abs(alto)
print(f"Píxeles magenta: {magenta} de {total} ({100 * magenta / total:.1f} %)")
if magenta < total * 0.05:
    print("FALLO: el vídeo AV1 no se ve en la ventana de Prisma")
    sys.exit(1)
print("CORRECTO: libmpv dibuja el AV1 en la ventana")
