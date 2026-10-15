# Portable project connections (schema 2)

The visual client is a .NET 10/Avalonia portable directory. It includes a
PyInstaller **one-directory** adapter; neither Python nor .NET need to be
installed by the developer. Single-file extraction to system temporary paths
is not used. English is the default; Spanish can be selected in each window.

## Console contracts

- `GET /ide/connection-profile?project={id}&adapter=cursor|vscode|claude`
  requires a console session and `agents.read`. The selected organization and
  application scope both project and enabled agent. The JSON attachment uses
  `Cache-Control: no-store` and never includes a credential.
- Profile schema 1: `server_url`, `organization_id`, `application_id`,
  `project_id`, `project_name`, `agent_id`, `agent_external_id`, `environment`,
  `adapter`, `language`. An HTTPS origin or loopback HTTP origin is required.
- `GET /v1/ide/projects/{id}/progress` requires `agents.read` and `audit.read`.
  It reports last connection, last IDE decision, decision ID and last reported
  result. A console simulation does not complete IDE connection progress.
- `GET /v1/ide/projects/{project}/decisions/{id}` requires that project's agent
  key and checks organization, application, agent and resource `project_id`.
  Its response uses the existing decision contract.
- `GET /ide/installer?platform={rid}` is authenticated. Only platform-tested,
  operator-approved stable signed artifacts are available publicly. Integrity
  is checked against SHA-256 before download.

Existing authorization, approval, consumption and evidence contracts remain.

## Local protocol and boundaries

`worker/deleguard-adapter portable` reads one bounded JSON request on stdin and
returns JSON on stdout. Supported operations: preview, install, doctor,
disconnect. Secrets are never command arguments, environment variables or log
messages. Preview does not write the selected project. Installation validates
the agent key and exact remote scope before committing files.

All managed persistence is in the selected canonical project:

```
.deleguard/<editor>/
  config.json            # scope and identity, no API key
  credential.vault       # authenticated local encryption
  manifest.json          # owned files, hashes, exact hook definitions
  runtime/               # native client and bundled adapter
  state.sqlite           # operations and evidence outbox
  pending-results/       # minimal durable completion receipts
  session.json           # private ephemeral broker descriptor, no API key
  backups/               # local rollback copies
  history/               # disconnected journals, with unique archive IDs
.deleguard/.setup.lock   # project-wide shared hook / exclusive setup lock
```

Only owned entries are merged into the project's editor directories. Git
exclusions are installed first and hooks last. Tracked `.deleguard` files block
credential installation. Links, moved configurations, modified managed files
and scope changes are rejected before replacement. Project names are labels;
identity hashes canonical root, origin, organization, application, project,
agent, environment and editor. Install never rotates the remote key.

Windows stores a CurrentUser DPAPI envelope in the project. Other platforms use
AES-256-GCM, a random salt and nonce, PBKDF2-HMAC-SHA256 with 600,000 iterations,
and a password of at least 12 characters. There is no plaintext fallback.

Each unlocked macOS/Linux project has its own temporary child process. It
retains the key in memory and proxies an exact route/action allowlist. Local
requests and replies use HMAC-SHA256 over identity, direction, nonce and entire
payload; replayed requests are rejected. The capability never travels on the
socket. Every forwarded request checks current server scope; decisions,
consumption and evidence are also checked against the project. Closing the
parent's pipe locks the child session. No key-export operation exists.

## Reuse, migration and disconnect

Reinstalling preserves the operation journal and deduplicates hooks. A different
account requires explicit disconnect or a different folder. Setup obtains an
exclusive lock; active hooks hold a shared lock. Minimal completion receipts
survive SQLite contention and never contain tool arguments or output.

Schema 1 migration requires an explicit selection and the same canonical scope.
The original Python launcher is parsed without executing it; the entire hook
template must match the selected project. Migration installs the local vault
and preserves history. It **does not automatically delete old external
credentials**: the UI requires ownership review, particularly for shared
legacy profiles. No new global credential is created. Legacy dependencies may
be removed only after all their owners are identified and migrated.

Disconnect takes a coherent SQLite backup, preserves completion receipts and
removes only manifest-validated owned hooks/files. Shared remote keys are not
revoked. Locked Windows runtime files can remain inert in the project and be
removed after their process exits; configuration and credential are removed.

## Release and acceptance

`scripts/build-portable-installer.py` builds a private validation artifact with
`signed=false` and `platform_tested=false`. The CI matrix builds and tests each
package on its actual OS; it does not publish installers.

Production release operations must sign the native executable and adapter,
verify trust on a clean target host, execute the acceptance matrix against the
signed package, then issue its immutable SHA-256 manifest as `channel=stable`,
`signed=true`, `platform_tested=true`. Release metadata is an administrator
trust boundary, not a user upload. Configure an absolute
`PortableInstaller:ReleaseDirectory` outside wwwroot.

For local Development validation only, `PortableInstaller:AllowLocalPreview`
can expose an explicitly tested private build to loopback requests; the UI
labels it unsigned. This exception is disabled outside Development or for
non-loopback clients. Unavailable platforms have no download link.

Before public release, test multiple accounts concurrently, reinstallation,
update, disconnect, copied/moved/linked paths, foreign/custom files, tracked
secrets, wrong vault password, modified vault, locked session, revoked key,
wrong organization, retained evidence and keyboard/narrow-screen flows in both
languages. Measure the five-minute connection and ten-minute first-operation
targets with new users; automated tests do not establish these usability times.

## Compatibility boundary

The portable client requires the updated IDE configuration endpoint. Its scope
confirmation includes the project ID, authenticated agent external ID and
environment, in addition to organization/application/agent IDs. An older server
that omits these fields is rejected before installation; upgrade the server
first. Older clients can still read the existing configuration fields.

The bundled adapter only accepts `portable` or project-local `--config` hook,
diagnostic and outbox commands. Legacy `install` / `--profile` commands and
global credential-store functions are disabled in frozen builds. Schema 1
support remains in the source CLI solely for migration and existing clients.

Cryptographic parameters follow the approved plan and use platform/library
implementations; see [OWASP cryptographic storage](https://cheatsheetseries.owasp.org/cheatsheets/Cryptographic_Storage_Cheat_Sheet.html)
and [OWASP password derivation guidance](https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html).

On macOS/Linux a tiny native launcher disables CoreCLR diagnostic IPC before
starting the managed client, using an environment variable scoped to that
process only. This prevents the runtime's default system-temp diagnostic socket.
The native build requires `cc`; end users do not need a compiler. Sign this
launcher and the managed apphost as part of platform release acceptance. See
[.NET diagnostic configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/debugging-profiling).
