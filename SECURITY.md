# Política de seguridad

## Versiones con soporte

Solo se corrige la última versión de la rama `main`. Las correcciones de seguridad no se aplican a versiones anteriores.

## Cómo comunicar una vulnerabilidad

Usa el aviso privado de GitHub: pestaña **Security** > **Report a vulnerability**. El informe solo lo ven los responsables del proyecto hasta que se publique la corrección.

No abras una incidencia pública con detalles que permitan explotar el fallo. Si puedes, indica:

- versión de Prisma y de Windows;
- pasos para reproducir el problema;
- un archivo de ejemplo sintético que no contenga material real ni datos personales.

## Alcance

Entran en el alcance los fallos de Prisma, como su código, los scripts de compilación y las pruebas, y la forma en que configura libmpv, también en Mac (libmpv, FFmpeg y dav1d incluidos en la app para el AV1). Los fallos de los decodificadores de libmpv o FFmpeg se comunican a esos proyectos. Aquí, avisa si Prisma necesita actualizar el motor.

## Medidas actuales

- Prisma no se conecta a internet ni contiene telemetría.
- El motor no sigue referencias de otros archivos, como M3U, EDL, CUE, HLS, comprimidos o capítulos enlazados. Tampoco carga configuración, scripts ni servidor IPC.
- La búsqueda de DLL de Windows excluye la carpeta actual. Si Prisma se ejecuta desde un disco local, tampoco carga DLL desde carpetas de red.
- La importación XML no resuelve entidades externas y limita tamaño, número de notas y longitud.
- La exportación CSV neutraliza fórmulas. La exportación JSON no incluye rutas completas.
- El envío a DaVinci Resolve ejecuta el `fuscript.exe` de la instalación de Resolve con un script fijo. Las notas viajan como datos y nunca como código.
- La comunicación entre ventanas de Prisma usa una tubería con nombre que solo puede abrir el usuario de Windows que la creó.
- La compilación descarga el motor de una URL fija y verifica la huella SHA-256 del archivo y de `libmpv-2.dll`.

- En Mac, libmpv se carga solo desde `Prisma.app/Contents/Frameworks` y se abre con las mismas opciones: sin configuración del usuario, sin atajos ni scripts de mpv. Las librerías llevan firma local (sin cuenta de Apple), por eso la app declara `disable-library-validation`; la compilación comprueba que no se carga ninguna librería de Homebrew.

Prisma no está firmado digitalmente. Para usarlo con garantías, compílalo desde este repositorio.
