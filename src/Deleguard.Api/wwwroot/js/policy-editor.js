(function () {
  var stateNode = document.getElementById("policy-editor-state");
  var visual = document.querySelector("[data-policy-visual]");
  var yamlPane = document.querySelector("[data-policy-yaml]");
  var yamlField = document.getElementById("Yaml");
  var form = document.querySelector("[data-policy-form]");
  var tabField = document.querySelector("[data-editor-tab-field]");
  if (!stateNode || !visual || !yamlPane || !yamlField || !form) {
    return;
  }

  var operators = ["eq", "ne", "gt", "gte", "lt", "lte", "exists", "contains"];
  var operatorLabels = {
    eq: window.dgText("js.policy.eq"),
    ne: window.dgText("js.policy.ne"),
    gt: window.dgText("js.policy.gt"),
    gte: window.dgText("js.policy.gte"),
    lt: window.dgText("js.policy.lt"),
    lte: window.dgText("js.policy.lte"),
    exists: window.dgText("js.policy.exists"),
    contains: window.dgText("js.policy.contains")
  };
  var whenLabels = {
    always: window.dgText("js.policy.Always"),
    field: window.dgText("js.policy.Field"),
    all: window.dgText("js.policy.All of"),
    any: window.dgText("js.policy.Any of")
  };
  var activeTab = "yaml";
  var visualDirty = false;
  var conversionConfirmed = false;
  function confirmConversion() {
    if (conversionConfirmed || yamlField.value.indexOf("#") < 0) return true;
    conversionConfirmed = window.confirm(window.dgText("js.policy.conversion"));
    return conversionConfirmed;
  }
  function showUnsaved() {
    var indicator = document.querySelector("[data-unsaved-indicator]");
    if (indicator) indicator.hidden = false;
  }
  visual.addEventListener("input", showUnsaved);
  visual.addEventListener("change", showUnsaved);
  yamlField.addEventListener("input", showUnsaved);
  var state = {};
  try {
    state = JSON.parse(stateNode.textContent || "{}");
  } catch (error) {
    state = { error: window.dgText("js.policy.state"), visualSupported: false };
  }

  function token() {
    var input = form.querySelector('input[name="__RequestVerificationToken"]');
    return input ? input.value : "";
  }

  function effectBadge(effect) {
    if (effect === "ALLOW") {
      return "allow";
    }
    if (effect === "REQUIRE_APPROVAL") {
      return "wait";
    }
    return "deny";
  }

  function yamlScalar(value) {
    if (value === true) {
      return "true";
    }
    if (value === false) {
      return "false";
    }
    if (value === null || value === undefined) {
      return "null";
    }
    if (typeof value === "number" && isFinite(value)) {
      return String(value);
    }
    var text = String(value);
    if (text === "") {
      return '""';
    }
    if (/^[A-Za-z0-9_./-]+$/.test(text) && !/^(true|false|null|yes|no|on|off)$/i.test(text)) {
      return text;
    }
    return JSON.stringify(text);
  }

  function parseValue(raw) {
    if (raw === true || raw === false || typeof raw === "number") {
      return raw;
    }
    var text = String(raw == null ? "" : raw).trim();
    if (text === "") {
      return "";
    }
    if (text === "true") {
      return true;
    }
    if (text === "false") {
      return false;
    }
    if (/^-?\d+$/.test(text)) {
      var asInt = Number(text);
      if (Number.isSafeInteger(asInt)) {
        return asInt;
      }
    }
    if (/^-?\d+\.\d+$/.test(text)) {
      return Number(text);
    }
    return text;
  }

  function emitField(lines, indent, field, op, value, asItem) {
    var prefix = asItem ? indent + "- " : indent;
    var nested = asItem ? indent + "  " : indent;
    lines.push(prefix + "field: " + yamlScalar(field || ""));
    lines.push(nested + "op: " + yamlScalar(op || "eq"));
    if ((op || "eq") !== "exists") {
      lines.push(nested + "value: " + yamlScalar(parseValue(value)));
    }
  }

  function emitYaml(doc) {
    var lines = [];
    lines.push("id: " + yamlScalar(doc.id || ""));
    lines.push("version: " + (Number(doc.version) || 1));
    lines.push("priority: " + (Number(doc.priority) || 0));
    lines.push("match:");
    lines.push("  action: " + yamlScalar(doc.action || ""));
    lines.push("  resource_type: " + yamlScalar(doc.resourceType || ""));
    var rules = doc.rules || [];
    if (rules.length === 0) {
      lines.push("rules: []");
    } else {
      lines.push("rules:");
      rules.forEach(function (rule) {
        lines.push("- name: " + yamlScalar(rule.name || "unnamed"));
        var kind = rule.whenKind || "always";
        if (kind === "field" && rule.field) {
          lines.push("  when:");
          emitField(lines, "    ", rule.field, rule.op, rule.value, false);
        } else if ((kind === "all" || kind === "any") && Array.isArray(rule.conditions)) {
          var fields = rule.conditions.filter(function (item) { return item && item.field; });
          if (fields.length > 0) {
            lines.push("  when:");
            lines.push("    " + kind + ":");
            fields.forEach(function (item) {
              emitField(lines, "    ", item.field, item.op, item.value, true);
            });
          }
        }
        var effect = rule.effect || "DENY";
        lines.push("  effect: " + effect);
        if (rule.reason) {
          lines.push("  reason: " + yamlScalar(rule.reason));
        }
        if (effect === "REQUIRE_APPROVAL") {
          lines.push("  approval:");
          if (rule.approvalExpiresIn) {
            lines.push("    expires_in: " + yamlScalar(rule.approvalExpiresIn));
          }
          var roles = Array.isArray(rule.approverRoles) ? rule.approverRoles.filter(Boolean) : [];
          if (roles.length > 0) {
            lines.push("    approver_roles: [" + roles.map(yamlScalar).join(", ") + "]");
          }
        }
      });
    }
    lines.push("fallback: " + (doc.fallback || "DENY"));
    return lines.join("\n") + "\n";
  }

  function syncYaml() {
    if (!state.rules || state.visualSupported === false) {
      return;
    }
    yamlField.value = emitYaml(state);
    if (window.DeleguardYamlEditor && typeof window.DeleguardYamlEditor.setValue === "function") {
      window.DeleguardYamlEditor.setValue(yamlField.value);
    }
  }

  function markDirty() {
    if (!confirmConversion()) { showTab("yaml", { skipSync: true }); return; }
    visualDirty = true;
    showUnsaved();
    syncYaml();
  }

  function optionList(values, selected, labels) {
    return values.map(function (value) {
      var label = labels && labels[value] ? labels[value] : value;
      return '<option value="' + escapeHtml(value) + '"' + (value === selected ? " selected" : "") + ">" + escapeHtml(label) + "</option>";
    }).join("");
  }

  function escapeHtml(value) {
    return String(value == null ? "" : value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function displayValue(value) {
    if (value === true || value === false || typeof value === "number") {
      return String(value);
    }
    return value == null ? "" : String(value);
  }

  function rolesText(roles) {
    return Array.isArray(roles) ? roles.join(", ") : "";
  }

  function conditionRow(item, ruleIndex, index) {
    var op = item.op || "eq";
    return (
      '<div class="condition-row" data-condition-index="' + index + '">' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Field")) + "</label><input data-c-field list=\"policy-field-list\" value=\"" + escapeHtml(item.field || "") + '" /></p>' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Operator")) + "</label><select data-c-op>" + optionList(operators, op, operatorLabels) + "</select></p>" +
        '<p data-c-value-wrap' + (op === "exists" ? " hidden" : "") + "><label>" + escapeHtml(window.dgText("js.policy.Value")) + "</label><input data-c-value value=\"" + escapeHtml(displayValue(item.value)) + '" /></p>' +
        "<p><button type=\"button\" class=\"ghost\" data-remove-condition>" + escapeHtml(window.dgText("js.policy.Remove")) + "</button></p>" +
      "</div>"
    );
  }

  function conditionSummary(rule) {
    var kind = rule.whenKind || "always";
    if (kind === "always") {
      return window.dgText("js.policy.Always");
    }
    if (kind === "field") {
      return (rule.field || "field") + " " + (operatorLabels[rule.op] || rule.op || "eq") + " " + displayValue(rule.value);
    }
    var count = Array.isArray(rule.conditions) ? rule.conditions.length : 0;
    return (kind === "any" ? (window.dgText("js.policy.Any of") + " ") : (window.dgText("js.policy.All of") + " ")) + count + (count === 1 ? window.dgText("js.policy.condition") : window.dgText("js.policy.conditions"));
  }

  function ruleCard(rule, index, total) {
    var effect = rule.effect || "DENY";
    var kind = rule.whenKind || "always";
    var approvalHidden = effect === "REQUIRE_APPROVAL" ? "" : " hidden";
    var fieldHidden = kind === "field" ? "" : " hidden";
    var groupHidden = kind === "all" || kind === "any" ? "" : " hidden";
    var conditions = rule.conditions || [];
    var collapsed = rule.expanded ? "" : " is-collapsed";
    return (
      '<article class="rule-card' + collapsed + '" data-rule-index="' + index + '">' +
        '<header class="rule-card-head">' +
          '<button type="button" class="rule-summary" data-toggle-rule>' +
            '<span class="badge ' + effectBadge(effect) + '">' + escapeHtml(effect) + "</span>" +
            "<strong>" + escapeHtml(rule.name || (window.dgText("js.policy.rule") + (index + 1))) + "</strong>" +
            '<span class="muted">' + escapeHtml(conditionSummary(rule)) + "</span>" +
          "</button>" +
          '<div class="rule-toolbar">' +
            '<button type="button" class="ghost compact" data-move-up' + (index === 0 ? " disabled" : "") + ">" + escapeHtml(window.dgText("js.policy.Up")) + "</button>" +
            '<button type="button" class="ghost compact" data-move-down' + (index === total - 1 ? " disabled" : "") + ">" + escapeHtml(window.dgText("js.policy.Down")) + "</button>" +
            "<button type=\"button\" class=\"ghost compact\" data-duplicate-rule>" + escapeHtml(window.dgText("js.policy.Copy")) + "</button>" +
            "<button type=\"button\" class=\"secondary compact\" data-delete-rule>" + escapeHtml(window.dgText("js.policy.Delete")) + "</button>" +
          "</div>" +
        "</header>" +
        '<div class="rule-body">' +
        '<div class="rule-grid">' +
          "<p><label>" + escapeHtml(window.dgText("js.policy.Name")) + "</label><input data-rule-name value=\"" + escapeHtml(rule.name || "") + '" /></p>' +
          "<p><label>" + escapeHtml(window.dgText("js.policy.Effect")) + "</label><select data-rule-effect>" +
            optionList(["DENY", "REQUIRE_APPROVAL", "ALLOW"], effect) +
          "</select></p>" +
          "<p class=\"rule-reason\"><label>" + escapeHtml(window.dgText("js.policy.Reason")) + "</label><input data-rule-reason value=\"" + escapeHtml(rule.reason || "") + '" /></p>' +
        "</div>" +
        '<div class="approval-fields"' + approvalHidden + ">" +
          "<p><label>" + escapeHtml(window.dgText("js.policy.Approval expires in")) + "</label><input data-rule-expires value=\"" + escapeHtml(rule.approvalExpiresIn || "15m") + '" placeholder="15m" /></p>' +
          "<p><label>" + escapeHtml(window.dgText("js.policy.Approver roles")) + "</label><input data-rule-roles value=\"" + escapeHtml(rolesText(rule.approverRoles)) + "\" placeholder=\"billing-manager\" /><span class=\"hint\">" + escapeHtml(window.dgText("js.policy.Comma-separated roles.")) + "</span></p>" +
        "</div>" +
        "<p><label>" + escapeHtml(window.dgText("js.policy.When this is true")) + "</label><select data-rule-when>" +
          optionList(["always", "field", "all", "any"], kind, whenLabels) +
        "</select></p>" +
        '<div class="field-condition"' + fieldHidden + ">" +
          '<div class="condition-row">' +
            "<p><label>" + escapeHtml(window.dgText("js.policy.Field")) + "</label><input data-rule-field list=\"policy-field-list\" value=\"" + escapeHtml(rule.field || "") + '" /></p>' +
            "<p><label>" + escapeHtml(window.dgText("js.policy.Operator")) + "</label><select data-rule-op>" + optionList(operators, rule.op || "eq", operatorLabels) + "</select></p>" +
            '<p data-rule-value-wrap' + ((rule.op || "eq") === "exists" ? " hidden" : "") + "><label>" + escapeHtml(window.dgText("js.policy.Value")) + "</label><input data-rule-value value=\"" + escapeHtml(displayValue(rule.value)) + '" /></p>' +
          "</div>" +
        "</div>" +
        '<div class="group-condition"' + groupHidden + ">" +
          conditions.map(function (item, cIndex) { return conditionRow(item, index, cIndex); }).join("") +
          "<button type=\"button\" class=\"ghost compact\" data-add-condition>" + escapeHtml(window.dgText("js.policy.Add condition")) + "</button>" +
        "</div>" +
        "</div>" +
      "</article>"
    );
  }

  function render() {
    if (!state.rules || state.visualSupported === false) {
      visual.innerHTML = "<p class=\"muted\">" + escapeHtml(window.dgText("js.policy.unavailable")) + "</p>";
      return;
    }

    var rules = state.rules;
    visual.innerHTML =
      '<div class="identity-strip">' +
        "<p class=\"slug-chip\"><span class=\"muted\">" + escapeHtml(window.dgText("js.policy.Id")) + "</span> <code>" + escapeHtml(state.id || "") + "</code>" +
          '<input type="hidden" data-doc-version value="' + escapeHtml(state.version || 1) + '" /></p>' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Action")) + "</label><input data-doc-action list=\"policy-action-list\" value=\"" + escapeHtml(state.action || "") + '" /></p>' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Resource type")) + "</label><input data-doc-resource value=\"" + escapeHtml(state.resourceType || "") + '" /></p>' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Priority")) + "</label><input data-doc-priority type=\"number\" value=\"" + escapeHtml(state.priority || 0) + '" /></p>' +
        "<p><label>" + escapeHtml(window.dgText("js.policy.Fallback")) + "</label><select data-doc-fallback>" + optionList(["DENY", "REQUIRE_APPROVAL", "ALLOW"], state.fallback || "DENY") + "</select></p>" +
      "</div>" +
      '<div class="rule-list">' +
        rules.map(function (rule, index) { return ruleCard(rule, index, rules.length); }).join("") +
      "</div>" +
      "<button type=\"button\" class=\"ghost\" data-add-rule>" + escapeHtml(window.dgText("js.policy.Add rule")) + "</button>";
  }

  function showTab(tab, options) {
    var opts = options || {};
    var buttons = document.querySelectorAll("[data-editor-tab]");
    if (tab === "visual" && state.visualSupported === false) {
      tab = "yaml";
    }
    activeTab = tab;
    if (tabField) {
      tabField.value = tab;
    }
    buttons.forEach(function (button) {
      var selected = button.getAttribute("data-editor-tab") === tab;
      button.classList.toggle("active", selected);
      button.setAttribute("aria-selected", selected ? "true" : "false");
    });
    visual.hidden = tab !== "visual";
    yamlPane.hidden = tab !== "yaml";
    if (tab === "yaml" && !opts.skipSync && visualDirty) {
      markDirty();
    }
    if (tab === "yaml" && window.DeleguardYamlEditor && typeof window.DeleguardYamlEditor.refresh === "function") {
      window.DeleguardYamlEditor.refresh();
    }
  }

  function newRule() {
    return {
      name: "new-rule",
      effect: "DENY",
      reason: "",
      whenKind: "always",
      field: "resource.attributes.status",
      op: "eq",
      value: "",
      conditions: [{ field: "resource.attributes.status", op: "eq", value: "" }],
      approvalExpiresIn: "15m",
      approverRoles: [],
      expanded: true
    };
  }

  function clone(value) {
    return JSON.parse(JSON.stringify(value));
  }

  function applyDocument(doc) {
    state = doc;
    visualDirty = false;
    render();
  }

  function parseYamlIntoVisual() {
    if (!confirmConversion()) return Promise.resolve(false);
    var warning = document.querySelector("[data-editor-warning]");
    return fetch(window.location.pathname + "?handler=Preview", {
      method: "POST",
      headers: {
        "Content-Type": "application/x-www-form-urlencoded;charset=UTF-8",
        RequestVerificationToken: token()
      },
      body: new URLSearchParams({
        Yaml: yamlField.value,
        __RequestVerificationToken: token()
      })
    }).then(function (response) {
      return response.json().then(function (body) {
        return { ok: response.ok, body: body };
      });
    }).then(function (result) {
      if (!result.ok) {
        if (warning) {
          warning.hidden = false;
          warning.textContent = result.body.error || window.dgText("js.policy.invalid");
        }
        showTab("yaml", { skipSync: true });
        return false;
      }
      if (result.body.visualSupported === false) {
        if (warning) {
          warning.hidden = false;
          warning.textContent = result.body.visualUnsupportedReason || window.dgText("js.policy.unsupported");
        }
        applyDocument(result.body);
        showTab("yaml", { skipSync: true });
        return false;
      }
      if (warning) {
        warning.hidden = true;
      }
      applyDocument(result.body);
      showTab("visual");
      return true;
    }).catch(function () {
      showTab("yaml", { skipSync: true });
      return false;
    });
  }

  visual.addEventListener("input", function (event) {
    var target = event.target;
    if (target.matches("[data-doc-id]")) {
      state.id = target.value;
    } else if (target.matches("[data-doc-version]")) {
      state.version = Number(target.value) || 1;
    } else if (target.matches("[data-doc-priority]")) {
      state.priority = Number(target.value) || 0;
    } else if (target.matches("[data-doc-action]")) {
      state.action = target.value;
    } else if (target.matches("[data-doc-resource]")) {
      state.resourceType = target.value;
    } else if (target.matches("[data-doc-fallback]")) {
      state.fallback = target.value;
    } else {
      var card = target.closest("[data-rule-index]");
      if (!card || !state.rules) {
        return;
      }
      var index = Number(card.getAttribute("data-rule-index"));
      var rule = state.rules[index];
      if (!rule) {
        return;
      }
      if (target.matches("[data-rule-name]")) {
        rule.name = target.value;
      } else if (target.matches("[data-rule-reason]")) {
        rule.reason = target.value;
      } else if (target.matches("[data-rule-expires]")) {
        rule.approvalExpiresIn = target.value;
      } else if (target.matches("[data-rule-roles]")) {
        rule.approverRoles = target.value.split(",").map(function (part) { return part.trim(); }).filter(Boolean);
      } else if (target.matches("[data-rule-field]")) {
        rule.field = target.value;
      } else if (target.matches("[data-rule-value]")) {
        rule.value = parseValue(target.value);
      } else if (target.matches("[data-c-field], [data-c-value]")) {
        var row = target.closest("[data-condition-index]");
        var cIndex = Number(row.getAttribute("data-condition-index"));
        rule.conditions = rule.conditions || [];
        rule.conditions[cIndex] = rule.conditions[cIndex] || { field: "", op: "eq", value: "" };
        if (target.matches("[data-c-field]")) {
          rule.conditions[cIndex].field = target.value;
        } else {
          rule.conditions[cIndex].value = parseValue(target.value);
        }
      }
    }
    markDirty();
  });

  visual.addEventListener("change", function (event) {
    var target = event.target;
    var card = target.closest("[data-rule-index]");
    if (target.matches("[data-doc-fallback]")) {
      state.fallback = target.value || "DENY";
      markDirty();
      return;
    }
    if (!card || !state.rules) {
      return;
    }
    var index = Number(card.getAttribute("data-rule-index"));
    var rule = state.rules[index];
    if (!rule) {
      return;
    }
    if (target.matches("[data-rule-effect]")) {
      rule.effect = target.value || "DENY";
      rule.expanded = true;
      render();
      markDirty();
      return;
    }
    if (target.matches("[data-rule-when]")) {
      rule.whenKind = target.value || "always";
      rule.expanded = true;
      if ((rule.whenKind === "all" || rule.whenKind === "any") && (!rule.conditions || rule.conditions.length === 0)) {
        rule.conditions = [{ field: "resource.attributes.status", op: "eq", value: "" }];
      }
      render();
      markDirty();
      return;
    }
    if (target.matches("[data-rule-op]")) {
      rule.op = target.value || "eq";
      render();
      markDirty();
      return;
    }
    if (target.matches("[data-c-op]")) {
      var row = target.closest("[data-condition-index]");
      var cIndex = Number(row.getAttribute("data-condition-index"));
      rule.conditions[cIndex].op = target.value || "eq";
      render();
      markDirty();
    }
  });

  visual.addEventListener("click", function (event) {
    var button = event.target.closest("button");
    if (!button) {
      return;
    }
    if (button.matches("[data-add-rule]")) {
      state.rules = state.rules || [];
      state.rules.forEach(function (item) { item.expanded = false; });
      state.rules.push(newRule());
      render();
      markDirty();
      var names = visual.querySelectorAll("[data-rule-name]");
      if (names.length > 0) {
        names[names.length - 1].focus();
      }
      return;
    }
    var card = button.closest("[data-rule-index]");
    if (!card || !state.rules) {
      return;
    }
    var index = Number(card.getAttribute("data-rule-index"));
    if (button.matches("[data-toggle-rule]")) {
      state.rules[index].expanded = !state.rules[index].expanded;
      render();
      return;
    } else if (button.matches("[data-delete-rule]")) {
      state.rules.splice(index, 1);
    } else if (button.matches("[data-duplicate-rule]")) {
      var copy = clone(state.rules[index]);
      copy.name = (copy.name || "rule") + "-copy";
      copy.effect = copy.effect || "DENY";
      copy.expanded = true;
      state.rules.splice(index + 1, 0, copy);
    } else if (button.matches("[data-move-up]") && index > 0) {
      var up = state.rules[index - 1];
      state.rules[index - 1] = state.rules[index];
      state.rules[index] = up;
    } else if (button.matches("[data-move-down]") && index < state.rules.length - 1) {
      var down = state.rules[index + 1];
      state.rules[index + 1] = state.rules[index];
      state.rules[index] = down;
    } else if (button.matches("[data-add-condition]")) {
      state.rules[index].conditions = state.rules[index].conditions || [];
      state.rules[index].conditions.push({ field: "resource.attributes.status", op: "eq", value: "" });
    } else if (button.matches("[data-remove-condition]")) {
      var row = button.closest("[data-condition-index]");
      var cIndex = Number(row.getAttribute("data-condition-index"));
      state.rules[index].conditions.splice(cIndex, 1);
    } else {
      return;
    }
    render();
    markDirty();
  });

  document.querySelectorAll("[data-editor-tab]").forEach(function (button) {
    button.addEventListener("click", function () {
      var tab = button.getAttribute("data-editor-tab");
      if (tab === "visual" && activeTab === "yaml") {
        parseYamlIntoVisual();
        return;
      }
      showTab(tab);
    });
  });

  form.addEventListener("submit", function (event) {
    if (activeTab !== "visual" || !state.rules) {
      return;
    }
    if (!confirmConversion()) { event.preventDefault(); showTab("yaml", { skipSync: true }); return; }
    var submitter = event.submitter;
    var action = submitter && (submitter.getAttribute("formaction") || "");
    var isSave = /handler=SaveVersion/i.test(action);
    if (isSave && Number(state.version) < Number(state.nextVersion || state.version)) {
      state.version = state.nextVersion;
      visualDirty = true;
      var versionInput = visual.querySelector("[data-doc-version]");
      if (versionInput) {
        versionInput.value = String(state.version);
      }
    }
    if (visualDirty || isSave || activeTab === "visual") {
      syncYaml();
    }
  });

  document.querySelectorAll("[data-fixture-preset]").forEach(function (button) {
    button.addEventListener("click", function () {
      var field = document.querySelector("[data-fixture-json]");
      if (field) {
        field.value = button.getAttribute("data-fixture-preset") || "";
      }
    });
  });

  if (state.rules && state.visualSupported !== false) {
    render();
    var initial = document.querySelector("[data-editor-tab].active");
    showTab(yamlField.value.indexOf("#") >= 0 ? "yaml" : (initial ? initial.getAttribute("data-editor-tab") : "visual"), { skipSync: true });
  } else {
    showTab("yaml", { skipSync: true });
  }
})();
