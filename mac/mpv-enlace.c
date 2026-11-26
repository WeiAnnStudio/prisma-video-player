// SPDX-License-Identifier: GPL-3.0-or-later
// Programa vacío que solo existe para enlazar con libmpv: así dylibbundler descubre libmpv y
// todas sus dependencias (ffmpeg, dav1d...) y las copia a la app. No se incluye en la app.
#include <mpv/client.h>

int main(void) { return mpv_client_api_version() == 0; }
