using System.Text.Json;
using System.Text.Json.Serialization;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Application.Authorization;
using Deleguard.Application.Identity;
using Deleguard.Application.Policies;
using Deleguard.Domain.Identity;
using Deleguard.Domain.Policies;
using Deleguard.Infrastructure.Identity;
using Deleguard.Policy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace Deleguard.Api.Pages.Policies;

public sealed class DetailModel : PageModel
{
    public const string CreatedFlashKey = "policy_created_slug";

    public static readonly string DraftLowJson = """{"status":"draft","amount":150000,"reportedToTaxAuthority":false}""";
    public static readonly string HighValueJson = """{"status":"draft","amount":15000000,"reportedToTaxAuthority":false}""";
    public static readonly string TaxReportedJson = """{"status":"issued","amount":150000,"reportedToTaxAuthority":true}""";

    private static readonly JsonSerializerOptions EditorJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly string[] GenericFieldPaths =
    [
        "action",
        "agent.id",
        "actor.id",
        "actor.roles",
        "context.environment",
        "resource.type",
        "resource.id"
    ];

    private static readonly string[] InvoiceAttributePaths =
    [
        "resource.attributes.status",
        "resource.attributes.amount",
        "resource.attributes.reportedToTaxAuthority"
    ];

    private readonly PolicyAdminHandler _handler;
    private readonly ActionAdminHandler _actions;
    private readonly AgentAdminHandler _agents;
    private readonly IAdminActionGateway _gateway;

    public DetailModel(
        PolicyAdminHandler handler,
        ActionAdminHandler actions,
        AgentAdminHandler agents,
        IAdminActionGateway gateway)
    {
        _handler = handler;
        _actions = actions;
        _agents = agents;
        _gateway = gateway;
    }

    public PolicyDefinition? Policy { get; private set; }
    public IReadOnlyList<PolicyVersion> Versions { get; private set; } = [];
    public IReadOnlyList<string> ActionNames { get; private set; } = [];
    public IReadOnlyList<string> AgentIds { get; private set; } = [];
    public IReadOnlyList<AgentListItem> AssignableAgents { get; private set; } = [];
    public IReadOnlyList<string> EnvironmentNames { get; private set; } = [];
    public IReadOnlyList<string> FieldPaths { get; private set; } = GenericFieldPaths;
    public string? Message { get; private set; }
    public bool IsError { get; private set; }
    public bool CanWrite { get; private set; }
    public bool CanPublish { get; private set; }
    public bool CanDelete { get; private set; }
    public bool IsCatalog { get; private set; }
    public string? PendingApprovalId { get; private set; }
    public PolicyTestResult? TestResult { get; private set; }
    public string EditorStateJson { get; private set; } = "{}";
    public string DefaultEditorTab { get; private set; } = "visual";
    public string? EditorParseError { get; private set; }
    public bool VisualSupported { get; private set; } = true;
    public string? LoadedVersionId { get; private set; }
    public int? LoadedVersionNumber { get; private set; }
    public int? PublishedVersionNumber { get; private set; }
    public string? PublishedYaml { get; private set; }
    public IReadOnlyList<PolicySimulationCase> SavedCases { get; private set; } = [];
    public IReadOnlyList<PolicyCaseComparison>? CaseComparisons { get; private set; }

    [BindProperty]
    public string CaseName { get; set; } = string.Empty;

    [BindProperty]
    public string Yaml { get; set; } = string.Empty;

    [BindProperty]
    public string ExpectedEffect { get; set; } = string.Empty;

    [BindProperty]
    public string EditorTab { get; set; } = "visual";

    [BindProperty]
    public string FixtureJson { get; set; } = DraftLowJson;

    [BindProperty]
    public List<string> SelectedAgentIds { get; set; } = [];

    [BindProperty]
    public string AgentId { get; set; } = "invoice-agent";

    [BindProperty]
    public string EnvironmentName { get; set; } = "production";

    [BindProperty]
    public string ResourceId { get; set; } = "FAC-TEST";

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var id = context.RouteData.Values["id"]?.ToString();
        if (!string.IsNullOrEmpty(id))
        {
            try
            {
                await _handler.EnsureApplicationAccessAsync(HttpContext.RequireOrganizationId(), id,
                    HttpContext.RequireProductApplicationId(), HttpContext.RequestAborted);
            }
            catch (NotFoundException)
            {
                context.Result = NotFound();
                return;
            }
        }
        await next();
    }

    public async Task<IActionResult> OnGetAsync(string id, string? version, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        if (TempData[CreatedFlashKey] is string slug)
        {
            Message = $"Saved draft {slug}. Authorize still ignores it until you add rules, test, and publish.";
        }

        await LoadAsync(id, cancellationToken, versionId: version);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveVersionAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            var saved = await _handler.AddVersionAsync(new AddPolicyVersionCommand(HttpContext.RequireOrganizationId(), id, Yaml), cancellationToken);
            Message = $"Saved draft v{saved.Version}. Publish it before authorize will use it.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken, keepEditor: true);
        return Page();
    }

    public async Task<IActionResult> OnPostAgentsAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            await _handler.ReplaceAgentsAsync(HttpContext.RequireOrganizationId(), id, SelectedAgentIds, cancellationToken);
            Message = "Agents updated. Authorize DENYs callers that are not assigned to this policy.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken, keepEditor: true);
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(string id, string versionId, CancellationToken cancellationToken)
    {
        try
        {
            var caller = HttpContext.EnsurePermission(PermissionCatalog.PoliciesPublish);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.policy.publish",
                "policy",
                id,
                null,
                PermissionCatalog.PoliciesPublish,
                ct => _handler.PublishAsync(new PublishPolicyCommand(HttpContext.RequireOrganizationId(), id, versionId), ct),
                cancellationToken);
            if (!outcome.Allowed)
            {
                PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
                IsError = !outcome.Pending;
                Message = outcome.Pending
                    ? $"Waiting for approval {outcome.ApprovalId}."
                    : outcome.Reason;
            }
            else
            {
                Message = $"Published v{outcome.Value!.Version}.";
            }
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(string id, CancellationToken cancellationToken)
    {
        var caller = HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            await _handler.EnsureCanDeleteAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
            var outcome = await _gateway.RunAsync(
                caller,
                "admin.policy.delete",
                "policy",
                id,
                null,
                PermissionCatalog.PoliciesWrite,
                async ct =>
                {
                    await _handler.DeleteAsync(HttpContext.RequireOrganizationId(), id, ct);
                    return true;
                },
                cancellationToken);
            if (outcome.Allowed)
            {
                return RedirectToPage("/Policies/Index");
            }

            PendingApprovalId = outcome.Pending ? outcome.ApprovalId : null;
            IsError = !outcome.Pending;
            Message = outcome.Pending ? $"Waiting for approval {outcome.ApprovalId}." : outcome.Reason;
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or ForbiddenException)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostTestAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        try
        {
            var attributes = ParseAttributes(FixtureJson);
            TestResult = await _handler.TestAsync(new PolicyTestCommand(
                HttpContext.RequireOrganizationId(),
                id,
                Yaml,
                string.IsNullOrWhiteSpace(ExpectedEffect) ? null : ExpectedEffect,
                string.IsNullOrWhiteSpace(AgentId) ? "invoice-agent" : AgentId.Trim(),
                string.IsNullOrWhiteSpace(EnvironmentName) ? "production" : EnvironmentName.Trim(),
                null,
                null,
                string.IsNullOrWhiteSpace(ResourceId) ? "FAC-TEST" : ResourceId.Trim(),
                attributes,
                null), cancellationToken);
        }
        catch (Exception ex)
        {
            IsError = true;
            Message = ex.Message;
        }

        await LoadAsync(id, cancellationToken, keepEditor: true);
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        await LoadAsync(id, cancellationToken, keepEditor: true);
        if (Policy is null)
        {
            return EditorError("Policy not found.", StatusCodes.Status404NotFound);
        }

        if (string.IsNullOrWhiteSpace(Yaml))
        {
            return EditorError("YAML is required.");
        }

        try
        {
            var compiled = PolicyYamlParser.Parse(Policy.Slug, Yaml);
            var document = PolicyEditorMapper.FromCompiled(compiled, NextVersion(compiled.Version));
            return Content(JsonSerializer.Serialize(document, EditorJson), "application/json");
        }
        catch (Exception ex)
        {
            return EditorError($"Invalid policy YAML: {ex.Message}");
        }
    }

    public async Task<IActionResult> OnPostSaveCaseAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        try
        {
            await _handler.SaveCaseAsync(CaseName, new PolicyTestCommand(HttpContext.RequireOrganizationId(), id, Yaml,
                ExpectedEffect, AgentId, EnvironmentName, null, null, ResourceId, ParseAttributes(FixtureJson), null), cancellationToken);
            Message = "Saved regression case. It will run against the exact version on every publication.";
        }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException or JsonException)
        { IsError = true; Message = ex.Message; }
        await LoadAsync(id, cancellationToken, keepEditor: true);
        return Page();
    }

    public async Task<IActionResult> OnPostCompareAsync(string id, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesRead);
        try { CaseComparisons = await _handler.CompareCasesAsync(HttpContext.RequireOrganizationId(), id, Yaml, cancellationToken); }
        catch (Exception ex) when (ex is RequestValidationException or ConflictException or NotFoundException)
        { IsError = true; Message = ex.Message; }
        await LoadAsync(id, cancellationToken, keepEditor: true);
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteCaseAsync(string id, string caseId, CancellationToken cancellationToken)
    {
        HttpContext.EnsurePermission(PermissionCatalog.PoliciesWrite);
        await _handler.DeleteCaseAsync(HttpContext.RequireOrganizationId(), id, caseId, cancellationToken);
        return RedirectToPage(new { id });
    }

    private async Task LoadAsync(string id, CancellationToken cancellationToken, bool keepEditor = false, string? versionId = null)
    {
        CanWrite = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesWrite) == true;
        CanPublish = HttpContext.GetConsoleUser()?.HasPermission(PermissionCatalog.PoliciesPublish) == true;
        try
        {
            var (policy, versions) = await _handler.GetAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
            Policy = policy;
            Versions = versions;
            SavedCases = await _handler.ListCasesAsync(HttpContext.RequireOrganizationId(), id, cancellationToken);
            PublishedYaml = versions.FirstOrDefault(item => item.Id == policy.PublishedVersionId)?.Yaml;
            var current = versions.FirstOrDefault(item => item.Id == policy.PublishedVersionId)
                          ?? versions.OrderByDescending(item => item.Version).FirstOrDefault();
            IsCatalog = PolicyAdminHandler.IsAdministrationCatalog(policy.Slug, current?.Action);
            CanDelete = CanWrite && !IsCatalog;
            var granted = await _handler.ListGrantsAsync(HttpContext.RequireOrganizationId(), [policy.Id], cancellationToken);
            AssignableAgents = (await _agents.ListAsync(HttpContext.RequireOrganizationId(), policy.ApplicationId, cancellationToken))
                .Where(agent => IsCatalog
                    || !string.Equals(agent.ExternalId, AccessControlIds.AdminMcpExternalId, StringComparison.Ordinal))
                .ToList();
            if (SelectedAgentIds.Count == 0)
            {
                SelectedAgentIds = granted.GetValueOrDefault(policy.Id, []).ToList();
            }

            ActionNames = (await _actions.ListAsync(HttpContext.RequireOrganizationId(), policy.ApplicationId, cancellationToken))
                .Select(action => action.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            AgentIds = (await _agents.ListAsync(HttpContext.RequireOrganizationId(), policy.ApplicationId, cancellationToken))
                .Select(agent => agent.ExternalId)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            EnvironmentNames = (await _agents.ListEnvironmentsAsync(HttpContext.RequireOrganizationId(), policy.ApplicationId, cancellationToken))
                .Select(environment => environment.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            PublishedVersionNumber = versions.FirstOrDefault(item => item.Published)?.Version;

            if (!keepEditor)
            {
                var ordered = versions.OrderByDescending(item => item.Version).ToList();
                var selected = !string.IsNullOrWhiteSpace(versionId)
                    ? versions.FirstOrDefault(item => string.Equals(item.Id, versionId, StringComparison.Ordinal))
                    : null;
                selected ??= ordered.FirstOrDefault();
                Yaml = selected?.Yaml ?? Yaml;
                LoadedVersionId = selected?.Id;
                LoadedVersionNumber = selected?.Version;
            }

            FieldPaths = BuildFieldPaths(Yaml, FixtureJson);
            HydrateEditor();
        }
        catch (NotFoundException)
        {
            Policy = null;
        }
    }

    private void HydrateEditor()
    {
        if (Policy is null)
        {
            EditorStateJson = "{}";
            return;
        }

        var next = Versions.Count == 0 ? 1 : Versions.Max(version => version.Version) + 1;
        if (string.IsNullOrWhiteSpace(Yaml))
        {
            VisualSupported = false;
            DefaultEditorTab = "yaml";
            EditorParseError = "YAML is empty.";
            EditorStateJson = SerializeEditor(new
            {
                error = EditorParseError,
                visualSupported = false,
                id = Policy.Slug,
                nextVersion = next
            });
            return;
        }

        try
        {
            var compiled = PolicyYamlParser.Parse(Policy.Slug, Yaml);
            var document = PolicyEditorMapper.FromCompiled(compiled, NextVersion(compiled.Version));
            VisualSupported = document.VisualSupported;
            EditorParseError = document.VisualUnsupportedReason;
            DefaultEditorTab = PreferredTab(document.VisualSupported);
            EditorStateJson = SerializeEditor(document);
        }
        catch (Exception ex)
        {
            VisualSupported = false;
            DefaultEditorTab = "yaml";
            EditorParseError = ex.Message;
            EditorStateJson = SerializeEditor(new
            {
                error = ex.Message,
                visualSupported = false,
                id = Policy.Slug,
                nextVersion = next
            });
        }
    }

    private string PreferredTab(bool visualSupported)
    {
        if (!visualSupported)
        {
            return "yaml";
        }

        return string.Equals(EditorTab, "yaml", StringComparison.OrdinalIgnoreCase) ? "yaml" : "visual";
    }

    private int NextVersion(int compiledVersion)
    {
        var next = Versions.Count == 0 ? 1 : Versions.Max(version => version.Version) + 1;
        return Math.Max(next, compiledVersion + 1);
    }

    private IActionResult EditorError(string message, int statusCode = StatusCodes.Status400BadRequest) =>
        new ContentResult
        {
            StatusCode = statusCode,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(new { error = message }, EditorJson)
        };

    private static string SerializeEditor(object document) =>
        JsonSerializer.Serialize(document, EditorJson).Replace("</", "<\\/", StringComparison.Ordinal);

    internal static IReadOnlyList<string> BuildFieldPaths(string yaml, string fixtureJson)
    {
        var paths = new SortedSet<string>(GenericFieldPaths, StringComparer.Ordinal);
        var action = TryReadMatchAction(yaml);
        if (string.IsNullOrWhiteSpace(action) || action.StartsWith("invoice.", StringComparison.Ordinal))
        {
            foreach (var path in InvoiceAttributePaths)
            {
                paths.Add(path);
            }
        }

        foreach (var key in ParseAttributeKeys(fixtureJson))
        {
            paths.Add("resource.attributes." + key);
        }

        return paths.ToList();
    }

    private static string? TryReadMatchAction(string yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml))
        {
            return null;
        }

        try
        {
            return PolicyYamlParser.Parse("field-catalog", yaml).Action;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> ParseAttributeKeys(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            yield break;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                yield break;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                yield return property.Name;
            }
        }
    }

    private static Dictionary<string, JsonElement> ParseAttributes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, JsonElement>();
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new RequestValidationException("Fixture JSON must be an object.");
        }

        var attributes = new Dictionary<string, JsonElement>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            attributes[property.Name] = property.Value.Clone();
        }

        return attributes;
    }
}
