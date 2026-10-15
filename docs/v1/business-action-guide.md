# Acción de negocio / Business action

Beta privada. Requisitos: API activa, usuario de consola, aplicación y entorno, agente con clave `dgk_`, acción registrada y política publicada para ese agente. Los clientes están en [sdks](../../sdks/README.md). No usar credenciales demo fuera del entorno local.

## Español

1. En la consola crea el agente, asigna `invoice.cancel` y publica la política de facturas. Usa un sistema de facturas de pruebas. Comprueba primero una factura borrador y otra cuyo importe exige aprobación. Playground evalúa reglas; su resultado no acredita una ejecución.
2. En tu servicio confiable carga la factura desde tu base de datos. Construye atributos de estado, importe, moneda y reporte fiscal. No aceptes esos hechos del modelo. Prepara los argumentos exactos y el destino de la acción.
3. Guarda un UUID en el registro del trabajo como `operation.id`. Conserva ese UUID al reintentar. Una nueva intención obtiene otro UUID, incluso si repite contenido. Calcula SHA-256 en minúsculas sobre una representación JSON canónica de argumentos y destino: claves ordenadas, sin espacios, misma codificación y representación de números al crear y comprobar el hash.
4. Envía `/v1/authorize` con `Idempotency-Key`, `operation: {id, hash}`, agente, acción y recurso. Incluye `resource.snapshot_hash`, calculado sobre los hechos confiables y la versión del recurso. Guarda la respuesta. El [contrato](../../deleguard_dev_pack/spec/openapi.yaml) define los campos.
5. `DENY` o error: termina sin ejecutar. `REQUIRE_APPROVAL`: conserva argumentos y decisión; una persona revisa en consola. Consulta `/v1/decisions/{decisionId}` hasta autorización vigente, denegación o vencimiento. Una aprobación no ejecuta la factura por sí misma.
6. Vuelve a leer el recurso. Si cambió su versión o los argumentos, detén esta operación. Con el mismo `operation.id`, `operation.hash` y snapshot actualizado, llama `/v1/permits/{permitId}/consume`. Ejecuta únicamente si `acquired=true`. Conserva inmediatamente `execution_id`.
7. Aplica el cambio con una comparación de versión o precondición atómica en tu sistema. La comprobación remota de DeleGuard no bloquea tu base de datos: la integración debe impedir cambios entre leer y escribir. Usa también la identidad de operación en el proveedor cuando soporte idempotencia.
8. Reporta `/v1/executions` con decisión, permiso, ejecución, snapshot y `SUCCESS`, `FAILED`, `PARTIAL` o `UNKNOWN`. Un timeout después del envío es `UNKNOWN`. Consulta el proveedor y reporta un resultado tardío; no repitas automáticamente el efecto. Si falló solo la entrega de evidencia, reenvía la misma evidencia.

Verificación: demuestra un bloqueo sin efecto, una aprobación con una ejecución, dos consumos simultáneos con un único ganador y un resultado incierto que no se repite. Conserva los identificadores y una referencia del sistema de facturas. `REPORTED` significa reporte del cliente; no significa verificación externa.

Recuperación: 401 → revisar agente/clave; 409 al consumir → revisar vinculación, política, revocación y ejecución existente; 503 → mantener bloqueo. Una incidencia puede asignarse y documentarse con `/v1/incidents`; resolverla no reactiva su permiso. Una nueva operación requiere una decisión explícita después de investigar.

## English

Create an agent, grant the invoice action and publish its policy in a test application. Read invoice facts from your trusted service. Persist a new operation UUID for each intent and preserve it across retries; hash canonical execution arguments and the destination. Supply a snapshot hash covering trusted facts and the resource version.

Authorize with an idempotency key. Stop on denial or error. For approval, persist the original command and poll the decision after a person reviews it. Re-read the resource, stop if it changed, and consume the permit with the original identity/hash and current snapshot. Execute only on `acquired=true`; persist `execution_id`. Use an atomic version precondition in your own system to close the gap between reading and writing.

Report the consumed execution. If the provider might have received the command but the result is unknown, report UNKNOWN and investigate without replay. Retry identical evidence after delivery failure, never the business effect. The SDKs implement this sequence but cannot supply your trusted resource reader or provider version precondition. Acceptance requires denied, approved, concurrent and uncertain cases with provider references. REPORTED is client evidence, not independent verification.
