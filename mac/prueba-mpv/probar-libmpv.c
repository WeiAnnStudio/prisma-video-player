// SPDX-License-Identifier: GPL-3.0-or-later
// Mini programa de prueba: abre un vídeo con libmpv sin ventana ni sonido y comprueba que
// se decodifica de verdad con dav1d hasta el final. Si todo va bien termina con código 0.
// Sirve para validar el empaquetado de libmpv antes de meterlo en la app.
#include <mpv/client.h>
#include <stdio.h>
#include <string.h>

int main(int argc, char **argv) {
    if (argc < 2) { fprintf(stderr, "uso: %s clip.mp4\n", argv[0]); return 2; }

    mpv_handle *mpv = mpv_create();
    if (!mpv) { fprintf(stderr, "No se pudo crear el motor\n"); return 3; }

    // Sin ventana, sin audio, sin aceleración por hardware (así se fuerza el decodificador
    // por software, que es el caso que nos interesa) y sin esperar el ritmo real del vídeo.
    mpv_set_option_string(mpv, "config", "no");
    mpv_set_option_string(mpv, "vo", "null");
    mpv_set_option_string(mpv, "ao", "null");
    mpv_set_option_string(mpv, "hwdec", "no");
    mpv_set_option_string(mpv, "untimed", "yes");
    mpv_request_log_messages(mpv, "v");
    mpv_observe_property(mpv, 0, "time-pos", MPV_FORMAT_DOUBLE);
    if (mpv_initialize(mpv) < 0) { fprintf(stderr, "No se pudo iniciar el motor\n"); return 3; }

    const char *orden[] = {"loadfile", argv[1], NULL};
    mpv_command(mpv, orden);

    int usa_dav1d = 0, cargado = 0, llego_al_final = 0, formato_mostrado = 0;
    double tiempo_maximo = 0;

    for (;;) {
        mpv_event *evento = mpv_wait_event(mpv, 30);
        if (evento->event_id == MPV_EVENT_NONE) break;              // 30 s sin noticias: algo va mal
        if (evento->event_id == MPV_EVENT_SHUTDOWN) break;
        if (evento->event_id == MPV_EVENT_LOG_MESSAGE) {
            mpv_event_log_message *registro = evento->data;
            if (strstr(registro->text, "dav1d")) usa_dav1d = 1;
        } else if (evento->event_id == MPV_EVENT_FILE_LOADED) {
            cargado = 1;
        } else if (evento->event_id == MPV_EVENT_PLAYBACK_RESTART && !formato_mostrado) {
            // Los datos de la imagen (formato y profundidad de color) existen ya al empezar a reproducir.
            char *formato = mpv_get_property_string(mpv, "video-format");
            char *pixeles = mpv_get_property_string(mpv, "video-params/pixelformat");
            printf("Formato de vídeo: %s, píxeles: %s\n", formato ? formato : "?", pixeles ? pixeles : "?");
            mpv_free(formato); mpv_free(pixeles);
            formato_mostrado = 1;
        } else if (evento->event_id == MPV_EVENT_PROPERTY_CHANGE) {
            mpv_event_property *propiedad = evento->data;
            if (propiedad->format == MPV_FORMAT_DOUBLE && propiedad->data) {
                double t = *(double *)propiedad->data;
                if (t > tiempo_maximo) tiempo_maximo = t;
            }
        } else if (evento->event_id == MPV_EVENT_END_FILE) {
            mpv_event_end_file *fin = evento->data;
            llego_al_final = (fin->reason == MPV_END_FILE_REASON_EOF);
            break;
        }
    }
    mpv_terminate_destroy(mpv);

    printf("Cargado: %s\n", cargado ? "sí" : "NO");
    printf("Decodificador dav1d: %s\n", usa_dav1d ? "sí" : "NO");
    printf("Tiempo reproducido: %.2f s\n", tiempo_maximo);
    printf("Llegó al final: %s\n", llego_al_final ? "sí" : "NO");

    // El clip de prueba dura 3 s: exigimos haber visto al menos 2,5 s de imágenes.
    int bien = cargado && usa_dav1d && llego_al_final && tiempo_maximo >= 2.5;
    printf("RESULTADO: %s\n", bien ? "CORRECTO" : "FALLO");
    return bien ? 0 : 1;
}
