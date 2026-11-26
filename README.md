# Prisma

Reproductor y herramienta de revisión de vídeo para Windows de 64 bits y Mac, en español. Reproduce H.264, HEVC y ProRes estándar, incluidos formatos de 10 bits, y AV1 en Mac con chip Apple. Permite avanzar fotograma a fotograma, tomar notas con código de tiempo y exportar marcadores a DaVinci Resolve o Premiere mediante XML.

En Windows, Prisma está basado en [mpv](https://mpv.io/). Usa libmpv como motor de reproducción, que a su vez decodifica con [FFmpeg](https://ffmpeg.org/) y muestra la imagen con [libplacebo](https://code.videolan.org/videolan/libplacebo). Prisma aporta la interfaz en español, la revisión fotograma a fotograma y las notas exportables. El motor se usa sin modificar, bajo su licencia GPL.

En Mac, Prisma usa el motor de Apple (AVFoundation) para ProRes, HEVC y H.264. Solo cuando abre un vídeo AV1 que el Mac no decodifica (M1 y M2) pasa a libmpv con [dav1d](https://code.videolan.org/videolan/dav1d) y FFmpeg, que van dentro de la app. Los botones, la barra de tiempo, las notas, el zoom y el código de tiempo funcionan igual con los dos motores. Los Mac con procesador Intel no pueden reproducir AV1 y Prisma lo explica. Con libmpv la imagen se dibuja en 8 bits por color.

La versión 0.3 renueva la interfaz oscura: controles vectoriales, tarjetas de revisión, editor de notas a panel completo y distribución compacta. Conserva el motor y el formato de las notas existentes.

La versión 0.3.1 añade protecciones de seguridad y privacidad sin cambiar el uso diario. Consulta [Seguridad y privacidad](#seguridad-y-privacidad).

La versión 0.4 está pensada para revisar en equipo: envía las notas a la línea de tiempo abierta en DaVinci Resolve Studio, y escribe un documento de ajustes en la carpeta de Google Drive de cada vídeo, sincronizada en tu equipo. Además, abre cada vídeo nuevo en la misma ventana. Consulta [Revisión en equipo](#revisión-en-equipo).

La versión 0.5 añade el tamaño de imagen ajustable (rueda del ratón, Ctrl + / Ctrl −, 100 % píxel a píxel), un código de tiempo que sigue en directo a la barra y que se puede copiar o escribir para saltar, y la inserción del código de tiempo en las notas (Ctrl+T). El documento de ajustes lleva una casilla por nota y omite los títulos «Nota N» sin cambiar. Desaparece la conexión con Claude y ChatGPT: las notas llegan a la nube solo a través de la carpeta de Google Drive sincronizada en el equipo.

La versión 0.6 reproduce AV1 en Mac con chip Apple: el motor de Apple lo usa si el chip lo decodifica (M3 o posterior) y, si no (M1 y M2), Prisma pasa solo a libmpv con dav1d, incluido en la app. Sube el mínimo a macOS 15 y arregla el clic en el código de tiempo en Mac, que ahora selecciona todo el código con un solo clic.

## Descargar

- **Windows 10 u 11 (64 bits):** [Descargar Prisma para Windows](https://github.com/WeiAnnStudio/prisma-video-player/releases/latest/download/Prisma-Windows.zip)
- **Mac (macOS 15 o posterior; AV1 solo en Mac con chip Apple):** [Descargar Prisma para Mac](https://github.com/WeiAnnStudio/prisma-video-player/releases/latest/download/Prisma-Mac.zip)

Los enlaces descargan siempre la última versión. Las anteriores y las huellas SHA-256 de cada descarga están en [Releases](https://github.com/WeiAnnStudio/prisma-video-player/releases).

**Windows.** Descomprime el .zip en una carpeta propia, por ejemplo `Documentos\Prisma`, y abre `Prisma.exe`. Prisma no está firmado digitalmente, así que Windows SmartScreen puede avisar la primera vez: pulsa «Más información» y «Ejecutar de todas formas». Para comprobar que el .zip es el publicado, en PowerShell `Get-FileHash .\Prisma-Windows.zip` debe dar la huella que aparece en la versión.

**Mac.** Abre el .zip y arrastra Prisma.app a Aplicaciones. Prisma no está firmado por Apple: la primera vez que lo abras, macOS lo bloquea. Ve a Ajustes del Sistema > Privacidad y seguridad y pulsa «Abrir igualmente» junto al aviso de Prisma. Solo hace falta una vez.

## Compilar y ejecutar

En Windows 10 u 11 de 64 bits con .NET Framework 4.8, descarga o clona el repositorio y ejecuta estos comandos desde su carpeta:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\Prisma\Prisma.exe
```

La primera compilación descarga libmpv y verifica su SHA-256. Usa el compilador C# de .NET Framework, sin instalar un SDK ni paquetes NuGet. Mantén `Prisma.exe` y `libmpv-2.dll` juntos. Consulta la [guía de usuario](Prisma/LEEME.txt) para ver los atajos y cómo exportar las notas.

Para compilar en otra carpeta mientras el reproductor está abierto, añade `-OutputDirectory tests/design` al primer comando.

## Archivos

- `src/MpvEngine.cs`: API C UTF-8, opciones de GPU, eventos y profundidad de color derivada del formato de píxel.
- `src/Hardening.cs`: protecciones de Windows aplicadas antes de cargar el motor.
- `src/PlayerForm.cs`: reproducción, ventana, listas, audio y subtítulos.
- `src/PlayerForm.Review.cs`: J/K/L, fotogramas, resumen, notas y guardado automático.
- `src/Markers.cs`: modelo de notas, tasa racional, importación/exportación XMEML, JSON y CSV.
- `src/MarkerPanel.cs`: editor de notas integrado.
- `src/ResolveBridge.cs` y `src/prisma_resolve.lua`: envío de notas a DaVinci Resolve Studio mediante su propio `fuscript.exe`.
- `src/SyncDelivery.cs`: entrega automática del .docx y el .prisma.json en una carpeta sincronizada.
- `src/SingleInstance.cs`: una sola ventana; los vídeos abiertos desde el Explorador llegan a ella.
- `src/Dialogs.cs`: diálogos de la interfaz.
- `Prisma/LEEME.txt`: guía de usuario y límites verificados.

## Validación

Compila Prisma primero y después ejecuta `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-tests.ps1` para descargar FFmpeg con huella fijada. Ejecuta `python .\tests\generate-fixtures.py` (Python 3) para generar los clips sintéticos.
`tests/run-tests.ps1` comprueba códecs, reproducción, pausa, búsqueda, final, listas, recuperación y preferencias. También comprueba que una lista M3U, un EDL o un manifiesto HLS no abren conexiones de red ni otros archivos.
`tests/run-review-tests.ps1` prueba la interfaz real: un fotograma por flecha, retroceso, pausa, velocidad, resumen, notas y tamaño de ventana. También valida XML/JSON/CSV, UTF-8, FPS fraccionarios, fórmulas neutralizadas en CSV y JSON sin rutas completas.
`tests/run-review-tests.ps1` comprueba además la carpeta sincronizada (documento con casillas, sin títulos «Nota N»), el código de tiempo escrito y el formato de envío a Resolve.
`tests/run-resolve-tests.ps1` ejecuta el script de Resolve con el `fuscript.exe` de DaVinci contra una línea de tiempo simulada: FPS, duplicados, fotogramas ocupados y notas fuera de rango.
`tests/RenderTests.cs` prueba la salida Direct3D 11/gpu-next con aceleración automática y fallback por CPU.
`tests/DesignPreview.cs` ofrece una vista manual con notas de ejemplo y datos aislados: F6 alterna tamaño normal/compacto y F7 abre/cierra el editor.

Los scripts escriben los informes en `tests/resultado.txt`, `tests/revision.txt` y `tests/renderizado.txt`.
La importación del XML generado por las pruebas se verificó en DaVinci Resolve 21.1.2 con vídeo, audio y notas en los fotogramas 0, 12 y 47. La validación interactiva de Premiere está pendiente.

## Decisiones y límites

- Los 10 bits se conservan en el motor; la salida final de pantalla depende del sistema. Nunca se presenta la profundidad del archivo como medición física de salida.
- Reverse shuttle: búsquedas exactas acotadas mientras el motor permanece pausado; sin audio. Evita el modo experimental de reproducción inversa de MPV y su elevado consumo de memoria con GOPs 4K. Puede saltar imágenes.
- XMEML exporta una secuencia nueva referenciada al render completo. No inserta marcadores en los archivos originales ni altera proyectos existentes.
- La importación XML solo usa marcadores de una secuencia, o de un único clip sin recortes, para evitar transformaciones temporales implícitas.
- XML bloquea resolución de entidades externas. Nombres de archivo y comandos llegan a libmpv como argumentos UTF-8, sin intérprete de shell.
- Los tiempos exportados usan fotogramas enteros y tasa racional; se presupone material CFR. El tiempo de origen es cero del render y el timecode de presentación es NDF.
- JSON conserva notas para intercambio completo. Resolve puede omitirlas al exportar XML FCP 7.

## Revisión en equipo

**Enviar a DaVinci Resolve (Ctrl+Mayús+R).** Añade las notas como marcadores rojos a la línea de tiempo abierta en Resolve, sin crear otra. Requiere DaVinci Resolve Studio con Preferencias > Sistema > General > Scripting externo (External scripting using) en **Local**. Los FPS de la línea de tiempo deben coincidir con los del vídeo, o no se importa nada. Enviar dos veces no duplica marcadores. Si un fotograma ya tiene un marcador, la nota pasa al siguiente fotograma libre. Las posiciones se cuentan desde el primer fotograma de la línea de tiempo, así que el render revisado debe empezar al principio de la línea de tiempo. Resolve gratuito no permite que otras aplicaciones lo controlen. En ese caso, Prisma lo detecta y ofrece exportar el XML, con los pasos para importarlo como línea de tiempo.

**Carpeta de Google Drive.** Cada vídeo recuerda su carpeta, que se elige con el botón «Carpeta de Drive» del panel de notas o desde Revisión > Carpeta de Google Drive para este vídeo. Es una carpeta que Google Drive para escritorio sincroniza en tu PC, por ejemplo `G:\Mi unidad\Revisiones` o una de Unidades compartidas (también sirven Dropbox u OneDrive). Cada vez que cambian las notas, Prisma escribe ahí «Ajustes Corto 12.docx» (título en negrita y una casilla de verificación por nota, sin los títulos «Nota N» que no se han cambiado) y «Corto 12.prisma.json», y Google Drive los sube. Si alguien editó el documento en la nube, Prisma conserva su versión con otro nombre antes de escribir la nueva. Si las notas no cambian, no reescribe nada. Prisma no se conecta a la nube: solo escribe en esa carpeta de tu equipo.

**Recibir las notas en otro equipo.** El editor descarga `Corto 12.prisma.json` de la carpeta y, con el mismo render abierto en su Prisma, pulsa Importar. Desde ahí puede enviarlas a su Resolve. Prisma exige que coincidan los FPS y que las notas quepan en la duración del vídeo.

**Una sola ventana.** Al abrir otro vídeo desde el Explorador, se reproduce en la ventana que ya está abierta y sustituye al anterior. Si abres varios a la vez, o en menos de dos segundos, forman una lista de reproducción, igual que con Archivo > Añadir a la lista (Ctrl+Mayús+O).

## Seguridad y privacidad

Prisma no se conecta a internet, no envía datos de uso y no contiene servidores ni telemetría. La única descarga ocurre al compilar: `build.ps1` obtiene libmpv de una URL fija y rechaza el archivo si su SHA-256 no coincide con el publicado. Además, comprueba la huella de `libmpv-2.dll` antes de copiarla.

En Mac, la compilación instala mpv y dylibbundler con Homebrew (GitHub Actions) y copia libmpv y sus librerías dentro de la app; no se descarga nada al usarla. Cada versión incluye `licencias/PROCEDENCIA-Mac.txt` con las versiones y licencias de esas librerías. La app lleva la firma local de siempre, con una excepción de macOS (`disable-library-validation`) necesaria para cargar esas librerías.

Protecciones en el reproductor:

- Solo abre los archivos que eliges, arrastras o recuperas de recientes, y siempre que existan. El motor no sigue referencias internas: listas M3U, montajes EDL y CUE, manifiestos HLS, archivos comprimidos ni capítulos enlazados. Un archivo malicioso no puede revelar tu IP abriendo una dirección de red ni enviar credenciales de Windows a una carpeta compartida remota.
- Lo que sueltas sobre el vídeo pasa por las mismas comprobaciones que el diálogo Abrir. Las direcciones web se ignoran.
- El motor arranca sin configuración, scripts, servidor IPC ni youtube-dl, y no muestra los vídeos en los controles multimedia de Windows ni en la pantalla de bloqueo.
- Windows deja de buscar DLL en la carpeta actual. Así, un DLL colocado junto a un vídeo no se carga al abrirlo. Si Prisma se ejecuta desde un disco local, tampoco se cargan DLL desde carpetas de red.
- El XML de notas no resuelve entidades externas. Además, los documentos importados tienen límites de tamaño, número de notas y longitud de texto.
- En CSV, las notas que empiezan por `=`, `+`, `-` o `@` llevan un apóstrofo delante para que Excel no las ejecute como fórmulas.
- Para enviar notas a Resolve, Prisma ejecuta el `fuscript.exe` de la carpeta de instalación de DaVinci Resolve con un script fijo incluido en Prisma. El texto de las notas viaja en un archivo de datos y nunca se ejecuta.
- La comunicación entre dos ventanas de Prisma usa una tubería con nombre que solo puede abrir tu usuario de Windows. Las rutas recibidas pasan las mismas comprobaciones que el diálogo Abrir.
- El JSON exportado solo incluye el nombre del vídeo, no la ruta completa. Para que DaVinci o Premiere enlacen el vídeo, el XML sí incluye la ruta completa, que puede mostrar el usuario de Windows y la estructura de carpetas.

Datos locales: `Prisma/datos/` guarda preferencias, recientes y notas, con las rutas de tus vídeos. Git ignora esa carpeta. No la incluyas si compartes una copia de Prisma. Para vaciar la lista, usa Archivo > Archivos recientes > Borrar lista de recientes.

Prisma no está firmado digitalmente. Compílalo desde este código o comprueba la huella de cualquier binario que recibas. Para comunicar una vulnerabilidad, consulta [SECURITY.md](SECURITY.md).

## Contribuir

Puedes abrir una incidencia para proponer una mejora o comunicar un problema. Los fallos de seguridad se comunican de forma privada, según [SECURITY.md](SECURITY.md). Para enviar código, crea un fork y abre una pull request con una descripción del cambio y de las pruebas que ejecutaste. Si adjuntas ejemplos, usa vídeos sintéticos y revisa las rutas y notas de los archivos exportados.

Prisma se publica bajo la [GPL-3.0 o posterior](LICENSE.txt). La [procedencia de libmpv](Prisma/licencias/PROCEDENCIA.txt) identifica el motor que descarga la compilación y sus licencias. En Mac, `PROCEDENCIA-Mac.txt` (dentro del zip) lista libmpv, FFmpeg, dav1d y el resto de librerías incluidas, con su versión, licencia y código fuente.

## Referencias

- [API y opciones de MPV](https://mpv.io/manual/stable/), incluidas las limitaciones de `play-direction` y la salida Direct3D 11.
- [Formato XML de Final Cut Pro 7](https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/FinalCutPro_XML/Topics/Topics.html).
- [Catálogo XMEML, incluidos los marcadores](https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/FinalCutPro_XML/Elements/Elements.html).
- [Adobe: diferencias entre XML y FCPXML](https://helpx.adobe.com/premiere/desktop/organize-media/import-files/migrate-from-final-cut-pro-x.html).
