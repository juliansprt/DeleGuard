# Registro de aceptación para liberar pilotos

Ningún campo pendiente equivale a certificado. La versión de fuente se identifica con `scripts/v1/source-manifest.ps1` hasta disponer de un repositorio Git y una revisión concreta. Guardar hashes de paquetes y resultados junto al manifiesto. CI automatizada está definida en `.github/workflows/v1-acceptance.yml`; su presencia no acredita una ejecución remota.

## Pruebas reproducibles

Ejecutar `pwsh ./scripts/v1/verify.ps1` desde el repositorio, con .NET 10, Python, Node compatible y Docker disponibles. Usa PostgreSQL desechable; rechaza `DELEGUARD_TEST_POSTGRES` para evitar una base compartida. Conservar TRX de cada suite y la salida completa. Resolver cualquier defecto alto/crítico antes de ofrecer pruebas conectadas. Un test omitido no cuenta como aprobado.

## Carga de autorización

Preparar cinco organizaciones **de validación**, cada una con un agente y fixture de autorización conocida ALLOW. Inyectar `DG_LOAD_URL` y `DG_LOAD_TENANTS_JSON` como secretos locales: array de cinco objetos `{organization_id, api_key, request}`. `request` contiene agente, acción y recurso de prueba. No guardar claves en los resultados.

Con k6 instalado, establecer `DG_LOAD_ACK=validation-only` y ejecutar `k6 run scripts/v1/load.js`. La prueba solicita 50 autorizaciones/s durante 15 minutos, distribuidas entre cinco organizaciones. No consume permisos ni ejecuta proveedores. Deben ser cero las iteraciones descartadas, y el p95 de `authorization_server_ms` inferior a 100 ms. `http_req_duration` registra por separado la latencia observada por el cliente. Guardar `artifacts/load-summary.json`, recursos asignados, región, versión y estado inicial de la base.

El tiempo de servidor procede de `Server-Timing: authorization;dur=…`: incluye evaluación/persistencia del handler, excluye transporte, lectura previa del cuerpo y escritura posterior de la respuesta. Informar esta definición junto al resultado. La prueba está preparada; todavía no se ha ejecutado la carga de quince minutos.

## Nueve combinaciones nativas

Completar [native-acceptance.csv](native-acceptance.csv) en equipos Windows x64, Linux x64 y macOS ARM64. Registrar versión exacta de IDE, extensión Copilot cuando corresponda, SO, paquete, hash, firma y comprobación de notarización en macOS. Para cada celda anexar logs y capturas de la ejecución nativa. Conservar configuración ajena e historial antes/después. La matriz de CI no sustituye estas evidencias.

## Operación gestionada

Antes de aprovisionar: cotización vigente de Azure East US para API y Keycloak siempre activos, worker, PostgreSQL, almacenamiento, Key Vault, logs y correo; sumar por separado desarrollo, validación y pilotos. Excluir consumo de modelos y firma del límite US$100–300, pero mostrarlos en la hoja de costes. Fijar réplicas máximas y alertas. No asumir que una alerta de Azure corta el gasto.

Ensayar en validación: instalación limpia sin seeds demo; migración controlada; comprobar HTTPS y proxies; acceso con email verificado y MFA administrativo; invitar/recuperar/desactivar usuario; actualizar/revertir aplicación; restaurar PostgreSQL a un punto en el tiempo en una instancia aparte; validar tenant, decisiones, cadena de auditoría, claves persistentes y permisos consumidos. Reabrir tráfico solo después de verificar que un permiso consumido antes del backup no pueda provocar un efecto repetido tras restaurar. Reconciliar con los proveedores y preservar el registro de incidencias durante recuperación.

Registrar RPO/RTO observados, fecha, operador, revisión, pasos, resultado y referencias. Verificar retención efectiva: auditoría 90 días, logs 30, backups siete y payloads opcionales cifrados máximo siete. La ausencia de infraestructura desplegada, restauración o datos de coste sigue siendo bloqueante.

## Usabilidad y piloto

Cinco personas sin conocimiento previo siguen una de las tres guías. Cronometrar hasta el primer efecto protegido y reportado, registrar cada ayuda manual. Exigir cuatro éxitos en menos de quince minutos. Después acordar un piloto limitado mediante [pilot-kit.md](../pilots/pilot-kit.md). No convertir entrevistas, demostraciones o sandbox internos en pilotos activos.
