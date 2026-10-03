# isTargetSleeping — reglas del workspace

App de bandeja para Windows (C# .NET 10, WPF + Win32) y su web estática en `site/`.

## Obligatorio en cada versión nueva: web, README e informe

Estas reglas son para cualquier agente que trabaje aquí (Claude, Codex u otro). **El agente que crea una versión
nueva es quien actualiza la web y los README, en el mismo trabajo; no se deja para otro agente ni para el usuario.**

Cuando se sube la versión (ver la tabla de abajo), el agente debe:

1. **README** — en `README.md` (inglés) **y** `README.es.md` (español), con el mismo contenido:
   - la placa `badge/version-x.y.z`;
   - las funciones nuevas o cambiadas en la tabla de funciones y en su sección (uso, cómo funciona, privacidad…),
     y cualquier dato que haya dejado de ser cierto.
2. **Web** — en `site/`:
   - `site/index.html`: versión (cabecera, nota bajo los botones de descarga, pie), enlaces de descarga y de
     notas de la versión, y una tarjeta o texto para cada función nueva que se vea en la app;
   - `site/novedades.html`: la entrada de la versión (ver «Página de novedades»);
   - `site/site.js`: la traducción inglesa de todo texto nuevo o cambiado (ninguna clave `data-i18n` sin traducir);
   - comprobar la web a 375, 768 y 1280 px, en claro y oscuro, en español e inglés.
3. **Publicación** — mientras la release `vX.Y.Z` no esté publicada en GitHub, la versión figura como
   «en desarrollo» en novedades y **no** se despliega la web con enlaces de descarga a esa versión (darían 404).
   Al publicarse la release: SHA-256 reales de sus `.sha256`, fecha en novedades, comprobar que cada enlace de
   descarga responde, y desplegar (el push a `main` lo hace solo; si no, `firebase deploy --only hosting`).
4. **Informe al usuario** — al terminar, el mensaje final debe decir **explícitamente qué cambió en la web**:
   qué archivos y secciones se tocaron, qué textos o tarjetas se añadieron, si ya está desplegada en
   https://istargetsleeping.web.app (o por qué no todavía) y qué falta. Igual para los README. Si algo de la web o
   de los README no se pudo actualizar, se dice y se explica por qué; nunca se omite en silencio.

## Versión global: se sube en cada commit con cambios de la app

Formato `MAJOR.MINOR.PATCH` (SemVer, lo exigen `package.ps1` y `packaging/verify-release.ps1`).
Antes de commitear cambios de la app (`src/`, `packaging/`, `build.ps1`, `package.ps1`, `tests/`), sube la
versión según el tamaño del cambio:

| Cambio | Salto | Ejemplo |
|---|---|---|
| Feature nueva o cambio pequeño | MINOR | 1.3.0 → 1.4.0 |
| Cambio muy grande (rediseño, ruptura, reescritura) | MAJOR | 1.3.0 → 2.0.0 |
| Solo arreglos de bugs | PATCH | 1.3.0 → 1.3.1 |

Un solo salto por commit (el mayor que aplique). Los commits que solo tocan docs, la web o el CI no suben la
versión.

Al subirla, actualiza **todo a la vez**:

- `VERSION` — única fuente: los `.csproj` leen `<Version>` de este archivo y el instalador la recibe de `package.ps1`.
- `README.md` y `README.es.md`: la placa de versión (`badge/version-x.y.z`) y el contenido de las funciones
  nuevas (ver «Obligatorio en cada versión nueva»).
- `CHANGELOG.md` (Keep a Changelog): mueve `[Unreleased]` a `## [x.y.z] — AAAA-MM-DD` y deja un `[Unreleased]` vacío.
- Web: las cadenas "Versión x.y.z" de `site/index.html` y del pie de `site/index.html` y `site/novedades.html`
  (clave `pie.version`), su traducción en el diccionario `EN` de `site/site.js`, los enlaces
  `releases/download/vX.Y.Z/isTargetSleeping-X.Y.Z-…` (también los botones «Descargar» de `site/novedades.html`) y el de "Notas de la versión" (`releases/tag/vX.Y.Z`). Los SHA-256 de `site/index.html` solo
  cambian cuando hay paquetes nuevos en `dist/` (`.sha256` junto a cada archivo).
- `site/novedades.html` (ver abajo).

## Página de novedades: cada versión o feature nueva se documenta

`site/novedades.html` es la documentación pública de cambios, de la versión más nueva a la más antigua.

- Cada versión nueva lleva su `<article class="version" id="vX-Y-Z">` con fecha, tipo de salto
  (`Grande` / `Feature` / `Arreglo`) y sus features en tarjetas agrupadas en **Nuevo**, **Cambios** y **Arreglos**.
- Cada feature nueva (de la app o de la web) se añade en el mismo commit que la introduce. Si aún no hay
  versión nueva, va al bloque `#proxima` ("Próxima versión"), que pasa a ser la versión al subirla.
- Bilingüe: español en el HTML con `data-i18n="nov.…"`, inglés en el diccionario `EN` de `site/site.js`.
  Ninguna clave `data-i18n` puede quedar sin traducir.
- Textos propios y breves, orientados a quien usa la app; el detalle técnico vive en `CHANGELOG.md`.

## Web (`site/`)

- `site/build.ps1` copia a `site/media/` (ignorado por git) las imágenes y videos de `Assets/` y `docs/`; no dupliques
  archivos en `site/`. Vista previa: servidor `site` de `.claude/launch.json` (puerto 8765). Para publicar,
  `site/build.ps1 -Out _site` genera el sitio completo con URLs versionadas por el contenido de sus assets;
  Firebase publica `_site/`, también ignorado por git. No edites esa salida: modifica las fuentes y regenera.
- Tema claro/oscuro con tokens en `:root` de `site/site.css` y `data-theme` en `<html>`; idioma con `data-i18n`.
- Comprueba a 375, 768 y 1280 px, en claro y oscuro, en español e inglés.
- Se publica en **Firebase Hosting** (proyecto `istargetsleeping`, https://istargetsleeping.web.app):
  `firebase deploy --only hosting`. El `predeploy` de `firebase.json` ejecuta `site/build.ps1`.
  `.github/workflows/firebase-hosting.yml` despliega en cada push a `main` que toque la web, si existe el secreto
  `FIREBASE_SERVICE_ACCOUNT_ISTARGETSLEEPING`.
- `site/firebase.js` inicializa la app web de Firebase («isTargetSleeping Web»). Solo Firebase App: no añadas
  Analytics ni otros servicios con seguimiento sin pedirlo.

## Commits

- Respeta estas reglas también al usar la skill `/commit`.
- Sin líneas de atribución a Claude en commits, PRs ni archivos.
