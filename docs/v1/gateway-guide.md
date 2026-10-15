# Modelo con presupuesto / Budgeted model call

Alcance previsto: chat completions, embeddings, listado de modelos y streaming de chat. La certificación conectada al proveedor sigue pendiente. Otros endpoints, múltiples respuestas por llamada e imágenes no forman parte del presupuesto certificado.

## Español

1. Crea un agente de Gateway y concede `llm.complete`/`llm.embed` según necesidad. Publica políticas de catálogo, región y aprobación. Configura un presupuesto mensual de pruebas por organización y rutas con modelo real, alias, proveedor, región y precios por millón. Comprueba los precios con el proveedor antes del piloto.
2. Ejecuta `src/Deleguard.AiGateway` en el entorno del cliente. Configura `Deleguard__Url`, `Deleguard__ApiKey`, `Deleguard__AgentId` y `Providers__openai__ApiKey` mediante secretos. `Providers__openai__BaseUrl` identifica el endpoint permitido. El valor de la clave del modelo no debe enviarse al servicio central.
3. Apunta el cliente OpenAI compatible al Gateway (por defecto local `http://127.0.0.1:5088/v1`). Envía Bearer del agente y `Idempotency-Key` persistente por intención. Desactiva reintentos automáticos de POST en tu biblioteca. Usa un alias listado en `/v1/models`.
4. Empieza con enrutamiento `static`, una respuesta de texto y un límite de salida pequeño. Gateway autoriza metadatos, reserva gasto y consume el permiso antes del proveedor. Los prompts permanecen en el Gateway y proveedor del cliente.
5. Si requiere aprobación, conserva cuerpo e identidad y reenvía exactamente esa solicitud después de aprobar. El primer intento pendiente reutiliza la decisión. La reanudación de una aprobación en un segundo intento de cascada aún no está cerrada; mantén `static` durante las primeras pruebas.
6. Inspecciona presupuesto reservado, estimado y confirmado. Desconecta un streaming y comprueba que el consumo desconocido conserva su reserva/estimación. La presencia de fragmentos de texto no acredita finalización. El fin del stream y el uso final deben contabilizarse antes de cerrar el intento.

Prueba límite mensual con dos llamadas simultáneas y capacidad para una sola. Incluye cambio de mes con reservas pendientes, error después del envío, sustitución de proveedor y bloqueo de rutas retiradas. Conserva la factura/usage del proveedor para reconciliación: la estimación depende de los precios y formatos admitidos. No promete limitar tráfico que no pasa por este Gateway.

409 después de consumo significa que el efecto no puede repetirse con esa identidad. Consulta decisión y proveedor; no cambies automáticamente la clave para esquivar el bloqueo. Un timeout no implica gasto cero. Al cerrar el piloto, revoca claves, detén el Gateway y reconcilia estimaciones pendientes.

## English

Grant model actions, publish catalog/region rules, set a small organization budget and configure routes with current provider prices. Run Gateway in the customer's environment; inject model secrets there. Point the OpenAI-compatible client at its `/v1` base, use the agent credential, preserve Idempotency-Key per intent and disable automatic POST retries.

Begin with static routing, one text response and a small output cap. Gateway authorizes metadata, reserves cost and consumes before calling the provider. Preserve the exact body and identity across a pending first-attempt approval. Resuming approval on later cascade attempts remains unfinished, so static routing is the initial acceptance path.

Test simultaneous reservations, month rollover, changed routes, provider errors and interrupted streams. Distinguish reserved, estimated and confirmed cost. Unknown usage is not zero; reconcile it against provider records. Consumed operations cannot be replayed, and traffic outside Gateway is outside its budget boundary. Provider-connected certification remains a release gate.
