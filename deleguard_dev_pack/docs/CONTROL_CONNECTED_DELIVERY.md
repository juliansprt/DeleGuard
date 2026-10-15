# Control conectado: entrega y validación

Implementación local del plan para primeros clientes, septiembre de 2026.

## Recorrido disponible

- Resumen operativo con agregaciones del día completo en UTC, aplicación y organización; enlaces a aprobaciones, cobertura, errores, evidencia pendiente y bloqueos de presupuesto.
- Activación separada en configuración, autorización probada y ejecución reportada. Una autorización en Playground no demuestra ejecución.
- Asistente `/agents/connect`, ficha `/agents/{id}` y matriz `/actions/coverage`. La asignación agrega permisos de acción y política de forma atómica sin reemplazar otras asignaciones. La cobertura indica preparación para evaluar; no garantiza ALLOW para cualquier recurso.
- Editor de políticas existente ampliado con casos persistidos, comparación contra la versión publicada, resultados esperados y control previo a publicar. Los casos no generan decisiones reales, permisos ni ejecuciones.
- AI Gateway en navegación principal, secciones de conexión, modelos, controles y actividad. Actividad por aplicación; presupuesto existente por organización.
- Actividad `/audit/activity` y expediente `/audit/decisions/{id}`, con decisión inicial, estado actual, versión/regla, aprobación, permiso y resultado reportado. Exportación JSON sin tokens de permisos ni payloads arbitrarios.
- Navegación por teclado, foco visible, menú móvil y controles de aprobación; capturas y documentación de consola en español e inglés actualizadas.

## Despliegue

Incluir la migración `20260917024226_PolicySimulationLibrary`, que crea la biblioteca de casos. Se aplicó correctamente en PostgreSQL desechable de validación. Usar el procedimiento habitual de migraciones de cada entorno; esta tarea no desplegó producción.

Publicar una nueva versión de política de producto requiere al menos un caso guardado y que todos sus casos pasen para esa versión. Las versiones ya publicadas siguen operando. Las políticas administrativas conservan su flujo propio. El contrato de `/v1/authorize` y sus tres resultados no cambió.

Las nuevas consultas mantienen alcance de organización y aplicación seleccionada, además de permisos de consola. La integración protegida sigue siendo necesaria: una llamada que la omita no queda controlada. La evidencia se etiqueta como reportada por la integración, sin afirmar verificación independiente.

## Validación técnica realizada

- 267 pruebas unitarias aprobadas.
- Suite completa de integración: 50 pruebas aprobadas antes del último refuerzo de asignación. Las cuatro pruebas de `ControlQueriesTests` se ejecutaron después con las consultas y el almacén finales.
- 25 pruebas de seguridad aprobadas.
- Compilación de API y de las 75 páginas de documentación correcta.
- Navegador: recorrido de consola, guardar/comparar caso, asignación idempotente, exportación, escritorio y aprobación móvil; menú cerrado excluido de navegación por teclado y Escape devuelve foco.
- Revisiones de autorización: correcciones de alcance, permisos, publicación y asignación concurrente; revisión final sin bloqueantes.

La compilación conserva avisos preexistentes de vulnerabilidades en `System.Security.Cryptography.Xml` 9.0.0. Deben resolverse en el mantenimiento de dependencias antes de publicar. La documentación también conserva avisos sobre colección i18n/entrada 404. No se hicieron llamadas pagadas a proveedores ni ensayos con clientes reales. No se presenta la revisión de teclado y móvil como una certificación completa de accesibilidad.

## Demostración y prueba con usuarios nuevos

En un entorno de demostración, conectar un agente, asignar `invoice.cancel` y su política publicada, ejecutar en Playground un caso permitido, uno denegado y otro que requiera revisión. Resolver la aprobación con un usuario autorizado; ejecutar a través de la integración y reportar su resultado. Abrir el expediente y exportarlo. Para Gateway, usar una integración de prueba que reporte alias, modelo, costo y motivo de ruta, sin prompts ni claves.

Validar con cinco participantes nuevos, sin instrucciones del facilitador durante cada tarea:

1. Conectar un agente y obtener su primera autorización de prueba. Medir tiempo, ayuda solicitada y errores.
2. Explicar qué acciones tiene asignadas y qué política publicada las evalúa. Registrar respuestas correctas y saltos entre pantallas.
3. Diagnosticar un bloqueo y encontrar su configuración. Medir tiempo y comprensión del motivo.
4. Resolver una aprobación desde móvil, identificando recurso, motivo y caducidad antes de actuar.
5. Reconstruir una decisión y distinguir autorización de ejecución reportada y de verificación independiente.

Registrar resultados por participante y tarea; no hay mediciones humanas todavía. Usar los fallos observados para priorizar la siguiente iteración antes de ampliar proveedores o funciones enterprise.
