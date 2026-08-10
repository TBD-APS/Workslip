#!/usr/bin/env python3
"""Check high-value Workslip documentation facts against the current repository.

The checker deliberately avoids reproducing the repository in another artifact. It
validates cheap, objective drift signals and leaves semantic review to humans/agents.
"""

from __future__ import annotations

import json
import re
import sys
from collections import defaultdict
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]

MAINTAINED_DOC_PATTERNS = (
    "README.md",
    "AGENTS.md",
    "Docs/*.md",
    "Docs/agents/*.md",
    "Docs/api/*.md",
    "Docs/architecture/*.md",
    "Docs/architecture/adr/*.md",
    "Docs/compliance/GDPR_AI_ACT_BASELINE.md",
    "Docs/operations/*.md",
    "Docs/release/*.md",
    "Docs/testing/*.md",
    "site/*.md",
    "src/FE/README.md",
    "src/FE/AGENTS.md",
    "src/BE/WorkslipApi/README.md",
    "src/BE/WorkslipApi/AGENTS.md",
    "src/BE/WorkslipApi/Postman/README.md",
    "src/BE/infrastructure/README.md",
    "src/BE/infrastructure/AGENTS.md",
    "src/BE/infrastructure/database/migrations/README.md",
)

ACTIVE_AGENT_FILES = (
    "AGENTS.md",
    "Docs/AGENTS.md",
    "src/FE/AGENTS.md",
    "src/BE/WorkslipApi/AGENTS.md",
    "src/BE/infrastructure/AGENTS.md",
)

RETIRED_ARTIFACT_PATTERNS = (
    "**/repomix-output.xml",
    "**/.repomixignore",
    ".github/workflows/update-repomix-after-release.yml",
)

RETIRED_DOCUMENTATION_PATTERNS = (
    "Docs/superpowers/**/*",
    "Docs/10-Projects/Workslip/**/*",
    "src/docs/**/*",
)

RETIRED_DOCUMENTATION_PATHS = (
    "Docs/agents/OPERATING_CONTRACT.md",
    "Docs/api/endpoint-catalog.md",
    "Docs/testing/full-stack-validation.md",
    "Docs/release/documentation-gate.md",
    "Docs/operations/go-live-production-data-cleanup.md",
)

INDEXED_DOC_SETS = (
    ("Docs/api/README.md", "Docs/api", "*.md"),
    ("Docs/architecture/README.md", "Docs/architecture", "*.md"),
    ("Docs/architecture/README.md", "Docs/architecture/adr", "*.md"),
)

LINK_RE = re.compile(r"!?\[[^\]]*]\(([^)]+)\)")
ISSUE_STATUS_LANGUAGE_RE = re.compile(
    r"\b(?:until|when|after)\s+WOR-\d+\s+(?:is\s+)?(?:completed|done|merged|closed)\b",
    re.IGNORECASE,
)
NPM_RUN_RE = re.compile(r"\bnpm\s+run\s+([A-Za-z0-9:_-]+)")
BULLET_RE = re.compile(r"^\s*-\s+(.+?)\s*$")


def error(path: Path, message: str, line: int = 1) -> None:
    try:
        relative = path.relative_to(ROOT).as_posix()
    except ValueError:
        relative = path.as_posix()
    print(f"{relative}:{line}: {message}")
    print(f"::error file={relative},line={line}::{message}")


def maintained_documents() -> list[Path]:
    files: set[Path] = set()
    for pattern in MAINTAINED_DOC_PATTERNS:
        files.update(path for path in ROOT.glob(pattern) if path.is_file())
    return sorted(files)


def target_from_markdown(raw: str) -> str:
    value = raw.strip()
    if value.startswith("<") and ">" in value:
        return value[1:value.index(">")].strip()

    match = re.match(r'''(?:"([^"]+)"|'([^']+)'|(\S+))''', value)
    if not match:
        return ""
    return next(group for group in match.groups() if group is not None)


def resolve_local_link(source: Path, destination: str) -> Path | None:
    if not destination or destination.startswith("#"):
        return None

    parsed = urlsplit(destination)
    if parsed.scheme or destination.startswith("//"):
        return None

    local_path = unquote(parsed.path)
    if not local_path:
        return None

    candidate = (
        ROOT / local_path.lstrip("/")
        if local_path.startswith("/")
        else source.parent / local_path
    )
    return candidate.resolve()


def validate_markdown(path: Path) -> int:
    failures = 0
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()

    if not any(re.match(r"^#\s+\S", line) for line in lines):
        error(path, "Expected at least one H1 heading.")
        failures += 1

    fence_count = sum(1 for line in lines if re.match(r"^\s*(?:```|~~~)", line))
    if fence_count % 2:
        error(path, "Unclosed fenced code block.")
        failures += 1

    for line_number, line in enumerate(lines, 1):
        if ISSUE_STATUS_LANGUAGE_RE.search(line):
            error(
                path,
                "Maintained docs must describe current state directly instead of depending on future Linear issue status.",
                line_number,
            )
            failures += 1

        for match in LINK_RE.finditer(line):
            destination = target_from_markdown(match.group(1))
            candidate = resolve_local_link(path, destination)
            if candidate is not None and not candidate.exists():
                error(path, f"Broken local link: {destination}", line_number)
                failures += 1

    return failures


def validate_entrypoints() -> int:
    failures = 0
    for relative in ("README.md", "AGENTS.md", "Docs/README.md"):
        path = ROOT / relative
        if not path.is_file():
            error(path, "Required repository/documentation entrypoint is missing.")
            failures += 1
    return failures


def validate_retired_artifacts() -> int:
    failures = 0
    seen: set[Path] = set()
    for pattern in RETIRED_ARTIFACT_PATTERNS:
        for path in ROOT.glob(pattern):
            if not path.exists() or path in seen:
                continue
            seen.add(path)
            error(path, "Retired duplicated repository-snapshot artifact must not be reintroduced.")
            failures += 1
    return failures


def validate_retired_documentation() -> int:
    failures = 0
    seen: set[Path] = set()

    for pattern in RETIRED_DOCUMENTATION_PATTERNS:
        for path in ROOT.glob(pattern):
            if not path.is_file() or path in seen:
                continue
            seen.add(path)
            error(
                path,
                "Historical implementation plans/specs belong in Git/Linear history, not beside current documentation.",
            )
            failures += 1

    for relative in RETIRED_DOCUMENTATION_PATHS:
        path = ROOT / relative
        if not path.is_file() or path in seen:
            continue
        seen.add(path)
        error(path, "Superseded documentation must not be reintroduced as repository guidance.")
        failures += 1

    return failures


def validate_docs_are_classified(documents: list[Path]) -> int:
    maintained = {path.resolve() for path in documents}
    failures = 0
    for path in sorted((ROOT / "Docs").rglob("*.md")):
        if not path.is_file() or path.resolve() in maintained:
            continue
        error(
            path,
            "Markdown under Docs/ must be part of the maintained documentation set; use Git/Linear for historical issue plans.",
        )
        failures += 1
    return failures


def linked_local_paths(index: Path) -> set[Path]:
    linked: set[Path] = set()
    text = index.read_text(encoding="utf-8")
    for match in LINK_RE.finditer(text):
        destination = target_from_markdown(match.group(1))
        candidate = resolve_local_link(index, destination)
        if candidate is not None:
            linked.add(candidate)
    return linked


def validate_directory_indexes() -> int:
    failures = 0
    for index_relative, directory_relative, pattern in INDEXED_DOC_SETS:
        index = ROOT / index_relative
        directory = ROOT / directory_relative
        if not index.is_file() or not directory.is_dir():
            continue

        linked = linked_local_paths(index)
        for document in sorted(directory.glob(pattern)):
            if not document.is_file() or document.resolve() == index.resolve():
                continue
            if document.resolve() not in linked:
                error(
                    index,
                    f"Documentation index does not link owned document: {document.relative_to(ROOT).as_posix()}",
                )
                failures += 1
    return failures


def validate_frontend_commands() -> int:
    package_path = ROOT / "src/FE/package.json"
    readme_path = ROOT / "src/FE/README.md"
    if not package_path.is_file() or not readme_path.is_file():
        return 0

    package = json.loads(package_path.read_text(encoding="utf-8"))
    scripts = set(package.get("scripts", {}))
    readme = readme_path.read_text(encoding="utf-8")
    failures = 0

    for match in NPM_RUN_RE.finditer(readme):
        script = match.group(1)
        if script not in scripts:
            line = readme.count("\n", 0, match.start()) + 1
            error(readme_path, f"README references missing package.json script: {script}", line)
            failures += 1

    if "test" in scripts and re.search(
        r"\b(?:there\s+is\s+)?(?:currently\s+)?no\s+(?:general\s+)?`?npm\s+test`?",
        readme,
        re.IGNORECASE,
    ):
        error(readme_path, "README says npm test is unavailable, but package.json defines a test script.")
        failures += 1

    return failures


def normalize_bullet(text: str) -> str:
    text = re.sub(r"\[([^\]]+)]\([^)]+\)", r"\1", text)
    text = text.replace("`", "")
    return re.sub(r"\s+", " ", text.strip()).casefold()


def validate_agent_duplication() -> int:
    occurrences: dict[str, list[tuple[Path, int, str]]] = defaultdict(list)
    failures = 0

    for relative in ACTIVE_AGENT_FILES:
        path = ROOT / relative
        if not path.is_file():
            continue
        for line_number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            match = BULLET_RE.match(line)
            if not match:
                continue
            raw = match.group(1)
            if len(raw) < 45:
                continue
            occurrences[normalize_bullet(raw)].append((path, line_number, raw))

    for entries in occurrences.values():
        if len({entry[0] for entry in entries}) < 2:
            continue
        locations = ", ".join(
            f"{path.relative_to(ROOT).as_posix()}:{line}" for path, line, _ in entries
        )
        first_path, first_line, first_text = entries[0]
        error(
            first_path,
            f"Exact agent rule is duplicated across scoped files ({locations}). Keep the shared rule in root AGENTS.md and the delta locally: {first_text}",
            first_line,
        )
        failures += 1

    return failures


def validate_agent_routing() -> int:
    failures = 0
    for relative in ACTIVE_AGENT_FILES:
        path = ROOT / relative
        if not path.is_file():
            continue
        if "OPERATING_CONTRACT.md" in path.read_text(encoding="utf-8"):
            error(path, "Active agent instructions must not route through the superseded operating contract.")
            failures += 1
    return failures


def main() -> int:
    documents = maintained_documents()
    if not documents:
        print("No maintained documentation files found.")
        return 1

    failures = 0
    failures += validate_entrypoints()
    failures += validate_retired_artifacts()
    failures += validate_retired_documentation()
    failures += validate_docs_are_classified(documents)
    failures += sum(validate_markdown(path) for path in documents)
    failures += validate_directory_indexes()
    failures += validate_frontend_commands()
    failures += validate_agent_duplication()
    failures += validate_agent_routing()

    if failures:
        print(f"Documentation truth check failed with {failures} error(s).")
        return 1

    print(f"Documentation truth check passed for {len(documents)} maintained file(s).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
