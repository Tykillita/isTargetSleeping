# Validación de RAM y procesos — 1.4.0

Validación realizada el 1 de octubre de 2026 en Windows x64, con .NET SDK 10.0.401.

## Resultados

- Banco completo de lógica: **551 comprobaciones**, sin fallos. Incluye reglas coincidentes,
  presión crítica desde el inicio, presión solo de compromiso, reintentos de 3/6 minutos,
  opciones desactivadas, cancelación y apagado confirmado del modo juego, cambios negativos
  y lectura de historial anterior.
- Interfaz WPF: **66 comprobaciones**, sin fallos. Español e inglés, agrupación, búsqueda,
  filtros, ordenación, métricas desconocidas, fallback residente, selección por identidad,
  confirmación y muestreo al pausar, minimizar y cerrar. Una tabla de 200 grupos y 800 procesos
  mantiene las filas virtualizadas. Capturas a 100 % y 150 % en `obj/process-previews/`.
- Finalización real: se crean únicamente procesos auxiliares de prueba, con padre, hijo y
  nieto. Se comprueba la salida individual, PID reutilizado, proceso ya cerrado, cancelación,
  raíces bloqueadas y descendientes descubiertos. No se finalizan aplicaciones del usuario.
- Protocolo e informes: límites de 1024 identidades y 32 000 caracteres, formato completo,
  versión/identificador, informes parciales y cambios negativos. El seguimiento con reloj
  simulado conserva una operación activa después de 130 segundos y rechaza informes ajenos.
- Publicaciones autocontenidas: app y agente para **x64 y ARM64**, versión **1.4.0**.
  El agente se publica con recorte y la app incorpora el agente de su arquitectura.
- Traducciones: todas las claves estáticas de la app tienen traducción inglesa.
- Web: 24 combinaciones de dos páginas, español/inglés, claro/oscuro y anchos de
  375/768/1280 px, sin desbordamiento horizontal. Resultados en `obj/site-previews/`.

## Muestreo durante diez minutos

300 capturas de solo lectura, cada dos segundos, con más de 450 procesos y compilaciones
concurrentes. Tras el calentamiento, los handles oscilan entre 277 y 282, sin crecimiento
sostenido. La memoria administrada después de GC pasa de 0,60 a 0,48 MiB. El conjunto residente
oscila hasta 37,65 MiB y termina en 31,30 MiB. CPU equivalente al 2,31 % de un núcleo; captura
media de 93 ms, con un máximo de 916 ms durante compilaciones concurrentes. Estos valores
describen este equipo y su carga, no una garantía para otros equipos.

## Límites de esta validación

No se instala el agente ni se ejecuta limpieza real sobre las aplicaciones del equipo. El flujo
real de instalación, acceso denegado y cancelación en el escritorio seguro de UAC necesita
comprobación interactiva. ARM64 se compila y publica; no se ejecuta en hardware ARM64.
El fallback de memoria se fuerza en pruebas y no se ejecuta en una instalación antigua de Windows.
La protección de decisiones manuales durante restauración se prueba con el estado del modo juego;
la interacción con servicios externos reales de Ollama/LM Studio requiere prueba de integración.

Actualización del 3 de octubre de 2026: servicio y tarea de Ollama ya tienen
[validación real de integración en Windows para 1.5.2](backend-validation-1.5.2.md).
Ese informe precisa la VM, las configuraciones y los límites; no amplía lo probado aquí
con LM Studio ni certifica UAC interactivo o hardware ARM64.

Los resultados de compilación y los bancos finales quedan en `artifacts/`, junto a los ejecutables
de verificación. Esa carpeta está ignorada por Git. La actualización del agente se ofrece en
**Ajustes › Liberar RAM › Actualizar**; consultas y finalización normal funcionan sin instalarlo.

## Revisión posterior

- **Prueba de finalización real inestable** (fallaba 1 de cada 3 ejecuciones). Causa: además del padre, el hijo
  y el nieto, el árbol incluye los `conhost.exe` de las consolas, que Windows cierra solo al morir su cliente.
  Si `TerminateProcess` llega mientras ese proceso ya está saliendo, devuelve `ERROR_ACCESS_DENIED` (5) y se
  clasificaba como *denegado*, lo que en uso real ofrecería un reintento como administrador innecesario.
  Corrección en `ProcessActions`: ante el error 5 se espera hasta 250 ms (dentro del límite de 15 s) a que el
  proceso salga antes de llamarlo denegado. Resultado: 10 de 10 ejecuciones sin fallos; la prueba imprime ahora
  el estado de cada proceso si vuelve a fallar.
- **Interfaz** adaptada al vidrio negro de la app: ventana de procesos y confirmación con la cabecera del panel,
  botones de icono Fluent, selector segmentado, búsqueda y menús de vidrio, tabla en `CardBox` y solo las acciones
  que aplican a la selección. La prueba de interfaz exporta también la confirmación (`confirm-*.png`).
- Batería completa tras los cambios: **617 comprobaciones** (551 de lógica y 66 de interfaz), sin fallos ni avisos
  de compilación. Publicación x64 de prueba correcta; `--status`, `--memory` y `--snapshot activity` funcionan.
