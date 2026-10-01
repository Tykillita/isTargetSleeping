# Revisión de isTargetSleeping 1.3.0

## Flujo de publicación

1. `VERSION` y la etiqueta deben ser `1.3.0` / `v1.3.0`, con notas en el changelog.
2. GitHub ejecuta las pruebas y compila para Windows x64 y ARM64.
3. Deben existir exactamente cuatro paquetes (portable ZIP e instalador EXE de cada arquitectura)
   y cuatro comprobantes SHA-256 válidos.
4. El proceso crea un borrador con esos ocho archivos y las notas. Una publicación existente nunca se modifica.
5. Revisar los paquetes y publicar desde GitHub. La app solo ofrecerá versiones estables publicadas.

## Comprobaciones automatizadas

El banco de pruebas usa un servidor local y un ejecutable auxiliar de prueba. No cierra la app del usuario
ni instala el agente de memoria. Comprueba:

- Versiones iguales, inferiores, superiores e inválidas; borradores y versiones preliminares.
- Paquetes x64/ARM64, SHA ausente, direcciones ajenas al repositorio, EXE de versión/arquitectura incorrectas.
- Sin versiones publicadas, error de conexión, error del servidor, límites 429/403 y espera de GitHub.
- Descarga válida, cancelación después de recibir bytes, hash incorrecto, ZIP corrupto, rutas externas y EXE duplicado.
- Respaldo hasta confirmar el arranque, reemplazo correcto, bloqueo temporal, errores de permisos y recuperación.
- Un proceso real confirma su inicio; un inicio fallido restaura y vuelve a abrir la versión anterior.
- Búsqueda inicialmente activada y conservación de `updateCheck: false`, repositorio personalizado e historial.

Los textos y botones de oferta, progreso y ajustes se revisaron en español e inglés mediante capturas de la app.
Ambas arquitecturas se compilan y sus encabezados PE/versiones se comprueban. La compilación incluye su agente
de memoria; si el agente instalado es de otra versión, Ajustes ofrece actualizarlo por separado y solicita UAC.

## Revisión manual antes de publicar

La comprobación local se realiza en Windows 11 x64. Windows 10 y la ejecución nativa ARM64 requieren sus equipos
correspondientes; compilar y comprobar un encabezado ARM64 no demuestra que su interfaz funcione en ese equipo.

- Abrir portable e instalador en Windows 10 y Windows 11, incluido ARM64 cuando esté disponible.
- Probar en una instalación de usuario y en una ubicación portable; comprobar que los ajustes y el historial
  siguen presentes tras una actualización real desde una versión anterior.
- Desactivar búsqueda automática y usar la búsqueda manual desde Ajustes y la bandeja.
- Probar una carpeta sin permisos de escritura y confirmar que se ofrece la descarga manual.
- Probar la actualización separada de un agente antiguo con su permiso de administrador.
- Revisar las notas y archivos del borrador antes de pulsar **Publish release**.

Las instalaciones antiguas sin repositorio configurado necesitan una primera actualización manual a 1.3.0.
