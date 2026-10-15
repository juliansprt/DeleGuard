(function () {
  "use strict";
  document.querySelectorAll("[data-role-impact]").forEach(function (output) {
    const form = output.closest("form");
    const boxes = Array.from(form.querySelectorAll("[data-permissions]"));
    const permissions = items => new Set(items.flatMap(box => JSON.parse(box.dataset.permissions)));
    const original = permissions(boxes.filter(box => box.dataset.original === "true"));
    function showImpact() {
      const selected = permissions(boxes.filter(box => box.checked));
      const added = Array.from(selected).filter(p => !original.has(p));
      const removed = Array.from(original).filter(p => !selected.has(p));
      output.textContent = !added.length && !removed.length ? window.dgText("js.roles.unchanged") :
        window.dgText("js.roles.added") + ": " + (added.join(", ") || window.dgText("js.roles.none")) + ". " +
        window.dgText("js.roles.removed") + ": " + (removed.join(", ") || window.dgText("js.roles.none")) + ".";
    }
    form.addEventListener("change", showImpact); showImpact();
  });
  const search = document.querySelector("[data-module-search]");
  if (search) {
    const normalize = value => value.normalize("NFD").replace(/[\u0300-\u036f]/g, "").toLowerCase().trim();
    search.addEventListener("input", function () {
      const query = normalize(search.value);
      let visible = 0;
      document.querySelectorAll("[data-module-card]").forEach(function (card) {
        card.hidden = !normalize(card.textContent).includes(query);
        if (!card.hidden) visible++;
      });
      document.querySelector("[data-module-empty]").hidden = visible > 0;
    });
  }
  document.querySelectorAll("[data-busy-form]").forEach(function (form) {
    form.addEventListener("submit", function (event) {
      if (form.dataset.submitting) { event.preventDefault(); return; }
      form.dataset.submitting = "true";
      form.setAttribute("aria-busy", "true");
      const button = event.submitter || form.querySelector('button[type="submit"],button:not([type])');
      if (button) {
        button.dataset.originalLabel = button.textContent;
        button.textContent = button.dataset.busyLabel || window.dgText("js.saving");
        // Keep successful submit controls in the POST, and prevent subsequent submissions above.
        button.setAttribute("aria-disabled", "true");
      }
    });
  });
  window.addEventListener("pageshow", function () {
    document.querySelectorAll("[data-busy-form]").forEach(function (form) {
      delete form.dataset.submitting; form.removeAttribute("aria-busy");
      form.querySelectorAll("[data-original-label]").forEach(function (button) {
        button.textContent = button.dataset.originalLabel; button.removeAttribute("aria-disabled");
      });
    });
  });
})();
