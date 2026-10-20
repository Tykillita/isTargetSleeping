# Reglas del workspace

Lee y aplica [CLAUDE.md](CLAUDE.md) antes de trabajar en este repositorio. El usuario ha adoptado ese documento como reglas compartidas del workspace, también para Codex.

Respeta sus instrucciones de versión, documentación bilingüe, web y commits. Conserva los cambios ajenos al trabajo solicitado y no añadas líneas de atribución de agentes.

## En cada versión nueva (obligatorio, para cualquier agente)

Si tu trabajo sube la versión de la app, **tú** actualizas en el mismo trabajo, sin dejarlo para otro agente ni para el usuario:

1. **README:** `README.md` y `README.es.md`, la placa de versión y las funciones nuevas o cambiadas, con el mismo contenido en los dos idiomas.
2. **Web:** `site/index.html` (versión, descargas y las funciones nuevas), `site/novedades.html` (la entrada de la versión) y `site/site.js` (la traducción inglesa de todo texto nuevo). Mientras la release no esté publicada, la versión figura como «en desarrollo» y no se despliega con enlaces de descarga a ella.
3. **Informe:** en tu mensaje final di explícitamente qué cambiaste en la web (archivos, secciones y textos o tarjetas añadidos), si ya está desplegada en https://istargetsleeping.web.app o por qué no todavía, y qué cambiaste en los README. Si algo no se pudo actualizar, dilo y explica por qué.

El detalle completo está en la sección «Obligatorio en cada versión nueva» de [CLAUDE.md](CLAUDE.md).

## Revisión visual obligatoria de WPF

Antes de cerrar cualquier cambio visible, ejecuta la app o el arnés visual y revisa capturas reales del estado afectado;
compilar o leer XAML no basta. Incluye español e inglés, tema normal y alto contraste, escalas 100/125/150 % cuando aplique,
y estados hover, foco, selección, deshabilitado y abierto, incluidos los popups. No uses controles nativos sin tema dentro
del cristal: revisa que las listas desplegables respeten los colores de la app, no queden blancas, no tapen contenido de forma
confusa y no se salgan, recorten ni solapen. Si no fue posible inspeccionar la interfaz, declara la limitación y no digas
que quedó visualmente verificada. Aplica también la sección completa de revisión visual de [CLAUDE.md](CLAUDE.md).
