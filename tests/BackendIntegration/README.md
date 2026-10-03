# Ollama backend integration / Integración de mecanismos de Ollama

## English

These opt-in integration checks run the actual published app's `--status`, `--on`
and `--off` against genuine Ollama 0.35.1 on an isolated elevated Windows runner.
They exercise Windows SCM and Task Scheduler, not replacements for those APIs.
The workflow pins the official Ollama archive and verifies its SHA-256.

Run `.github/workflows/backend-integration.yml` on GitHub Actions. It builds the
app and the small native SCM test host, creates exclusive manual/no-trigger
fixtures on loopback ports 59152/59153, and checks two complete start/stop cycles,
the API, empty model lists, PID/creation identities, repeated actions and cleanup.
The task also checks termination of its wrapper and auxiliary child. JSON reports
and logs are retained in the `backend-integration-evidence` artifact.

**Use a disposable Windows VM.** The service requires an elevated token and runs
as LocalService; the CI task runs as SYSTEM without stored credentials. Both are
removed in `finally`. Tests refuse to perform full stop actions when unrelated
Ollama processes are present. They never load/download models or clean RAM.
`build.ps1 -Test` remains the ordinary suite and does not install these fixtures.

This verifies the tested OS, architecture and fixture configuration. It does not
certify every NSSM/WinSW configuration, physical GPUs, inference workloads or
interactive UAC acceptance/cancellation. A CI Windows VM is not the user's PC.

## Español

Estas pruebas de integración optativas ejecutan `--status`, `--on` y `--off` de
la app publicada con Ollama 0.35.1 real en un entorno Windows elevado y aislado.
Comprueban SCM y el Programador de tareas de Windows, sin sustituir sus APIs.
El workflow fija el paquete oficial de Ollama y verifica su SHA-256.

Ejecuta `.github/workflows/backend-integration.yml` en GitHub Actions. Compila la
app y un pequeño host nativo de prueba para SCM, crea recursos exclusivos de
arranque manual/sin disparadores en los puertos locales 59152/59153 y verifica
dos ciclos completos, la API, listas de modelos vacías, identidad PID/hora,
órdenes repetidas y limpieza. La tarea comprueba también la salida del wrapper
y su hijo auxiliar. Los informes JSON y logs quedan en el artefacto
`backend-integration-evidence`.

**Usa una VM Windows desechable.** El servicio exige permisos elevados y funciona
como LocalService; la tarea de CI usa SYSTEM sin guardar credenciales. Ambos se
eliminan en `finally`. Las pruebas rechazan el apagado completo si hay procesos
Ollama ajenos. No descargan/cargan modelos ni limpian RAM. `build.ps1 -Test` sigue
siendo la suite habitual y no instala estos recursos.

Esto verifica el sistema, arquitectura y configuración probados. No certifica
todas las configuraciones NSSM/WinSW, GPU físicas, inferencias ni la aceptación
o cancelación interactiva de UAC. La VM Windows de CI no es el PC del usuario.
