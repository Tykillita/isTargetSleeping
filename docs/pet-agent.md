# Nimbo como agente (complemento «Mascota conectada»)

El complemento `isTargetSleeping-pet` (descarga aparte, no va dentro del .exe) convierte a la mascota en un asistente que puede
**operar la app, recordarte cosas y relevar los permisos de Claude Code a tu teléfono y a la placa ESP32**. El modelo carga
por defecto desde Ollama local; también puede usar proveedores remotos o la sesión local de Codex CLI del mismo usuario de Windows.
El servidor de Nimbo sigue corriendo en la PC para mantener la agenda, la memoria, el puente con la app y las conexiones del teléfono y la placa.

## Qué sabe hacer

| Función | Cómo pedirlo | Detalle |
|---|---|---|
| Aprobar permisos de agentes | Pestaña **Agentes** | Permisos, preguntas y planes de Claude Code. Requiere «Aprobar permisos desde el notch» en la app (instala los hooks de Claude Code). |
| Recordatorios y rutinas | «recuérdame a las 5 pm llamar a mamá», «en 10 minutos…», «mañana a las 8…», «todos los días a las 7…», «cada 2 horas…» | Pestaña **Agenda**. Suenan en el chat, en el notch de la PC y en la placa. Si la PC estaba apagada, llegan «tardíos» (hasta 24 h). |
| Pomodoro | «inicia / pausa / reinicia el pomodoro» | Opera el widget de la app. |
| Tareas y notas | «añade una tarea comprar leche a las 18:30», «crea una nota: …» | Se crean en los widgets de la app. |
| Memoria | «recuerda que me gusta el té», «olvida lo del té» | Pestaña **Memoria**: ver, buscar, editar y borrar. Nada se guarda sin que lo pidas. Olvidar pregunta antes. |
| Uso de IA | «¿cuánto uso me queda en Claude?» | Enciende el widget de Uso si estaba apagado y responde con las cifras reales. |
| Clima, tareas y pomodoro (lectura) | «¿qué clima hace?», «¿qué tareas tengo?» | La app publica un resumen cada 10–60 s. El clima requiere activar el widget de Clima. |
| Avisos proactivos | Automáticos | Agentes que terminan o piden atención; uso de IA ≥ 80 %. Respetan el horario de silencio. |

## Modelo y autenticación

El servidor de Nimbo se inicia con la app de escritorio y sigue ejecutándose en esta PC. El proveedor y el modelo se eligen en la web de Nimbo: **Ajustes → Proveedor de Nimbo**. Los cambios se aplican en caliente, sin reiniciar el servidor. En instalaciones que aún no guardaron esta configuración web, las opciones `--provider`, `--model` y las variables de entorno existentes sirven como valores iniciales.

| Proveedor | Autenticación | Modelo predeterminado |
|---|---|---|
| Ollama local | Ollama instalado y ejecutándose en esta PC | `gemma4:latest` |
| Ollama Cloud | OAuth de Ollama por el servicio local (sin descargar pesos), o clave API directa sin Ollama instalado | `gemma4:cloud` con OAuth; `gemma4:31b` con clave directa |
| Grok / xAI | Clave API de xAI | `grok-4.7` |
| Gemini | Clave API de AI Studio o OAuth ADC de Google Cloud | `gemini-3.8-flash` |
| Codex | OAuth guardado por Codex CLI en esta PC | El predeterminado de Codex |
| Claude API | Clave de Claude Console | `claude-haiku-4-5-20251001` |

En Ajustes, selecciona el método de autenticación y escribe la clave API o el ID de proyecto de Google Cloud donde corresponda. Para Codex, Gemini y Ollama Cloud puedes iniciar el login desde esa misma pantalla; se abre en la PC donde corre Nimbo. Cada proveedor y método conserva su modelo y sus credenciales por separado y cifrados. La clave API no se vuelve a mostrar después de guardarla. Para conservar la que ya está guardada, deja vacío el campo; usa «Borrar la clave guardada» para eliminarla.

Nimbo cifra la configuración y las claves en disco mediante DPAPI de Windows, vinculadas al usuario que ejecuta el servidor. La sesión OAuth de Codex permanece administrada por Codex CLI; Gemini usa las credenciales ADC de Google Cloud. Ollama Cloud puede usar el login de la app/CLI Ollama mediante el servidor local, que reenvía al cloud sin cargar pesos en la PC; el endpoint directo de Ollama requiere una API key. xAI requiere clave API. Claude se conecta por la API de Claude Console; Nimbo no reutiliza el OAuth de Claude.ai porque Anthropic no permite que una app de terceros enrute suscripciones Claude.ai. Las solicitudes remotas envían al proveedor el historial de chat, la personalidad y contexto de Nimbo (incluidos recuerdos y resúmenes de la app/equipo), además de las imágenes del último mensaje. El uso remoto puede generar cargos según la cuenta del proveedor. Codex CLI se ejecuta sin shell/MCP/navegador. Las acciones siguen pasando por el broker local y sus permisos.

## Permisos (pestaña Ajustes del celular y Ajustes del notch en la app)
Se editan en los dos sitios: es el mismo archivo `policy.json`, que el complemento relee solo. En la app están en Ajustes del notch → «Permisos de Nimbo» (pausa, modo estricto, decidir permisos y lo delicado desde el teléfono, horario de silencio y el modo de cada herramienta).
Cada herramienta tiene un modo: **Normal** (el recomendado según su riesgo), **Auto**, **Preguntar** o **Apagado**.
- Lo que solo lee o se deshace fácilmente va en automático; lo sensible (olvidar un recuerdo) o lo que toque la PC pregunta.
- **Preguntar** envía una confirmación al celular, a la placa y a la PC; **gana la primera respuesta** y las demás se ignoran.
- **Pausar a Nimbo** lo apaga todo; **Modo estricto** convierte lo automático en «preguntar».
- Lo que nace de contenido ajeno (un agente, un archivo) nunca se ejecuta solo.
- Los permisos viven en `policy.json`; si el archivo se daña, Nimbo queda **en pausa** hasta repararlo.
- Todo lo que hace queda en **Actividad** (quién, qué, cuándo, desde qué canal y con qué resultado), que puedes borrar.

## Seguridad de las aprobaciones
- Lo que `AgentRisk` marca como delicado (borrado recursivo, `git push --force`, `sudo`, credenciales…) **no se puede permitir desde el
  teléfono ni la placa**, solo denegar. Hay un interruptor («Permitir lo delicado desde aquí») apagado por defecto, y la app lo exige
  también por su cuenta (`petApproveRisky`).
- No existe «siempre permitir» desde la mascota.
- Las peticiones caducan cuando Claude Code corta el hook (~280 s); la tarjeta muestra la cuenta regresiva.
- Si la app deja de renovar `approvals.json` (60 s), no se ofrece nada para decidir.

## Agentes propios (pestaña Agentes del celular → «Mis agentes»)

Un agente propio es un perfil guardado en `%LOCALAPPDATA%\isTargetSleeping	eamgents.json` (también se puede editar a mano): nombre, instrucciones (hasta 4000 caracteres),
motor (`claude`, `codex` u `ollama`), modelo, si puede escribir, herramientas permitidas, **tope de tokens (obligatorio)** y tiempo máximo.

- Herramientas: lista cerrada (`Read, Grep, Glob, Edit, Write, WebSearch, WebFetch, git diff, git status`); no hay Bash libre. Sin poder escribir no se admiten `Edit`/`Write`.
- Un modelo local (Ollama) responde sin herramientas ni acceso a archivos y nunca escribe.
- Un perfil de solo lectura se ejecuta como una respuesta (sin rama) leyendo la carpeta del proyecto; uno que escribe trabaja en una rama `team/<id>` y nunca se fusiona solo.
- Se crean, editan y borran desde el celular (`/v1/agent-profiles`, con sesión y CSRF) y se activan o desactivan desde Ajustes del notch → «Mis agentes». El complemento y la app validan las mismas reglas.
- Ejecutarlos lo hace Team. **Nimbo propone y tú confirmas**: «pídele a Revisor que mire el diff de hoy» o «lanza el agente Revisor en isTargetSleeping para …» prepara la tarea y te la enseña (agente, proyecto, tope de tokens y si escribe) en el celular, la placa o la PC; gana la primera respuesta. Lanzar un agente (`team.enqueue`) **siempre pregunta**, aunque lo pongas en Auto.
- La app vuelve a validar al encolar: agente activo, proyecto conocido (la carpeta de Team, la de código o un proyecto de tus sesiones recientes) y presupuesto. Nunca fusiona ni descarta ramas.
- Presupuesto en `pet\policy.json`: `team_daily_tokens` (por defecto 300000 al día) y `team_max_queued` (por defecto 3 trabajos en cola).
- Nimbo te avisa (chat, push, notch y placa) cuando un plan espera tu aprobación, cuando un trabajo termina y cuando se detiene y necesita tu decisión. También puede contarte qué está haciendo el equipo.
- Límite actual: solo lanza tus agentes propios (no el reparto automático de Team); las cadenas planificar → implementar → revisar quedan para después.

## Cómo conectar el teléfono
En la app: Ajustes del notch → **Conectar celular** → «Mostrar código QR». Escanéalo con la cámara con Tailscale activo. Ver `pet-web.md`.

## Archivos (carpeta `%LOCALAPPDATA%\isTargetSleeping\pet`)
`policy.json` (permisos), `nimbo-provider.bin` (configuración cifrada del proveedor), `nimbo-provider-keys` (llaves DPAPI), `pet.db` (estado, recordatorios, memoria, actividad), `approvals.json`, `usage.json`, `app-state.json`,
`agent-state.json` (los escribe la app), `commands\` y `results\` (órdenes de la mascota a la app, lista cerrada y sin repetidos).

## Límites conocidos
- El modelo local pequeño no siempre acierta con herramientas libres: por eso las órdenes claras («inicia un pomodoro», «recuérdame…»)
  se detectan sin depender del modelo y la acción siempre pasa por los permisos.
- Notificaciones push (teléfono bloqueado o web cerrada): Ajustes → Notificaciones. En iPhone exige añadir la web a la pantalla de
  inicio (Compartir → «Añadir a pantalla de inicio»), abrirla desde el ícono y emparejarla ahí (tiene su propio almacenamiento). Los avisos
  que se pierdan se repiten al reconectar (hasta 24 h en el teléfono, 10 min en la placa). Las claves VAPID y las suscripciones viven
  en `push.json`; solo se aceptan servicios de push conocidos (Apple, Google, Mozilla, Microsoft).
  Diagnóstico: `isTargetSleeping-pet push-test` envía una prueba y muestra lo que respondió cada servicio; cada envío queda en `push.json.log`
  (un `403 BadJwtToken` de Apple significa que la firma VAPID no es válida).
 La placa muestra
  las peticiones de agentes solo con la app de la mascota abierta, y los recordatorios sobre cualquier pantalla.
