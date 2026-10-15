(function () {
  var catalogNode = document.getElementById("playground-catalog");
  var form = document.querySelector("[data-playground-form]");
  if (!catalogNode || !form) {
    return;
  }

  var catalog = {};
  try {
    catalog = JSON.parse(catalogNode.textContent || "{}");
  } catch (error) {
    catalog = {};
  }

  var orgId = catalog.orgId || "";
  var storageKey = orgId ? "deleguard.playground." + orgId : "";
  var storageVersion = 3;
  var agentSelect = form.querySelector("[data-playground-agent]");
  var keyInput = form.querySelector("[data-playground-key]");
  var actionSelect = form.querySelector("[data-playground-action]");
  var resourceType = form.querySelector("[data-playground-resource-type]");
  var resourceId = form.querySelector("[data-playground-resource-id]");
  var attributes = form.querySelector("[data-playground-attributes]");
  var keyHint = document.querySelector("[data-key-hint]");
  var prefixWarning = document.querySelector("[data-prefix-warning]");
  var clearButton = document.querySelector("[data-clear-playground]");
  var presets = document.querySelector("[data-policy-scenarios]");

  function selectedAgent() {
    if (!agentSelect) {
      return null;
    }
    var option = agentSelect.options[agentSelect.selectedIndex];
    if (!option) {
      return null;
    }
    return {
      id: option.value,
      prefix: option.getAttribute("data-key-prefix") || "",
      environment: option.getAttribute("data-environment") || ""
    };
  }

  function selectedActionName() {
    return actionSelect ? actionSelect.value : "";
  }

  function scenariosFor(action) {
    var all = catalog.scenarios || {};
    return all[action] || [];
  }

  function prettyJson(value) {
    try {
      if (typeof value === "string") {
        return JSON.stringify(JSON.parse(value), null, 2);
      }
      return JSON.stringify(value || {}, null, 2);
    } catch (error) {
      return "{}";
    }
  }

  function applyScenario(scenario, type) {
    if (resourceId) {
      resourceId.value = scenario && scenario.resourceId
        ? scenario.resourceId
        : (type ? type + "-1" : "resource-1");
    }
    if (attributes) {
      attributes.value = scenario ? prettyJson(scenario.attributes) : "{}";
    }
  }

  function defaultScenario(action) {
    var list = scenariosFor(action);
    for (var i = 0; i < list.length; i++) {
      if (list[i].effect === "ALLOW") {
        return list[i];
      }
    }
    return list[0] || null;
  }

  function renderScenarioChips(action) {
    if (!presets) {
      return;
    }
    presets.replaceChildren();
    var list = scenariosFor(action);
    presets.hidden = list.length === 0;
    list.forEach(function (scenario) {
      var button = document.createElement("button");
      button.type = "button";
      button.className = "ghost";
      button.textContent = window.dgText("js.scenario." + (scenario.label || scenario.effect));
      button.title = scenario.effect || "";
      button.addEventListener("click", function () {
        applyScenario(scenario, resourceType ? resourceType.value.trim() : "");
        writeStore();
      });
      presets.appendChild(button);
    });
  }

  function readStore() {
    if (!storageKey) {
      return null;
    }
    try {
      var raw = window.localStorage.getItem(storageKey);
      return raw ? JSON.parse(raw) : null;
    } catch (error) {
      return null;
    }
  }

  function writeStore() {
    if (!storageKey) {
      return;
    }
    try {
      window.localStorage.setItem(storageKey, JSON.stringify({
        v: storageVersion,
        agentId: agentSelect ? agentSelect.value : "",
        apiKey: keyInput ? keyInput.value : "",
        action: actionSelect ? actionSelect.value : "",
        resourceType: resourceType ? resourceType.value : "",
        resourceId: resourceId ? resourceId.value : "",
        attributesJson: attributes ? attributes.value : ""
      }));
    } catch (error) {
      // Private mode or quota — keep the form usable without persistence.
    }
  }

  function restore() {
    var stored = readStore();
    if (stored && stored.v === storageVersion) {
      if (agentSelect && stored.agentId) {
        var exists = Array.prototype.some.call(agentSelect.options, function (option) {
          return option.value === stored.agentId;
        });
        if (exists) {
          agentSelect.value = stored.agentId;
        }
      }
      if (keyInput && stored.apiKey) {
        keyInput.value = stored.apiKey;
      }
      if (actionSelect && stored.action) {
        var actionExists = Array.prototype.some.call(actionSelect.options, function (option) {
          return option.value === stored.action;
        });
        if (actionExists) {
          actionSelect.value = stored.action;
        }
      }
    } else if (stored && agentSelect && stored.agentId) {
      var agentExists = Array.prototype.some.call(agentSelect.options, function (option) {
        return option.value === stored.agentId;
      });
      if (agentExists) {
        agentSelect.value = stored.agentId;
      }
    }

    syncResourceTypeFromAction();
    var action = selectedActionName();
    var keepForm = stored
      && stored.v === storageVersion
      && stored.action === action
      && stored.resourceType
      && stored.resourceId
      && stored.attributesJson;
    if (keepForm) {
      if (resourceType) {
        resourceType.value = stored.resourceType;
      }
      if (resourceId) {
        resourceId.value = stored.resourceId;
      }
      if (attributes) {
        attributes.value = stored.attributesJson;
      }
    } else {
      applyScenario(defaultScenario(action), resourceType ? resourceType.value.trim() : "");
    }

    syncUi();
  }

  function syncResourceTypeFromAction() {
    if (!actionSelect || !resourceType) {
      return;
    }
    var option = actionSelect.options[actionSelect.selectedIndex];
    if (option) {
      var type = option.getAttribute("data-resource-type") || "";
      if (type) {
        resourceType.value = type;
      }
    }
  }

  function syncUi() {
    var agent = selectedAgent();
    var prefix = agent && agent.prefix ? agent.prefix : "";
    if (keyHint) {
      keyHint.textContent = prefix
        ? window.dgText("js.playground.prefix", prefix)
        : window.dgText("js.playground.key");
    }
    if (prefixWarning) {
      var key = keyInput ? keyInput.value.trim() : "";
      prefixWarning.hidden = !key || !prefix || key.indexOf(prefix) === 0;
    }
    renderScenarioChips(selectedActionName());
    syncPolicyHint();
  }

  function syncPolicyHint() {
    var hint = document.querySelector("[data-policy-hint]");
    if (!hint) {
      return;
    }
    hint.replaceChildren();
    hint.classList.remove("err");
    var action = selectedActionName();
    var type = resourceType ? resourceType.value.trim() : "";
    var matches = (catalog.policies || []).filter(function (policy) {
      return policy.action === action && policy.resourceType === type;
    });
    if (matches.length === 0) {
      hint.classList.add("err");
      hint.textContent = window.dgText("js.playground.nopolicy");
      return;
    }
    hint.appendChild(document.createTextNode(matches.length === 1 ? window.dgText("js.playground.policy") : window.dgText("js.playground.policies")));
    matches.forEach(function (policy, index) {
      if (index) {
        hint.appendChild(document.createTextNode(", "));
      }
      var link = document.createElement("a");
      link.href = "/policies/" + encodeURIComponent(policy.id);
      link.textContent = policy.slug + (policy.version ? " v" + policy.version : "");
      hint.appendChild(link);
    });
  }

  if (agentSelect) {
    agentSelect.addEventListener("change", function () {
      syncUi();
      writeStore();
    });
  }
  if (keyInput) {
    keyInput.addEventListener("input", function () {
      syncUi();
      writeStore();
    });
  }
  if (actionSelect) {
    actionSelect.addEventListener("change", function () {
      syncResourceTypeFromAction();
      applyScenario(defaultScenario(selectedActionName()), resourceType ? resourceType.value.trim() : "");
      syncUi();
      writeStore();
    });
  }
  ["input", "change"].forEach(function (eventName) {
    if (resourceType) {
      resourceType.addEventListener(eventName, function () {
        syncUi();
        writeStore();
      });
    }
    if (resourceId) {
      resourceId.addEventListener(eventName, writeStore);
    }
    if (attributes) {
      attributes.addEventListener(eventName, writeStore);
    }
  });

  form.addEventListener("submit", writeStore);

  if (clearButton) {
    clearButton.addEventListener("click", function () {
      if (storageKey) {
        try {
          window.localStorage.removeItem(storageKey);
        } catch (error) {
          // Ignore storage failures.
        }
      }
      if (keyInput) {
        keyInput.value = "";
      }
      syncResourceTypeFromAction();
      applyScenario(defaultScenario(selectedActionName()), resourceType ? resourceType.value.trim() : "");
      syncUi();
    });
  }

  restore();
})();
