#!/usr/bin/env python3
"""Verify the shared knowledge artifacts against their published checksums.

The knowledge contract is shared with consuming product repositories, which copy
the templates and the validator rather than importing them — there is no package
boundary to import across. A copy that silently drifts from the platform's is
worse than no copy: both sides keep validating, against different rules.

This script is the detector, and it runs in two directions:

*   In this repository it is a gate. Editing a shared artifact without updating
    shared-artifacts.json fails, which forces the contract version to move with
    the artifacts instead of behind them.
*   In a consuming repository it is the check that its copies are current. Point
    ``--manifest`` at the platform's manifest and ``--root`` at the consumer's
    checkout: mismatches are reported per file with both digests.

Stdlib only, to match validate_knowledge.py — a consumer must be able to run it
without installing anything.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

DEFAULT_MANIFEST = Path(__file__).resolve().parent / "shared-artifacts.json"
DEFAULT_ROOT = Path(__file__).resolve().parents[2]


def digest(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--manifest",
        type=Path,
        default=DEFAULT_MANIFEST,
        help="Shared-artifact manifest to verify against (default: the one beside this script).",
    )
    parser.add_argument(
        "--root",
        type=Path,
        default=DEFAULT_ROOT,
        help="Repository root the manifest paths are resolved against (default: this repository).",
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Rewrite the manifest digests from the files on disk. Bump contractVersion in the "
        "same change — the digests are what a consumer pins, so moving them silently is the "
        "drift this script exists to catch.",
    )
    args = parser.parse_args()

    if not args.manifest.is_file():
        print(f"error: manifest not found: {args.manifest}", file=sys.stderr)
        return 2

    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    artifacts = manifest.get("artifacts")
    if not isinstance(artifacts, list) or not artifacts:
        print("error: manifest declares no artifacts", file=sys.stderr)
        return 2

    version = manifest.get("contractVersion", "(unversioned)")
    failures: list[str] = []
    changed = False

    for artifact in artifacts:
        relative = artifact.get("path")
        if not relative:
            failures.append(f"manifest entry declares no 'path': {artifact!r}")
            continue

        expected = artifact.get("sha256") or ""
        path = args.root / relative

        # A missing file is a failure in both modes: --update fills in digests, it does not
        # get to declare an incomplete manifest healthy.
        if not path.is_file():
            failures.append(f"{relative}: missing from {args.root}")
            continue

        actual = digest(path)
        if actual == expected:
            continue

        if args.update:
            artifact["sha256"] = actual
            changed = True
        elif expected:
            failures.append(f"{relative}: expected {expected}, found {actual}")
        else:
            failures.append(f"{relative}: manifest declares no sha256 (run --update)")

    if args.update:
        if failures:
            print("Cannot update the manifest:", file=sys.stderr)
            for failure in failures:
                print(f"  {failure}", file=sys.stderr)
            return 1
        if changed:
            args.manifest.write_text(
                json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8"
            )
            print(f"updated {args.manifest} — bump contractVersion before committing")
        else:
            print("digests already match; nothing to update")
        return 0

    if failures:
        print(f"Shared knowledge artifacts do not match contract {version}:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        print(
            "\nIf this repository changed an artifact on purpose, run "
            "`python tools/knowledge/verify_shared_artifacts.py --update` and bump "
            "contractVersion in docs/KNOWLEDGE_CONTRACT.md and the manifest.\n"
            "If a consuming repository is being checked, its copies are behind this contract.",
            file=sys.stderr,
        )
        return 1

    print(f"{len(artifacts)} shared knowledge artifacts match contract {version}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
