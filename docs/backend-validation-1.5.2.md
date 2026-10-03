# Backend validation — isTargetSleeping 1.5.2

[Español](#es) · [English](#en)

<a id="en"></a>

## English

Both Windows service and scheduled task backends passed integration tests with
the genuine Ollama server on **2026-10-03**. These tests ran on an isolated
GitHub Actions Windows virtual machine and exercised the application's actual
`--status`, `--on` and `--off` commands, including backend detection and `Switch`.

The evidence is [run 37136362486](https://github.com/Tykillita/isTargetSleeping/actions/runs/37136362486),
completed successfully at **16:21:09 UTC**, for commit
[`0a7a3e742c0e69436f863810f0f39de9278ddf62`](https://github.com/Tykillita/isTargetSleeping/commit/0a7a3e742c0e69436f863810f0f39de9278ddf62).
The downloaded `backend-integration-evidence` artifact was inspected: both JSON
reports contain `Passed: true`, with 48 successful service checks and 37
successful task assertions.

### Environment and binary identities

| Component | Observed value |
|---|---|
| Runner | GitHub-hosted `windows-2025`, isolated VM |
| Windows | Microsoft Windows Server 2025 Datacenter, version `10.0.26100`, build `26100`, 64-bit |
| PowerShell | `7.6.6` |
| Application | isTargetSleeping `1.5.2`, x64; EXE FileVersion `1.5.2.0`, built from the commit above |
| Ollama API version | `0.35.1` in all four cycles |
| Official Ollama archive | [`ollama-windows-amd64.zip`, v0.35.1](https://github.com/ollama/ollama/releases/download/v0.35.1/ollama-windows-amd64.zip) |

SHA-256 values recorded in `environment.json`:

```text
Official Ollama archive:
dc50b9ca7f9023c86525012632cd1615b093d0407987444a7f62ecab617e8e93

Ollama executable extracted from that archive:
3cc17c238b081adf2bd3a206e52a063b637af1bf283d95b4ffd73a2bdec8b027

Application executable tested in this CI run:
77de7e38f5af7b28b927712c5e6277a7f4f0850d395541a24b3f9d8b34a78a6e
```

The application digest identifies this run's EXE, rather than an installer or ZIP
from a release. The service fixture's copied Ollama EXE matched the original EXE
digest byte for byte.

### Service Control Manager results

The manual service
`isTargetSleeping-BackendVerification-Service-3428d849bfa8` ran a native SCM
fixture under `NT AUTHORITY\LocalService`, with private models, profile and temp
directories. The genuine server listened only on `127.0.0.1:59152`.

| Cycle | Ollama PID | Creation time, UTC | Start observation | Stop observation |
|---|---:|---|---:|---:|
| 1 | 2924 | `2026-10-03T16:20:18.0786399Z` | 1028 ms | 3050 ms |
| 2 | 7420 | `2026-10-03T16:20:30.5024828Z` | 915 ms | 3030 ms |

Both cycles verified exact backend detection, SCM Running/Stopped states,
HTTP `/api/version`, an empty `/api/tags` result and ownership of the listening
port by the recorded PID. Repeated start preserved the same process identity;
repeated stop succeeded. A restart-on-failure action with a one-second delay was
configured, and neither manual SCM stop caused a restart during the subsequent
three-second observation.

All CLI calls returned exit code 0. Cleanup confirmed deletion of this service,
exit of the owned Ollama children, closure of the API, restoration of fixture
ACLs and an unchanged process/listener baseline.

### Task Scheduler results

The task `\isTargetSleeping-BackendVerification-Task-185f583a` had no triggers
and ran as `SYSTEM` (`LogonType: 5`, `RunLevel: 1`). Its action launched a
PowerShell wrapper and the genuine `ollama serve`, using private models and
`127.0.0.1:59153`.

| Cycle | Ollama PID | Creation time, UTC | Wrapper PID | Observed Ollama descendant | Start / stop observation |
|---|---:|---|---:|---|---|
| 1 | 9452 | `2026-10-03T16:20:47.9376361Z` | 4300 | `conhost.exe`, PID 1036 | 1771 / 3081 ms |
| 2 | 8224 | `2026-10-03T16:20:56.8257702Z` | 7388 | `conhost.exe`, PID 8424 | 1078 / 3021 ms |

The CLI selected this exact task before and while running. Both cycles verified
Task Scheduler state, API version, zero models, PID/creation identities, a fresh
identity on the second start, repeated start and repeated stop. After stopping,
the task was inactive and the wrapper, Ollama server and observed descendant
identities had all exited; the API was offline. All CLI calls returned exit code
0. Cleanup removed the task successfully.

### Scope, cleanup and reproduction

The initial and final snapshots for both backends contained no Ollama/app
processes and no listener on port 11434. The server models remained empty. These
tests therefore establish detection and lifecycle operation for the fixtures
above on the recorded Windows VM.

They do **not** establish model inference, CPU/GPU runner termination, GPU/VRAM
behavior, ARM64 execution, interactive UAC acceptance or cancellation, coexistence with another
Ollama instance, every third-party service wrapper, or operation on this user's
physical Windows 11 PC. Only `ollama.exe` was extracted; logs record missing
`llama-server.exe` during GPU discovery. No inference or model loading was
attempted, and this discovery message did not prevent the API/lifecycle checks.

The [workflow](../.github/workflows/backend-integration.yml) and
[fixtures](../tests/BackendIntegration) reproduce this validation on an isolated
administrator runner. Dispatch `backend-integration` through GitHub Actions,
then inspect its `backend-integration-evidence` artifact: `environment.json`,
`service/service-*.report.json`, task `report.json`, manifest and server/host
logs. The workflow retains that artifact for 30 days; a new run produces fresh
evidence. The fixtures are intended for that isolated environment.

<a id="es"></a>

## Español

Los mecanismos de servicio de Windows y tarea programada superaron pruebas de
integración con el servidor real de Ollama el **2026-10-03**. Las pruebas se
ejecutaron en una máquina virtual Windows aislada de GitHub Actions y utilizaron
los comandos reales `--status`, `--on` y `--off` de la aplicación, incluyendo la
detección del mecanismo y `Switch`.

La evidencia corresponde a la
[ejecución 37136362486](https://github.com/Tykillita/isTargetSleeping/actions/runs/37136362486),
completada correctamente a las **16:21:09 UTC**, con el commit
[`0a7a3e742c0e69436f863810f0f39de9278ddf62`](https://github.com/Tykillita/isTargetSleeping/commit/0a7a3e742c0e69436f863810f0f39de9278ddf62).
Se descargó y revisó el artefacto `backend-integration-evidence`: ambos informes
JSON contienen `Passed: true`, con 48 comprobaciones correctas del servicio y 37
aserciones correctas de la tarea.

### Entorno e identidad de los binarios

| Componente | Valor observado |
|---|---|
| Runner | `windows-2025` hospedado por GitHub, VM aislada |
| Windows | Microsoft Windows Server 2025 Datacenter, versión `10.0.26100`, compilación `26100`, 64 bits |
| PowerShell | `7.6.6` |
| Aplicación | isTargetSleeping `1.5.2`, x64; FileVersion del EXE `1.5.2.0`, compilado desde el commit anterior |
| Versión de la API de Ollama | `0.35.1` en los cuatro ciclos |
| Archivo oficial de Ollama | [`ollama-windows-amd64.zip`, v0.35.1](https://github.com/ollama/ollama/releases/download/v0.35.1/ollama-windows-amd64.zip) |

Valores SHA-256 registrados en `environment.json`:

```text
Archivo oficial de Ollama:
dc50b9ca7f9023c86525012632cd1615b093d0407987444a7f62ecab617e8e93

Ejecutable de Ollama extraído de ese archivo:
3cc17c238b081adf2bd3a206e52a063b637af1bf283d95b4ffd73a2bdec8b027

Ejecutable de la aplicación probado en esta ejecución de CI:
77de7e38f5af7b28b927712c5e6277a7f4f0850d395541a24b3f9d8b34a78a6e
```

El hash de la aplicación identifica el EXE de esta ejecución, distinto del
instalador o ZIP de una release. La copia del EXE de Ollama usada por el servicio
coincidió byte a byte con el hash del EXE original.

### Resultados del Administrador de control de servicios

El servicio manual
`isTargetSleeping-BackendVerification-Service-3428d849bfa8` ejecutó un fixture
SCM nativo con `NT AUTHORITY\LocalService`, usando directorios privados para
modelos, perfil y archivos temporales. El servidor real escuchó únicamente en
`127.0.0.1:59152`.

| Ciclo | PID de Ollama | Hora de creación, UTC | Observación de arranque | Observación de parada |
|---|---:|---|---:|---:|
| 1 | 2924 | `2026-10-03T16:20:18.0786399Z` | 1028 ms | 3050 ms |
| 2 | 7420 | `2026-10-03T16:20:30.5024828Z` | 915 ms | 3030 ms |

Los dos ciclos comprobaron la detección del mecanismo exacto, los estados SCM
Running/Stopped, HTTP `/api/version`, una respuesta vacía de `/api/tags` y que el
PID registrado era propietario del puerto. El arranque repetido conservó la
misma identidad de proceso; la parada repetida fue correcta. Se configuró una
acción de reinicio ante fallo con un segundo de demora, y ninguna parada manual
por SCM provocó un reinicio durante los tres segundos de observación posteriores.

Todas las llamadas CLI devolvieron código de salida 0. La limpieza confirmó la
eliminación de este servicio, la salida de sus procesos hijos de Ollama, el
cierre de la API, la restauración de las ACL del fixture y la conservación del
estado inicial de procesos y puertos.

### Resultados del Programador de tareas

La tarea `\isTargetSleeping-BackendVerification-Task-185f583a` no tenía
desencadenadores y se ejecutó como `SYSTEM` (`LogonType: 5`, `RunLevel: 1`). Su
acción lanzó un wrapper PowerShell y el servidor real `ollama serve`, con modelos
privados y `127.0.0.1:59153`.

| Ciclo | PID de Ollama | Hora de creación, UTC | PID del wrapper | Descendiente de Ollama observado | Observación de arranque / parada |
|---|---:|---|---:|---|---|
| 1 | 9452 | `2026-10-03T16:20:47.9376361Z` | 4300 | `conhost.exe`, PID 1036 | 1771 / 3081 ms |
| 2 | 8224 | `2026-10-03T16:20:56.8257702Z` | 7388 | `conhost.exe`, PID 8424 | 1078 / 3021 ms |

La CLI seleccionó esta tarea exacta antes y durante su ejecución. Los dos ciclos
comprobaron el estado del Programador de tareas, la versión de la API, cero
modelos, las identidades PID/hora de creación, una identidad nueva en el segundo
arranque y las órdenes repetidas de arranque y parada. Al detenerla, la tarea
quedó inactiva y salieron el wrapper, el servidor de Ollama y las identidades de
los descendientes observados; la API quedó desconectada. Todas las llamadas CLI
devolvieron código de salida 0. La limpieza eliminó correctamente la tarea.

### Alcance, limpieza y reproducción

Las muestras iniciales y finales de ambos mecanismos no contenían procesos de
Ollama/app ni escuchas en el puerto 11434. Los modelos del servidor permanecieron
vacíos. Estas pruebas acreditan la detección y el ciclo de encendido/apagado de
los fixtures anteriores en la VM Windows registrada.

No acreditan inferencia de modelos, finalización de runners de CPU/GPU,
comportamiento de GPU/VRAM, ejecución ARM64, aceptación o cancelación interactiva de UAC,
coexistencia con otra instancia de Ollama, todos los wrappers de servicio de
terceros, ni funcionamiento en el PC físico Windows 11 del usuario. Solo se
extrajo `ollama.exe`; los logs registran la ausencia de `llama-server.exe` durante
el descubrimiento de GPU. No se intentó inferencia ni carga de modelos, y ese
mensaje no impidió las comprobaciones de API y encendido/apagado.

El [workflow](../.github/workflows/backend-integration.yml) y los
[fixtures](../tests/BackendIntegration) permiten reproducir esta validación en un
runner administrador aislado. Ejecuta `backend-integration` desde GitHub Actions
y revisa su artefacto `backend-integration-evidence`: `environment.json`,
`service/service-*.report.json`, el `report.json` de la tarea, su manifiesto y los
logs del servidor y host. El workflow conserva el artefacto durante 30 días; una
nueva ejecución genera evidencia nueva. Los fixtures están destinados a ese
entorno aislado.
