using Deleguard.Application.Abstractions;

namespace Deleguard.Api.Pages;

public sealed record ConsoleModule(string Key, string Title, string Url, string Permission, string Group,
    string Symbol, string Purpose, string[] Steps, string Outcome);

/// <summary>Shared, permission-aware navigation and help; never used to authorize a request.</summary>
public static class ConsoleGuide
{
    public static IReadOnlyList<ConsoleModule> Modules { get; } = [
        new("ide", "IDE Control", "/ide", "agents.read", "Conectar", "⌘", "Controla las operaciones de tu asistente en Cursor, VS Code o Claude Code.",
            ["Prepara un proyecto: el asistente crea su agente y aplica el perfil inicial.", "Download your connection profile and open the portable installer for this folder.", "Prueba una operación con aprobación desde tu editor."], "Una decisión y un resultado visibles en Actividad. La cobertura depende de los eventos del IDE."),
        new("agents", "Agentes", "/agents", "agents.read", "Conectar", "◈", "Identifica qué asistente o aplicación solicita cada operación.",
            ["Conecta un agente y elige su entorno.", "Guarda su clave y asigna las acciones y políticas que necesita.", "Comprueba una petición desde tu integración."], "Cada operación queda asociada a un agente. Para un editor, usa el inicio rápido de IDE Control."),
        new("actions", "Acciones", "/actions", "actions.read", "Proteger", "↗", "Define qué operaciones quieres controlar, como enviar un correo o modificar un archivo.",
            ["Registra una acción con un nombre reconocible y el recurso que afecta.", "Selecciona los agentes que pueden solicitarla.", "Crea y publica una política para decidir cuándo permitirla."], "Registrar una acción no concede permiso: necesita una política publicada y asignada."),
        new("policies", "Políticas", "/policies", "policies.read", "Proteger", "◇", "Decide qué se permite, qué se bloquea y qué requiere aprobación humana.",
            ["Elige una acción y sus agentes; crea un borrador.", "Configura reglas en el editor y prueba casos permitidos y bloqueados.", "Publica la versión que superó las pruebas."], "Solo la versión publicada toma decisiones. Puedes editar borradores sin activar sus cambios."),
        new("playground", "Probar una acción", "/playground", "agents.write", "Proteger", "▷", "Comprueba cómo responde tu configuración sin escribir una integración.",
            ["Elige agente y acción.", "Completa los datos del recurso y envía la solicitud.", "Lee la decisión; si requiere aprobación, sigue su enlace."], "Se crea una decisión real, pero Playground no ejecuta la operación en tu sistema externo."),
        new("approvals", "Aprobaciones", "/approvals", "approvals.read", "Supervisar", "✓", "Revisa las operaciones que necesitan una decisión humana antes de continuar.",
            ["Abre una solicitud pendiente.", "Comprueba agente, recurso, alcance y caducidad.", "Aprueba o rechaza y añade el contexto necesario."], "Aprobar concede un permiso limitado. La integración debe retomar la operación; la consola no la ejecuta."),
        new("audit", "Actividad", "/audit/activity", "audit.read", "Supervisar", "≋", "Sigue cada solicitud desde la decisión hasta el resultado reportado.",
            ["Filtra las decisiones o abre la actividad de un agente.", "Abre un registro para ver regla aplicada, aprobación y evidencia.", "Revisa los fallos o resultados pendientes con tu integración."], "Una decisión ALLOW no confirma una ejecución. La evidencia es un resultado reportado por el cliente."),
        new("ai-gateway", "AI Gateway", "/access/ai-gateway", "gateway.read", "Integraciones", "⇄", "Añade, si lo necesitas, control de llamadas a modelos y presupuesto.",
            ["Configura un proveedor y sus alias de modelo.", "Define el presupuesto y habilita el gateway.", "Conecta tu SDK al gateway local y comprueba una llamada."], "Es opcional para IDE Control y la API de autorización. Las claves del proveedor permanecen en el gateway local."),
        new("webhooks", "Webhooks", "/webhooks", "webhooks.read", "Integraciones", "↪", "Recibe eventos de DeleGuard en tus servicios para continuar tus flujos.",
            ["Registra la URL HTTPS que recibirá los eventos.", "Guarda el secreto de firma y selecciona los eventos.", "Envía una prueba y revisa el estado de entrega."], "Tu servicio verifica la firma y procesa los eventos. Una entrega fallida queda disponible para diagnóstico."),
        new("mcp", "Admin MCP", "/access/mcp", "mcp.read", "Integraciones", "⚙", "Permite que un cliente MCP administre DeleGuard con permisos explícitos.",
            ["Habilita únicamente las herramientas administrativas necesarias.", "Emite una credencial para el usuario que administrará la cuenta.", "Copia la conexión a tu cliente MCP."], "Es opcional. Admin MCP administra la plataforma; IDE Control protege las operaciones del proyecto."),
        new("settings", "Organización", "/settings", "applications.read", "Administrar", "▦", "Organiza el trabajo en aplicaciones independientes dentro de tu organización.",
            ["Empieza con la aplicación que ya está seleccionada.", "Crea otra aplicación cuando necesites separar un producto.", "Cambia de aplicación con el selector de la barra lateral."], "Agentes, acciones y políticas se muestran para la aplicación seleccionada."),
        new("environments", "Entornos", "/environments", "environments.read", "Administrar", "◫", "Separa las identidades de desarrollo, pruebas y producción.",
            ["Utiliza el entorno inicial o crea uno de desarrollo.", "Elige el entorno al registrar un agente.", "Mantén cada clave en su integración correspondiente."], "No hace falta crear más entornos para empezar. Cada agente utiliza el entorno con el que se registró."),
        new("users", "Usuarios", "/users", "users.read", "Administrar", "♙", "Invita a las personas que configurarán, revisarán o auditarán las operaciones.",
            ["Añade el usuario con su correo.", "Asigna el rol adecuado a su responsabilidad.", "Comprueba el acceso y deshabilítalo cuando deje de necesitarlo."], "Las personas usan roles de consola; los agentes se conectan con sus propias claves."),
        new("roles", "Roles y permisos", "/roles", "roles.read", "Administrar", "⊙", "Define qué puede hacer cada persona dentro de la consola.",
            ["Revisa primero los roles disponibles.", "Asigna solo los permisos necesarios para cada responsabilidad.", "Vuelve a Usuarios para asignar el rol."], "Los permisos de consola y las políticas para agentes son controles distintos.")
    ];

    public static IEnumerable<ConsoleModule> For(ConsoleUserPrincipal? user) =>
        Modules.Where(m => user?.HasPermission(m.Permission) == true);

    public static ConsoleModule? Find(string? key) => Modules.FirstOrDefault(m => m.Key == key);
}
