(function () {
  var toggle = document.querySelector("[data-sidebar-toggle]");
  var backdrop = document.querySelector("[data-sidebar-backdrop]");
  var sidebar = document.querySelector(".sidebar");
  var mobileNavigation = window.matchMedia("(max-width: 960px)");
  function syncSidebar() {
    if (sidebar) sidebar.inert = mobileNavigation.matches && !document.body.classList.contains("sidebar-open");
  }
  mobileNavigation.addEventListener("change", syncSidebar);
  syncSidebar();
  function closeSidebar() {
    document.body.classList.remove("sidebar-open");
    if (toggle) toggle.setAttribute("aria-expanded", "false");
    syncSidebar();
  }
  if (toggle) {
    toggle.addEventListener("click", function () {
      document.body.classList.toggle("sidebar-open");
      toggle.setAttribute("aria-expanded", String(document.body.classList.contains("sidebar-open")));
      syncSidebar();
      if (document.body.classList.contains("sidebar-open") && sidebar) sidebar.querySelector("a,button,select")?.focus();
    });
  }
  document.addEventListener("keydown", function (event) {
    if (event.key === "Escape" && document.body.classList.contains("sidebar-open")) {
      closeSidebar();
      if (toggle) toggle.focus();
    }
    if (event.key === "Tab" && mobileNavigation.matches && document.body.classList.contains("sidebar-open") && sidebar) {
      var items = Array.from(sidebar.querySelectorAll('a[href],button,select,summary,input')).filter(function (el) { return el.offsetParent !== null && !el.disabled; });
      var first = items[0], last = items[items.length - 1];
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus(); }
      else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus(); }
    }
  });
  if (backdrop) {
    backdrop.addEventListener("click", closeSidebar);
  }

  function fallbackCopy(value, done) {
    var ta = document.createElement("textarea");
    ta.value = value;
    ta.setAttribute("readonly", "");
    ta.style.position = "fixed";
    ta.style.left = "-9999px";
    document.body.appendChild(ta);
    ta.select();
    try {
      if (document.execCommand("copy")) done();
      else throw new Error('Clipboard unavailable');
    } catch (e) {
      var status = document.getElementById("clipboard-status"); if (status) status.textContent = window.dgText("js.copy.failed");
    }
    document.body.removeChild(ta);
  }

  function markCopied(button) {
    var original = button.textContent;
    button.textContent = window.dgText("js.copy.done"); var status = document.getElementById("clipboard-status"); if (status) status.textContent = window.dgText("js.copy.announcement");
    setTimeout(function () {
      button.textContent = original;
    }, 1200);
  }

  function openHashDetails() {
    var id = (location.hash || "").replace(/^#/, "");
    if (!id) {
      return;
    }
    var el = document.getElementById(id);
    if (el) { var parent = el; while (parent) { if (parent.tagName === "DETAILS") parent.open = true; parent = parent.parentElement; } }
  }
  openHashDetails();
  window.addEventListener("hashchange", openHashDetails);

  document.addEventListener("click", function (event) {
    var button = event.target.closest("[data-copy]");
    if (!button) {
      return;
    }
    var value = button.getAttribute("data-copy") || "";
    var target = button.getAttribute("data-copy-target");
    if (target) {
      var node = document.querySelector(target);
      if (node) {
        value = (node.textContent || "").trim();
      }
    }
    if (!value) {
      return;
    }
    if (navigator.clipboard && navigator.clipboard.writeText) {
      navigator.clipboard.writeText(value).then(function () {
        markCopied(button);
      }).catch(function () {
        fallbackCopy(value, function () { markCopied(button); });
      });
      return;
    }
    fallbackCopy(value, function () { markCopied(button); });
  });

  var dialog = document.getElementById("confirm-dialog");
  var pendingForm = null;
  if (dialog) {
    var title = dialog.querySelector("#confirm-title");
    var body = dialog.querySelector("#confirm-body");
    var ok = dialog.querySelector("#confirm-ok");
    document.addEventListener("click", function (event) {
      var trigger = event.target.closest("[data-confirm]");
      if (!trigger) {
        return;
      }
      event.preventDefault();
      pendingForm = trigger.closest("form");
      if (!pendingForm) {
        return;
      }
      if (title) {
        title.textContent = trigger.getAttribute("data-confirm-title") || window.dgText("js.confirm");
      }
      if (body) {
        body.textContent = trigger.getAttribute("data-confirm-body") || "";
      }
      if (ok) {
        ok.textContent = trigger.getAttribute("data-confirm-action") || window.dgText("js.confirm");
        if (trigger.getAttribute("data-confirm-danger") === "true") {
          ok.className = "secondary";
        } else {
          ok.removeAttribute("class");
        }
      }
      dialog.showModal();
    });
    dialog.addEventListener("close", function () {
      if (dialog.returnValue === "confirm" && pendingForm) {
        var form = pendingForm;
        pendingForm = null;
        HTMLFormElement.prototype.submit.call(form);
      } else {
        pendingForm = null;
      }
    });
  }

  var env = document.getElementById("agent-environment");
  var warn = document.querySelector("[data-env-warning]");
  function syncEnvWarning() {
    if (!env || !warn) {
      return;
    }
    warn.hidden = env.value !== "production";
  }
  if (env) {
    env.addEventListener("change", syncEnvWarning);
    syncEnvWarning();
  }

  var actionName = document.getElementById("action-name");
  var nameWarn = document.querySelector("[data-name-warning]");
  function syncNameWarning() {
    if (!actionName || !nameWarn) {
      return;
    }
    var value = actionName.value.trim();
    nameWarn.hidden = !value || /^[a-z][a-z0-9_.:-]{1,127}$/.test(value);
  }
  if (actionName) {
    actionName.addEventListener("input", syncNameWarning);
    syncNameWarning();
  }

  var source = document.getElementById("agent-key");
  if (source) {
    document.querySelectorAll("form").forEach(function (form) {
      form.addEventListener("submit", function () {
        var hidden = form.querySelector("[data-agent-key]");
        if (hidden) {
          hidden.value = source.value;
        }
      });
    });
  }

  document.addEventListener("click", function (event) {
    var tab = event.target.closest("[data-snippet-tab]");
    if (!tab) {
      return;
    }
    var group = tab.closest("[data-snippet-tabs]");
    if (!group) {
      return;
    }
    var name = tab.getAttribute("data-snippet-tab");
    group.querySelectorAll("[data-snippet-tab]").forEach(function (el) {
      el.classList.toggle("active", el === tab);
    });
    group.querySelectorAll("[data-snippet-panel]").forEach(function (panel) {
      panel.hidden = panel.getAttribute("data-snippet-panel") !== name;
    });
  });

  document.querySelectorAll("[data-provider-preset]").forEach(function (select) {
    function syncCustomProvider() {
      var form = select.closest("form");
      var custom = form ? form.querySelector("[data-custom-provider]") : null;
      if (custom) {
        custom.hidden = select.value !== "custom";
      }
    }
    select.addEventListener("change", syncCustomProvider);
    syncCustomProvider();
  });
})();
