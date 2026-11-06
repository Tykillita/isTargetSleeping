# Protocolo Mascota v1

Contrato entre la mascota de la placa (ESP32, app `pet`), la web movil y el modulo "Mascota conectada" de
isTargetSleeping. Esquema formal: `pet-v1.schema.json`. Este documento se copia a `isTargetSleeping/docs/`.

## Principios
- **La PC es la autoridad.** El estado vive en la PC con una revision `rev` (entero que solo sube). La placa dibuja
  localmente (20 FPS, toque < 150 ms) y sincroniza; si la PC no esta, la placa sigue funcionando sola.
- JSON UTF-8, **maximo 4 KB por mensaje**. Texto de la mascota **maximo 400 caracteres**. Expresion de una lista cerrada.
- Cada accion lleva un **id unico de 128 bits** (32 hex). La PC deduplica por id; reenviar es seguro.
- Nada de `setInsecure`: la placa valida el certificado de la PC (ver Seguridad).

## Expresiones (lista cerrada)
`open`, `left`, `right`, `squint`, `smile`, `sleep`, `happy`, `listen`, `think`, `speak`.
Cualquier otro valor se rechaza con `bad_expression` (la PC nunca lo reenvia a la placa).

## Envoltorio
Todo mensaje: `{"v":1,"type":"<tipo>","id":"<32 hex>", ...}`. `rev` aparece en los mensajes que describen estado.
Versiones: `v` distinto de 1 -> `error` con `code:"bad_version"` y se cierra.

## Endpoints (HTTPS en la LAN para la placa; 127.0.0.1 para la web via Tailscale Serve)
| Metodo y ruta | Cuerpo | Respuesta |
|---|---|---|
| `GET /v1/state` | - | `state` (snapshot con `rev`) |
| `WS /v1/events` | primer mensaje del cliente: `hello` | flujo de `state`, `say`, `status`, `ack`, `error` |
| `POST /v1/chat` | `chat` `{id,text}` | `202` + `{"v":1,"type":"chat_accepted","id":..,"chat_id":..}` |
| `POST /v1/chat/{id}/cancel` | - | `200` `{"type":"chat_cancelled"}`; la mascota vuelve a `idle` |

## Mensajes
**Placa -> PC**
- `hello` `{since_rev}`: al abrir el WebSocket. La PC responde un `state` completo y luego lo nuevo.
- `action` `{id, kind, age_s, name?}`: `kind` = `tap` | `play` | `dizzy` | `rest` | `wake` | `rename`. `age_s` = segundos
  desde que ocurrio (la placa estuvo sin conexion). La PC limita el efecto de acciones viejas (maximo 8 h de ausencia).
- `chat` `{text}` y `cancel` `{chat_id}` (tambien por este canal): equivalen a `POST /v1/chat` y a `POST /v1/chat/{id}/cancel`;
  la PC responde `chat_accepted` / `chat_cancelled` / `error` por el mismo WebSocket. La placa usa solo este canal
  (una unica conexion TLS: la memoria del ESP32 no admite dos).
- `ping` `{}`: cada 30 s; la PC responde `pong`.

**PC -> placa / web**
- `state` `{rev, pet:{name, energy, affection, pose, asleep}}`: `energy` y `affection` 0-100.
- `say` `{chat_id, text, expression, final}`: texto de la mascota (<= 400 car.). `final:true` cierra la respuesta.
  Si hay varios `say` de un mismo `chat_id` se concatenan en orden.
- `status` `{chat_id, phase}`: `phase` = `idle` | `listening` | `thinking` | `speaking`. La placa lo mapea a las poses
  `listen`/`think`/`speak`; `idle` siempre devuelve a `open` (nunca queda "pensando").
- `ack` `{ref, rev}`: la accion `ref` fue aplicada (o ya lo estaba).
- `error` `{code, message}`: ver codigos.
- `agents` `{state}`: lo que hacen los agentes de IA de la PC (`idle` | `working` | `attention` | `alert` | `done`), el mismo estado que
  ilumina el notch de isTargetSleeping. La placa reacciona: `working` -> pensar, `attention` -> escuchar, `alert` -> entornar,
  `done` -> celebrar. Se envia al cambiar y tambien tras `hello` si no es `idle`.
- `pong` `{}`.

## Reglas de sincronizacion
1. Al conectar la placa abre `WS /v1/events`, envia `hello{since_rev}` y despues sus acciones pendientes (cola acotada de 16, la mas
   antigua se descarta).
2. La PC aplica cada accion nueva una sola vez (registro de los ultimos 256 ids), sube `rev` y emite `ack` + `state`.
3. Si `state.rev` es menor que el que la placa ya mostraba, gana la PC (la placa reemplaza su copia).
4. Reconexion con espera creciente 1, 2, 4 ... 60 s; mientras no haya conexion la placa sigue sola y acumula acciones.
5. Respuestas tardias: cada chat lleva un `chat_id`; una respuesta cuyo `chat_id` ya fue cancelado o reemplazado se descarta.
6. Una generacion de IA a la vez; cola maxima de 3 mensajes (`busy` si se llena).

## Codigos de error
`bad_json`, `bad_version`, `too_large` (>4 KB), `text_too_long` (>400 car.), `bad_expression`, `unauthorized`,
`busy`, `not_found`, `model_missing`, `model_unavailable`, `cancelled`, `internal`.
HTTP: 400 (bad_json/too_large/text_too_long/bad_expression), 401 (unauthorized), 404, 409 (busy), 503 (model_*).

## Seguridad
- **Placa:** HTTPS/WSS con la CA propia que genera el modulo. La CA y un **token de dispositivo** (alcance solo "mascota") se
  graban en NVS por **USB** (comando serie), nunca por Wi-Fi. `Authorization: Bearer <token>`; sin `setInsecure`. El host o
  IP se configura en Ajustes; el certificado incluye ese host/IP en el SAN.
- **Web movil:** solo por Tailscale (la PC no se expone a Internet). Emparejado con un codigo temporal que muestra
  isTargetSleeping; despues cookie `HttpOnly` + `SameSite=Strict` con expiracion, comprobacion de `Origin` y token anti-CSRF
  en los `POST`.
- Los tokens nunca aparecen en logs; los mensajes largos o JSON invalido se rechazan antes de procesarlos.

## Ejemplos
```json
{"v":1,"type":"action","id":"5b2f0c9a41d34e7f8a3c6d1e90ab12cd","kind":"tap","age_s":0}
{"v":1,"type":"state","id":"0c1d...","rev":42,"pet":{"name":"Mira","energy":74,"affection":61,"pose":"happy","asleep":false}}
{"v":1,"type":"say","id":"7a..","chat_id":"c9..","text":"¡Hola! Me alegra verte.","expression":"happy","final":true}
{"v":1,"type":"error","id":"..","code":"text_too_long","message":"maximo 400 caracteres"}
```

## Pruebas obligatorias (resumen)
PC apagada, Ollama parado, modelo ausente, Wi-Fi caido, reinicio de la placa, tildes en espanol, mensajes largos, JSON
invalido, respuestas tardias, envios simultaneos, cancelar, token invalido, sesion expirada, certificado no confiable.
