"""Check local contribution-document links, GitHub heading anchors, and hero SVG.

Uses only the Python standard library. External links are intentionally not fetched.
Run from any directory with: python scripts/validate-contribution-docs.py
"""

from html import unescape
from html.parser import HTMLParser
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
DOCUMENTS = (
    "README.md",
    "CONTRIBUTING.md",
    "docs/DEVELOPMENT.md",
    ".github/PULL_REQUEST_TEMPLATE.md",
)


def without_code(text):
    """Ignore fenced examples when checking Markdown prose."""
    lines = []
    fence = None
    for line in text.splitlines():
        marker = re.match(r"^\s{0,3}(`{3,}|~{3,})", line)
        if marker:
            run = marker.group(1)
            if fence is None:
                fence = run
            elif run[0] == fence[0] and len(run) >= len(fence):
                fence = None
            continue
        if fence is None:
            lines.append(line)
    return "\n".join(lines)


class HtmlReferences(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []
        self.anchors = set()

    def handle_starttag(self, tag, attrs):
        for name, value in attrs:
            if value and name in ("href", "src"):
                self.links.append(value)
            if value and (name == "id" or (tag == "a" and name == "name")):
                self.anchors.add(value)


def anchors(path):
    text = without_code(path.read_text(encoding="utf-8"))
    html = HtmlReferences()
    html.feed(text)
    result = set(html.anchors)
    counts = {}
    for heading in re.findall(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", text, re.MULTILINE):
        heading = re.sub(r"<[^>]*>", "", heading)
        heading = re.sub(r"\[([^]]+)\]\([^)]*\)", r"\1", heading)
        slug = unescape(heading).lower()
        slug = "".join(c for c in slug if c.isalnum() or c in "_ -")
        slug = slug.replace(" ", "-")
        duplicate = counts.get(slug, 0)
        counts[slug] = duplicate + 1
        result.add(f"{slug}-{duplicate}" if duplicate else slug)
    return result


def main():
    errors = []
    checked = 0
    for name in DOCUMENTS:
        source = ROOT / name
        if not source.is_file():
            errors.append(f"{name}: required document is missing")
            continue
        text = without_code(source.read_text(encoding="utf-8"))
        html = HtmlReferences()
        html.feed(text)
        # Match destinations in inline links/images and reference definitions.
        markdown = re.findall(r"\]\(\s*(?:<([^>\n]+)>|([^\s)]+))", text)
        references = re.findall(r"^ {0,3}\[[^]\n]+\]:\s*(?:<([^>\n]+)>|(\S+))", text, re.MULTILINE)
        links = html.links + [a or b for a, b in markdown + references]
        for link in links:
            link = unescape(link)
            url = urlsplit(link)
            if url.scheme or url.netloc:
                continue
            path = unquote(url.path)
            target = ((ROOT if path.startswith("/") else source.parent) / path.lstrip("/")).resolve() if path else source
            if not target.is_relative_to(ROOT) or not target.exists():
                errors.append(f"{name}: local link does not exist in the repository: {link}")
                continue
            checked += 1
            if url.fragment and target.suffix.lower() in (".md", ".mdx"):
                if unquote(url.fragment) not in anchors(target):
                    errors.append(f"{name}: heading anchor does not exist: {link}")

    hero = ROOT / "docs/assets/readme-hero.svg"
    try:
        if ET.parse(hero).getroot().tag != "{http://www.w3.org/2000/svg}svg":
            errors.append("docs/assets/readme-hero.svg: root must be an SVG element")
    except (ET.ParseError, OSError) as error:
        errors.append(f"docs/assets/readme-hero.svg: {error}")

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    print(f"Validated {len(DOCUMENTS)} documents, {checked} local references, and the hero SVG.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
