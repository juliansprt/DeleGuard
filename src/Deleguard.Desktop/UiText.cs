namespace Deleguard.Desktop;

public sealed class UiText
{
    public string Language {get;set;}="en";
    public string this[string key]=>Language=="es" && Spanish.TryGetValue(key,out var value)?value:key;
    private static readonly Dictionary<string,string> Spanish=new()
    {
        ["Project connection"]="Conexión del proyecto",["Protect this project."]="Protege este proyecto.",
        ["Connect your editor with clear limits. Everything stays inside the folder you choose."]="Conecta tu editor con límites claros. Todo queda dentro de la carpeta que elijas.",
        ["1 · Choose"]="1 · Elige",["2 · Review"]="2 · Revisa",["3 · Connect"]="3 · Conecta",
        ["PROJECT ONLY"]="SOLO ESTE PROYECTO",["No global settings or shared credentials"]="Sin configuración global ni credenciales compartidas",
        ["Connection profile"]="Perfil de conexión",["Download it from IDE Control. It contains no API key."]="Descárgalo desde IDE Control. No contiene la API key.",
        ["Import profile"]="Importar perfil",["No profile selected"]="Ningún perfil seleccionado",["Project folder"]="Carpeta del proyecto",
        ["Choose folder"]="Elegir carpeta",["Editor"]="Editor",["Agent API key"]="API key del agente",
        ["Paste the key from IDE Control. Leave empty to keep a working local key."]="Pega la clave de IDE Control. Déjalo vacío para conservar una clave local que funciona.",
        ["Vault password"]="Contraseña de la bóveda",["At least 12 characters. Used only to unlock this project; it is never sent to DeleGuard."]="Al menos 12 caracteres. Solo desbloquea este proyecto; nunca se envía a DeleGuard.",
        ["Review installation"]="Revisar instalación",["Install in this project"]="Instalar en este proyecto",["Review the changes"]="Revisa los cambios",
        ["Only this folder will change. Existing operation history and unrelated editor settings are preserved."]="Solo cambiará esta carpeta. Se conservan el historial y las configuraciones ajenas del editor.",
        ["Files to write"]="Archivos que se escribirán",["Check connection"]="Comprobar conexión",["Unlock project"]="Desbloquear proyecto",["Lock project"]="Bloquear proyecto",
        ["Disconnect project"]="Desconectar proyecto",["Cancel"]="Cancelar",["Confirm disconnect"]="Confirmar desconexión",
        ["Remove this installation's hooks and local key? Other projects and remote keys will not change. Operation history is archived locally."]="¿Quitar los hooks y la clave local de esta instalación? Los otros proyectos y las claves remotas no cambiarán. El historial se archiva localmente.",
        ["Connection checked. Restart the editor and complete the approval test in IDE Control."]="Conexión comprobada. Reinicia el editor y completa la prueba de aprobación en IDE Control.",
        ["Connected. Next: restart your editor and test an approval from IDE Control."]="Conectado. Sigue reiniciando el editor y probando una aprobación desde IDE Control.",
        ["Unlocked for this session."]="Desbloqueado para esta sesión.",["Project locked."]="Proyecto bloqueado.",["Project disconnected. Other installations are unchanged."]="Proyecto desconectado. Las otras instalaciones se conservan.",
        ["Working…"]="Procesando…",["Choose a folder and import its connection profile to begin."]="Elige una carpeta e importa su perfil de conexión para empezar.",
        ["Existing connection loaded. Check it, unlock it, or enter a replacement key and review the update."]="Conexión existente cargada. Compruébala, desbloquéala o introduce otra clave y revisa la actualización.",
        ["Review customized files before replacing them"]="Revisar archivos personalizados antes de reemplazarlos",
        ["Migrate this existing project to local credentials"]="Migrar este proyecto a credenciales locales",
        ["Migration copies this project's connection. The old external credential requires separate ownership review before removal."]="La migración copia la conexión de este proyecto. Retirar la credencial externa anterior requiere revisar su uso por separado.",
        ["Files and hooks"]="Archivos y hooks",["Local credential"]="Credencial local",["Server connection"]="Conexión al servidor",
        ["Policy test"]="Prueba de política",["Hook coverage"]="Cobertura de hooks",["MCP connection"]="Conexión MCP",["Hook timeout"]="Tiempo límite del hook",
        ["OK"]="Correcto",["Needs attention"]="Requiere atención",["Not verified"]="Sin verificar",["Partial coverage"]="Cobertura parcial",["Not configured"]="Sin configurar",
        ["Closing this window locks the project sessions unlocked here."]="Cerrar esta ventana bloquea las sesiones de proyecto desbloqueadas aquí.",
        ["Language"]="Idioma",["Another project"]="Otro proyecto",
        ["Existing installations and custom files"]="Instalaciones anteriores y archivos personalizados",
        ["Connected with a project-local vault. Review the previous credential in your system store before removing it; shared credentials must be kept until their other connections are migrated."]="Conectado con una bóveda dentro del proyecto. Revisa la credencial anterior del almacén del sistema antes de eliminarla; conserva las compartidas hasta migrar sus otras conexiones.",
        ["Import the connection profile downloaded from IDE Control."]="Importa el perfil de conexión descargado desde IDE Control.",
        ["Select an existing project folder."]="Selecciona una carpeta de proyecto existente.",
        ["Use the complete portable package; its bundled runtime is missing."]="Usa el paquete portable completo; falta el runtime incluido.",
        ["The folder or its managed files use a link. Choose a direct project folder."]="La carpeta o sus archivos administrados usan un enlace. Elige una carpeta directa.",
        ["The saved identity does not match this folder. Reconnect explicitly; do not copy installation files between projects."]="La identidad guardada no coincide con esta carpeta. Reconecta explícitamente; no copies instalaciones entre proyectos.",
        ["This folder belongs to another connection. Disconnect it explicitly or choose another folder."]="Esta carpeta pertenece a otra conexión. Desconéctala explícitamente o elige otra carpeta.",
        ["The key was rejected. Generate or copy the key for this project's agent in IDE Control."]="La clave fue rechazada. Genera o copia en IDE Control la clave del agente de este proyecto.",
        ["The vault is locked, its password is incorrect, or its contents changed."]="La bóveda está bloqueada, la contraseña es incorrecta o su contenido cambió.",
        ["Use a vault password between 12 and 1024 characters."]="Usa una contraseña de bóveda de entre 12 y 1024 caracteres.",
        ["Unlock this project before checking or updating its connection."]="Desbloquea este proyecto antes de comprobar o actualizar su conexión.",
        ["Another setup is changing this folder. Wait for it to finish and review again."]="Otra instalación está cambiando esta carpeta. Espera a que termine y revisa de nuevo.",
        ["Files in .deleguard are tracked by Git. Remove them from version control before storing a key."]="Git está versionando archivos de .deleguard. Retíralos del control de versiones antes de guardar una clave.",
        ["Git tracking could not be checked. Make Git available and retry."]="No se pudo comprobar el seguimiento de Git. Habilita Git y vuelve a intentarlo.",
        ["Managed files were edited. Review their changes before allowing replacement."]="Se editaron archivos administrados. Revisa los cambios antes de permitir su reemplazo.",
        ["The project uses an older installation. Import its profile and select migration."]="El proyecto usa una instalación anterior. Importa su perfil y selecciona la migración.",
        ["The project path rules do not match this operating system. Review its case sensitivity in IDE Control."]="Las reglas de rutas no coinciden con este sistema. Revisa la distinción de mayúsculas del proyecto en IDE Control.",
        ["Connection unavailable. Check the server, project and key, then retry."]="Conexión no disponible. Revisa el servidor, el proyecto y la clave y vuelve a intentarlo.",
        ["The installation timed out. Review the project before retrying."]="La instalación agotó el tiempo. Revisa el proyecto antes de reintentar."
    };
    public string Error(string code)=>this[code switch
    {
        "invalid_profile"=>"Import the connection profile downloaded from IDE Control.","invalid_project"=>"Select an existing project folder.",
        "runtime_missing"=>"Use the complete portable package; its bundled runtime is missing.","linked_path" or "path_outside_project"=>"The folder or its managed files use a link. Choose a direct project folder.",
        "project_moved"=>"The saved identity does not match this folder. Reconnect explicitly; do not copy installation files between projects.",
        "account_mismatch"=>"This folder belongs to another connection. Disconnect it explicitly or choose another folder.",
        "invalid_key"=>"The key was rejected. Generate or copy the key for this project's agent in IDE Control.",
        "vault_locked" or "invalid_vault"=>"The vault is locked, its password is incorrect, or its contents changed.",
        "password_length"=>"Use a vault password between 12 and 1024 characters.","session_locked"=>"Unlock this project before checking or updating its connection.",
        "installation_busy"=>"Another setup is changing this folder. Wait for it to finish and review again.",
        "tracked_credentials"=>"Files in .deleguard are tracked by Git. Remove them from version control before storing a key.",
        "git_check_unavailable"=>"Git tracking could not be checked. Make Git available and retry.",
        "customized_files"=>"Managed files were edited. Review their changes before allowing replacement.",
        "migration_required"=>"The project uses an older installation. Import its profile and select migration.",
        "filesystem_mismatch"=>"The project path rules do not match this operating system. Review its case sensitivity in IDE Control.",
        "installation_timeout"=>"The installation timed out. Review the project before retrying.",
        _=>"Connection unavailable. Check the server, project and key, then retry."
    }];
}
