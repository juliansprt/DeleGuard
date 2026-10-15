# DeleGuard

Plataforma de autorización determinista, aprobaciones humanas y evidencia de ejecución para agentes de IA. Antes de ejecutar una acción sensible, tu integración consulta `POST /v1/authorize` y recibe `ALLOW`, `DENY` o `REQUIRE_APPROVAL`. El modelo de IA no participa en la decisión de autorización.

```text
agente → autorización → ALLOW / DENY / REQUIRE_APPROVAL
                          ↓             ↓ aprobación humana
                        permiso → consumo único → ejecución → evidencia → auditoría
```

Incluye una consola web, políticas YAML, aislamiento por organización, API de administración, Guard MCP remoto, un AI Gateway opcional y clientes .NET, Python y TypeScript. La demo inicial protege `invoice.cancel`.

## Requisitos

Para ejecutar la API y el worker localmente:

- SDK **.NET 10**.
- **Docker Engine o Docker Desktop con contenedores Linux y Docker Compose v2**, para PostgreSQL 16 y Redis 7. También puedes usar PostgreSQL 16 existente y ajustar la conexión.
- **Git** para clonar el repositorio.
- Los comandos siguientes usan **PowerShell 7**, disponible en Windows, Linux y macOS. Se incluyen las variables equivalentes para Bash.

Opcionales: Python 3 para el ejemplo de autorización; Node.js **22.12 o superior** y npm para el sitio de documentación; Node.js **22.18 o superior** para el cliente TypeScript sin compilador. No se necesita una clave de un proveedor de IA para ejecutar la plataforma ni la demo básica.

## Inicio local

Ejecuta los comandos desde la raíz del repositorio. La ruta recomendada utiliza los datos en Docker y los procesos .NET en tu equipo.

### 1. Clonar y arrancar los datos

```powershell
git clone https://github.com/juliansprt/DeleGuard.git
cd DeleGuard
docker compose -f deploy/docker-compose.yml up -d --wait postgres redis
```

PostgreSQL queda en `localhost:5432`, con base, usuario y contraseña `deleguard`. Redis queda en `localhost:6379`. El volumen `deleguard_pg` conserva los datos entre reinicios. Redis es opcional para la corrección de las decisiones.

### 2. Ejecutar la API y la consola

En una terminal, desde la raíz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/Deleguard.Api --no-launch-profile --urls http://127.0.0.1:5080
```

Es necesario indicar `Development`: `--no-launch-profile` no carga las variables de `launchSettings.json`. En este entorno, la API aplica las migraciones EF Core y carga la organización, usuario, agente y política de demostración automáticamente. Espera a que anuncie que escucha en el puerto 5080 antes de arrancar el worker.

### 3. Ejecutar el worker

En **otra terminal**, desde la misma raíz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/Deleguard.Workers --no-launch-profile
```

El worker procesa el outbox, entrega webhooks, vence aprobaciones y aplica la retención de cargas remotas. Mantén ambos procesos abiertos.

En Bash, sustituye las dos asignaciones de cada terminal por:

```bash
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
```

### 4. Comprobar y abrir la consola

```powershell
Invoke-RestMethod http://127.0.0.1:5080/health
```

La respuesta de salud debe ser `Healthy`. Abre [la consola](http://127.0.0.1:5080/login) e inicia sesión con las credenciales locales:

| Uso | Valor de demostración |
| --- | --- |
| Correo de la consola | `admin@demo.local` |
| Contraseña de la consola | `DeleguardDemo!23` |
| Token de recuperación de la consola | `dgc_demo_console_local_dev_only` |
| Clave del agente, Bearer para `/v1/authorize` | `dgk_demo_invoice_agent_local_dev_only` |
| Token de administración MCP, Bearer para `/mcp` | `dgm_demo_admin_local_dev_only` |

Estas credenciales son públicas y exclusivas del entorno local. El inicio rápido usa el login local; no requiere Keycloak ni configurar Google o Microsoft.

| Superficie | URL local |
| --- | --- |
| Consola | [http://127.0.0.1:5080](http://127.0.0.1:5080) |
| Aprobaciones | [http://127.0.0.1:5080/approvals](http://127.0.0.1:5080/approvals) |
| Salud, incluida la conexión a PostgreSQL | [http://127.0.0.1:5080/health](http://127.0.0.1:5080/health) |
| OpenAPI generado | [http://127.0.0.1:5080/openapi/v1.json](http://127.0.0.1:5080/openapi/v1.json) |
| MCP de administración | `http://127.0.0.1:5080/mcp` |
| Guard MCP remoto | `http://127.0.0.1:5080/mcp/guard` |

## Probar las tres decisiones

Con la API iniciada y Python instalado, ejecuta el cliente existente desde la raíz:

```powershell
$env:DELEGUARD_URL = "http://127.0.0.1:5080"
python examples/python/authorize_demo.py
```

No necesita instalar paquetes externos. En Bash, usa `export DELEGUARD_URL=http://127.0.0.1:5080` y `python3 examples/python/authorize_demo.py`.

La [política de facturas](policies/invoice-cancel-production.yaml) produce estos resultados en la organización de demostración:

| Factura | Resultado esperado |
| --- | --- |
| Borrador, COP 5.000.000, sin reporte fiscal | `ALLOW` |
| Emitida, COP 35.000.000, sin reporte fiscal | `REQUIRE_APPROVAL` |
| Reportada a la autoridad fiscal, cualquier monto | `DENY` |

El script muestra las respuestas de autorización y crea la aprobación pendiente; no cancela facturas. Puedes inspeccionar y resolver la solicitud desde **Aprobaciones** con el administrador demo. La política de alto valor solicita el rol `billing-manager`; el administrador de la organización también puede resolverla.

Para integrar una acción real, usa los [clientes beta](sdks/README.md) y el [contrato de ejecución](deleguard_dev_pack/spec/execution-v1.md): conserva `operation.id`, calcula `operation.hash` como SHA-256 de los argumentos exactos y conserva `Idempotency-Key` en los reintentos. Después de una aprobación, consulta la decisión; antes del efecto externo, actualiza los hechos del recurso y consume el permiso. Ejecuta únicamente cuando el consumo devuelva `acquired: true` y reporta la evidencia con su `execution_id`.

El entorno del agente de demostración se llama `production`; ese valor identifica su registro de negocio y es independiente del entorno .NET `Development` utilizado para arrancar localmente.

## Configuración

La API y el worker leen sus respectivos `appsettings.json` y variables de entorno .NET. Usa doble guion bajo (`__`) para las claves anidadas. Por ejemplo, si PostgreSQL usa otro puerto, configura la misma conexión en **ambas terminales**, antes de arrancar los procesos:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5433;Database=deleguard;Username=deleguard;Password=TU_CLAVE_LOCAL"
```

| Variable | Función |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Entorno de ejecución. Usa `Development` en el inicio local. |
| `ConnectionStrings__Postgres` | Conexión PostgreSQL compartida por API y worker. |
| `Permits__SigningKey` | Clave de firma de permisos; debe coincidir en los procesos que la utilizan. |
| `DemoAuth__ApiKeyPepper` | Pepper para hashes de credenciales. Cambiarlo invalida credenciales existentes. |
| `DemoAuth__ConsoleToken`, `DemoAuth__AllowEmergencyConsoleToken` | Acceso local de recuperación. |
| `Webhooks__ProtectionKey` | Clave de protección de secretos de webhooks; debe coincidir entre API y worker. |
| `Webhooks__AllowLoopback`, `Webhooks__AllowInsecureHttp` | Excepciones para probar webhooks locales; desactivadas por defecto en el arranque local. |
| `Oidc__Enabled`, `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret` | Inicio de sesión mediante proveedor OIDC. |
| `Oidc__RequireVerifiedEmail` | Exige correo verificado con OIDC. |
| `DataProtection__KeyPath` | Directorio persistente de claves de sesión; por defecto, `dpkeys` dentro del proyecto API. |
| `Docs__PublicUrl` | Enlace al sitio de documentación; por defecto `http://localhost:4321`. |

Un archivo `.env` no se carga automáticamente al ejecutar `dotnet run`. Exporta las variables en cada terminal o utiliza un gestor de secretos. No guardes claves reales en archivos versionados.

### Docker Compose completo y producción

[deploy/docker-compose.yml](deploy/docker-compose.yml) también define imágenes para API, worker, documentación, Keycloak y Mailpit. **El stack completo requiere ajustar su configuración antes de ejecutarlo**: actualmente declara la API como `Production`, pero incluye claves demo, OIDC HTTP y excepciones de webhooks que la aplicación rechaza. Utiliza el inicio local anterior para una demostración reproducible.

La aplicación valida en producción: secretos únicos de al menos 32 caracteres para firma, pepper y webhooks; PostgreSQL con contraseña propia y `SSL Mode=VerifyFull`; OIDC con HTTPS y correo verificado; claves de Data Protection persistentes; y ausencia de tokens demo o excepciones inseguras de webhooks. En producción no se cargan los datos demo y las migraciones deben ejecutarse como un paso controlado con `--migrate-only` antes de iniciar API y worker. Consulta [Program.cs](src/Deleguard.Api/Program.cs) y [ProductionConfiguration.cs](src/Deleguard.Infrastructure/ProductionConfiguration.cs) para las condiciones exactas.

El archivo [docker-compose.dev.yml](deploy/docker-compose.dev.yml) es un override de PostgreSQL en memoria, no un override de entorno para toda la aplicación. Solo úsalo con datos descartables: el contenido de esa base se pierde al detener o recrear su contenedor.

## Componentes opcionales

### Sitio de documentación

Documentación de uso en español e inglés, con guías de integración y capturas de la consola. Puedes ejecutarla sin instalar Node en tu equipo:

```powershell
docker compose -f deploy/docker-compose.yml up -d --build docs
```

Abre [http://localhost:4321](http://localhost:4321). Para editarla con Node y npm:

```powershell
cd docs-site
npm ci
npm run dev
```

Para verificar la compilación estática, ejecuta `npm run build` desde `docs-site`. Consulta su [README](docs-site/README.md).

### Agente de demostración con interfaz web

Con la API funcionando, abre otra terminal desde la raíz:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:DELEGUARD_URL = "http://127.0.0.1:5080"
dotnet run --project demo/Deleguard.DemoAgent --no-launch-profile --urls http://127.0.0.1:5280
```

Abre [http://127.0.0.1:5280](http://127.0.0.1:5280). Sin `OPENAI_API_KEY`, el agente usa un intérprete local de demostración. El proveedor de IA es opcional y se configura solamente en este proceso.

### MCP, AI Gateway e integración con IDE

- **Guard MCP remoto:** se sirve desde `/mcp/guard` en la API y usa claves de agente `dgk_`. Configura los servidores MCP remotos HTTPS, acciones y políticas en la consola. Las herramientas sin mapeo no se autorizan.
- **MCP de administración:** `/mcp` utiliza tokens `dgm_` separados de las claves de agente. El token demo está disponible en `Development`.
- **AI Gateway:** proceso independiente compatible con OpenAI en `http://127.0.0.1:5088/v1`. Configura el catálogo en la consola y `Providers__openai__ApiKey` en el gateway; inicia con `dotnet run --project src/Deleguard.AiGateway --no-launch-profile --urls http://127.0.0.1:5088`. Lee su [guía](src/Deleguard.AiGateway/README.md). Las claves del proveedor permanecen en el gateway; solo los metadatos se envían a autorización.
- **IDE y cliente de escritorio:** consulta la [guía de conexión por proyecto](integrations/ide/README.md) y el [contrato del instalador portable](deleguard_dev_pack/spec/portable-installer.md). La publicación del código fuente no incluye instaladores firmados.
- El [adaptador MCP local heredado](src/Deleguard.McpGuard/README.md) se conserva por compatibilidad; las nuevas conexiones usan el Guard remoto.

## Compilar y validar

Desde la raíz, con el SDK .NET 10:

```powershell
dotnet build Deleguard.slnx
dotnet test tests/Deleguard.UnitTests
dotnet test tests/Deleguard.DemoAgent.Tests
dotnet test tests/Deleguard.Desktop.Tests
```

Las suites de integración y seguridad necesitan Docker activo: crean bases PostgreSQL desechables con Testcontainers. No requieren tener la API local abierta.

```powershell
dotnet test tests/Deleguard.IntegrationTests
dotnet test tests/Deleguard.SecurityTests
```

Para ejecutar la verificación de aceptación usada en CI, necesitas además Python y Node.js:

```powershell
pwsh -File scripts/v1/verify.ps1
```

Ese script comprueba las suites principales, el cliente .NET, los contratos del adaptador IDE y la carga de los clientes Python y TypeScript.

### Estado de la validación inicial

Se verificaron la compilación Release, el arranque de API y worker, `/health` y las tres decisiones del ejemplo Python. Pasaron 534 pruebas unitarias, 36 de seguridad, 98 de integración y 15 del agente demo. El sitio de documentación también compiló correctamente.

Quedan dos incidencias conocidas: NuGet informa `NU1902`/`NU1903` para `System.Security.Cryptography.Pkcs` y `System.Security.Cryptography.Xml` 10.0.12; el script de aceptación trata `NU1903` y `NU1904` como errores. La suite de escritorio pasó 21 pruebas, omitió una y falló en la comprobación de ancho de ventana de [WindowTests.cs](tests/Deleguard.Desktop.Tests/WindowTests.cs) en modo headless. El inicio local documentado funcionó; estas incidencias siguen pendientes de corrección.

## Estructura del repositorio

| Ruta | Contenido |
| --- | --- |
| `src/Deleguard.Api` | HTTP, consola Razor y endpoints MCP |
| `src/Deleguard.Application` | Casos de uso y contratos de aplicación |
| `src/Deleguard.Domain` | Entidades e invariantes |
| `src/Deleguard.Policy` | DSL YAML y motor de decisiones nativo |
| `src/Deleguard.Infrastructure` | Persistencia EF Core, credenciales, permisos y datos demo |
| `src/Deleguard.Workers` | Outbox, webhooks, vencimientos y retención |
| `src/Deleguard.AiGateway` | Gateway opcional para proveedores de IA |
| `src/Deleguard.Desktop`, `src/Deleguard.Desktop.Core` | Cliente de conexión por proyecto |
| `demo/`, `examples/`, `sdks/` | Demo y clientes de integración |
| `policies/` | Políticas de producto y fixtures |
| `integrations/ide/` | Adaptadores para IDE |
| `docs-site/` | Documentación Astro/Starlight |
| `deploy/`, `.github/workflows/` | Contenedores y automatización de validación |
| `deleguard_dev_pack/` | PRD, ADRs, contratos y material de diseño; no es código de ejecución |

Para conocer los contratos, empieza por [START_HERE](deleguard_dev_pack/START_HERE.md), el [PRD](deleguard_dev_pack/docs/02_PRD_MVP.md), [OpenAPI](deleguard_dev_pack/spec/openapi.yaml) y el [contrato de ejecución](deleguard_dev_pack/spec/execution-v1.md).

## Detener y resolver problemas locales

Detén API y worker con `Ctrl+C` en sus respectivas terminales. Para detener los contenedores conservando el volumen:

```powershell
docker compose -f deploy/docker-compose.yml down
```

| Síntoma | Comprobación |
| --- | --- |
| No conecta con PostgreSQL o `/health` falla | Revisa `docker compose -f deploy/docker-compose.yml ps` y la conexión configurada en ambos procesos. |
| El puerto 5432, 6379, 5080 o 4321 está ocupado | Libera el puerto o ajusta el mapeo/URL y las conexiones que lo usan. |
| Aparece `Production requires...` al iniciar | En el inicio local, configura las dos variables de entorno como `Development` en esa terminal. |
| El ejemplo Python intenta acceder a 8080 | Exporta `DELEGUARD_URL=http://127.0.0.1:5080`; el valor por defecto del script es otro puerto. |
| `401` al llamar autorización | Usa la clave `dgk_` del agente; los tokens `dgc_` y `dgm_` corresponden a otras superficies. |
| `DENY` inesperado | Verifica organización, aplicación, agente, entorno, acciones concedidas y política publicada. Una acción sin política aplicable se deniega. |
| Una aprobación no permite ejecutar | Revisa rol del aprobador, vencimiento y estado de la decisión; consulta y consume el permiso vigente antes de ejecutar. |

Para revisar los servicios de datos: `docker compose -f deploy/docker-compose.yml logs postgres redis`. Elimina un volumen únicamente si deseas descartar explícitamente sus datos.
