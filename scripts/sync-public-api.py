#!/usr/bin/env python3
"""Synchronize PublicAPI.Unshipped.txt files with the compiled public surface.

Runs a full Release build, harvests RS0016 (symbol missing from declared API)
and RS0017 (declared symbol no longer in source) warnings, and rewrites each
affected project's PublicAPI.Unshipped.txt (sorted, deduplicated, header kept).

Usage:
    python3 scripts/sync-public-api.py           # update files in place
    python3 scripts/sync-public-api.py --check   # fail (exit 1) if drift exists
"""
import re, subprocess, sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
HEADER = "#nullable enable"
ADD_RE = re.compile(r"warning RS0016: Symbol '(?P<sym>.+?)' is not part of the declared.*?\[(?P<proj>[^\]]+\.csproj)\]")
DEL_RE = re.compile(r"warning RS0017: Symbol '(?P<sym>.+?)'\s.*\[(?P<proj>[^\]]+\.csproj)\]")


def build() -> str:
    proc = subprocess.run(
        ["dotnet", "build", str(REPO / "PubSoft.sln"), "-c", "Release", "--no-incremental"],
        capture_output=True, text=True)
    return proc.stdout + proc.stderr


def main() -> int:
    check = "--check" in sys.argv
    print("Building to collect API warnings...", file=sys.stderr)
    adds, dels = {}, {}
    for line in build().splitlines():
        if m := ADD_RE.search(line):
            adds.setdefault(Path(m["proj"]).resolve().parent, set()).add(m["sym"])
        elif m := DEL_RE.search(line):
            dels.setdefault(Path(m["proj"]).resolve().parent, set()).add(m["sym"])

    touched = []
    for proj_dir in sorted(set(adds) | set(dels)):
        api = proj_dir / "PublicAPI.Unshipped.txt"
        symbols = set()
        if api.exists():
            symbols = {l.strip() for l in api.read_text().splitlines()
                       if l.strip() and not l.startswith("#")}
        symbols = (symbols - dels.get(proj_dir, set())) | adds.get(proj_dir, set())
        content = HEADER + "\n" + "\n".join(sorted(symbols)) + "\n"
        if api.exists() and api.read_text() == content:
            continue
        name = proj_dir.name
        print(f"{'would update' if check else 'updated'} {name}: "
              f"+{len(adds.get(proj_dir, set()))} -{len(dels.get(proj_dir, set()))}")
        touched.append(name)
        if not check:
            api.write_text(content)

    if touched and check:
        print(f"\nPublic API drift in {len(touched)} project(s): {', '.join(touched)}",
              file=sys.stderr)
        return 1
    print(f"Done: {len(touched)} project(s) {'need' if check else ''} updating."
          if touched else "Done: PublicAPI files already in sync.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
