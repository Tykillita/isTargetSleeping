# Preparación de isTargetSleeping 1.5.0

Preparada el **2 de octubre de 2026** en Windows x64, con .NET SDK 10.0.401 e Inno Setup 6.7.3.
Esta preparación no publica una release ni despliega la web. Ambas cosas se hicieron después, sobre el
commit `96d95cb` con la etiqueta `v1.5.0`: la release quedó publicada el 2 de octubre de 2026 (03:55 UTC)
y la web, con los SHA-256 de esos archivos, en el mismo push.

## Contenido

- Memoria ahora en Actividad: RAM física, compromiso, paginación y caché del sistema.
- Reglas de limpieza por porcentaje, intervalo, presión crítica y juegos con las zonas configuradas;
  pausa mínima y reintentos. Se retira la integración de Mem Reduct.
- Mascotas con limpieza propia, reacciones en la cama, nuevas animaciones de Mira y llama y paseo
  de la capibara en cocodrilo. Las limpiezas largas conservan el final de su animación.
- Cierre de la ventana de procesos con ✕/Esc, protecciones de limpieza y visibilidad de las mascotas corregidos.

## Comprobaciones realizadas

- `build.ps1 -Test`: **691 comprobaciones**, 625 de lógica/integración nativa y 66 de interfaz WPF;
  todas correctas. Incluye ajustes e historial anteriores, protocolos, finalización de procesos
  auxiliares de prueba, reglas, reintentos, memoria, mascotas, actualizador y UI en español/inglés.
- App y agente publicados para **x64 y ARM64**. Encabezados PE y versión 1.5.0 verificados en los cuatro ejecutables.
- Instaladores y ZIP de ambas arquitecturas generados. `verify-release.ps1 -Tag v1.5.0 -VerifyAssets`
  verifica exactamente ocho archivos y sus hashes en `artifacts/release-1.5.0`.
- Capturas de panel, Actividad y Ajustes regeneradas en modo demo para ambos idiomas, junto con las
  vistas previas de Ajustes. No se usan datos del usuario en estas imágenes.
- README inglés y español actualizados: funciones, estado de publicación, permisos, comandos, pruebas
  y estructura del proyecto. Web: portada, tarjetas, novedades, traducciones y aviso de versión en desarrollo.
- Web pública y cuatro descargas de 1.4.0 responden HTTP 200. Los hashes mostrados corresponden
  a esa release publicada, no a los paquetes locales de desarrollo.
- `git diff --check`, sintaxis JavaScript y referencias de código eliminadas comprobados.

Los registros de pruebas/empaquetado están en `artifacts/tests-1.5.0.log` y `artifacts/package-1.5.0.log`.
Los ocho archivos listos para adjuntar están en **`artifacts/release-1.5.0/`**; las notas extraídas
del changelog están en `artifacts/release-notes-1.5.0.md`. Estas salidas están ignoradas por Git.
Los paquetes anteriores de `dist/` se conservan; no deben mezclarse con los ocho archivos de esta versión.

## Publicación

Los cuatro pasos se cumpliendo en este orden:

1. `96d95cb` subido a `main` con la etiqueta **`v1.5.0`**; el workflow `prepare-release` pasó sus pruebas,
   generó los ocho archivos y creó el borrador. Los hashes de CI difieren de los paquetes locales, así que
   para la web se usaron los `.sha256` de los archivos efectivamente publicados.
2. Borrador revisado y publicado; las cuatro descargas de 1.5.0 responden 200.
3. `site/index.html`, `site/novedades.html` y `site/site.js` actualizados con los enlaces, los hashes y la
   fecha real, y sin avisos de desarrollo; en ambos README se retiró el aviso conservando la placa 1.5.0.
4. Ese push de documentación lo despliega el workflow de Firebase en <https://istargetsleeping.web.app>.

Hasta publicar la release, los botones conservan **1.4.0**, evitando enlaces a archivos todavía inexistentes.

## Alcance de la validación

ARM64 se compila y se inspecciona, pero no se ejecuta en hardware ARM64. No se instala el agente ni se ejecuta
limpieza real sobre las aplicaciones del equipo. UAC, instalación real y Windows 10 requieren una comprobación
interactiva en esos entornos. Las pruebas de finalización crean y cierran únicamente sus propios auxiliares.
El ensayo de muestreo de diez minutos de 1.4.0 se conserva en `docs/ram-process-validation.md` como resultado
histórico; no se presenta como una ejecución nueva de 1.5.0.
