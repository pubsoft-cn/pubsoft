import json
import sys
import zipfile
from pathlib import Path
from xml.etree import ElementTree


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


def read_package_metadata(package_path):
    with zipfile.ZipFile(package_path) as package:
        nuspec_paths = [name for name in package.namelist() if name.lower().endswith(".nuspec")]
        if len(nuspec_paths) != 1:
            raise ValueError(f"{package_path.name}: expected one nuspec, found {len(nuspec_paths)}")

        root = ElementTree.fromstring(package.read(nuspec_paths[0]))

    metadata = next((element for element in root if local_name(element.tag) == "metadata"), None)
    if metadata is None:
        raise ValueError(f"{package_path.name}: nuspec has no metadata element")

    package_id = next((element.text for element in metadata if local_name(element.tag) == "id"), None)
    if not package_id:
        raise ValueError(f"{package_path.name}: nuspec has no package id")

    dependencies = set()
    for element in metadata.iter():
        if local_name(element.tag) == "dependency":
            dependency_id = element.attrib.get("id")
            if not dependency_id:
                raise ValueError(f"{package_path.name}: dependency has no id")
            dependencies.add(dependency_id)

    return package_id, dependencies


def main():
    if len(sys.argv) != 2:
        print("Usage: verify-package-dependencies.py <package-directory>", file=sys.stderr)
        return 2

    package_directory = Path(sys.argv[1])
    allowlist_path = Path(__file__).with_name("package-dependency-allowlist.json")
    allowed = json.loads(allowlist_path.read_text(encoding="utf-8"))
    packages = sorted(package_directory.glob("*.nupkg"))
    actual = {}

    for package_path in packages:
        package_id, dependencies = read_package_metadata(package_path)
        key = package_id.casefold()
        if key in actual:
            raise ValueError(f"Duplicate package id in feed: {package_id}")
        actual[key] = (package_id, dependencies)

    allowed_by_id = {package_id.casefold(): (package_id, set(dependencies)) for package_id, dependencies in allowed.items()}
    missing_packages = sorted(set(allowed_by_id) - set(actual))
    unexpected_packages = sorted(set(actual) - set(allowed_by_id))
    failures = []

    if missing_packages:
        failures.append("Missing packages: " + ", ".join(allowed_by_id[key][0] for key in missing_packages))
    if unexpected_packages:
        failures.append("Unexpected packages: " + ", ".join(actual[key][0] for key in unexpected_packages))

    for key in sorted(set(actual) & set(allowed_by_id)):
        package_id, dependencies = actual[key]
        _, expected_dependencies = allowed_by_id[key]
        unexpected = sorted(dependencies - expected_dependencies)
        missing = sorted(expected_dependencies - dependencies)
        if unexpected:
            failures.append(f"{package_id}: unexpected dependencies: {', '.join(unexpected)}")
        if missing:
            failures.append(f"{package_id}: missing dependencies: {', '.join(missing)}")

    if failures:
        print("Package dependency allowlist check failed:", file=sys.stderr)
        for failure in failures:
            print(f"- {failure}", file=sys.stderr)
        return 1

    print(f"Package dependency allowlist check passed for {len(actual)} packages.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (OSError, ValueError, zipfile.BadZipFile, ElementTree.ParseError) as error:
        print(f"Package dependency allowlist check failed: {error}", file=sys.stderr)
        raise SystemExit(1)