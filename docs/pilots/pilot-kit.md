# DeleGuard — kit de primeros pilotos

Documento de trabajo, 24 septiembre 2026. No constituye oferta contractual final ni aceptación técnica. Inicio conectado condicionado a [los bloqueos de salida](../v1/IMPLEMENTATION-STATUS.md). No se han enviado mensajes a prospectos.

## Oferta / Offer

**Controla las acciones sensibles de tus agentes antes de ejecutarlas.** Piloto asistido de 30 días para un SaaS B2B, una aplicación, un responsable técnico y un flujo concreto: por ejemplo, una cancelación de factura o la creación de un issue de prueba. Define cuándo permitir, bloquear o pedir aprobación; conserva un expediente de la decisión y del resultado conocido.

**Control sensitive agent actions before execution.** A 30-day assisted pilot covering one B2B SaaS application and one workflow. Define allow, deny and selective approval rules, then investigate decisions and reported outcomes. Coverage applies to the integrated execution path. Independent verification and each supported client must pass acceptance before being promised.

La propuesta se valida en una entrevista técnica y una demo. La fecha se confirma después de aceptación. Facturación manual; tarifa y continuidad se acuerdan por escrito antes de activar un servicio pagado. No hay cobros ni renovación automática definidos en este borrador.

## Demo de diez minutos / Ten-minute demo

| Minuto | Acción y evidencia que debe observarse |
|---|---|
| 0–1 | Presentar un flujo real y quién responde por él. Mostrar alcance y credenciales de sandbox. |
| 1–3 | Pedir una acción que la política deniega. Comprobar ausencia de efecto en el sistema destino. |
| 3–5 | Pedir acción de mayor riesgo; mostrar acción, recurso, política, argumentos revisados y caducidad. Aprobar desde otra identidad autorizada. |
| 5–7 | Consumir y ejecutar una sola vez. Repetir la identidad y comprobar que no aparece otro efecto. |
| 7–9 | Mostrar decisión, aprobación, ejecución, reporte y referencia externa. Simular pérdida de respuesta; explicar UNKNOWN y cómo se investiga. |
| 9–10 | Elegir un flujo piloto, acordar responsable y criterio observable de éxito. |

No representar una pantalla, mock o fixture como una comprobación externa. Si un paso no está implementado, mostrar su estado pendiente y no ofrecerlo como terminado.

## Entrevista / Interview

Preguntar por el último incidente concreto: qué intentó hacer el agente, qué sistema podía modificar, cuándo lo detectaron, cómo frenaron la acción, cuánto tardaron en reconstruirla y qué pruebas faltaron. Pedir ejemplos sin secretos ni datos personales innecesarios. Identificar frecuencia, coste de intervención y responsable de compra. Preguntar qué flujo autorizarían hoy en sandbox y qué impediría pasar a producción. Evitar preguntar solo si «les gusta» la idea.

Ask about the last actual incident: intended effect, target system, detection, response, investigation time and missing evidence. Identify frequency, owner, current workaround and a specific sandbox workflow. Record assumptions separately from demonstrated pain and willingness to pay.

## Acuerdo de piloto para completar con el cliente

- Participantes: organización, aplicación, responsable técnico, aprobadores y contacto de soporte.
- Flujo: acción, sistema destino, conjunto de recursos permitido, entorno, volumen y presupuesto; criterios para habilitar efectos reales limitados.
- Datos: inventario de metadatos y referencias; prompts fuera del servicio central; payloads opcionales cifrados, máximo siete días. Retención objetivo de auditoría 90 días, logs 30 y backups siete; activar el piloto solo tras verificar su aplicación.
- Acceso: claves separadas por agente, roles mínimos, revisión de aprobadores, procedimiento de rotación, suspensión y baja.
- Soporte: canal y horario acordados, responsables de incidentes y escalado. No prometer SLA de disponibilidad ni cobertura 24/7 sin capacidad operativa.
- Éxito: primera integración medible, bloqueos esperados, ninguna duplicación en casos ensayados y tiempo de investigación comparado con la línea base.
- Cierre: exportación acordada, revocación, eliminación/retención según acuerdo, reconciliación del gasto y decisión explícita sobre continuidad pagada.

Validar tratamiento de datos y términos con las partes antes de contratar; este borrador no acredita cumplimiento legal.

## Calendario y medición

Semana 1: entrevista, línea base, sandbox y criterios firmados. Semana 2: fallos, aprobación y recuperación. Semana 3: acciones reales limitadas solo tras aceptación. Semana 4: resultados, exportación y propuesta manual de continuidad.

Registrar cada sesión en [pilot-results.csv](pilot-results.csv). Calcular bloqueos incorrectos sobre solicitudes etiquetadas por el cliente, no sobre todas las solicitudes. Medir integración desde acceso a documentación hasta primera ejecución protegida; registrar tiempo calendario y tiempo activo por separado. Objetivo de onboarding: cuatro de cinco personas nuevas en menos de 15 minutos, sin ayuda no registrada. Objetivo comercial: tres pilotos activos y una continuidad pagada; ambos siguen pendientes de clientes reales.
