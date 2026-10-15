using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Deleguard.Api.Localization;
using Deleguard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Deleguard.IntegrationTests;

public sealed class LocalizationCatalogTests
{
    [Fact]
    public void Catalogs_have_matching_keys_placeholders_and_client_messages()
    {
        var en = Catalog("en"); var es = Catalog("es");
        Assert.Equal(en.Keys.Order(), es.Keys.Order());
        foreach (var (key, value) in en)
        {
            Assert.False(string.IsNullOrWhiteSpace(value), key);
            Assert.False(string.IsNullOrWhiteSpace(es[key]), key);
            Assert.Equal(Placeholders(value), Placeholders(es[key]));
        }
        Assert.True(en.Count > 1000);
    }

    [Theory]
    [InlineData("en", "22/22 rules ready", "Waiting for approval abc-123.")]
    [InlineData("es", "22/22 reglas listas", "Esperando la aprobación abc-123.")]
    public void Formatting_translates_messages_and_preserves_identifiers(string language, string count, string approval)
    {
        var prior = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var text = new UiText();
            Assert.Equal(count, text.Format("rules.ready", 22, 22));
            Assert.Equal(approval, text.Message("Waiting for approval abc-123."));
            Assert.Equal("customer-defined <label>", text.Message("customer-defined <label>"));
            Assert.Equal("filesystem.read", text["filesystem.read"]);
            Assert.All(text.ClientMessages.Keys, key => Assert.StartsWith("js.", key));
        }
        finally { CultureInfo.CurrentUICulture = prior; }
    }

    private static string[] Placeholders(string value) => Regex.Matches(value, @"\{[0-9]+\}").Select(m => m.Value).Order().ToArray();
    private static Dictionary<string, string> Catalog(string language)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Deleguard.Api.Localization.UiStrings.{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream!)!;
    }
}

public sealed class LocalizationTests(AdminAccessPostgresFixture postgres) : IClassFixture<AdminAccessPostgresFixture>
{
    private WebApplicationFactory<Program> Factory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseSetting("ConnectionStrings:Postgres", postgres.ConnectionString);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = postgres.ConnectionString,
            ["Oidc:Enabled"] = "false", ["DemoAuth:AllowEmergencyConsoleToken"] = "true",
            ["DemoAuth:ConsoleToken"] = DemoSeeder.DemoConsoleToken,
            ["DemoAuth:DemoOrganizationId"] = DemoSeeder.OrganizationId,
            ["DemoAuth:ApiKeyPepper"] = "deleguard-dev-pepper",
            ["Permits:SigningKey"] = "deleguard-dev-permit-hmac-key-change-in-prod-32"
        }));
    });

    [Fact]
    public async Task Default_is_English_even_when_browser_and_query_request_Spanish()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es-CO,es;q=0.9");
        var html = await client.GetStringAsync("/login?culture=es&ui-culture=es");
        Assert.Contains("<html lang=\"en\">", html);
        Assert.Contains("name=\"culture\"", html);
        client.DefaultRequestHeaders.Add("Cookie", UiText.CookieName + "=unsupported");
        Assert.Contains("<html lang=\"en\">", await client.GetStringAsync("/login"));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    public async Task Every_console_module_renders_in_the_selected_language(string language)
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        client.DefaultRequestHeaders.Add("Cookie", UiText.CookieName + "=" + language);
        foreach (var path in new[] { "/", "/start", "/ide", "/agents", "/agents/connect", "/actions", "/actions/coverage", "/policies", "/policies/pol_invoice_cancel", "/playground", "/approvals", "/audit", "/audit/activity", "/access/ai-gateway", "/webhooks", "/access/mcp", "/settings", "/environments", "/users", "/roles" })
        {
            using var response = await client.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, path + ": " + response.StatusCode);
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Contains("<html lang=\"" + language + "\">", html);
            Assert.Contains("id=\"ui-messages\"", html);
            Assert.Contains(language == "es" ? "Idioma" : "Language", html);
            Assert.DoesNotContain("@T[", html);
            if (path == "/audit/activity") Assert.Contains(language == "es" ? "Fallos de hoy" : "Today's failures", html);
            if (path == "/policies/pol_invoice_cancel") Assert.Contains(language == "es" ? "Solo evaluar" : "Evaluate only", html);
        }
    }

    [Fact]
    public async Task Translated_form_labels_keep_the_same_submitted_option_values()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoSeeder.DemoConsoleToken);
        foreach (var path in new[] { "/actions", "/policies/pol_invoice_cancel", "/playground", "/ide" })
        {
            var english = await client.GetStringAsync(path);
            client.DefaultRequestHeaders.Add("Cookie", UiText.CookieName + "=es");
            var spanish = await client.GetStringAsync(path);
            client.DefaultRequestHeaders.Remove("Cookie");
            static string[] Values(string html) => Regex.Matches(html, "<option[^>]*value=\"([^\"]*)\"").Select(m => m.Groups[1].Value).Where(v => v is not "en" and not "es").Order().ToArray();
            Assert.Equal(Values(english), Values(spanish));
        }
    }

    [Theory]
    [InlineData("/start#how-it-works", "/start#how-it-works")]
    [InlineData("https://outside.example/", "/")]
    [InlineData("//outside.example/", "/")]
    public async Task Language_selection_persists_and_only_redirects_locally(string returnUrl, string expected)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync("/login");
        using var response = await client.PostAsync("/login/language", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["culture"] = "es", ["returnUrl"] = returnUrl, ["__RequestVerificationToken"] = Token(html)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(UiText.CookieName + "=es") && cookie.Contains("httponly") && cookie.Contains("samesite=lax"));
        Assert.Contains("<html lang=\"es\">", await client.GetStringAsync("/login"));
    }

    [Fact]
    public async Task Unsupported_language_and_missing_antiforgery_are_rejected()
    {
        await using var factory = Factory(); using var client = factory.CreateDefaultClient(new Microsoft.AspNetCore.Mvc.Testing.Handlers.CookieContainerHandler(), new OperationIdentityHandler());
        var html = await client.GetStringAsync("/login");
        foreach (var form in new[] {
            new Dictionary<string, string> { ["culture"] = "fr", ["__RequestVerificationToken"] = Token(html) },
            new Dictionary<string, string> { ["culture"] = "es" }
        })
        {
            using var response = await client.PostAsync("/login/language", new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.False(response.Headers.TryGetValues("Set-Cookie", out var values) && values.Any(v => v.StartsWith(UiText.CookieName + "=")));
        }
    }

    private static string Token(string html) => WebUtility.HtmlDecode(Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
}
