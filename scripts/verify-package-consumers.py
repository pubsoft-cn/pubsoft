import json
import sys
from pathlib import Path
from xml.etree import ElementTree


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def main():
    repo_root = Path(__file__).resolve().parent.parent
    allowlist_path = Path(__file__).with_name("package-dependency-allowlist.json")
    package_ids = json.loads(allowlist_path.read_text(encoding="utf-8"))
    tests_directory = repo_root / "tests"
    expected_projects = {
        (tests_directory / f"{package_id}ConsumerSmoke" / f"{package_id}ConsumerSmoke.csproj").resolve(): package_id
        for package_id in package_ids
    }
    aggregate_project = (tests_directory / "PubSoft.PackageConsumerSmoke" / "PubSoft.PackageConsumerSmoke.csproj").resolve()
    actual_projects = {
        project.resolve()
        for project in tests_directory.glob("*ConsumerSmoke/*.csproj")
        if project.resolve() != aggregate_project
    }
    failures = []

    for project_path in sorted(set(expected_projects) - actual_projects):
        failures.append(f"Missing isolated consumer: {project_path.relative_to(repo_root)}")
    for project_path in sorted(actual_projects - set(expected_projects)):
        failures.append(f"Unlisted isolated consumer: {project_path.relative_to(repo_root)}")

    for project_path, package_id in expected_projects.items():
        if project_path not in actual_projects:
            continue
        root = ElementTree.parse(project_path).getroot()
        references = []
        for element in root.iter():
            if local_name(element.tag) != "PackageReference":
                continue
            reference = element.attrib.get("Include") or element.attrib.get("Update")
            if reference and reference.casefold().startswith("pubsoft."):
                references.append(reference)

        if len(references) != 1 or references[0].casefold() != package_id.casefold():
            found = ", ".join(sorted(references)) or "none"
            failures.append(
                f"{project_path.relative_to(repo_root)} must directly reference only {package_id}; found {found}"
            )

    if failures:
        print("Package consumer check failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(f"Package consumer references passed for {len(package_ids)} isolated consumers.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, ElementTree.ParseError) as error:
        print(f"Package consumer check failed: {error}", file=sys.stderr)
        raise SystemExit(1)