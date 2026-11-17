# Prisma

Reproductor y herramienta de revisión de vídeo para Windows de 64 bits, en español. Reproduce H.264, HEVC y ProRes estándar, incluidos formatos de 10 bits. Permite avanzar fotograma a fotograma, tomar notas con código de tiempo y exportar marcadores a DaVinci Resolve o Premiere mediante XML.

La versión 0.3 renueva la interfaz oscura: controles vectoriales, tarjetas de revisión, editor de notas a panel completo y distribución compacta. Conserva el motor y el formato de las notas existentes.

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
- `src/PlayerForm.cs`: reproducción, ventana, listas, audio y subtítulos.
- `src/PlayerForm.Review.cs`: J/K/L, fotogramas, resumen, notas y guardado automático.
- `src/Markers.cs`: modelo de notas, tasa racional, importación/exportación XMEML, JSON y CSV.
- `src/MarkerPanel.cs`: editor de notas integrado.
- `Prisma/LEEME.txt`: guía de usuario y límites verificados.

## Validación

Compila Prisma primero y después ejecuta `powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\prepare-tests.ps1` para descargar FFmpeg con huella fijada. Ejecuta `python .\tests\generate-fixtures.py` (Python 3) para generar los clips sintéticos.
`tests/run-tests.ps1` comprueba códecs, reproducción, pausa, búsqueda, final, listas, recuperación y preferencias.
`tests/run-review-tests.ps1` prueba la interfaz real: un fotograma por flecha, retroceso, pausa, velocidad, resumen, notas y tamaño de ventana. También valida XML/JSON/CSV, UTF-8 y FPS fraccionarios.
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

## Contribuir

Puedes abrir una incidencia para proponer una mejora o comunicar un problema. Para enviar código, crea un fork y abre una pull request con una descripción del cambio y de las pruebas que ejecutaste. Si adjuntas ejemplos, usa vídeos sintéticos y revisa las rutas y notas de los archivos exportados.

Prisma se publica bajo la [GPL-3.0 o posterior](LICENSE.txt). La [procedencia de libmpv](Prisma/licencias/PROCEDENCIA.txt) identifica el motor que descarga la compilación y sus licencias.

## Referencias

- [API y opciones de MPV](https://mpv.io/manual/stable/), incluidas las limitaciones de `play-direction` y la salida Direct3D 11.
- [Formato XML de Final Cut Pro 7](https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/FinalCutPro_XML/Topics/Topics.html).
- [Catálogo XMEML, incluidos los marcadores](https://developer.apple.com/library/archive/documentation/AppleApplications/Reference/FinalCutPro_XML/Elements/Elements.html).
- [Adobe: diferencias entre XML y FCPXML](https://helpx.adobe.com/premiere/desktop/organize-media/import-files/migrate-from-final-cut-pro-x.html).
