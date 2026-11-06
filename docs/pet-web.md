# Web móvil de la mascota (Tailscale + QR)

Parte del complemento `isTargetSleeping-pet` (descarga aparte, no va en el .exe de la app).

## Uso
1. `isTargetSleeping-pet serve` arranca el servidor. La web escucha **solo en 127.0.0.1:8780** (HTTP); nunca en la LAN.
2. Publícala en tu tailnet (HTTPS lo pone Tailscale): `tailscale serve --bg 8780`. Tailscale muestra la URL `https://<pc>.<tailnet>.ts.net`.
3. `isTargetSleeping-pet pair --url https://<pc>.<tailnet>.ts.net` muestra un **QR** (consola y página local) con un código de un solo uso.
4. Escanea el QR con el teléfono (con Tailscale activo): la web se empareja sola y queda la sesión.

## Chat: modo Equipo

La caja de chat tiene un botón **Equipo**. Al activarlo, la caja muestra proyecto (solo los que la app conoce), modo (Auto, Ahorro, Calidad, Diseño, Velocidad, Solo Claude, Solo Codex), esfuerzo y si se **revisa el plan**; lo que escribes se lanza como tarea del equipo y siempre pide confirmación en el celular, la placa o la PC. Sin activarlo, Nimbo también propone mandar al equipo las órdenes claras de código («Arregla el error al guardar los ajustes en el proyecto X»); las preguntas y la charla no se envían. Tu elección se recuerda en el teléfono.

La caja también tiene **dictado por voz** (botón de micrófono; usa el reconocimiento del navegador y solo aparece si está disponible) y, en modo Equipo, **adjuntar imágenes y archivos** (hasta 5, de 10 MB cada uno; imágenes, PDF, texto y código). Los adjuntos se suben a la PC al lanzar, quedan visibles en la confirmación y se borran al terminar la tarea. Los tipos de archivo están en una lista cerrada.

## Pestaña Agentes

- **Permisos y preguntas** con una hoja a pantalla completa: qué quiere hacer el agente, la ruta y, en ediciones, el cambio exacto (líneas rojas y verdes; nunca en archivos sensibles). Las preguntas de varias opciones se contestan una a una y las de selección múltiple admiten varias marcas.
- **Trabajando ahora**: toca un agente para ver su línea de tiempo, cambios, contexto y acciones.
- **Equipo**: trabajo en curso, plan pendiente (editable), cola e **historial**; toca una entrada para ver lo que pediste, cada paso (agente, modelo, tiempo, tokens), los archivos que cambió y el informe completo (con botón de copiar). Fusionar o descartar una rama solo se hace en la app de la PC.
- **Mis agentes**: crear, editar y borrar agentes propios (ver `pet-agent.md`).
- La lista se actualiza sin repintarse entera: lo que estás eligiendo o escribiendo no se pierde.

## Conectar

La pantalla de conexión tiene **Escanear código QR** (usa la cámara de la propia web; si no hay permiso, queda el código escrito). La lectura del QR usa jsQR (Apache-2.0), servida por el propio complemento.

## Seguridad
- Código de 8 caracteres (40 bits), vale 5 minutos, un solo uso, se anula tras 5 fallos; en disco solo su hash.
- El código viaja en el fragmento `#c=` de la URL: el navegador no lo envía al servidor ni queda en registros.
- Sesión: cookie HttpOnly + SameSite=Strict (Secure tras HTTPS), 7 días, máx. 16, en memoria (reiniciar el servidor las cierra).
- POST exige cabecera `X-CSRF`; toda petición con `Origin` ajeno (incluido WebSocket) se rechaza.
- El puerto del dispositivo (ESP32) no sirve la web ni acepta cookies; el token del ESP32 no abre la web.

## Requisitos / límites
- PC encendida con el servidor activo; sin Tailscale en el teléfono la URL no responde.
- Revocar: reiniciar el servidor cierra todas las sesiones; "Salir" en la web cierra la propia.
