using System.Text;
using Deleguard.Api.Http;
using Deleguard.Application;
using Deleguard.Application.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Deleguard.IntegrationTests;

public sealed class AdminOperationBindingTests
{
    private static async Task<OperationRef> Bind(string json, string application = "app_one", string? key = "same-operation")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = "POST"; http.Request.Path = "/v1/policies/pol_1/publish";
        http.Request.ContentType = "application/json";
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        http.Items[ApplicationContextExtensions.ItemKey] = new ApplicationListItem(application, application, application, "product", DateTimeOffset.UtcNow, 0);
        if (key is not null) http.Request.Headers["Idempotency-Key"] = key;
        return await new AdminOperationContext(new HttpContextAccessor { HttpContext = http }).ReadAsync(default);
    }

    [Fact]
    public async Task Same_command_is_canonical_but_changed_version_or_application_cannot_reuse_approval()
    {
        var first = await Bind("{\"version_id\":\"one\",\"roles\":[\"operator\"]}");
        var repeated = await Bind("{\"roles\":[\"operator\"], \"version_id\": \"one\"}");
        Assert.Equal(first, repeated);
        Assert.NotEqual(first.Hash, (await Bind("{\"version_id\":\"two\",\"roles\":[\"operator\"]}")).Hash);
        Assert.NotEqual(first.Hash, (await Bind("{\"version_id\":\"one\",\"roles\":[\"operator\"]}", "app_two")).Hash);
        Assert.NotEqual(first.Hash, (await Bind("{\"version_id\":\"one\",\"roles\":[\"org-admin\"]}")).Hash);
    }

    [Fact]
    public async Task Missing_identity_is_rejected() =>
        await Assert.ThrowsAsync<RequestValidationException>(() => Bind("{}", key: null));
}
