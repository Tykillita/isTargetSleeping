# Plan de mejoras de isTargetSleeping

**Estado: PLAN — NO IMPLEMENTADO.** Redactado el 3 de octubre de 2026.

**Base: 1.5.2, publicada el 3 de octubre de 2026.** Su [validación de servicios y tareas](backend-validation-1.5.2.md#es) se documenta por separado. La revisión funcional parte del código de 1.5.1 y de las capacidades que conserva 1.5.2. Ninguna propuesta aquí está implementada por aparecer en este plan. Consulta la [release 1.5.2](https://github.com/Tykillita/isTargetSleeping/releases/tag/v1.5.2).

[Equivalent English plan](improvements-plan.en.md)

## Lectura rápida

La dirección propuesta es convertir isTargetSleeping en un administrador de recursos para IA local: controlar varias instancias, conservar memoria disponible para lo que estás haciendo y explicar cada decisión. Sus controles actuales, privacidad y personalidad siguen siendo la base del producto.

El plan contiene **46 mejoras**, organizadas por impacto, prioridad, esfuerzo, riesgo y dependencias. Tres apuestas concentran el cambio grande:

1. **Presupuesto de memoria con simulador:** decidir qué puede dormir para conservar un margen de RAM/VRAM, observando el coste de recargarlo.
2. **Gestor de varias instancias:** administrar por separado Ollama, varios llama.cpp y LM Studio, aprovechando las capacidades reales de cada versión.
3. **Reservas temporales de clientes:** permitir que una integración local declare que todavía necesita un modelo, con identidad y vencimiento verificables.

La primera entrega debe reforzar estados, operaciones e identidad. La segunda puede introducir motores modernos y varias instancias. El administrador por presupuesto se prueba primero en observación. Las reservas y mascotas comunitarias quedan para pilotos posteriores.

**Prioridad inmediata de aislamiento:** el apagado actual de una tarea llama a una búsqueda global de servidores `ollama serve`. La idea 04 debe acotar esa finalización antes de habilitar control multiinstancia. Este documento propone el arreglo; no lo implementa.

**Resultado esperado:** menos operaciones equivocadas, menos presión sostenida y una experiencia comprensible. Los objetivos de rendimiento son hipótesis que requieren medición; este plan no promete multiplicadores de velocidad, FPS ni ahorro de energía.

## Primeras cinco acciones sugeridas

1. **Cerrar la línea base 1.5.2:** guardar resultados de compilación, pruebas, versiones de motores y hardware; medir una sesión normal y otra con presión. Esto separa mejoras reales de variaciones de carga.
2. **Especificar el coordinador y los estados:** describir quién puede pedir cada operación, qué se puede cancelar y qué intención prevalece. Incluir la deuda de finalización global de tareas y construir pruebas de aislamiento antes de conectar todas las acciones.
3. **Crear el contrato de capacidades e identidad de instancia:** adaptar Ollama primero y añadir una segunda instancia llama.cpp de prueba. No ampliar proveedores antes de demostrar aislamiento.
4. **Prototipar presupuesto y simulador en memoria:** reproducir registros de referencia, producir decisiones explicables y verificar que simular ejecuta cero acciones.
5. **Entregar un piloto de varias instancias y observación:** recoger localmente recargas, presión y consumo propio; elegir la siguiente fase a partir de los resultados.

## 1. Qué existe y qué cambiaría

### Funciones actuales que se conservan

- Retirar de la memoria los modelos inactivos, dormirlos manualmente y controlar Ollama desde panel, bandeja, atajos, CLI y enlaces.
- Detectar app, servicio, tarea programada y ejecución manual; vigilante y modo juego que recuerdan decisiones manuales.
- Agente elevado, limpieza configurable, solicitudes identificadas, resultados completos/parciales y observaciones posteriores.
- Resumen de aplicaciones y ventana de procesos con filtros, agrupación, protecciones y confirmación de finalización.
- Métricas de RAM, compromiso y GPU; historial local, atribución aproximada de clientes y gestión de modelos.
- Actualizaciones con verificación y rollback, interfaz ES/EN y mascotas opcionales con animaciones nativas.

La limpieza de 1.5.x conserva sus zonas y reglas configuradas. Los futuros perfiles se eligen explícitamente. Una migración de preferencias no activa otra estrategia ni convierte automáticamente una instalación detectada en una instancia administrada.

### Evidencia del código que orienta el plan

| Área | Situación observada | Consecuencia para el diseño |
|---|---|---|
| Contratos de motores | [OllamaController](../src/IsTargetSleeping/Core/OllamaController.cs) e [IEngine](../src/IsTargetSleeping/Core/Engines/IEngine.cs) mantienen caminos distintos. | Unificar operaciones y estado conservando capacidades específicas. |
| llama.cpp | [LlamaCppEngine](../src/IsTargetSleeping/Core/Engines/LlamaCppEngine.cs) selecciona el PID menor y recuerda una línea de arranque. | Hace falta identidad por instancia y varias configuraciones completas. |
| Apagado de tarea | En [Backend](../src/IsTargetSleeping/Core/Backend.cs), `Switch.Stop(Task)` llama a `KillServers()` después de `ScheduledTasks.Stop`. Esa función recorre `Detector.ServerPids()`, que busca globalmente procesos `ollama serve`, sin filtro de tarea, endpoint o propiedad. | Una tarea puede afectar a otra instancia alcanzable. Acotar el objetivo y sus descendientes es una deuda P0 de la idea 04, previa al gestor multiinstancia. |
| LM Studio | [LmStudioEngine](../src/IsTargetSleeping/Core/Engines/LmStudioEngine.cs) usa puerto 1234 y API v0 fijos. | Negociar puerto, API y autenticación; graduar soporte con evidencia real. |
| GPU | [Gpu](../src/IsTargetSleeping/Core/Gpu.cs) escoge el adaptador de mayor VRAM dedicada. | Medir varios adaptadores y evitar dobles conteos de memoria compartida. |
| Clientes | [Clients](../src/IsTargetSleeping/Core/Clients.cs) correlaciona conexiones por puerto y una ventana de diez segundos; agrupa por nombre. | Separar instancia, identidad y confianza de atribución. |
| Historial | [Stats](../src/IsTargetSleeping/Core/Stats.cs) conserva eventos de noventa días; las muestras de treinta minutos viven en RAM. | Añadir series persistentes resumidas con límites de disco. |
| Interfaz | [PanelWindow](../src/IsTargetSleeping/UI/PanelWindow.cs) no se redimensiona; [Theme](../src/IsTargetSleeping/UI/Theme.cs) fija vidrio negro. | Conservar flyout y ofrecer una vista ampliada y alternativas accesibles. |
| Relojes | Controladores, clientes, procesos, bandeja y mascotas muestrean con relojes separados. | Compartir capturas cuando sea seguro y medir el consumo propio. |
| Persistencia | Ajustes e historial usan reemplazo atómico; algunos fallos de lectura/escritura se silencian. | Conservar atomicidad y añadir recuperación, esquema y diagnóstico. |

Esta revisión identifica necesidades de diseño; no equivale a una auditoría exhaustiva de seguridad ni a una medición nueva de todas las funciones.

## 2. Las tres apuestas grandes

### A. Administrador por presupuesto

**Ejemplo de uso:** antes de abrir un juego eliges «conservar 6 GiB de RAM disponible». El panel muestra dos modelos quietos, uno fijado y una aplicación protegida. Propone dormir el modelo menos prioritario, muestra una estimación y explica por qué deja el resto. Puedes simular, aceptar esa acción o activar un perfil reversible.

**Por dentro:** un evaluador recibe muestras de RAM disponible, compromiso, VRAM por adaptador, modelos, actividad y reservas. Produce un `ActionPlan` explicable con alcance, datos observados, estimación, condiciones y vencimiento. El coordinador revalida identidad, actividad y protecciones antes de ejecutar. Se comprueban salidas y evolución de memoria; las acciones pendientes siguen visibles.

**Criterio de decisión:** comparar margen recuperado y duración de presión con recargas, latencia y paginación. La selección puede preferir retirar de la memoria un modelo realmente quieto antes de repetir recortes sin efecto sostenido. Los modos avanzados mantienen sus operaciones configuradas y sus protecciones.

**Límites:** un presupuesto es un objetivo; otras aplicaciones pueden consumir el margen inmediatamente. La memoria de un archivo de modelo no predice por sí sola contexto, buffers ni consumo del runtime. La app no cambia las opciones de solicitudes ajenas para imponer un presupuesto.

**Puerta de continuación:** demostrar menos tiempo con presión sostenida en cargas de referencia, sin aumentar recargas o latencia por encima del presupuesto de coste acordado. Si el simulador decide con datos insuficientes, debe explicarlo y omitir esa acción.

### B. Gestor de varias instancias

**Ejemplo de uso:** ejecutas Ollama para Obsidian, llama.cpp en 8080 para programación y otro llama.cpp en 8081 para embeddings. Cada tarjeta tiene identidad, modelos, consumo y política. Al jugar, apagas sólo el motor de programación; los otros permanecen según tu perfil.

**Por dentro:** `EngineInstance` identifica proveedor, versión, endpoint, backend, procesos y propiedad. `EngineCapabilities` declara operaciones admitidas y calidad de señales de actividad/memoria. Cada instancia mantiene estados e intención propios; el coordinador evita conflictos. El motor informa una liberación del modelo por API o una suspensión nativa cuando exista, con fallback comprobado para versiones anteriores. Antes de habilitar este control, el apagado de tareas deja de usar la lista global como conjunto de objetivos: el plan exige identificar la instancia, revalidarla y actuar sólo sobre sus procesos y descendientes.

LM Studio recomienda su [API v1](https://lmstudio.ai/docs/developer/rest). El servidor actual de [llama.cpp](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) documenta router, carga y liberación de modelos, eventos y suspensión por inactividad. La disponibilidad exacta debe negociarse y verificarse para las versiones soportadas; no se presupone en instalaciones viejas.

**Límites:** un endpoint que responde no demuestra que su proceso pertenezca a esta instancia. Los backends detectados pueden ser de otro usuario o estar administrados externamente. El control remoto requeriría un proyecto específico de seguridad y queda fuera de las primeras fases.

**Puerta de continuación:** tres instancias simultáneas, operaciones aisladas, recuperación de una instancia caída y ninguna acción sobre procesos ajenos. Añadir un caso de dos tareas Ollama y un servidor manual: al detener la tarea A, la tarea B y el servidor manual conservan sus identidades y responden; repetir con A ya cerrada y con error al detenerla. Si la pertenencia no puede verificarse, no ampliar la finalización a la lista global. El catálogo de compatibilidad declara lo probado y las capacidades desconocidas.

### C. Reservas temporales de clientes

**Ejemplo de uso:** un script de análisis solicita conservar su modelo durante quince minutos y renueva una reserva mientras trabaja. La app muestra «reservado por análisis.py hasta 14:30». Al cerrar el script o vencer la reserva, vuelve la política habitual. El usuario puede revocarla.

**Por dentro:** un canal local por usuario permite adquirir, renovar y cancelar reservas con identidad verificable, TTL y límites por cliente. Un canal con ACL por usuario, como named pipes, evita abrir un servidor accesible a toda la red. La reserva expresa necesidad de uso; no entrega al cliente permisos administrativos. Después pueden añadirse herramientas para scripts, Stream Deck o MCP sobre el mismo contrato.

**Límites:** no exige canalizar todas las inferencias por la app, ni registrar prompts o respuestas. Un proceso conectado no recibe una reserva ilimitada. La política ante presión crítica y reserva activa debe ser explícita y visible.

**Puerta de continuación:** un cliente caído no deja reserva permanente; entradas falsas se rechazan; identidad, cuotas y expiración se prueban. El piloto sólo continúa si las integraciones reales evitan liberar modelos todavía necesarios con un coste propio aceptable.

## 3. Clasificación y estimaciones

- **Prioridad:** P0 = fundamento; P1 = siguiente evolución del producto; P2 = expansión; P3 = piloto posterior.
- **Impacto:** Alto = cambia fiabilidad o uso frecuente; Medio = mejora usos específicos o comprensión.
- **Esfuerzo:** M = varios días; L = varias semanas; XL = proyecto transversal. Incluye validación; requiere recalibración después del prototipo.
- **Riesgo:** Bajo, Medio o Alto según alcance sobre procesos, privilegios, compatibilidad y persistencia. No mide probabilidad de un fallo ya demostrado.
- **Dependencias:** números del backlog. Son orden lógico, no una obligación de entregar todas las ideas anteriores.

## 4. Backlog de 46 mejoras

### Fiabilidad y arquitectura

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 01 | Coordinador común para panel, CLI, enlaces, juego, vigilante, descarga y agente; IDs, estados, cancelación segura y límites. | Alto / P0 | XL / Alto | Base. Mil secuencias generadas de órdenes concurrentes sin ejecuciones incompatibles ni pérdida de intención. |
| 02 | Intención y estado explícitos: deseado, observado, arrancando, cargando, durmiendo, inaccesible y fallido. | Alto / P0 | L / Alto | 01. Toda transición identifica su solicitante y conserva la última decisión manual. |
| 03 | Vigilante sensible a arranque y carga: plazos por fase y salud real antes de reiniciar. | Alto / P0 | L / Alto | 02. Carga lenta y API ocupada no producen reinicios falsos en escenarios soportados. |
| 04 | Identidad y apagado por backend: ejecutable, PID/creación, servicio/tarea, endpoint y propiedad; sustituir el alcance global de `Switch.Stop(Task)` por objetivos de esa instancia. | Alto / P0 | L / Alto | 01–02. Dos tareas y un servidor manual: detener A, incluso ya cerrada o con error, conserva identidad/respuesta de B y del manual. Pertenencia desconocida no amplía el alcance. |
| 05 | Esquema versionado de preferencias/historial, migraciones, respaldo válido y aviso de corrupción. | Alto / P0 | M / Medio | Fixtures antiguos; interrupción al escribir conserva el último archivo válido. |
| 06 | Diagnóstico local exportable: acciones, códigos y versiones, con previsualización y rutas/usuarios redactados. | Medio / P1 | M / Medio | 01, 05. Nunca incluye prompts; redacción probada y exportación explícita. |

### Memoria y modelos

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 07 | Presupuestos independientes de RAM disponible, compromiso y VRAM por adaptador. | Alto / P1 | XL / Alto | 02, 19. Objetivos explicables; no presentarlos como límites físicos garantizados. |
| 08 | Observación y simulador: mostrar las acciones que habría tomado una política. | Alto / P1 | L / Bajo | 01, 07, 30. Simular ejecuta cero acciones; resultados reproducibles desde un registro local. |
| 09 | Perfiles legado, conservar modelos calientes, equilibrado y margen de memoria; histéresis y cooldown de carga/liberación. | Alto / P1 | L / Alto | 07–08. La importación conserva el comportamiento anterior hasta elegir otro perfil. |
| 10 | Evaluar efecto sostenido a 30 s/2 min/5 min y recargas; evitar repetición sin beneficio demostrado. | Alto / P1 | L / Medio | 30 y contratos de medición. Diferencias negativas y carga externa visibles; no atribuirles causalidad automática. |
| 11 | Política por modelo: fijado, siesta, prioridad y coste tolerable de recarga; respetar actividad y reservas. | Alto / P1 | L / Alto | 02, 15. Modelo fijado protegido de reglas normales; crisis definida explícitamente. Reservas se integran cuando exista 36. |
| 12 | Asesor previo a carga: memoria observada/estimada, contexto, margen y tiempo de carga esperado. | Medio / P2 | L / Medio | 19, 30. Separar estimación y medición, indicando incertidumbre. |

Recortar el working set puede retirar páginas residentes que vuelvan a usarse después; los fallos de página y la presión posterior importan para evaluar el resultado. [Microsoft: Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set).

Ollama expone modelos activos, tamaño y VRAM, y permite precarga y `keep_alive`. Las peticiones de otros clientes pueden sobrescribir la permanencia configurada: el diseño debe respetar ese límite. [Modelos activos](https://docs.ollama.com/api/ps), [FAQ de Ollama](https://docs.ollama.com/faq).

### Motores e instancias

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 13 | Incorporar Ollama al contrato común de motores conservando sus funciones específicas. | Alto / P0 | XL / Alto | 01–02. Paridad de carga, liberación de memoria, estados y reglas anteriores. |
| 14 | Gestor multiinstancia con nombre, endpoint, identidad, modelos y política por instancia. | Alto / P1 | XL / Alto | 04, 13. Tres instancias simultáneas; selección estable tras reinicio de un proceso. |
| 15 | Capacidades por versión: liberar modelo de memoria, apagar servidor, cancelar, medir actividad, autenticación y eventos. | Alto / P0 | L / Medio | 13. Una capacidad desconocida se muestra no disponible y no se improvisa con una acción destructiva. |
| 16 | LM Studio v1 con fallback v0, puerto configurable, secretos protegidos y pruebas reales. | Alto / P1 | L / Alto | 15. Matriz de versiones y acciones; retirar experimental sólo para lo probado. |
| 17 | Dormir llama.cpp mediante API/TTL nativo y reconocer `sleeping`; fallback de cierre/reinicio en versiones anteriores. | Alto / P1 | L / Alto | 15. Dormir/despertar conserva argumentos; sondeos de salud no despiertan el modelo. |
| 18 | Arranque completo: ejecutable, argumentos, directorio, variables y permisos; cambios previsualizados. | Medio / P2 | L / Alto | 04, 14. Relanzamiento equivalente con espacios/Unicode; secretos ausentes de logs. |
| 19 | Varios adaptadores GPU, memoria compartida/dedicada y atribución por adaptador. | Alto / P1 | L / Medio | Fuentes DXGI/PDH válidas. Dos GPU y ausencia de GPU probadas; memoria compartida sin doble conteo. |

### Interfaz y uso diario

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 20 | Flyout compacto y ventana ampliada redimensionable con recursos, modelos, instancias e historial. | Alto / P1 | L / Medio | 14, 30. Estado común; dimensiones restauradas dentro del monitor. |
| 21 | Ajustes sencillos/avanzados con buscador y explicación del efecto de cada regla. | Alto / P1 | L / Bajo | Catálogo de preferencias. Encontrar una opción por nombre o efecto; paridad ES/EN. |
| 22 | Primer inicio guiado: motor, estrategia y alcance del agente/UAC; prueba reversible de conexión. | Alto / P1 | M / Bajo | 15, 21. Consulta de modelos sin agente; usuario conoce permisos antes de activar. |
| 23 | Paleta de comandos y atajos configurables, conflictos visibles y enlaces actuales conservados. | Medio / P2 | M / Medio | 01. Acciones por teclado; hotkey ocupado explicado. |
| 24 | Accesibilidad: AutomationProperties, lectura de estados, foco, contraste y escalado de texto. | Alto / P0 | L / Medio | Inventario UI. Flujo completo por teclado/Narrator, escala 100–200 % y alto contraste. |
| 25 | Tema claro opcional y modo sistema; vidrio negro conservado y opción opaca accesible. | Medio / P2 | L / Medio | 24. Legibilidad en todos los temas; estados identificables sin depender sólo del color. |

Conservar WPF y las piezas nativas útiles permite dedicar el esfuerzo a comportamiento y experiencia. Windows recomienda acceso programático, teclado y contraste como pilares de accesibilidad. [Accesibilidad Windows](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility).

### Procesos y mediciones

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 26 | Evolución por aplicación, cambios grandes y marcas de carga/liberación; explicar qué coincidió con un pico. | Alto / P1 | L / Medio | 30. Historial optativo/acotado; decir coincidencia cuando no haya causalidad demostrada. |
| 27 | Protecciones persistentes por ejecutable/usuario y motivo visible. | Alto / P1 | M / Medio | Identidades actuales. Ruta igual con otro propietario no hereda protección accidental. |
| 28 | Cierre normal de aplicaciones con ventana como acción separada de finalización forzada. | Medio / P2 | L / Alto | 01, 04. Confirmar salida; no garantizar conservación de trabajo ni escalar automáticamente a kill. |
| 29 | Atribución de clientes por endpoint completo y PID/creación, con confianza alta/media/desconocida. | Alto / P1 | L / Medio | 04, 14. Nombres iguales no se mezclan; solicitudes breves no reciben certeza falsa. |
| 30 | Series temporales persistentes resumidas de 1 h/24 h/7 d con retención y límite de disco. | Alto / P1 | L / Medio | 05. Reiniciar conserva gráficas; compactación y límites probados. |
| 31 | Informes comparables: margen sostenido, presión, recargas y coste de limpieza; exportar CSV/JSON. | Medio / P2 | L / Medio | 10, 30. No sumar repetidamente la misma memoria como ahorro acumulado garantizado. |

### Juegos, perfiles e integraciones

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 32 | Perfil por juego/app: motores que se apagan o conservan, presupuesto y previsualización. | Alto / P1 | L / Alto | 01, 07, 14. Un juego con IA puede conservar su motor sin desactivar todo el modo juego. |
| 33 | Restauración por intención/generación: retrasar, cancelar o saltar según decisiones posteriores. | Alto / P0 | L / Alto | 02; extensión con 32. Cambio de juego y apagado manual no restauran indebidamente. |
| 34 | Pausar automatizaciones 15/30/60 min o hasta reinicio, indicando qué permanece habilitado. | Alto / P1 | M / Medio | 01, 21. Acciones manuales disponibles; vencimiento observable. |
| 35 | Perfiles batería/enchufado, bloqueo y suspensión/reanudación; revalidar estado al volver. | Medio / P2 | L / Medio | 02, 37. Sleep/resume real probado; reanudar no restaura por sí solo procesos. |
| 36 | Reservas locales por cliente, identidad, expiración, cancelación e integraciones posteriores. | Alto / P3 | XL / Alto | 01, 04, 11. Cliente caído libera reserva; petición sin autenticar no ejecuta acciones privilegiadas. |

### Mascotas y Windows

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 37 | Presupuesto de render/muestreo: capturas compartidas y FPS adaptativos; reposo fuera de pantalla o con sesión bloqueada. | Alto / P1 | L / Medio | Línea base. Comparar mascota apagada/dormida/activa; handles sin deriva sostenida tras una hora. |
| 38 | Posición por monitor, DPI mixto, reconexión, barra oculta y reinicio de Explorer. | Medio / P2 | L / Medio | 37. Sin ventanas fuera de pantalla; posición lógica conservada al cambiar DPI. |
| 39 | Mascota como acceso opcional a estado/explicación y avisos breves controlados. | Medio / P2 | M / Bajo | 02, 20. Cero limpieza/finalización accidental por gesto; silencio y accesibilidad respetados. |
| 40 | Paquetes comunitarios sólo de datos, límites de cuadros/imágenes, vista previa y formato validado. | Medio / P3 | XL / Alto | Esquema versionado y renderer acotado. Contenido inválido/enorme rechazado antes de consumir recursos. |

Las animaciones expresan estados útiles. No se premian limpiezas innecesarias ni se revela información privada mediante avisos de escritorio. Las mascotas continúan opcionales.

### Validación, publicación y privacidad

| ID | Propuesta y resultado | Impacto / Prioridad | Esfuerzo / Riesgo | Dependencias y aceptación |
|---|---|---|---|---|
| 41 | Matriz pública de Windows, arquitectura, motor y wrapper probados; distinguir físico/VM/compilado. | Alto / P0 | L / Bajo | Banco aislado. Cada etiqueta probado enlaza evidencia exacta y límites. |
| 42 | Pruebas generativas de protocolos elevados, entradas hostiles, PID reutilizado y recuperación de fallos. | Alto / P0 | L / Alto | 01 y contratos actuales. Fuzzing acotado, sin acciones sobre procesos ajenos; formato desconocido rechazado. |
| 43 | Suite visual/UI Automation: ES/EN, DPI, temas, teclado y capturas de flujos críticos. | Alto / P1 | L / Medio | 24–25. Detectar regresiones funcionales, de traducción y layout; no depender sólo de igualdad de píxeles. |
| 44 | Procedencia de releases y firma Authenticode operativa; validar firma, hashes, arquitectura y rollback. | Alto / P1 | L / Medio | CI y custodia de certificados. Evaluar coste; no prometer eliminación de avisos SmartScreen. |
| 45 | Catálogo de funciones/compatibilidad que genere documentación y verifique versiones, links, capturas y traducciones. | Alto / P1 | L / Bajo | 15, 41. Publicación bloqueada ante inconsistencias; videos versionados y regeneración reproducible. |
| 46 | Centro de datos locales: retención, exportar, borrar, revisar diagnósticos y proteger secretos. | Alto / P1 | L / Medio | 05–06, 30. Borrado comprobado; exportación optativa; sin telemetría implícita. |

## 5. Arquitectura propuesta y migración

```text
Panel / CLI / enlaces / reglas / juego / vigilante / integraciones
                              |
                     OperationCoordinator
                     intención + identidad
                              |
              EngineInstance + EngineCapabilities
                 |             |             |
               Ollama       llama.cpp     LM Studio
                              |
             SnapshotStore -> PolicyEvaluator -> ActionPlan
                              |
                  observación / ejecución validada
                              |
             resultados + métricas + historial local
```

Contratos orientativos para prototipar: `EngineInstanceId`, `EngineCapabilities`, `OperationRequest`, `OperationState`, `PolicyDecision`, `ActionPlan`, `ResourceSnapshot` y `ClientLease`. Reutilizar `ProcessIdentity`, resultados del agente y contratos actuales donde corresponda. Una migración por adaptadores permite probar paridad antes de sustituir caminos antiguos.

Las operaciones elevadas conservan alcance específico, validación estricta y comprobación dentro del agente. Una capacidad de lectura no concede permiso de ejecución. Finalizar sigue exigiendo el flujo de confirmación que corresponda; un perfil automático no autoriza matar aplicaciones arbitrarias. La enumeración global puede servir para observar, pero no define los objetivos de una orden dirigida a una instancia. El fallo o cierre previo de una tarea tampoco autoriza un barrido global de servidores.

Para procesos que la app lanza y administra puede evaluarse Job Objects, con compatibilidad y fallbacks. La herencia y breakaway de Windows impiden asumir un control universal de todos los descendientes externos. [Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects).

Los secretos nuevos usarán almacenamiento protegido por usuario y se excluirán de argumentos registrados, exports y capturas. Extensiones comunitarias serán datos validados; cargar código de terceros no forma parte de esta propuesta inicial.

## 6. Hipótesis que hay que validar

| Hipótesis | Experimento | Evidencia para decidir |
|---|---|---|
| Dormir modelos quietos resulta más útil que repetir recortes bajo ciertas cargas. | Repetir una carga fija con política actual, control sin acciones y propuesta. | Presión sostenida, recargas, latencia y fallos de página; conservar diferencias negativas. |
| Un presupuesto entendible reduce intervenciones manuales. | Prototipo con usuarios: configurar margen y explicar tres decisiones. | Tareas completadas, errores de interpretación y acciones deshechas. |
| Una instancia se puede administrar sin afectar a otras. | Tres motores/puertos, dos tareas más un servidor manual, cierres, errores de Stop, PID reutilizado y reinicio externo. | Estados, identidades y API de cada instancia antes/después; sólo cambia el objetivo solicitado. |
| Las APIs/TTL nativas de motores actuales mejoran sueño/despertar. | Probar versiones nuevas y anteriores con generación, siesta y recarga. | Compatibilidad declarada, salida real y ausencia de reinicios innecesarios. |
| Compartir muestras reduce coste propio. | Misma carga y hardware con relojes actuales y coordinados. | CPU, tiempos de muestreo, handles, memoria y fluidez. |
| La atribución con confianza es más útil que un nombre definitivo aproximado. | Clientes simultáneos, conexiones breves y procesos con mismo nombre. | Precisión donde sea medible; desconocido en casos no atribuibles. |
| Reservas temporales evitan liberar modelos aún necesarios sin bloquear recursos indefinidamente. | Clientes que renuevan, terminan, se bloquean y falsifican solicitudes. | Reservas expiradas, rechazo de entradas y coste por cliente. |

El registro local debe contener señales necesarias para estos experimentos y permanecer acotado. Enviar resultados al mantenedor será una acción explícita del usuario.

## 7. Fases y puertas de entrega

Estimaciones orientativas para un equipo pequeño con validación continua y acceso a los entornos necesarios. Suponen un desarrollador dedicado y apoyo de QA; la capacidad parcial, conseguir hardware o resolver compatibilidad puede ampliar los plazos. Son horizontes de planificación, se recalculan al medir los prototipos y no incluyen todos los experimentos posteriores. Los IDs indican áreas de trabajo: cada fase selecciona un incremento coherente y puede dividir mejoras grandes; no promete cerrar todo el backlog de esas áreas en una sola entrega. No sumar tamaños de tareas como si todas pudieran ejecutarse en paralelo.

| Fase | Horizonte orientativo | Alcance | Puerta para cerrar |
|---|---|---|---|
| 0. Medir y diseñar | 1–2 semanas | Línea base publicada, estados, costes, hardware de referencia y cargas repetibles. | Registro reproducible y métricas actuales; lista de casos e invariantes. |
| 1. Fundamento fiable | 4–8 semanas | 01–05, 13, 15, 24, 33, 41–42; núcleo de contratos y adaptadores graduales. | Paridad y migración, concurrentes, cancelación, identidad e intención verificados; cierre de tarea aislado. |
| 2. Motores modernos | 4–8 semanas | 14, 16–19, 29; primeras mejoras 21–22. | Tres instancias reales, versiones anteriores y catálogo exacto de capacidades. |
| 3. Presupuesto y simulador | 4–8 semanas | 07–11, 27, 30–32, 34; piloto antes de expansión. | Observación sin acciones, decisiones reproducibles y estudio comparativo de costes. |
| 4. Experiencia y eficiencia | 4–8 semanas | Incrementos de 20, 23, 25–26, 31, 35, 37–39, 43, 45–46. | Accesibilidad, consumo propio, exportación y coherencia documental del incremento elegido. |
| 5. Pilotos posteriores | Por aprobar después de medir | 12, 36 y 40; completar 06/44 según necesidades y distribución. | Cada piloto tiene presupuesto, usuarios, criterio de cancelar/continuar y validación propia. |

Las mejoras 06 y 44 pueden adelantarse donde sean necesarias para soporte o publicación. No hace falta esperar al final para proteger datos o verificar paquetes.

Entregar incrementos pequeños con documentación y rollback. Nombrar versiones según el alcance real y SemVer del workspace: funciones compatibles pueden ser minor; una ruptura pública requiere major. No asignar una futura versión a todas las ideas del backlog.

## 8. Objetivos medibles

Estos objetivos son puertas de ingeniería en entornos declarados, no garantías universales para cualquier PC.

- **Aislamiento:** cero acciones sobre instancia ajena en pruebas de control, juegos, vigilante y recuperación; caso de dos tareas y servidor manual obligatorio para 04.
- **Actividad:** cero liberaciones automáticas de modelos durante generación activa detectable en cargas de referencia. Actividad desconocida conserva su etiqueta y el comportamiento conservador elegido.
- **Intención:** reinicio, bloqueo, UAC denegado y actualización no borran preferencias ni decisiones manuales.
- **Simulador:** cero llamadas de ejecución; mismo registro y política producen mismo plan.
- **Estabilidad:** pruebas de una hora y una sesión de ocho horas; handles sin crecimiento sostenido tras calentamiento y regresión de coste propio acotada frente a la línea base.
- **Muestreo:** objetivo inicial p95 menor de 200 ms con quinientos procesos en el equipo de referencia; confirmar o recalibrar después de medir. Filtrar/ordenar mil procesos debe mantener respuesta útil.
- **Interfaz:** objetivo inicial p95 menor de 250 ms al abrir panel caliente; definir método de medición y cargas de fondo.
- **Efecto:** comparar tiempo con presión, disponible, recargas, latencia y paginación usando la misma secuencia. No inferir FPS ni energía de un descenso de RAM.
- **Accesibilidad:** recorridos completos con teclado/Narrator, contraste y escala 100–200 %, en ambos idiomas.
- **Compatibilidad:** matriz pública distingue equipo físico, VM, compilación y fallback simulado; cada afirmación probado tiene evidencia.
- **Privacidad:** secretos excluidos de logs/exports y retención/borrado comprobados; la migración no incorpora telemetría.
- **Entrega:** versión, paquetes, hashes, README bilingües, novedades, capturas y web comprobados en el mismo trabajo. Enlazar descargas después de publicación.

### Método de medición

En la fase 0 se fijan hardware, versión de Windows, motores, modelos, contexto y configuración. Repetir al menos tres veces cada escenario comparable; registrar calentamiento y carga de fondo. Medir CPU como tiempo de CPU y fracción de un núcleo, memoria privada/residente por separado, handles y tamaño de datos. Conservar distribución, p95 y variabilidad; fijar el presupuesto permitido antes de evaluar la entrega.

Los bancos usan procesos, tareas, servicios y puertos de prueba identificados y limpian sólo sus propios recursos. El estudio comparativo usa control sin acciones, política vigente y propuesta sobre la misma secuencia; cualquier interferencia se anota. Los objetivos de 200/250 ms son iniciales: si la línea base contradice su viabilidad, recalibrarlos con evidencia y dejar el motivo registrado. El resultado de compilar una arquitectura no se convierte en una prueba física de ella.

## 9. Riesgos y mitigaciones

| Riesgo | Mitigación propuesta | Puerta |
|---|---|---|
| Ciclos de liberar/recargar que empeoren latencia. | Histéresis, cooldown, prioridad y presupuesto de coste; comenzar en observación. | Comparación de recargas/latencia con línea base. |
| Acción sobre un proceso o backend distinto. | Identidad, propiedad, revalidación y operaciones de alcance explícito. | Pruebas adversarias de PID, puertos y nombres. |
| `Switch.Stop(Task)` extiende el cierre actual a la lista global de servidores. | Idea 04 P0: resolver objetivo/descendientes verificables antes de finalización; no ampliar alcance ante error o identidad desconocida. | Dos tareas más un servidor manual, Stop correcto/fallido y objetivo ya cerrado; los otros conservan identidad y respuesta. |
| Confundir carga lenta con cuelgue. | Estado por fase, señales del proveedor y plazos medidos. | Carga prolongada y generación real sin falso reinicio. |
| APIs de motores cambian entre versiones. | Negociación, adaptadores, fallback y matriz declarada. | Versiones nuevas/anteriores y respuestas parciales probadas. |
| Más muestreo/series consumen recursos. | Capturas compartidas, compactación, límites y retención optativa. | CPU/RAM/handles/disco dentro del presupuesto acordado. |
| Exportación revela datos privados. | Redacción por campo, previsualización, secretos excluidos y acción explícita. | Fixtures con rutas, usuarios y tokens. |
| Reservas o paquetes de mascotas introducen entrada no confiable. | Identidad/TTL/cuotas; datos sin código y límites de recursos. | Fuzzing, expiración y archivos hostiles rechazados. |
| Refactor transversal rompe comportamiento existente. | Adaptadores, paridad, migración gradual y rollback. | Bancos anteriores y nuevos correctos antes de reemplazar. |

## 10. Qué no priorizar ahora

- **Reescribir WPF en otro framework:** exigiría rehacer integración nativa, accesibilidad y pruebas; los beneficios propuestos pueden conseguirse con la arquitectura actual.
- **Prometer liberar una cantidad fija o acelerar cualquier PC:** memoria compartida, buffers y carga externa cambian el resultado. Primero medir en cargas reproducibles.
- **Matar automáticamente procesos arbitrarios para cumplir un presupuesto:** añade riesgo de pérdida de trabajo; el plan prioriza recursos administrados y controles explícitos.
- **Tocar servicios, inicio o reglas de energía de todo Windows:** ampliarían permisos y alcance. Los perfiles iniciales sólo coordinan recursos de la app.
- **Control remoto, cuentas o sincronización en nube en la primera etapa:** requieren seguridad, identidad y política de datos nuevas; no resuelven las primeras deudas locales.
- **Proxy obligatorio para inferencia o registro de conversaciones:** haría depender clientes externos de la app y aumentaría exposición de datos. Las reservas son optativas.
- **Plugins con código descargado:** revisar firma y sandbox sería otro proyecto. El formato comunitario inicial sólo acepta datos acotados.
- **Gamificar una cifra de RAM liberada:** podría incentivar recortes innecesarios. Las mascotas deben comunicar estados y respetar el trabajo.
- **Medir ahorro energético por CPU o RAM solamente:** una afirmación de energía necesita método/hardware adecuados; el primer objetivo es consumo propio y presión.

## 11. Fuentes y reglas de mantenimiento

Fuentes primarias consultadas el 3 de octubre de 2026; volver a comprobarlas al implementar, porque los motores evolucionan:

- [Ollama: modelos activos](https://docs.ollama.com/api/ps) — señales de modelo y memoria.
- [Ollama: FAQ](https://docs.ollama.com/faq) — precarga, permanencia y límites de control desde clientes.
- [llama.cpp server](https://github.com/ggml-org/llama.cpp/tree/master/tools/server) — router, APIs, eventos y sueño nativo.
- [LM Studio REST](https://lmstudio.ai/docs/developer/rest) — API v1, modelos y autenticación.
- [Microsoft Working Set](https://learn.microsoft.com/en-us/windows/win32/memory/working-set) — residencia y fallos de página.
- [Microsoft Job Objects](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects) — agrupación de procesos y excepciones.
- [Microsoft accesibilidad Windows](https://learn.microsoft.com/en-us/windows/apps/develop/accessibility) — teclado, acceso programático y contraste.
- [Código Backend revisado](../src/IsTargetSleeping/Core/Backend.cs) — evidencia del alcance global actual de `Switch.Stop(Task)`, `KillServers` y `Detector.ServerPids`.
- [Reglas del workspace](../CLAUDE.md) — versiones, README bilingües, web, publicación y entrega.

Al convertir una propuesta en trabajo, registrar alcance, diseño, pruebas, decisiones y estado de entrega. Mantener ambos planes equivalentes; no marcar una idea implementada sólo por existir un prototipo. Los resultados de integración de 1.5.2 viven en su informe de validación, no en este plan.
