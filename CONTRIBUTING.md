# Contributing to isTargetSleeping

**English** · [Español](#es)

Thanks for helping improve isTargetSleeping. Read the [workspace rules](CLAUDE.md) before changing the project.
The app is native Windows software built with C#/.NET 10, WPF and Win32.

## Report a bug or suggest a feature

Use [GitHub Issues](https://github.com/Tykillita/isTargetSleeping/issues). Search existing issues first, then
describe the expected behavior, what happened and the steps to reproduce it. Include the app version,
Windows version, x64/ARM64 architecture and, when relevant, the model engine and how it was installed.
Screenshots and a relevant excerpt from `%LOCALAPPDATA%\isTargetSleeping\Logs\app.log` can help; remove private
paths, usernames, tokens and other sensitive data before sharing them.

Report suspected vulnerabilities through the private channel in [SECURITY.md](SECURITY.md), rather than a public issue.

## Build and check a change

Use Windows 10 (2004 or later) or Windows 11, Git, PowerShell 7 (`pwsh`) and the .NET 10 SDK.
Inno Setup 6 is needed only to generate installers.
Python with Pillow and NumPy is optional for regenerating README illustrations.

```powershell
git clone https://github.com/Tykillita/isTargetSleeping.git
cd isTargetSleeping
.\build.ps1 -Test              # logic, native integration and WPF checks
.\build.ps1                    # self-contained x64/ARM64 app for this PC
.\build.ps1 -Arch arm64        # cross-build for Windows ARM64
.\package.ps1 -RequireInstaller # x64 and ARM64 ZIPs/installers with SHA-256 (requires Inno Setup)
```

The test benches include native integration and WPF checks, so run them on Windows. Compiling ARM64 on x64 does
not replace testing on an ARM64 PC. For UI changes, check Spanish and English, keyboard access and display scaling.
Use test-created processes to test termination, and a controlled environment for elevated cleanup.

## Submit a pull request

Fork the repository, create a descriptive branch and keep changes focused. Explain the problem, the resulting
behavior and how you checked it. Add meaningful regression checks for behavior changes and report any validation
you could not perform. Keep unrelated changes out of the pull request. Contributions use the project's [MIT license](LICENSE).

For commits that change the app (`src/`, `tests/`, `packaging/`, `build.ps1` or `package.ps1`):

- Update `VERSION` once per commit, using the largest applicable SemVer change: PATCH for fixes, MINOR for
  features or small behavior changes, MAJOR for major redesigns, rewrites or breaking changes.
- Keep `[Unreleased]` empty when recording a new version in `CHANGELOG.md`, with a dated version entry.
- Update both README files and the website's version information, feature text, release notes and English
  translations together. Follow the full checklist in [CLAUDE.md](CLAUDE.md).
- Mark a version still awaiting publication as in development. Keep website downloads pointed at published
  assets; switch their links, date and actual SHA-256 values only after the release exists.

Commits limited to documentation, website or CI changes do not increase the app version. Before submitting,
run the relevant checks and `git diff --check`. See the README's development section for the release workflow.

---

<a id="es"></a>

# Contribuir a isTargetSleeping

[English](#contributing-to-istargetsleeping) · **Español**

Gracias por ayudar a mejorar isTargetSleeping. Lee las [reglas del workspace](CLAUDE.md) antes de cambiar el proyecto.
La app es software nativo de Windows, desarrollado con C#/.NET 10, WPF y Win32.

## Reportar un fallo o sugerir una función

Usa [GitHub Issues](https://github.com/Tykillita/isTargetSleeping/issues). Busca primero si ya se ha reportado;
después explica el comportamiento esperado, lo que ocurrió y los pasos para reproducirlo. Incluye la versión de
la app, de Windows, la arquitectura x64/ARM64 y, si corresponde, el motor de modelos y cómo lo instalaste.
Pueden ayudar capturas y un fragmento relevante de `%LOCALAPPDATA%\isTargetSleeping\Logs\app.log`; elimina rutas
privadas, nombres de usuario, tokens y otros datos sensibles antes de compartirlos.

Reporta posibles vulnerabilidades por el canal privado de [SECURITY.md](SECURITY.md#es), en lugar de un issue público.

## Compilar y comprobar un cambio

Usa Windows 10 (2004 o posterior) o Windows 11, Git, PowerShell 7 (`pwsh`) y el SDK de .NET 10.
Inno Setup 6 solo hace falta para generar
instaladores. Python con Pillow y NumPy es opcional para regenerar las ilustraciones del README.

```powershell
git clone https://github.com/Tykillita/isTargetSleeping.git
cd isTargetSleeping
.\build.ps1 -Test              # comprobaciones de lógica, integración nativa y WPF
.\build.ps1                    # app autocontenida x64/ARM64 para este PC
.\build.ps1 -Arch arm64        # compilación cruzada para Windows ARM64
.\package.ps1 -RequireInstaller # ZIPs/instaladores x64 y ARM64 con SHA-256 (requiere Inno Setup)
```

Los bancos incluyen integración nativa e interfaz WPF: ejecútalos en Windows. Compilar ARM64 en x64 no sustituye
probar en un PC ARM64. Si cambias la interfaz, comprueba español e inglés, teclado y escalado de pantalla.
Usa procesos creados por las pruebas para comprobar finalización y un entorno controlado para limpieza elevada.

## Enviar una propuesta de cambios

Haz un fork, crea una rama descriptiva y mantén los cambios centrados en su objetivo. Explica el problema,
el comportamiento resultante y cómo lo comprobaste. Añade pruebas de regresión útiles cuando cambie el
comportamiento e indica las comprobaciones que no pudiste hacer. Excluye cambios ajenos de la propuesta.
Las contribuciones usan la [licencia MIT](LICENSE) del proyecto.

Para commits que cambien la app (`src/`, `tests/`, `packaging/`, `build.ps1` o `package.ps1`):

- Actualiza `VERSION` una sola vez por commit, con el mayor salto SemVer que corresponda: PATCH para arreglos,
  MINOR para funciones o cambios pequeños y MAJOR para grandes rediseños, reescrituras o rupturas.
- Deja `[Unreleased]` vacío al registrar una versión nueva en `CHANGELOG.md`, con su entrada fechada.
- Actualiza ambos README y la información de versión, funciones, novedades y traducciones inglesas de la web
  en el mismo trabajo. Sigue la lista completa de [CLAUDE.md](CLAUDE.md).
- Marca como «en desarrollo» una versión pendiente de publicación. La web debe descargar archivos publicados;
  cambia enlaces, fecha y SHA-256 reales solo cuando exista la release.

Los commits limitados a documentación, web o CI no suben la versión de la app. Antes de enviar los cambios,
ejecuta las comprobaciones pertinentes y `git diff --check`. El apartado de desarrollo del README explica el flujo de releases.
