#!/usr/bin/env python3
"""Regression coverage for Pages host ownership and its fail-closed diagnostics."""

from __future__ import annotations

import io
import unittest
from contextlib import redirect_stderr, redirect_stdout

from validate_pages_config import main, validate_pages_config

APPROVED = {
    "PAGES_BASE_PATH": "/Workslip",
    "PAGES_ORIGIN": "https://tbd-aps.github.io",
    "PAGES_BASE_URL": "https://tbd-aps.github.io/Workslip",
}


class PagesConfigTests(unittest.TestCase):
    def test_approved_project_site_with_optional_trailing_slash(self) -> None:
        for suffix in ("", "/"):
            with self.subTest(suffix=suffix):
                self.assertEqual(
                    validate_pages_config({**APPROVED, "PAGES_BASE_URL": APPROVED["PAGES_BASE_URL"] + suffix}),
                    [],
                )

    def test_missing_metadata_and_legacy_custom_domain_remain_blocked(self) -> None:
        for env in (
            {},
            {**APPROVED, "PAGES_BASE_PATH": ""},
            {
                "PAGES_BASE_PATH": "",
                "PAGES_ORIGIN": "https://www.mrsoftware.dk",
                "PAGES_BASE_URL": "https://www.mrsoftware.dk",
            },
        ):
            with self.subTest(env=env):
                output = io.StringIO()
                with redirect_stderr(output):
                    self.assertEqual(main(env), 1)
                self.assertIn("github-pages-domain-runbook.md", output.getvalue())
                self.assertIn("approved operator", output.getvalue())
                self.assertIn("base path must be /Workslip", output.getvalue())

    def test_correct_path_cannot_authorize_another_host_or_url(self) -> None:
        for patch in (
            {"PAGES_ORIGIN": "https://www.mrsoftware.dk"},
            {"PAGES_ORIGIN": "http://tbd-aps.github.io"},
            {"PAGES_BASE_URL": "https://www.mrsoftware.dk/Workslip"},
            {"PAGES_BASE_URL": "https://tbd-aps.github.io/Other"},
            {"PAGES_BASE_URL": "https://tbd-aps.github.io/Workslip?other=1"},
            {"PAGES_BASE_PATH": "/workslip"},
        ):
            with self.subTest(patch=patch):
                self.assertTrue(validate_pages_config({**APPROVED, **patch}))

    def test_unexpected_metadata_is_never_echoed_into_action_logs(self) -> None:
        marker = "UNEXPECTED_METADATA\\n::notice::INJECTED"
        output = io.StringIO()
        with redirect_stderr(output), redirect_stdout(output):
            self.assertEqual(main({key: marker for key in APPROVED}), 1)
        self.assertNotIn(marker, output.getvalue())
        self.assertNotIn("::notice::", output.getvalue())

    def test_success_exits_zero(self) -> None:
        with redirect_stdout(io.StringIO()):
            self.assertEqual(main(APPROVED), 0)


if __name__ == "__main__":
    unittest.main()
