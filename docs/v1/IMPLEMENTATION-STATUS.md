# DeleGuard v1 — estado de implementación

Fecha de trabajo: 24 de septiembre de 2026. **Implementación parcial; todavía no liberable para pilotos conectados.** Este registro distingue código probado, preparación y aceptación pendiente. No se han aprovisionado recursos de Azure, publicado paquetes ni contactado clientes.

## Cambios implementados

- Identidad de operación independiente del contenido, vinculación de decisión/permiso y consumo atómico común mediante `POST /v1/permits/{permitId}/consume`. El contrato exige identidad y hash; normaliza atributos IDE heredados antes de validarlos.
- Ejecución persistida en PostgreSQL con unicidad y alcance por organización, aplicación, entorno y agente. Comprobación de suspensión, política, aprobación, vigencia, revocación, argumentos y snapshot. Compatibilidad con el registro previo de consumo IDE.
- Serialización de estado y auditoría, concurrencia en aprobación y triggers de caducidad. Las migraciones conservan historial; permisos antiguos sin vinculación suficiente no habilitan ejecución.
- Evidencia idempotente ligada a ejecución consumida, reportes tardíos y resultados UNKNOWN no repetibles. API de incidencias con asignación/resolución y suspensión de aplicación; REPORTED no equivale a verificación independiente.
- Evaluación sin permisos ni efectos, búsqueda de operaciones y consulta de decisiones con ejecución. OpenAPI y esquema de autorización actualizados, con validación de referencias.
- Guard MCP local consume antes de llamar, conserva pendientes cifrados y puede reanudar tras reinicio. Vincula argumentos, mapping, configuración, entorno efectivo y directorio de trabajo. No hereda variables adicionales después de calcular esa vinculación.
- Guard remoto: conexiones con alcance, descubrimiento mediante SDK MCP, esquemas, argumentos tipados, pendientes cifrados, reanudación y reportes de resultado/error/timeout; destinos HTTPS autorizados y sin redirecciones.
- Gateway: identidad estable, caché separada por credencial, consumo común, reservas atómicas, contabilización por intento, estimaciones conservadas tras consumo incierto y mes anterior, lectura del uso final de streaming. Embeddings ya no recibe parámetros exclusivos de chat.
- Administración: hash del comando completo, continuación de aprobaciones coincidentes, consumo antes del callback y reporte de incertidumbre. Versiones de política explícitas. Destino de webhook fijado en mensajes; recuperación limitada a intervalo fijo, con fecha y zona obligatorias en HTTP/MCP/formulario.
- Configuración de producción separada: validaciones de secretos/OIDC/TLS, cookies seguras, proxies confiables, cuerpo máximo, claves persistentes, migraciones controladas y worker de caducidad de payloads remotos. System.Security.Cryptography.Xml actualizado a 10.0.12.
- Clientes fuente beta .NET/Python/TypeScript; tres guías ES/EN, CI de aceptación, manifiesto de fuente, script de carga y matriz nativa pendiente.
- Política de ejemplo GitHub y 30 casos elaborados con policy-author; no se ha publicado ni se ha ejecutado contra GitHub.
- [Kit de piloto](../pilots/pilot-kit.md), [oferta local](../pilots/offer.html), [20 equipos candidatos con fuentes](../pilots/prospects.md) y registro de métricas preparados. Los candidatos son hipótesis por cualificar, no oportunidades confirmadas.

## Verificación final de esta entrega

Comando: `pwsh ./scripts/v1/verify.ps1`, ejecutado en Windows con PostgreSQL desechable. Salida: `artifacts/v1-acceptance-final.log`; terminación 0.

| Verificación | Resultado |
|---|---|
| Unitarias | 534 aprobadas |
| Seguridad | 36 aprobadas |
| Integración | 98 aprobadas |
| Adaptadores IDE Python | 79 aprobadas, 2 omitidas; 81 descubiertas |
| Cliente .NET | Compila, 0 errores y 0 avisos |
| Cliente Python / TypeScript | Compilación Python e importación Node comprobadas; no equivalen a E2E |
| Vulnerabilidades NuGet altas/críticas | Sin NU1903/NU1904 en la ejecución; configuradas como errores |
| Carga k6 | Sintaxis comprobada; carga de quince minutos NO ejecutada |

TRX: `tests/Deleguard.{UnitTests,SecurityTests,IntegrationTests}/TestResults/v1-{UnitTests,SecurityTests,IntegrationTests}.trx`. Permanecen avisos de nulabilidad en fixtures existentes de integración. La CI alojada aún no ha ejecutado este estado. El manifiesto `artifacts/source-manifest.json` identifica la fuente local; no hay commit ni PR creado.

authz-reviewer realizó revisiones sucesivas. Confirmó el cierre de los últimos hallazgos sobre fechas variables, entorno MCP heredado y excepción de identidad para filesystem.*. No quedan hallazgos altos/críticos en esas correcciones revisadas; esa conclusión acotada no acredita todos los criterios del producto.

## Trabajo necesario antes de liberar

| Área del plan | Pendiente concreto |
|---|---|
| A — Garantía común | Aceptación en clientes/proveedores reales. Hidratación confiable de hechos y precondición de versión en cada integración sensible; atributos MCP extraídos de argumentos no bastan. |
| B — MCP | OAuth real: emisor, audiencia, scopes, asociación al agente y pruebas de clientes. Protocolo remoto completo. Archivos de credenciales y logins internos del downstream requieren vinculación adicional. Limpieza periódica local sin actividad. |
| C — Gateway | Reanudación de aprobación en segundo intento de cascada, proveedores reales, reconciliación de facturas y exposición completa del estado contable. Usar static para el primer recorrido de validación. |
| D — Consola | Mostrar contenido revisable completo de comandos administrativos con secretos protegidos; aceptación de todas las reanudaciones. Correo con reintentos/estado e invitación, recuperación y baja completas. |
| E — Evidencia | Integración GitHub que produzca y verifique un efecto; verificador autorizado, expediente exportable completo, comprobación de integridad, UI/alertas de incidencias y simulación histórica. |
| F — Nativo | Nueve combinaciones IDE/SO en equipos limpios, firma, notarización, instalación/actualización y evidencias. Todas figuran PENDING en la matriz. |
| G — Integración | E2E de tres clientes y distribución; estados completos de onboarding en consola y ensayo con cinco personas. Las guías necesitan aceptación por usuarios nuevos. |
| Operación | Cotización East US, infraestructura reproducible, tres entornos, despliegue limpio, MFA administrativo, límites por cliente, exportaciones acotadas, observabilidad, retención y restauración PITR. Sin evidencia de cumplir US$100–300/mes. |
| Rendimiento | Cinco organizaciones, 50 autorizaciones/s, quince minutos, p95 de procesamiento <100 ms. Preparado scripts/v1/load.js y Server-Timing; sin resultado medido. |
| Comercial | Cualificar candidatos, entrevistar, acordar datos/soporte/precio y activar tres pilotos de 30 días. No hay pilotos ni continuidad pagada acreditados. |

Estos pendientes incluyen desarrollo que aún falta, además de validaciones externas. No son todos atribuibles a permisos o credenciales del usuario. El [runbook](acceptance-runbook.md) define la evidencia necesaria antes de cambiar estados.

## Límites y recuperación

Conservar operation.id en reintentos y cambiarlo solo por una intención nueva. Ejecutar únicamente cuando el consumo devuelva acquired=true; guardar la ejecución antes del efecto. Un fallo tras consumo no vuelve a habilitarlo. Reportar un resultado tardío o resolver una incidencia no permite repetir la ejecución original.

La integración debe obtener hechos confiables y usar compare-and-set/precondiciones en el proveedor: el consumo remoto no mantiene bloqueada la base del cliente. No se promete aislamiento del equipo ni control de credenciales usadas por rutas distintas. Una restauración necesita reconciliación de ejecuciones antes de reabrir efectos para no resucitar permisos anteriores al backup.

Los pendientes remotos se cifran y el worker los elimina al caducar. Los locales caducan a siete días y se limpian al leer/guardar; su borrado periódico inactivo sigue pendiente. Nunca certificar retención física solo por tener un timestamp de expiración.

La cuenta Azure está accesible para lectura, pero no se ha desplegado nada. Dominio, proveedor de correo, identidades de firma y equipos nativos deben concretarse para la aceptación operativa.

