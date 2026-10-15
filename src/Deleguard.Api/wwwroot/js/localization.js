(function () {
  "use strict";
  const messages = JSON.parse(document.getElementById("ui-messages")?.textContent || "{}");
  window.dgText = function (key, ...args) {
    const template = messages[key] || key;
    return template.replace(/\{(\d+)\}/g, (_, index) => String(args[Number(index)] ?? ""));
  };
  let unsaved = false;
  document.addEventListener("submit", function (event) {
    if (!event.target.matches("[data-language-form]")) { unsaved = false; return; }
    if (unsaved && !window.confirm(window.dgText("js.language.unsaved"))) {
      event.preventDefault();
      event.target.querySelector('[name="culture"]').value = document.documentElement.lang;
      return;
    }
    const input = event.target.querySelector('[name="returnUrl"]');
    if (input) input.value = location.pathname + location.search + location.hash;
  });
  document.addEventListener("invalid", function (event) {
    const field = event.target;
    if (!field.validity || typeof field.setCustomValidity !== "function") return;
    field.setCustomValidity("");
    const state = field.validity;
    const key = state.valueMissing ? "required" : state.typeMismatch ? (field.type === "email" ? "email" : "url")
      : state.patternMismatch ? "format" : state.rangeUnderflow || state.rangeOverflow ? "range"
      : state.badInput || state.stepMismatch ? "number" : null;
    if (key) field.setCustomValidity(window.dgText("js.validation." + key));
  }, true);
  document.addEventListener("input", function (event) {
    const form = event.target.closest?.("form");
    if (form?.method.toLowerCase() === "post" && !form.matches("[data-language-form]")) unsaved = true;
    if (typeof event.target.setCustomValidity === "function") event.target.setCustomValidity("");
  });
})();
