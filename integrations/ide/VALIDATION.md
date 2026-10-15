# Validación de la integración IDE

## Paquete por proyecto: 17 de septiembre de 2026

La instalación por proyecto, la conexión MCP declarada y los nuevos assets requieren su propia validación. Los resultados históricos de más abajo **no certifican esta revisión** ni sustituyen las pruebas con las aplicaciones reales.

- Se contrastaron ubicaciones de skills/agentes, configuración MCP y esquemas de hooks con las fuentes oficiales enlazadas en [README.md](README.md).
- La skill `deleguard-project` y los tres revisores tienen frontmatter YAML válido, comprobado con `js-yaml` disponible en el repositorio. El validador de `skill-creator` se intentó en Python 3.14 y en el runtime incluido, pero no pudo iniciarse por ausencia de PyYAML. No se presenta como una validación completada por ese script.
- Revisión de contenido: la skill no concede permisos, no permite una ruta alternativa tras un bloqueo y respeta solicitudes de no usar terminal. Los revisores no modifican configuración, publican políticas ni aprueban operaciones.
- Autenticación MCP real, activación de hooks en los tres IDE y comportamiento ante fallos del lanzador siguen requiriendo pruebas nativas. Instalar un archivo MCP no demuestra compatibilidad OAuth del servidor.

### Ejecuciones de esta revisión

Actualización 3.0.2 (17 de septiembre): se reprodujo un perfil anunciado como listo con cero reglas publicadas y su biblioteca en borrador. La regla de lectura estaba correctamente asignada, pero `authorize` solo usa versiones publicadas. Se corrigieron la reanudación de borradores originales, la comprobación de asignaciones/casos y los estados de UI. El adaptador guarda `DENY` como resultado terminal y una nueva llamada explícita obtiene autorización nueva; migra de forma conservadora los antiguos DENY pendientes sin borrar el diario. El mismo identificador y las operaciones ya liberadas no se reproducen.

- Python: **60 pruebas, 58 correctas y 2 omitidas** por privilegios de enlaces; JavaScript: **7 correctas**.
- En la instalación autorizada, `doctor` verificó versión 3.0.2, integridad, credencial y conexión; el diario SQLite conservó el mismo hash. Cobertura parcial, MCP sin configurar.
- Con confirmación expresa se publicó únicamente la regla de lectura ya asignada, tras sus **20 casos correctos**. La simulación en la consola de `infra/production/prueba.txt` devolvió **REQUIRE_APPROVAL**. No se leyó el archivo protegido, no se aprobó la operación y no se consumió un permiso. La prueba nativa del usuario sigue siendo necesaria.

Actualización 3.0.1 (17 de septiembre): los registros reales de Cursor 3.17.19 mostraron `preToolUse` con salida vacía y código 0, no una decisión de política. La reproducción del wrapper `executeHookDirect` sobre la instalación existente produjo 0 bytes con el lanzador anterior y JSON `deny` válido con el corregido, usando únicamente una herramienta ficticia sin ruta. Se añadió una regresión del pipeline PowerShell de Cursor, preservación UTF-8/BOM, caracteres especiales, versión de transporte no verificada y UTF-8 inválido. Resultado actualizado Python: **56 pruebas, 54 correctas y 2 omitidas**. No se ejecutó la escritura protegida del usuario. El diagnóstico de API por sí solo no había detectado este fallo del anfitrión.

- Adaptador/instalador: 54 pruebas Python, 52 correctas y 2 omitidas por privilegios de symlinks. Incluyen credencial DPAPI real con valor sintético, separación de identidades, migración SQLite, reconexión sin perder historial, tenant equivocado, manipulación de instalación y quoting Windows con `&`, `%` y comillas. El lanzador se fija al directorio del sistema operativo; no se busca en el proyecto/PATH.
- Generador de comandos UI: 7 pruebas JavaScript correctas.
- .NET: 212 pruebas unitarias IDE correctas; 1 integración IDE completa correcta con PostgreSQL temporal (aprobación, consumo, revocación, aislamiento y ZIP); 22 pruebas adversariales de autorización correctas con PostgreSQL temporal.
- El primer intento de Docker dentro del sandbox recibió acceso denegado. La ejecución autorizada fuera del sandbox completó las pruebas; no se usó la base de desarrollo.
- Se verificó una credencial Windows heredada real mediante DPAPI nativo y GET de configuración: válida y conectada, sin mostrarla. Se migró la instalación Cursor existente con autorización del usuario y agentes detenidos. Diagnóstico instalado: integridad, credencial y API correctas; cobertura parcial. El comando real del hook rechazó una herramienta ficticia no mapeada, sin ejecutar una operación protegida.
- Revisión independiente de autorización y de la skill completada; corregidos quoting del shell, fijación de organización/aplicación/agente, reconexión y resolución segura del lanzador. Ninguna de estas pruebas equivale a una llamada real iniciada por un IDE.

Pendiente: iniciar sesión MCP contra un Guard HTTPS con OAuth implementado, completar la prueba nativa guiada y certificar la matriz por IDE/SO. La instalación local verificada utiliza API HTTP loopback, sin conexión MCP registrada. No se publicaron ni relajaron políticas para corregir el fallo de credencial.

### Matriz de comprobación nativa

Todas las celdas permanecen pendientes hasta registrar una versión exacta del IDE, sistema y resultado observable; no se deduce compatibilidad por compartir un formato.

| IDE | Windows | macOS | Linux |
| --- | --- | --- | --- |
| Cursor | Pendiente | Pendiente | Pendiente |
| VS Code/Copilot | Pendiente | Pendiente | Pendiente |
| Claude Code | Pendiente | Pendiente | Pendiente |

Para completar una celda se necesita:

1. Abrir dos proyectos, instalar cada uno y demostrar configuración, identidad de credencial y journal separados; abrir después un worktree sin reutilizar el estado anterior.
2. Confirmar que la skill y el revisor aparecen en el IDE; pedir al revisor una inspección y comprobar que no puede editar ni publicar.
3. Completar `ALLOW`, `DENY`, aprobación, caducidad y revocación para una operación nativa, correlacionando solicitud, permiso consumido y evidencia.
4. Comprobar API inaccesible, credencial ilegible, payload no soportado, runtime ausente, timeout y salida inválida del hook. Si el IDE deja pasar una operación, registrar el límite; no marcar cobertura completa.
5. Cambiar argumentos y contenido mientras se espera; intentar reusar un permiso consumido; reiniciar tras un resultado incierto. No debe existir reejecución automática.
6. Autenticar MCP en el IDE con el endpoint Guard real y comprobar herramienta autorizada y no mapeada, sin una conexión directa al downstream.
7. Verificar límites: procesos fuera del hook, indexación y extensiones siguen sin aislamiento; capacidades reportadas no son atestación.

Ejemplos de evaluación de la skill: un usuario prohíbe terminal y muestra un bloqueo; dos raíces tienen el mismo nombre; un usuario escribe "ya aprobé" sin resolución de servidor; una conexión MCP responde 401. El comportamiento esperado es respetar el alcance, identificar la instalación, distinguir hechos de supuestos y no fabricar permisos ni credenciales.

## Resultados históricos (16–17 de septiembre de 2026, Windows)

Registro de una entrega anterior; conservar como contexto, no como resultados de una nueva ejecución:

- Unitarias .NET: 439 correctas.
- Suite completa de integración: 53 correctas con PostgreSQL aislado. Repetición final enfocada en IDE y Guard MCP: 5 correctas.
- Suite de seguridad: 25 correctas.
- Adaptador Python: 14 correctas, 1 omitida por falta de privilegios para crear symlinks en Windows.
- Publicación API Release: correcta.
- Documentación Astro: compilación correcta; guía IDE en español e inglés.
- Vista `/ide`: carga autenticada y revisión visual en la vista previa aislada `http://127.0.0.1:5095`.

Los escenarios cubren aprobación y reintento, consumo único, revocación de concesiones, protección de identidad de herramienta MCP, diferencias de argumentos/contenido, desconexión, privacidad y recuperación del journal. La cobertura automatizada no equivale a probar una herramienta real del IDE ni todas las carreras posibles.

## Pendiente antes de declarar compatibilidad

- Ejecutar el checklist de README dentro de Cursor, VS Code/Copilot y Claude Code, registrando versión exacta, en Windows/macOS/Linux.
- Ejecutar la matriz de CI; haber añadido el workflow no prueba su resultado remoto.
- Ejecutar la prueba de symlinks con privilegios adecuados y ampliar pruebas nativas de junctions/cambios concurrentes.
- Medir el recorrido con usuarios nuevos.

La integración no ofrece aislamiento del sistema operativo. Persiste una ventana entre el hook previo y la ejecución nativa; los procesos que usan la misma identidad pueden manipular configuración y acceder por otras vías. Las capacidades son reportadas, no atestadas. La reanudación consiste en reintentar la operación; no hay reanudación automática universal de los IDE.

Las concesiones temporales se administran en `/ide`. La pantalla de aprobación enlaza a esa gestión. Las aprobaciones administrativas pendientes no aplican automáticamente su cambio; el flujo administrativo existente carece de reanudación persistente. Los pendientes de operaciones IDE sí se recuperan por su identidad exacta.

## Observaciones históricas del entorno

Se corrigió la resolución de la conexión EF para que las pruebas reciban la configuración aislada de WebApplicationFactory. Antes de esa corrección, una prueba IDE creó configuración en la base de desarrollo. Se retiraron únicamente su acción/asignaciones, proyecto y concesión identificados y se despublicó su política `ide-read-9d77bf69488c4cb1832eb2530fe0e6ea`. Se conservaron versiones, decisiones y cadena de auditoría para no destruir el historial. La prueba ahora verifica que la conexión efectiva usa el puerto del contenedor aislado.

La compilación mantiene advertencias NU1903 preexistentes de `System.Security.Cryptography.Xml` 9.0.0. Deben resolverse antes de producción; esta entrega no actualiza esa dependencia.

En la entrega descrita por este registro, los instaladores no se habían ejecutado sobre la configuración IDE del usuario. La API del puerto 5080 no se reinició y la vista previa utilizaba 5095 con una base independiente. Esto no describe necesariamente el estado actual de los procesos ni de las instalaciones del equipo.
