# Local development and operations

[← Back to the README](../README.md)

This guide expands on the [quick start](../README.md#quick-start) with configuration, optional components, validation, and troubleshooting. Run commands from the repository root unless another directory is specified.

## Optional tools

| Purpose | Requirement |
| --- | --- |
| Authorization example | Python 3, with no external packages |
| Local documentation site | Node.js **22.12 or later** and npm |
| TypeScript client without a compiler | Node.js **22.18 or later** |

You do not need AI provider keys for the basic demo. The API and worker use the quick start prerequisites: the .NET 10 SDK, Docker with Linux containers, and Compose v2.

## Demo access

The quick start uses PostgreSQL at `localhost:5432`, with `deleguard` as the database name, username, and password; Redis is exposed at `localhost:6379`. The API and worker run in `Development`. These credentials are public and intended only for local development.

| Purpose | Local value |
| --- | --- |
| Console email | `admin@demo.local` |
| Password | `DeleguardDemo!23` |
| Console recovery token | `dgc_demo_console_local_dev_only` |
| Agent key for `/v1/authorize` | `dgk_demo_invoice_agent_local_dev_only` |
| Administration token for `/mcp` | `dgm_demo_admin_local_dev_only` |

| Interface | Address |
| --- | --- |
| Console | `http://127.0.0.1:5080/login` |
| Approvals | `http://127.0.0.1:5080/approvals` |
| Health | `http://127.0.0.1:5080/health` |
| Generated OpenAPI | `http://127.0.0.1:5080/openapi/v1.json` |
| Administration MCP | `http://127.0.0.1:5080/mcp` |
| Remote Guard MCP | `http://127.0.0.1:5080/mcp/guard` |

The demo agent's environment is named `production`; this is an attribute of its business registration, independent of the .NET `Development` environment.

## Configuration

The API and worker read their respective `appsettings.json` files and .NET environment variables. Use double underscores (`__`) for nested keys. For example, if PostgreSQL uses a different port, set the same connection in **both terminals** before starting the processes:

```powershell
$env:ConnectionStrings__Postgres = "Host=localhost;Port=5433;Database=deleguard;Username=deleguard;Password=YOUR_LOCAL_PASSWORD"
```

| Variable | Purpose |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | Runtime environment. Use `Development` for local startup. |
| `ConnectionStrings__Postgres` | PostgreSQL connection shared by the API and worker. |
| `Permits__SigningKey` | Permit signing key; it must match across processes that use it. |
| `DemoAuth__ApiKeyPepper` | Pepper for credential hashes. Changing it invalidates existing credentials. |
| `DemoAuth__ConsoleToken`, `DemoAuth__AllowEmergencyConsoleToken` | Local recovery access. |
| `Webhooks__ProtectionKey` | Webhook secret protection key; it must match between the API and worker. |
| `Webhooks__AllowLoopback`, `Webhooks__AllowInsecureHttp` | Exceptions for testing local webhooks; disabled by default during local startup. |
| `Oidc__Enabled`, `Oidc__Authority`, `Oidc__ClientId`, `Oidc__ClientSecret` | Sign-in through an OIDC provider. |
| `Oidc__RequireVerifiedEmail` | Requires a verified email address with OIDC. |
| `DataProtection__KeyPath` | Persistent directory for session keys; defaults to `dpkeys` inside the API project. |
| `Docs__PublicUrl` | Link to the documentation site; defaults to `http://localhost:4321`. |

A `.env` file is not loaded automatically when running `dotnet run`. Export the variables in each terminal or use a secrets manager. Do not commit real keys to version control.

### Full Docker Compose stack and production

[deploy/docker-compose.yml](../deploy/docker-compose.yml) also defines images for the API, worker, documentation, Keycloak, and Mailpit. **The full stack requires configuration changes before running it**: it currently sets the API to `Production` while including demo keys, HTTP OIDC, and webhook exceptions that the application rejects. Use the [local quick start](../README.md#quick-start) for a reproducible demo.

In production, the application requires unique secrets of at least 32 characters for signing, the pepper, and webhooks; PostgreSQL with its own password and `SSL Mode=VerifyFull`; OIDC with HTTPS and verified email; persistent Data Protection keys; and no demo tokens or insecure webhook exceptions. Demo data is not loaded in production, and migrations must run as a controlled step with `--migrate-only` before starting the API and worker. See [Program.cs](../src/Deleguard.Api/Program.cs) and [ProductionConfiguration.cs](../src/Deleguard.Infrastructure/ProductionConfiguration.cs) for the exact conditions.

[docker-compose.dev.yml](../deploy/docker-compose.dev.yml) is an override for in-memory PostgreSQL storage, not an environment override for the entire application. Use it only with disposable data: that database's contents are lost when its container is stopped or recreated.

## Optional components

### Documentation site

User documentation in English and Spanish, with integration guides and console screenshots. You can run it without installing Node on your computer:

```powershell
docker compose -f deploy/docker-compose.yml up -d --build docs
```

Open [http://localhost:4321/en/](http://localhost:4321/en/) for English documentation. Spanish documentation is available at [http://localhost:4321/](http://localhost:4321/). To edit the site with Node and npm:

```powershell
cd docs-site
npm ci
npm run dev
```

To check the static build, run `npm run build` from `docs-site`. See its [README](../docs-site/README.md).

### Demo agent web interface

With the API running, open another terminal at the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:DOTNET_ENVIRONMENT = "Development"
$env:DELEGUARD_URL = "http://127.0.0.1:5080"
dotnet run --project demo/Deleguard.DemoAgent --no-launch-profile --urls http://127.0.0.1:5280
```

Open [http://127.0.0.1:5280](http://127.0.0.1:5280). Without `OPENAI_API_KEY`, the agent uses a local demo interpreter. The AI provider is optional and is configured only in this process.

### MCP, AI Gateway, and IDE integration

- **Remote Guard MCP:** served by the API at `/mcp/guard`, using `dgk_` agent keys. Configure HTTPS remote MCP servers, actions, and policies in the console. Unmapped tools are not authorized.
- **Administration MCP:** `/mcp` uses `dgm_` tokens, separate from agent keys. The demo token is available in `Development`.
- **AI Gateway:** a separate OpenAI-compatible process at `http://127.0.0.1:5088/v1`. Configure the catalog in the console and `Providers__openai__ApiKey` in the gateway; start it with `dotnet run --project src/Deleguard.AiGateway --no-launch-profile --urls http://127.0.0.1:5088`. Read its [guide](../src/Deleguard.AiGateway/README.md). Provider keys remain in the gateway; only metadata is sent for authorization.
- **IDE and desktop client:** see the [per-project connection guide](../integrations/ide/README.md) and [portable installer contract](../deleguard_dev_pack/spec/portable-installer.md). Publishing the source code does not include signed installers.
- The [legacy local MCP adapter](../src/Deleguard.McpGuard/README.md) is retained for compatibility; new connections use the remote Guard.

## Build and validation

From the repository root, with the .NET 10 SDK:

```powershell
dotnet build Deleguard.slnx
dotnet test tests/Deleguard.UnitTests
dotnet test tests/Deleguard.DemoAgent.Tests
dotnet test tests/Deleguard.Desktop.Tests
```

The integration and security suites require Docker to be running: they create disposable PostgreSQL databases with Testcontainers. They do not require the local API to be running.

```powershell
dotnet test tests/Deleguard.IntegrationTests
dotnet test tests/Deleguard.SecurityTests
```

To run the acceptance checks used in CI, you also need Python and Node.js:

```powershell
pwsh -File scripts/v1/verify.ps1
```

This script checks the main suites, the .NET client, the IDE adapter contracts, and loading the Python and TypeScript clients.

### Initial validation results

The Release build, API and worker startup, `/health`, and all three decisions in the Python example were verified. The following tests passed: 534 unit tests, 36 security tests, 98 integration tests, and 15 demo agent tests. The documentation site also built successfully.

Two known issues remain: NuGet reports `NU1902`/`NU1903` for `System.Security.Cryptography.Pkcs` and `System.Security.Cryptography.Xml` 10.0.12; the acceptance script treats `NU1903` and `NU1904` as errors. The desktop suite passed 21 tests, skipped one, and failed the window width check in [WindowTests.cs](../tests/Deleguard.Desktop.Tests/WindowTests.cs) in headless mode. The documented local startup worked; these issues are still pending resolution.

## Repository structure

| Path | Contents |
| --- | --- |
| `src/Deleguard.Api` | HTTP, Razor console, and MCP endpoints |
| `src/Deleguard.Application` | Use cases and application contracts |
| `src/Deleguard.Domain` | Entities and invariants |
| `src/Deleguard.Policy` | YAML DSL and native decision engine |
| `src/Deleguard.Infrastructure` | EF Core persistence, credentials, permits, and demo data |
| `src/Deleguard.Workers` | Outbox, webhooks, expiry, and retention |
| `src/Deleguard.AiGateway` | Optional gateway for AI providers |
| `src/Deleguard.Desktop`, `src/Deleguard.Desktop.Core` | Per-project connection client |
| `demo/`, `examples/`, `sdks/` | Demo and integration clients |
| `policies/` | Product policies and fixtures |
| `integrations/ide/` | IDE adapters |
| `docs-site/` | Astro/Starlight documentation |
| `deploy/`, `.github/workflows/` | Containers and validation automation |
| `deleguard_dev_pack/` | PRD, ADRs, contracts, and design material; not runtime code |

For the contracts, start with [START_HERE](../deleguard_dev_pack/START_HERE.md), the [PRD](../deleguard_dev_pack/docs/02_PRD_MVP.md), [OpenAPI](../deleguard_dev_pack/spec/openapi.yaml), and the [execution contract](../deleguard_dev_pack/spec/execution-v1.md).

## Stopping services and troubleshooting

Stop the API and worker with `Ctrl+C` in their respective terminals. To stop the containers while preserving the volume:

```powershell
docker compose -f deploy/docker-compose.yml down
```

| Symptom | Check |
| --- | --- |
| PostgreSQL connection fails or `/health` fails | Check `docker compose -f deploy/docker-compose.yml ps` and the connection configured in both processes. |
| Port 5432, 6379, 5080, or 4321 is in use | Free the port or adjust its mapping/URL and the connections that use it. |
| `Production requires...` appears at startup | For local startup, set both environment variables to `Development` in that terminal. |
| The Python example tries to access port 8080 | Export `DELEGUARD_URL=http://127.0.0.1:5080`; the script defaults to a different port. |
| Authorization calls return `401` | Use the agent's `dgk_` key; `dgc_` and `dgm_` tokens are for other interfaces. |
| Unexpected `DENY` | Verify the organization, application, agent, environment, granted actions, and published policy. An action with no applicable policy is denied. |
| An approval does not allow execution | Check the approver's role, expiry, and decision status; retrieve and consume the current permit before executing. |

To inspect the data services, run `docker compose -f deploy/docker-compose.yml logs postgres redis`. Delete a volume only when you explicitly want to discard its data.
