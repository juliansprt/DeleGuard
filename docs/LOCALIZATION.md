# Interface localization

DeleGuard ships with English and Spanish. English is the default, including when
the browser requests Spanish. The language selector is available on the public
site, sign-in, onboarding, and every console page. The choice is stored for one
year in the `dg_ui_language` cookie and follows navigation. Switching language
keeps the current path, query and section. An unsaved form prompts before reload.

## Rendering boundary

- `src/Deleguard.Api/Localization/UiText.cs` loads the embedded UTF-8 catalogs
  `UiStrings.en.json` and `UiStrings.es.json`. Both files must contain the same
  keys and preserve numbered placeholders. `WithCulture="false"` keeps the JSON
  resources in the API assembly instead of satellite assemblies.
- Razor uses the injected `T` indexer for authored UI strings and `T.Format`
  for variable text. `T.Message` translates known application notification
  templates at the display boundary. Unknown diagnostics retain their source.
- `_LocalizationScript` serializes only `js.*` catalog entries using the default
  HTML-safe JSON encoder. JavaScript uses `window.dgText(key, ...args)`.
- Keep Razor's HTML encoding and use `textContent` or escaped text in JavaScript.
  Never translate HTML attributes that control behavior, submitted identifiers,
  YAML/JSON, commands, user names, policy reasons, resource values or secrets.
- `CurrentUICulture` controls language; numeric form parsing remains English.
  `/v1` and `/mcp` are excluded from request localization. No decisions,
  permission checks, policy content or API contracts depend on language.
- The language POST uses Razor antiforgery and only permits local redirects.
  Its cookie is HttpOnly, SameSite=Lax and Secure on HTTPS. No account data or
  credentials are stored in the browser by localization.

## Adding or updating text

Add the same key to both JSON catalogs, provide both translations, and render
through `T` or `dgText`. Use complete messages with `{0}` placeholders where
word order can change. Leave technical values in form fields unchanged. A new
language also needs its catalog registered in `UiText` and the selector.

CodeMirror uses `EditorState.phrases`, exported by the local bundle, for search
and accessibility messages. Preserve that export if rebuilding the bundle.
The versioned module import prevents clients from using a cached older export.
External identity providers and user-authored documentation retain their own
language settings.

## Verification

```powershell
python scripts/check-localization.py
dotnet test tests/Deleguard.IntegrationTests/Deleguard.IntegrationTests.csproj --filter 'FullyQualifiedName~Localization|FullyQualifiedName~QuickStart'
node --test integrations/ide/test_ide_setup_ui.mjs tests/ui/localization.test.mjs
```

Integration tests require Docker and use isolated PostgreSQL containers. They
cover catalog parity, template formatting, English defaults, language cookies,
antiforgery, local redirects, all console modules, and invariant form values.
The IDE tests verify that translations do not change generated commands or
path quoting. Also check the guide, IDE flow and policy editor visually in both
languages, including a narrow viewport and CodeMirror search.
