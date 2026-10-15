"""Check UI catalogs and literal references without external dependencies."""
import json
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1] / "src" / "Deleguard.Api"


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"Duplicate localization key: {key}")
        result[key] = value
    return result


catalogs = {
    language: json.loads(
        (root / "Localization" / f"UiStrings.{language}.json").read_text(encoding="utf-8"),
        object_pairs_hook=unique,
    )
    for language in ("en", "es")
}
assert catalogs["en"].keys() == catalogs["es"].keys(), "Catalog keys differ"
for key, english in catalogs["en"].items():
    spanish = catalogs["es"][key]
    assert english.strip() and spanish.strip(), f"Empty translation: {key}"
    assert sorted(re.findall(r"\{\d+\}", english)) == sorted(re.findall(r"\{\d+\}", spanish)), key

references = re.compile(r'(?:\bT\[|\bT\.Format\(|\bdgText\()\s*("(?:[^"\\]|\\.)*")')
files = list((root / "Pages").rglob("*.cshtml")) + list((root / "wwwroot" / "js").glob("*.js"))
missing = []
for file in files:
    for match in references.finditer(file.read_text(encoding="utf-8-sig")):
        key = json.loads(match[1])
        if key.endswith(".") and any(k.startswith(key) for k in catalogs["en"]):
            continue  # Known prefix, e.g. js.validation. + validity type.
        if key not in catalogs["en"]:
            missing.append(f"{file.relative_to(root)}: {key}")
assert not missing, "Missing translations:\n" + "\n".join(missing)
print(f"Validated {len(catalogs['en'])} translations in two languages and {len(files)} source files.")
