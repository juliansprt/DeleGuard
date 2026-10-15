namespace Deleguard.Infrastructure.Persistence;

public static class AdminPolicyFiles
{
    public static IReadOnlyDictionary<string, string> All()
    {
        return Deleguard.Application.Identity.AdminMcpCatalog.Tools.ToDictionary(
            spec => spec.Action.Replace('.', '-').Replace('_', '-') + ".yaml",
            AdminPolicyYaml.For,
            StringComparer.Ordinal);
    }
}
