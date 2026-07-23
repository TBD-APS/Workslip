#!/usr/bin/env python3
"""Require an explicit documentation decision for pull requests."""

from __future__ import annotations

import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

DECISIONS = {
    "updated": re.compile(r"-\s*\[[xX]\]\s*Documentation updated\b", re.IGNORECASE),
    "none": re.compile(r"-\s*\[[xX]\]\s*No documentation impact\b", re.IGNORECASE),
    "waiver": re.compile(r"-\s*\[[xX]\]\s*Documentation waiver\b", re.IGNORECASE),
}

IMPACT_PREFIXES = (
    "src/BE/WorkslipApi/Endpoints/",
    "src/BE/WorkslipApi/Configuration/",
    "src/BE/WorkslipApi/Workslip.Application/",
    "src/BE/WorkslipApi/Workslip.Domain/",
    "src/BE/WorkslipApi/Workslip.Infrastructure/Schema/",
    "src/FE/src/routes/",
    "src/FE/src/features/",
    "src/FE/src/providers/",
    "src/FE/src/api/generated/",
    ".github/workflows/",
)

IMPACT_FILES = {
    "src/FE/src/sw.ts",
    "src/FE/src/registerSW.ts",
    "src/FE/vite.config.ts",
}

DOC_PREFIXES = (
    "Docs/",
    "src/BE/WorkslipApi/Postman/",
)

DOC_FILES = {
    "README.md",
    "AGENTS.md",
    "src/FE/README.md",
    "src/BE/WorkslipApi/README.md",
    ".github/pull_request_template.md",
    "CHANGELOG.md",
}


def fail(message: str) -> int:
    print(f"::error::{message}")
    return 1


def changed_files(base_sha: str, head_sha: str) -> list[str]:
    result = subprocess.run(
        ["git", "diff", "--name-only", f"{base_sha}...{head_sha}"],
        cwd=ROOT,
        check=True,
        text=True,
        capture_output=True,
    )
    return [line.strip() for line in result.stdout.splitlines() if line.strip()]


def has_prefix(path: str, prefixes: tuple[str, ...]) -> bool:
    return any(path.startswith(prefix) for prefix in prefixes)


def field(body: str, label: str) -> str | None:
    match = re.search(rf"(?im)^\s*{re.escape(label)}\s*:\s*(.+?)\s*$", body)
    if not match:
        return None
    value = match.group(1).strip()
    return value if value and value != "-" else None


def main() -> int:
    event_path = os.environ.get("GITHUB_EVENT_PATH")
    if not event_path:
        print("No GitHub event file; PR documentation-impact check skipped.")
        return 0

    event = json.loads(Path(event_path).read_text(encoding="utf-8"))
    pull_request = event.get("pull_request")
    if not pull_request:
        print("Not a pull_request event; PR documentation-impact check skipped.")
        return 0

    body = pull_request.get("body") or ""
    selected = [name for name, pattern in DECISIONS.items() if pattern.search(body)]
    if len(selected) != 1:
        return fail(
            "Select exactly one PR documentation decision: "
            "Documentation updated, No documentation impact, or Documentation waiver."
        )

    base_sha = pull_request["base"]["sha"]
    head_sha = pull_request["head"]["sha"]
    files = changed_files(base_sha, head_sha)

    impact = [
        path for path in files
        if path in IMPACT_FILES or has_prefix(path, IMPACT_PREFIXES)
    ]
    docs = [
        path for path in files
        if path in DOC_FILES or has_prefix(path, DOC_PREFIXES)
    ]

    decision = selected[0]
    if decision == "updated" and not docs:
        return fail(
            "Documentation updated is checked, but no maintained documentation file changed."
        )

    if decision == "waiver":
        owner = field(body, "Waiver owner")
        expires = field(body, "Waiver expires")
        follow_up = field(body, "Follow-up")
        if not owner or not expires or not follow_up:
            return fail(
                "A documentation waiver requires Waiver owner, Waiver expires, and Follow-up."
            )
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", expires):
            return fail("Waiver expires must use YYYY-MM-DD.")

    if impact:
        print("Documentation-impact paths:")
        for path in impact:
            print(f"  - {path}")
    else:
        print("No configured documentation-impact paths changed.")

    print(f"Documentation decision accepted: {decision}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
