import { EditorState, EditorView, basicSetup, yaml, autocompletion } from "/lib/codemirror/cm6.js?v=i18n-1";

const textarea = document.getElementById("Yaml");
const host = document.querySelector("[data-yaml-host]");
if (!textarea || !host) {
  throw new Error("Policy YAML host missing.");
}

const keywords = [
  { label: "id", type: "keyword" },
  { label: "version", type: "keyword" },
  { label: "priority", type: "keyword" },
  { label: "match", type: "keyword" },
  { label: "action", type: "keyword" },
  { label: "resource_type", type: "keyword" },
  { label: "rules", type: "keyword" },
  { label: "name", type: "keyword" },
  { label: "when", type: "keyword" },
  { label: "field", type: "keyword" },
  { label: "op", type: "keyword" },
  { label: "value", type: "keyword" },
  { label: "all", type: "keyword" },
  { label: "any", type: "keyword" },
  { label: "effect", type: "keyword" },
  { label: "reason", type: "keyword" },
  { label: "approval", type: "keyword" },
  { label: "expires_in", type: "keyword" },
  { label: "approver_roles", type: "keyword" },
  { label: "fallback", type: "keyword" }
];

const effects = ["ALLOW", "DENY", "REQUIRE_APPROVAL"].map((label) => ({ label, type: "enum" }));
const operators = ["eq", "ne", "gt", "gte", "lt", "lte", "exists", "contains"].map((label) => ({ label, type: "enum" }));

function optionsFromList(id, type) {
  return Array.from(document.querySelectorAll("#" + id + " option")).map((option) => ({
    label: option.value,
    type
  }));
}

function completions(context) {
  const word = context.matchBefore(/[\w./:-]*/);
  if (!word && !context.explicit) {
    return null;
  }

  const line = context.state.doc.lineAt(context.pos).text;
  const trimmed = line.trim();
  let items = keywords;
  if (/^(op|-\s+op):/.test(trimmed) || /op:\s*$/.test(line)) {
    items = operators;
  } else if (/^(effect|fallback):/.test(trimmed)) {
    items = effects;
  } else if (/action:/.test(line)) {
    items = optionsFromList("policy-action-list", "class");
  } else if (/field:/.test(line)) {
    items = optionsFromList("policy-field-list", "property");
  }

  const from = word ? word.from : context.pos;
  return {
    from,
    options: items,
    validFor: /^[\w./:-]*$/
  };
}

let applying = false;
const view = new EditorView({
  doc: textarea.value,
  extensions: [
    basicSetup,
    EditorState.phrases.of({
      "Control character": window.dgText("js.cm.Control character"),
      "close": window.dgText("js.cm.close"),
      "Folded lines": window.dgText("js.cm.Folded lines"),
      "Unfolded lines": window.dgText("js.cm.Unfolded lines"),
      "to": window.dgText("js.cm.to"),
      "folded code": window.dgText("js.cm.folded code"),
      "unfold": window.dgText("js.cm.unfold"),
      "Fold line": window.dgText("js.cm.Fold line"),
      "Unfold line": window.dgText("js.cm.Unfold line"),
      "Selection deleted": window.dgText("js.cm.Selection deleted"),
      "Go to line": window.dgText("js.cm.Go to line"),
      "go": window.dgText("js.cm.go"),
      "replaced match on line $": window.dgText("js.cm.replaced match on line $"),
      "replaced $ matches": window.dgText("js.cm.replaced $ matches"),
      "current match": window.dgText("js.cm.current match"),
      "on line": window.dgText("js.cm.on line"),
      "Completions": window.dgText("js.cm.Completions"),
      "Diagnostics": window.dgText("js.cm.Diagnostics"),
      "No diagnostics": window.dgText("js.cm.No diagnostics"),
      "Find": window.dgText("js.cm.Find"),
      "Replace": window.dgText("js.cm.Replace"),
      "next": window.dgText("js.cm.next"),
      "previous": window.dgText("js.cm.previous"),
      "all": window.dgText("js.cm.all"),
      "match case": window.dgText("js.cm.match case"),
      "regexp": window.dgText("js.cm.regexp"),
      "by word": window.dgText("js.cm.by word"),
      "replace": window.dgText("js.cm.replace"),
      "replace all": window.dgText("js.cm.replace all")
    }),
    EditorView.contentAttributes.of({ "aria-label": window.dgText("js.yaml.label") }),
    yaml(),
    autocompletion({ override: [completions] }),
    EditorView.updateListener.of((update) => {
      if (!update.docChanged || applying) {
        return;
      }
      textarea.value = update.state.doc.toString();
      textarea.dispatchEvent(new Event("input", { bubbles: true }));
    })
  ],
  parent: host
});

window.DeleguardYamlEditor = {
  getValue() {
    return view.state.doc.toString();
  },
  refresh() {
    requestAnimationFrame(function () {
      view.requestMeasure();
    });
  },
  setValue(value) {
    const next = value == null ? "" : String(value);
    if (next === view.state.doc.toString()) {
      return;
    }
    applying = true;
    view.dispatch({
      changes: { from: 0, to: view.state.doc.length, insert: next }
    });
    textarea.value = next;
    applying = false;
  }
};

const form = document.querySelector("[data-policy-form]");
if (form) {
  form.addEventListener("submit", () => {
    textarea.value = view.state.doc.toString();
  });
}
