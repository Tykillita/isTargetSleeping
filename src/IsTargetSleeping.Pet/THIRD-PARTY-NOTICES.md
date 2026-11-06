# Componentes de terceros del complemento «Mascota conectada»

El complemento (`isTargetSleeping-pet`) es un programa aparte de la app. Estas son las bibliotecas que incluye y su licencia
(verificar las versiones y los textos de licencia exactos antes de publicar una versión).

| Componente | Para qué | Licencia |
|---|---|---|
| ASP.NET Core (Kestrel) | Servidor local HTTPS/WebSocket de la mascota y la web móvil | MIT |
| Microsoft.Data.Sqlite + SQLitePCLRaw | Estado, mensajes, recordatorios, memoria y registro de actividad | MIT · Apache-2.0 |
| QRCoder 1.6.0 | Código QR para emparejar el teléfono | MIT |
| WebPush 1.0.12 (web-push-csharp) | Notificaciones push con VAPID | MIT |
| BouncyCastle.Cryptography | Criptografía de Web Push (dependencia de WebPush) | MIT |
| Newtonsoft.Json | Dependencia de WebPush | MIT |
| jsQR 1.4.0 (`Web/jsqr.js`) | Leer el QR de emparejamiento con la cámara de la web móvil | Apache-2.0 |

La mascota usa **Ollama** instalado por el usuario (no se redistribuye) y el modelo que el usuario elija; cada modelo tiene su propia licencia.
El arte de la mascota (silueta, ojos, mejillas) es el de la mascota del notch de isTargetSleeping; el ícono de la web instalada es el de la app.
