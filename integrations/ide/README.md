# DeleGuard portable project connections

The default setup is the visual .NET/Avalonia client. It includes the adapter
runtime; developers do not need Python installed. English is the default and
Spanish is available in both IDE Control and the client.

1. Prepare a project in **IDE Control**, keep its agent API key, and download its
   connection profile. The profile contains scope identifiers, never a secret.
2. Open the portable client, import the profile and choose the project folder
   and editor. Enter that project's key and review the exact files to change.
3. Connect, restart the editor, and follow the approval test in IDE Control.
   A successful connection alone does not demonstrate protected execution.

Open the same portable client again for another project or account. Credentials,
runtime, history and diagnostics stay in `.deleguard/<editor>/` in the selected
project. It does not create a global account, project history, service, startup
task or persistent environment variable. Windows uses a project-local DPAPI
vault; macOS/Linux use a password-protected local vault and a temporary process
for each unlocked project. Closing the client locks those sessions.

An existing connection can be checked, updated or explicitly disconnected. No
installation rotates remote keys. Disconnect preserves local operation history
and other projects. Copied/moved folders, links, tracked credential paths and
customized managed files require resolution before installation can continue.

See [portable contracts and release acceptance](../../deleguard_dev_pack/spec/portable-installer.md).
Build a **private validation package** with `python scripts/build-portable-installer.py`.
Only signed, natively tested release packages may be published. The historical
source ZIP is no longer generated or publicly served by the web application.

## Existing installations (legacy CLI reference)

The following material documents schema 1 for support and migration. New
connections must use the portable visual client above. These legacy commands
may use external credential stores and do **not** satisfy the no-global-state
contract. Migrate only the selected project; review ownership before removing
any shared external credential.

### Legacy project adapter

Integración local para Cursor, VS Code/Copilot y Claude Code: hooks, diagnóstico, una skill de ayuda y un agente revisor. No requiere AI Gateway. Es un **paquete instalable en el proyecto**, no una extensión de marketplace firmada ni un broker aislado.

## Empezar

1. En la consola DeleGuard, crea el agente y registra el proyecto en **IDE Control**. Confirma qué carpeta quieres proteger. Conserva la clave del agente fuera del chat y del repositorio.
2. Aplica el perfil disponible y revisa sus resultados antes de publicar. Un proyecto registrado sin acciones asignadas y políticas publicadas todavía no está listo. Las carpetas del proyecto restringen las políticas: no convierten un bloqueo en permiso.
3. Descarga el instalador completo, incluida su carpeta `assets`. Elige el IDE y ejecuta la instalación como usuario humano, fuera de una operación activa del agente. Necesitas Python 3.11+.
4. Abre ese proyecto en el IDE, revisa los archivos instalados y completa las confirmaciones de confianza/autenticación que muestre el IDE. Ejecuta el diagnóstico y luego la prueba protegida descrita abajo.

Ejemplo desde este repositorio, en PowerShell:

```powershell
./integrations/ide/install.ps1 --root C:/work/mi-tienda --url https://deleguard.example --project mi-tienda --agent mi-agente --environment develop --adapter cursor
```

En macOS/Linux:

```sh
sh integrations/ide/install.sh --root /work/mi-tienda --url https://deleguard.example --project mi-tienda --agent mi-agente --environment develop --adapter cursor
```

En el ZIP los instaladores están en la raíz: usa `./install.ps1` o `sh ./install.sh`. Cambia `--adapter` a `vscode` o `claude` según corresponda. `--root` es la carpeta del proyecto que abrirás, no la carpeta donde extrajiste el ZIP; si lo omites, se usa la carpeta actual. La API requiere HTTPS; HTTP solo se permite en loopback para desarrollo local.

Añade `--plan` para revisar destinos e identidad de credencial sin escribir, pedir una clave ni conectar a la API. `--profile` se conserva como opción heredada pero no identifica la nueva instalación. `--replace-managed` permite reemplazar archivos administrados por DeleGuard modificados localmente: revisa el plan antes de usarlo; no es una autorización para borrar configuración ajena.

No pongas claves en los argumentos del comando, variables versionadas, hooks, MCP ni skills. Windows utiliza DPAPI; Linux requiere Secret Service desbloqueado con `secret-tool`. En macOS, usa `--plan` para obtener la identidad de credencial, crea en Keychain un elemento de contraseña genérica con servicio `deleguard-ide` y esa identidad como cuenta, y después instala con `--keychain-ready`. La ausencia de un almacén seguro no habilita un archivo de claves en texto plano.

## Varios proyectos en la misma máquina

Cada raíz y adaptador tienen configuración, runtime y estado propios:

```text
mi-tienda/
  .deleguard/
    cursor/
      config.json
      deleguard_ide.py
      project_setup.py
      state.sqlite
  .cursor/
    hooks.json
    mcp.json                         # si se conectó Guard MCP
    skills/deleguard-project/SKILL.md
    agents/deleguard-reviewer.md
```

Los hooks indican el archivo local exacto: no seleccionan un perfil de un directorio global. La identidad de credencial se deriva del servidor, raíz, proyecto, agente, entorno y adaptador. Otra carpeta, worktree, entorno o servidor obtiene una identidad diferente. Instalar un proyecto no cambia la configuración de los demás.

Las credenciales permanecen fuera del repositorio, cifradas en el almacén del sistema y separadas por identidad de instalación. Que el sistema operativo almacene secretos en una ubicación común no significa que todos los proyectos compartan una clave. El journal `state.sqlite` pertenece al proyecto/adaptador, no se sincroniza y debe conservarse durante actualizaciones.

No copies `.deleguard` ni el journal para conectar otra carpeta. Ejecuta una instalación propia. No borres el journal para solucionar un bloqueo: puede contener una operación ya liberada cuyo resultado aún es incierto. La configuración local y el estado no deben versionarse; revisa las exclusiones generadas antes de hacer commit.

| Elemento | Cursor | VS Code/Copilot | Claude Code |
| --- | --- | --- | --- |
| Configuración | `.deleguard/cursor/config.json` | `.deleguard/vscode/config.json` | `.deleguard/claude/config.json` |
| Hooks | `.cursor/hooks.json` | `.github/hooks/deleguard.json` | `.claude/settings.json` |
| Skill | `.cursor/skills/deleguard-project/` | `.github/skills/deleguard-project/` | `.claude/skills/deleguard-project/` |
| Revisor | `.cursor/agents/deleguard-reviewer.md` | `.github/agents/deleguard-reviewer.agent.md` | `.claude/agents/deleguard-reviewer.md` |
| MCP | `.cursor/mcp.json` | `.vscode/mcp.json` | `.mcp.json` |

Los archivos existentes de otros integradores se conservan. Revisa cualquier conflicto que informe el instalador; no reemplaces toda la configuración del IDE para resolverlo. En espacios multi-root, configura cada raíz y prueba su selección: abrir dos carpetas no demuestra que los hooks se enruten correctamente.

### Migrar una instalación anterior

Añade `--migrate-profile nombre-anterior` a la instalación. La migración requiere que coincidan raíz, URL, proyecto, agente, entorno y adaptador, y comprueba el comando anterior antes de sustituirlo. No utiliza un perfil de otro proyecto como alternativa. Conserva la configuración y el historial anterior hasta verificar los resultados inciertos; no traslada permisos entre identidades diferentes.

## Skill y agente revisor

Pide **“Usa deleguard-project para diagnosticar la protección de este proyecto”**. La skill explica el diagnóstico, diferencia un fallo de conexión de una decisión de política y guía las aprobaciones sin proponer saltarse el bloqueo.

Pide **“Usa deleguard-reviewer para revisar esta instalación sin modificarla”**. El revisor se limita a inspección y recomendaciones; no publica políticas, aprueba solicitudes ni ejecuta la operación protegida. Cursor usa un agente de solo lectura; los otros adaptadores restringen sus herramientas a lectura/búsqueda.

Estas instrucciones mejoran la colaboración del agente. **No son controles de seguridad** y no sustituyen los hooks, las políticas, la autenticación ni el aislamiento.

## Diagnóstico y errores concretos

Para reemplazar una clave revocada o ilegible, repite el comando de instalación del **mismo proyecto** con `--reconnect`: solicita una clave nueva, comprueba su proyecto/organización y conserva el historial. En macOS actualiza primero la cuenta exacta del Llavero y usa `--keychain-ready`. No borres el journal para reparar credenciales. La migración `--migrate-profile` copia también el historial SQLite, incluidas operaciones pendientes o de resultado incierto; no las ejecuta.

Desde la raíz del proyecto, ejecuta solo el comando de tu IDE (`python3` en macOS/Linux):

```sh
python .deleguard/cursor/deleguard_ide.py doctor --config .deleguard/cursor/config.json
python .deleguard/vscode/deleguard_ide.py doctor --config .deleguard/vscode/config.json
python .deleguard/claude/deleguard_ide.py doctor --config .deleguard/claude/config.json
```

El diagnóstico no ejecuta la operación bloqueada ni consume un permiso para comprobar conectividad. Si el usuario pidió no usar terminal, el agente debe respetarlo y ofrecer este paso al usuario, no leer el archivo por otra vía.

| Problema | Qué revisar |
| --- | --- |
| Configuración ausente o raíz diferente | Instalar en la carpeta abierta; no elegir otro perfil para eludir el error. |
| Credencial ausente o ilegible | Almacén seguro y usuario del sistema que instaló el adaptador. No supone por sí solo que la clave sea incorrecta. |
| API devuelve 401/403 | Autenticación, revocación y alcance; comprobar el agente del proyecto sin revelar la clave. |
| API inaccesible | Dirección, TLS, conectividad y servidor; `localhost` se refiere a la máquina donde corre el hook. |
| Herramienta no reconocida o datos incompletos | Versión del IDE, nombre/esquema de herramienta y hook requerido. No inventar equivalencias ni permitir por defecto. |
| Política bloquea | Asignación y versión publicada; simular la acción exacta antes de modificar la regla. |
| Requiere aprobación | Abrir la solicitud indicada y esperar su resolución en DeleGuard. |
| Resultado incierto | Conservar el journal y revisar qué ocurrió antes de reintentar. |

`connected` solo verifica conexión. Un archivo de hook presente solo verifica instalación en disco. Ninguno prueba que el IDE haya ejecutado ese hook, que MCP esté autenticado o que todas las acciones estén cubiertas.

## Conectar Guard MCP

Añade `--mcp-url https://deleguard.example/mcp/guard` al instalar para preparar la conexión **HTTPS** en el proyecto. La confianza y la autenticación interactiva se completan en el IDE. No agregues el mismo downstream directamente como alternativa.

Configurar una URL **no implementa OAuth en el servidor**. No se debe anunciar MCP listo hasta que el servidor desplegado admita autenticación compatible, el IDE complete el inicio de sesión y una operación autorizada se pruebe. Si el despliegue solo admite claves estáticas, no pegues `dgk_` en el archivo MCP para hacerlo funcionar: esa compatibilidad requiere trabajo en el servidor.

La clave del hook y el inicio de sesión MCP son mecanismos diferentes. El paquete no exporta la credencial del hook a un servidor MCP ni registra procesos `stdio` para Guard remoto. Otras conexiones configuradas globalmente en el IDE pueden seguir existiendo: deben auditarse por separado; el instalador no las elimina silenciosamente.

## Probar protección real

Usa un archivo de prueba sin secretos y una regla cuyo resultado conozcas.

1. Pide una lectura nativa que deba requerir aprobación. Debe detenerse **antes** de leer y mostrar una solicitud con proyecto, acción y ruta correctos.
2. Recházala y comprueba que sigue bloqueada. Repite con una nueva operación aprobada; solo después de la aprobación registrada debe poder liberarse una llamada.
3. Prueba un archivo permitido y uno denegado, API desconectada y herramienta no reconocida. Un bloqueo de integración no se debe confundir con un `DENY` de política.
4. Revisa resultado y evidencia. Cambiar argumentos, herramienta o archivo mientras se espera debe invalidar esa autorización. No debe haber reejecución automática después de un reinicio o timeout.

Que `infra/production` solicite aprobación no significa que la raíz sea escribible. **Crear `prueba.txt` en la raíz depende de la política publicada**; una política limitada a `src` puede bloquearlo correctamente. Simula ese caso si quieres permitirlo. No amplíes reglas para ocultar un error de instalación.

### “No applicable policy” no es una aprobación pendiente

Ese `DENY` significa que en esa solicitud no había una política publicada y asignada aplicable al agente y acción. Un borrador, una carpeta marcada como protegida o el riesgo `High` no sustituyen una política publicada. Revisa la aplicación y el agente en la consola, compara los casos guardados y publica la regla prevista. No cambies una prohibición a permiso solo para que desaparezca el mensaje.

Desde la versión 3.0.2, el adaptador diferencia una denegación definitiva de una solicitud pendiente y enlaza el registro específico. Tras corregir la configuración, una **nueva llamada explícita** del IDE se autoriza de nuevo; no modifica la decisión anterior ni ejecuta nada automáticamente. Conserva los registros antiguos y no borres `state.sqlite`: una llamada ya liberada o completada sigue bloqueada contra repetición.

Las concesiones de carpeta son temporales y limitadas a acciones de archivo seguras. No permiten borrado, procesos, red, credenciales, cambios de IDE ni MCP. Un `DENY` explícito o una aprobación exigida por la política no se convierte en permiso mediante una concesión.

## Límites y compatibilidad

En Windows, el lanzador Cursor 3.0.1 reproduce el transporte de Cursor 3.17.x: entrada desde archivo UTF-8 sin BOM y pipeline PowerShell. Ejecuta Python directamente en ese pipeline, sin un segundo PowerShell, conserva UTF-8 y reconoce BOM en el adaptador. El contrato queda fijado en la configuración; otra familia de versiones se bloquea con `cursor_transport_unverified` hasta validar su transporte, para no transformar argumentos incorrectamente. No ejecutes el comando interno del hook como si fuera un comando de CMD: su anfitrión es PowerShell de Cursor. `doctor` usa el CLI normal.

Esta entrega usa hooks: su cobertura sigue siendo parcial. No aísla procesos, red, extensiones, indexación, ejecución fuera del agente ni efectos internos de scripts. Un proceso con la identidad del usuario puede modificar la integración. Existe una ventana entre comprobación y ejecución nativa; no se presenta como garantía de aislamiento o ejecución exactamente una vez.

La presencia de 22 categorías en el servidor no significa que el adaptador intercepte las 22. Las capacidades reportadas no son atestación. Conserva los controles nativos del IDE. Los fallos del propio lanzador, salida inválida y timeouts dependen del IDE: por ejemplo, Claude Code puede tratar errores de hooks distintos de la salida bloqueante como no bloqueantes. DeleGuard no puede prometer fail-closed si el IDE no invoca el hook o ignora su fallo. Si una prueba permite ejecutar en esas condiciones, esa combinación no está validada.

Consulta [VALIDATION.md](VALIDATION.md) para separar pruebas automatizadas de pruebas reales pendientes en cada IDE/sistema.

Formatos oficiales consultados el 17 de septiembre de 2026:

- Cursor: [skills](https://prod.cursor.com/docs/skills), [subagentes](https://prod.cursor.com/docs/subagents), [hooks](https://prod.cursor.com/docs/hooks), [MCP](https://prod.cursor.com/docs/mcp).
- VS Code: [skills](https://code.visualstudio.com/docs/agent-customization/agent-skills), [agentes](https://code.visualstudio.com/docs/agent-customization/custom-agents), [hooks](https://code.visualstudio.com/docs/agents/reference/hooks-reference), [MCP](https://code.visualstudio.com/docs/agent-customization/mcp-servers).
- Claude Code: [skills](https://code.claude.com/docs/en/skills), [subagentes](https://code.claude.com/docs/en/sub-agents), [hooks](https://code.claude.com/docs/en/hooks), [MCP](https://code.claude.com/docs/en/mcp).
