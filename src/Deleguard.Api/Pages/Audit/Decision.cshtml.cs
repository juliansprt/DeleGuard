using System.Text.Json;
using Deleguard.Api.Http;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace Deleguard.Api.Pages.Audit;
public sealed class DecisionModel(IControlQueries queries) : PageModel
{
    public DecisionCase Case { get; private set; } = null!;
    public async Task<IActionResult> OnGetAsync(string id, CancellationToken ct)
    {
        HttpContext.EnsurePermission(PermissionCatalog.AuditRead);
        var item = await queries.CaseAsync(HttpContext.RequireOrganizationId(), HttpContext.RequireProductApplicationId(), id, ct);
        if (item is null) return NotFound();
        Case = item; return Page();
    }
    public async Task<IActionResult> OnGetExportAsync(string id, CancellationToken ct)
    {
        var result = await OnGetAsync(id, ct);
        if (result is not PageResult) return result;
        Response.Headers.CacheControl = "no-store";
        return File(JsonSerializer.SerializeToUtf8Bytes(new { schema_version = 1, evidence_kind = "client_reported", record = Case },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }), "application/json", $"deleguard-{Case.Decision.Id}.json");
    }
}
