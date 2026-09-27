#!/usr/bin/env python3
"""Static release gate for the RogueRust plugin collection."""
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
PLUGINS = ROOT / "plugins"
DOCS = ROOT / "docs"
README = ROOT / "README.md"
DLL_URL = "https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases"

errors = []
checks = 0

def check(ok, message):
    global checks
    checks += 1
    if not ok:
        errors.append(message)

def has_any(doc, *needles):
    return any(needle in doc for needle in needles)

readme = README.read_text(encoding="utf-8")
plugin_files = sorted(PLUGINS.glob("*.cs"))
doc_files = {p.stem: p for p in DOCS.glob("RogueRust*.md") if p.name not in {"README.md", "RELEASE_READINESS.md"}}

check(bool(plugin_files), "No plugin sources found in plugins/")

for source in plugin_files:
    text = source.read_text(encoding="utf-8")
    info = re.search(r'\[Info\("([^"]+)",\s*"[^"]+",\s*"([^"]+)"\)\]', text)
    check(info is not None, f"{source.name}: missing parseable [Info] metadata")
    if not info:
        continue
    plugin_name, version = info.groups()

    # Public documentation is paired to the source filename. A compatibility build may
    # intentionally retain a legacy [Info] title while using the RogueRust family filename.
    expected_doc = DOCS / f"{source.stem}.md"
    check(expected_doc.exists(), f"{source.name}: missing docs/{source.stem}.md")
    check(f"plugins/{source.name}" in readme, f"{source.name}: missing source link in root README")
    check(version in readme, f"{source.name}: version {version} not present in root README catalogue")
    if expected_doc.exists():
        doc = expected_doc.read_text(encoding="utf-8")
        check(version in doc, f"{expected_doc.name}: source version {version} not documented")
        check(DLL_URL in doc, f"{expected_doc.name}: missing RogueRust DLL release link")
        check(f"../plugins/{source.name}" in doc, f"{expected_doc.name}: missing/corrupt source link")
        check("## Features" in doc, f"{expected_doc.name}: missing Features section")
        check(has_any(doc, "## Compatibility", "**Frameworks:** Oxide / Carbon", "Rust · Oxide · Carbon"), f"{expected_doc.name}: missing Oxide/Carbon compatibility statement")
        check(has_any(doc, "## Permissions", "## Permissions and commands"), f"{expected_doc.name}: missing Permissions section")
        check(has_any(doc, "## Commands", "## Permissions and commands"), f"{expected_doc.name}: missing Commands section")
        check("## Configuration" in doc, f"{expected_doc.name}: missing Configuration section")
        check(has_any(doc, "## Installation", "## Installation and updating"), f"{expected_doc.name}: missing Installation section")
        check(has_any(doc, "## Updating", "## Installation and updating"), f"{expected_doc.name}: missing Updating section")

source_stems = {p.stem for p in plugin_files}
for stem, path in doc_files.items():
    check(stem in source_stems, f"{path.name}: documentation has no matching plugin source")

check(DLL_URL in readme, "README.md: missing RogueRust DLL release link")

if errors:
    print(f"RELEASE GATE FAILED: {len(errors)} issue(s) across {checks} checks")
    for error in errors:
        print(f" - {error}")
    sys.exit(1)

print(f"RELEASE GATE PASSED: {checks} static checks across {len(plugin_files)} plugins")
print("NOTE: compile/load and in-game smoke tests remain runtime release gates and are not asserted by this script.")
