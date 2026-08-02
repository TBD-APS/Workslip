#!/usr/bin/env python3
"""Validate maintained Workslip documentation without external dependencies."""

from __future__ import annotations

import re
import sys
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]

ACTIVE_DOC_PATTERNS = (
    "README.md",
    "AGENTS.md",
    "Docs/README.md",
    "Docs/api/*.md",
    "Docs/api/**/*.md",
    "Docs/architecture/*.md",
    "Docs/architecture/**/*.md",
    "Docs/compliance/*.md",
    "Docs/compliance/**/*.md",
    "Docs/release/*.md",
    "Docs/release/**/*.md",
    "src/FE/README.md",
    "src/BE/WorkslipApi/README.md",
    "src/BE/WorkslipApi/Postman/README.md",
)

LINK_RE = re.compile(r"!?\[[^\]]*]\(([^)]+)\)")
GROUP_RE = re.compile(
    r"var\s+(?P<name>\w+)\s*=\s*app\."
    r"(?:MapGroup|MapReadGroup|MapUserGroup|MapAdminGroup)"
    r"\(\s*\"(?P<path>[^\"]+)\"",
    re.DOTALL,
)
TUPLE_GROUP_RE = re.compile(
    r"var\s*\((?P<names>[^)]+)\)\s*=\s*app\."
    r"(?:MapReadUserGroups|MapReadAdminGroups)"
    r"\(\s*\"(?P<path>[^\"]+)\"",
    re.DOTALL,
)
GROUP_ENDPOINT_RE = re.compile(
    r"(?P<name>\w+)\.Map(?P<method>Get|Post|Put|Patch|Delete)"
    r"\(\s*\"(?P<path>[^\"]+)\"",
    re.DOTALL,
)
APP_ENDPOINT_RE = re.compile(
    r"app\.Map(?P<method>Get|Post|Put|Patch|Delete)"
    r"\(\s*\"(?P<path>[^\"]+)\"",
    re.DOTALL,
)


def error(path: Path, message: str, line: int = 1) -> None:
    relative = path.relative_to(ROOT).as_posix()
    print(f"::error file={relative},line={line}::{message}")


def active_documents() -> list[Path]:
    files: set[Path] = set()
    for pattern in ACTIVE_DOC_PATTERNS:
        files.update(path for path in ROOT.glob(pattern) if path.is_file())
    return sorted(files)


def target_from_markdown(raw: str) -> str:
    value = raw.strip()
    if value.startswith("<") and ">" in value:
        return value[1 : value.index(">")].strip()

    match = re.match(r'''(?:"([^"]+)"|'([^']+)'|(\S+))''', value)
    if not match:
        return ""
    return next(group for group in match.groups() if group is not None)


def validate_markdown(path: Path) -> int:
    failures = 0
    try:
        text = path.read_text(encoding="utf-8")
    except UnicodeDecodeError as exc:
        error(path, f"File is not valid UTF-8: {exc}")
        return 1

    lines = text.splitlines()
    if not any(re.match(r"^#\s+\S", line) for line in lines):
        error(path, "Expected at least one H1 heading.")
        failures += 1

    fence_count = sum(1 for line in lines if re.match(r"^\s*(?:```|~~~)", line))
    if fence_count % 2:
        error(path, "Unclosed fenced code block.")
        failures += 1

    for line_number, line in enumerate(lines, 1):
        for match in LINK_RE.finditer(line):
            destination = target_from_markdown(match.group(1))
            if not destination or destination.startswith("#"):
                continue

            parsed = urlsplit(destination)
            if parsed.scheme or destination.startswith("//"):
                continue

            local_path = unquote(parsed.path)
            if not local_path:
                continue

            candidate = (
                ROOT / local_path.lstrip("/")
                if local_path.startswith("/")
                else path.parent / local_path
            )
            if not candidate.resolve().exists():
                error(path, f"Broken local link: {destination}", line_number)
                failures += 1

    return failures


def normalize_route(path: str) -> str:
    path = re.sub(r"{([^}:]+):[^}]+}", r"{\1}", path)
    path = "/" + path.strip("/")
    return path if path != "/" else "/"


def join_route(base: str, relative: str) -> str:
    if relative == "/":
        return normalize_route(base) + "/"
    return normalize_route(f"{base.rstrip('/')}/{relative.lstrip('/')}")


def extract_endpoints() -> set[tuple[str, str]]:
    endpoints: set[tuple[str, str]] = set()
    endpoint_dir = ROOT / "src/BE/WorkslipApi/Endpoints"
    sources = list(endpoint_dir.glob("*Endpoints.cs"))
    sources.append(ROOT / "src/BE/WorkslipApi/Configuration/EndpointConfiguration.cs")

    for source in sources:
        if not source.exists():
            continue
        text = source.read_text(encoding="utf-8-sig")
        groups: dict[str, str] = {}

        for match in GROUP_RE.finditer(text):
            groups[match.group("name")] = match.group("path")

        for match in TUPLE_GROUP_RE.finditer(text):
            for name in match.group("names").split(","):
                groups[name.strip()] = match.group("path")

        for match in GROUP_ENDPOINT_RE.finditer(text):
            base = groups.get(match.group("name"))
            if base is None:
                continue
            endpoints.add(
                (
                    match.group("method").upper(),
                    join_route(base, match.group("path")),
                )
            )

        for match in APP_ENDPOINT_RE.finditer(text):
            endpoints.add(
                (
                    match.group("method").upper(),
                    normalize_route(match.group("path")),
                )
            )

    return endpoints


def validate_api_catalog() -> int:
    catalog = ROOT / "Docs/api/endpoint-catalog.md"
    if not catalog.exists():
        print("Docs API catalog not present yet; endpoint drift check skipped.")
        return 0

    text = catalog.read_text(encoding="utf-8")
    failures = 0
    for method, route in sorted(extract_endpoints()):
        row = re.compile(rf"\|\s*{re.escape(method)}\s*\|\s*`{re.escape(route)}`\s*\|")
        if not row.search(text):
            error(catalog, f"Endpoint missing from catalog: {method} {route}")
            failures += 1

    return failures


def main() -> int:
    documents = active_documents()
    if not documents:
        print("No maintained documentation files found.")
        return 1

    failures = sum(validate_markdown(path) for path in documents)
    failures += validate_api_catalog()

    if failures:
        print(f"Documentation validation failed with {failures} error(s).")
        return 1

    print(f"Documentation validation passed for {len(documents)} file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
