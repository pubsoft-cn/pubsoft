# Contributing

Thanks for helping build PubSoft. Pull requests, issues, and discussions are welcome.

## Workflow

1. **Open an issue first** for anything beyond a typo — agree on direction before investing in a large change.
2. Fork, create a topic branch from `main`, and keep one logical change per PR.
3. Commit messages follow `type: summary` — `feat:`/`fix:`/`tests:`/`ci:`/`tools:`/`lift:`/`security:`/`docs:`/`build:` — with the *why* in the body. One complete semantic node per commit, no drive-by noise.
4. Before pushing, run `bash scripts/verify-before-push.sh` — it mirrors CI (format → API ledger → build → tests). PRs that fail these gates will not merge.
5. Open the PR against `main` and fill the template — reviewers will respond.

## Rules that bite

- New public symbols must land in `PublicAPI.Unshipped.txt` (`bash scripts/sync-public-api.sh`) — the analyzer and CI both enforce this.
- Bilingual docs: every `README.md`/`docs/*.md` needs its `.zh-CN.md` pair.
- New packages must follow `docs/documentation-governance.md` — template, front-matter, registry row, tests.
- Never commit secrets, internal identifiers, or customer data — CI scans for them.
- Dependencies with known advisories fail restore (NU1903/NU1904 are errors) — pick patched versions.

## What to expect

- Maintainers review for semantic completeness, test coverage, and boundary honesty — not style nits; `dotnet format` owns style.
- Merges are squash or rebase-by-author; meaningful history is preserved.

## Security

Do not open public issues for vulnerabilities — see [SECURITY.md](SECURITY.md).
