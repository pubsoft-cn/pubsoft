#!/usr/bin/env python3
"""Regenerate package-dependency-allowlist.json from declared project references.

The allowlist is a GENERATED baseline (same discipline as PublicAPI.*.txt):
source of truth = csproj PackageReference/ProjectReference items. Anything the
pack pipeline can drop (PrivateAssets="all", ReferenceOutputAssembly="false")
is excluded, mirroring what actually lands in the nuspec.

    python3 scripts/sync-package-deps.py           # rewrite the baseline
    python3 scripts/sync-package-deps.py --check   # exit 1 on drift, no write

Adding a dependency therefore produces an allowlist diff in the PR — the
reviewable tripwire — instead of a hand-edited file that drifts.
"""
import json
import sys
from pathlib import Path
from xml.etree import ElementTree

REPO = Path(__file__).resolve().parent.parent
ALLOWLIST = Path(__file__).with_name("package-dependency-allowlist.json")


def local(tag: str) -> str:
    return tag.rsplit("}", 1)[-1]


def declared_deps(csproj: Path) -> list[str]:
    root = ElementTree.parse(csproj).getroot()
    deps: set[str] = set()
    for el in root.iter():
        name = local(el.tag)
        if name == "PackageReference":
            if (el.attrib.get("PrivateAssets") or "").strip().lower() == "all":
                continue
            if include := el.attrib.get("Include"):
                deps.add(include)
        elif name == "ProjectReference":
            # OutputItemType/PrivateAssets'd references never reach the nuspec
            if (el.attrib.get("PrivateAssets") or "").strip().lower() == "all":
                continue
            if (el.attrib.get("ReferenceOutputAssembly") or "").strip().lower() == "false":
                continue
            if include := el.attrib.get("Include"):
                deps.add(Path(include.replace("\\", "/")).stem)
    return sorted(deps)


def generate() -> dict[str, list[str]]:
    result = {}
    for csproj in sorted((REPO / "src").glob("*/*/*.csproj")):
        root = ElementTree.parse(csproj).getroot()
        packable = True
        for el in root.iter():
            if local(el.tag) == "IsPackable" and (el.text or "").strip().lower() == "false":
                packable = False
        if not packable:
            continue
        package_id = csproj.stem  # repo convention: csproj name == PackageId
        result[package_id] = declared_deps(csproj)
    return result


def main() -> int:
    generated = generate()
    rendered = json.dumps(generated, indent=2, ensure_ascii=False) + "\n"
    if "--check" in sys.argv:
        if ALLOWLIST.read_text(encoding="utf-8") != rendered:
            print("package-dependency-allowlist.json is stale — run scripts/sync-package-deps.py",
                  file=sys.stderr)
            return 1
        print(f"Dependency allowlist in sync ({len(generated)} packages).")
        return 0
    ALLOWLIST.write_text(rendered, encoding="utf-8")
    print(f"Regenerated {ALLOWLIST.name} for {len(generated)} packages.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
