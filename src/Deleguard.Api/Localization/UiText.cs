using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Localization;

namespace Deleguard.Api.Localization;

/// <summary>Presentation-only strings. Protocol values and user-authored content are never translated.</summary>
public sealed class UiText
{
    public const string CookieName = "dg_ui_language";
    public static readonly string[] SupportedLanguages = ["en", "es"];
    private static readonly IReadOnlyDictionary<string, string> English = Load("en");
    private static readonly IReadOnlyDictionary<string, string> Spanish = Load("es");
    public string Language => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    public string this[string? key] => Translate(key, Language);
    public string Format(string key, params object?[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);
    private static readonly (string Key, Regex Pattern)[] MessageTemplates = English.Keys
        .Where(key => !key.StartsWith("js.", StringComparison.Ordinal) && Regex.IsMatch(key, @"\{[0-9]+\}"))
        .Select(key => (key, new Regex("^" + Regex.Replace(Regex.Escape(key), @"\\\{([0-9]+)}",
            match => "(?<arg" + match.Groups[1].Value + ">.*?)") + "$",
            RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(50)))).ToArray();
    public string Message(string? message)
    {
        if (string.IsNullOrEmpty(message)) return "";
        if (English.ContainsKey(message)) return this[message];
        if (message.Length > 8192) return message;
        foreach (var (key, pattern) in MessageTemplates)
        {
            var match = pattern.Match(message);
            if (!match.Success) continue;
            var values = Regex.Matches(key, @"\{([0-9]+)\}").Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToArray();
            var args = Enumerable.Range(0, values.Max() + 1).Select(i => (object?)match.Groups["arg" + i].Value).ToArray();
            return Format(key, args);
        }
        return message;
    }
    public static string Translate(string? key, string language)
    {
        if (string.IsNullOrEmpty(key)) return key ?? "";
        var catalog = language == "es" ? Spanish : English;
        return catalog.TryGetValue(key, out var value) ? value : English.GetValueOrDefault(key, key);
    }
    public IReadOnlyDictionary<string, string> ClientMessages =>
        (Language == "es" ? Spanish : English).Where(pair => pair.Key.StartsWith("js.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"Deleguard.Api.Localization.UiStrings.{language}.json")
            ?? throw new InvalidOperationException($"Missing UI catalog: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    public static RequestLocalizationOptions Options() => new()
    {
        DefaultRequestCulture = new RequestCulture("en", "en"),
        // Form numbers, policy inputs and protocol serialization keep their existing conventions.
        SupportedCultures = [CultureInfo.GetCultureInfo("en")],
        SupportedUICultures = SupportedLanguages.Select(CultureInfo.GetCultureInfo).ToArray(),
        RequestCultureProviders = [new CustomRequestCultureProvider(context =>
        {
            var language = context.Request.Cookies[CookieName];
            return Task.FromResult<ProviderCultureResult?>(new("en", language is "es" ? "es" : "en"));
        })]
    };
}
