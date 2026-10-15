using System.Text.Json;
using System.Text.Json.Nodes;
using Deleguard.Api.Mcp;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Deleguard.Domain.Common;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Deleguard.Api.Http;

public sealed class AdminOperationContext(IHttpContextAccessor accessor) : IAdminOperationContext
{
    public const string Field = "dg_operation_id";
    public async Task<OperationRef> ReadAsync(CancellationToken ct)
    {
        var request = accessor.HttpContext?.Request ?? throw new RequestValidationException("Administrative transport context required.");
        string? id = request.Headers["Idempotency-Key"].FirstOrDefault();
        object payload;
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            id ??= form[Field].FirstOrDefault();
            if (form.Files.Count != 0) throw new RequestValidationException("File mutations require an explicit content-bound command.");
            payload = form.Where(x => x.Key is not (Field or "__RequestVerificationToken"))
                .ToDictionary(x => x.Key, x => x.Value.ToArray());
        }
        else
        {
            request.Body.Position = 0;
            using var reader = new StreamReader(request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync(ct);
            request.Body.Position = 0;
            payload = string.IsNullOrWhiteSpace(body) ? JsonSerializer.SerializeToElement(new { }) : JsonSerializer.Deserialize<JsonElement>(body);
            if (request.Path.StartsWithSegments("/mcp") && !string.IsNullOrWhiteSpace(body))
            {
                var command = JsonNode.Parse(body)!["params"]!.DeepClone().AsObject();
                id ??= command["_meta"]?["deleguard_operation_id"]?.GetValue<string>() ?? command["arguments"]?[OperationSchema.Field]?.GetValue<string>();
                if (id is null && Deleguard.Application.Identity.AdminMcpCatalog.Find(command["name"]?.GetValue<string>() ?? "")?.ReadOnlyHint == true)
                    id = Guid.NewGuid().ToString("N");
                command.Remove("_meta");
                (command["arguments"] as JsonObject)?.Remove(OperationSchema.Field);
                payload = JsonSerializer.SerializeToElement(command);
            }
        }
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(char.IsControl))
            throw new RequestValidationException("A stable Idempotency-Key (or form operation identity) is required. Reuse it only when retrying this operation.");
        var hash = CanonicalJson.HashHex(JsonSerializer.SerializeToNode(new { request.Method, path = request.Path.Value,
            application = (accessor.HttpContext!.Items.TryGetValue("deleguard.admin.target_application", out var target) ? target : accessor.HttpContext.GetProductApplication()?.Id),
            query = request.Query.ToDictionary(x => x.Key, x => x.Value.ToArray()), payload }));
        return new(id, hash);
    }
}

[HtmlTargetElement("form", Attributes = "method")]
public sealed class AdminOperationTagHelper(IHttpContextAccessor accessor) : TagHelper
{
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!string.Equals(output.Attributes["method"]?.Value?.ToString(), "post", StringComparison.OrdinalIgnoreCase)) return;
        var request = accessor.HttpContext!.Request;
        var id = request.HasFormContentType ? request.Form[AdminOperationContext.Field].FirstOrDefault() : null;
        var input = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("input");
        input.Attributes["type"] = "hidden";
        input.Attributes["name"] = AdminOperationContext.Field;
        input.Attributes["value"] = id ?? Guid.NewGuid().ToString("N");
        output.PostContent.AppendHtml(input);
    }
}
