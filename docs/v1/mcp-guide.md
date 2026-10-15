# Herramienta MCP / MCP tool

Estado: Guard local y remoto con claves de servicio implementados; OAuth y aceptación desde clientes MCP reales siguen pendientes. No anunciar interoperabilidad certificada. Los esquemas añaden `_deleguard_operation_id` a cada herramienta protegida.

## Español

1. Registra acción, recurso, política, agente y asignaciones. En la configuración MCP de la aplicación vincula el servidor y herramienta con la acción y rutas de atributos. La clave del agente determina organización, aplicación y entorno; no se seleccionan mediante argumentos del modelo.
2. Local: configura `Deleguard:Url`, `ApiKey`, `AgentId` y `DownstreamMcps` en el proceso Guard. Cada conexión tiene `Name`, `Transport` (`stdio` o `http`), y `Command`/`Arguments` o `Url`; los secretos se inyectan desde el entorno. Conserva el directorio del journal y sus claves entre reinicios. No compartas ese directorio entre cuentas.
3. Remoto: usa `/mcp/guard` con Bearer `dgk_` y conexiones configuradas por organización/aplicación. El servidor exige HTTPS y destinos autorizados; no se permiten redirecciones a otro destino. Las credenciales del servidor MCP permanecen en la configuración del servicio, fuera de los argumentos.
4. Descubre herramientas con `tools/list`. Verifica que una herramienta sin asignación no aparezca. Invoca la herramienta con los argumentos que exige su esquema y un `_deleguard_operation_id` estable. Ejemplo de identidad: el UUID persistido de tu trabajo, no un hash del contenido.
5. En REQUIRE_APPROVAL guarda `decision_id`, aprueba desde consola y llama `get_decision` con ese identificador. Guard recupera los argumentos pendientes, comprueba el estado actual, consume y ejecuta una vez. La identidad auxiliar no se envía a la herramienta.
6. Repite la consulta y confirma que no hay un segundo efecto. Repite después de reiniciar Guard. Cambia argumentos, revoca la asignación o deja caducar el permiso y comprueba el bloqueo. Si se pierde la respuesta del proveedor, inspecciona la ejecución incierta antes de actuar.

Los atributos extraídos de argumentos no son una fuente confiable para importes o estado de negocio. Para un piloto sensible, el servidor MCP/integración debe obtenerlos del sistema propietario y debe aplicar precondiciones de versión al efecto. La demostración `invoice.cancel` permite aprender el flujo; no sustituye esa integración. Las llamadas directas al servidor o credenciales usadas fuera de Guard quedan fuera de cobertura.

Los pendientes cifrados caducan a los siete días. El worker elimina payloads remotos; Guard local limpia al leer/guardar pendientes. La eliminación periódica de archivos locales sin actividad sigue pendiente y debe cerrarse antes de prometer una retención física máxima de siete días. Si faltan argumentos, claves locales o vinculación válida, se bloquea la reanudación. Guard vincula la configuración, el entorno efectivo entregado al subproceso y el directorio de trabajo; cambiar credenciales o destino por esas vías exige nueva autorización. Los logins internos, archivos externos y credenciales que el servidor MCP lea por su cuenta requieren validación adicional en la integración. La migración de datos antiguos no fabrica permisos de ejecución.

## English

Register and grant an action, then map a downstream server/tool to it. Local Guard uses `Deleguard` and `DownstreamMcps` configuration; retain its journal and protection keys across restarts. Remote Guard uses `/mcp/guard`, scoped service credentials and an allowed HTTPS destination. OAuth certification is pending.

Discover the published schema and supply a persisted `_deleguard_operation_id`. On REQUIRE_APPROVAL, preserve the decision and review it in the console. Poll `get_decision`; Guard resumes the original command, consumes once and reports the result. Verify repeated polls and restarts never duplicate the effect, and that revocation, expiry and changed arguments block execution. Unknown provider outcomes require investigation.

Argument-derived business attributes need a trusted hydrator in the integration before sensitive use. Apply a resource version precondition at the provider. Direct tools and credentials outside the configured Guard are outside coverage. Pending arguments are encrypted and expire after seven days; periodic deletion of inactive local files remains unfinished. They cannot resume if their keys or binding are missing.
