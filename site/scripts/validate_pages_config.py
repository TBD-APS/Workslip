#!/usr/bin/env python3
"""Refuse Pages metadata outside Workslip's approved project-site address."""

from __future__ import annotations

import os
import sys
from collections.abc import Mapping

EXPECTED_ORIGIN = "https://tbd-aps.github.io"
EXPECTED_BASE_PATH = "/Workslip"
EXPECTED_BASE_URL = EXPECTED_ORIGIN + EXPECTED_BASE_PATH


def validate_pages_config(env: Mapping[str, str]) -> list[str]:
    failures: list[str] = []
    if env.get("PAGES_BASE_PATH") != EXPECTED_BASE_PATH:
        failures.append("GitHub Pages base path must be /Workslip; an empty root path is not approved.")
    if env.get("PAGES_ORIGIN") != EXPECTED_ORIGIN:
        failures.append("GitHub Pages origin must be https://tbd-aps.github.io.")
    if env.get("PAGES_BASE_URL") not in {EXPECTED_BASE_URL, EXPECTED_BASE_URL + "/"}:
        failures.append("GitHub Pages base URL must be https://tbd-aps.github.io/Workslip/.")
    return failures


def main(env: Mapping[str, str] | None = None) -> int:
    failures = validate_pages_config(os.environ if env is None else env)
    if failures:
        for failure in failures:
            print(f"ERROR: {failure}", file=sys.stderr)
        print(
            "Review Docs/operations/github-pages-domain-runbook.md. A legacy Pages custom "
            "domain can produce a root base path. An approved operator must inspect the "
            "repository Pages settings and clear only the legacy custom domain when "
            "authorized; keep GitHub Actions as the publishing source. Source changes "
            "do not change that setting. Preserve the company Worker, DNS, Azure hosts "
            "and existing release/environment approvals.",
            file=sys.stderr,
        )
        return 1
    print("Pages target is the approved Workslip project site.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
