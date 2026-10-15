import assert from "node:assert/strict";
import fs from "node:fs";
import vm from "node:vm";
import test from "node:test";

const source = fs.readFileSync(new URL("../../src/Deleguard.Api/wwwroot/js/localization.js", import.meta.url), "utf8");
function load(language = "en", accept = false) {
  const catalog = JSON.parse(fs.readFileSync(new URL(`../../src/Deleguard.Api/Localization/UiStrings.${language}.json`, import.meta.url), "utf8"));
  const listeners = {}; const prompts = [];
  const window = { confirm: message => { prompts.push(message); return accept; } };
  const location = { pathname: "/start", search: "?filter=mine", hash: "#how-it-works" };
  vm.runInNewContext(source, { window, location, document: {
    documentElement: { lang: language },
    getElementById: () => ({ textContent: JSON.stringify(catalog) }),
    addEventListener: (event, handler) => { listeners[event] = handler; }
  } });
  return { listeners, window, prompts };
}

test("language selection retains the route, query, and section", () => {
  const { listeners, prompts } = load(); const returnUrl = {};
  listeners.submit({ target: { matches: () => true, querySelector: () => returnUrl } });
  assert.equal(returnUrl.value, "/start?filter=mine#how-it-works");
  assert.deepEqual(prompts, []);
});

test("cancelling a language change keeps unsaved inputs and the selected language", () => {
  const { listeners, prompts } = load("es"); const select = { value: "en" };
  const field = { closest: () => ({ method: "post", matches: () => false }) };
  listeners.input({ target: field }); let prevented = false;
  listeners.submit({ target: { matches: () => true, querySelector: () => select }, preventDefault: () => { prevented = true; } });
  assert.equal(prevented, true); assert.equal(select.value, "es");
  assert.match(prompts[0], /sin guardar/);
  assert.equal(Object.hasOwn(field, "value"), false);
});

test("validation follows the selected language and clears when input changes", () => {
  for (const language of ["en", "es"]) {
    const { listeners, window } = load(language); let message;
    const field = { type: "email", validity: { typeMismatch: true }, setCustomValidity: value => { message = value; } };
    listeners.invalid({ target: field });
    assert.equal(message, window.dgText("js.validation.email"));
    listeners.input({ target: field }); assert.equal(message, "");
  }
});
