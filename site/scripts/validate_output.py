#!/usr/bin/env python3
"""Validate generated Jekyll output using only the Python standard library."""

from __future__ import annotations

import sys
from html.parser import HTMLParser
from pathlib import Path
from urllib.parse import unquote, urlsplit

REQUIRED_OUTPUTS = (
    "index.html",
    "404.html",
    "robots.txt",
    "sitemap.xml",
    "features/index.html",
    "demo/index.html",
    "security/index.html",
    "privacy/index.html",
    "terms/index.html",
    "status/index.html",
    "changelog/index.html",
)


class PageParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.links: list[str] = []
        self.h1_count = 0
        self.main_ids: list[str | None] = []

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        values = dict(attrs)
        if tag == "h1":
            self.h1_count += 1
        if tag == "main":
            self.main_ids.append(values.get("id"))
        if tag in {"a", "link"} and values.get("href"):
            self.links.append(values["href"] or "")
        if tag in {"img", "script", "iframe"} and values.get("src"):
            self.links.append(values["src"] or "")


def is_external(destination: str) -> bool:
    parsed = urlsplit(destination)
    return bool(parsed.scheme or destination.startswith("//"))


def strip_baseurl(local_path: str, baseurl: str) -> str:
    normalized = "/" + baseurl.strip("/") if baseurl.strip("/") else ""
    if not normalized:
        return local_path
    if local_path == normalized:
        return "/"
    if local_path.startswith(f"{normalized}/"):
        return local_path[len(normalized) :]
    return local_path


def resolve_local_target(
    root: Path,
    source: Path,
    destination: str,
    baseurl: str,
) -> Path | None:
    if not destination or destination.startswith("#") or is_external(destination):
        return None

    parsed = urlsplit(destination)
    local_path = strip_baseurl(unquote(parsed.path), baseurl)
    if not local_path:
        return None

    candidate = root / local_path.lstrip("/") if local_path.startswith("/") else source.parent / local_path
    candidate = candidate.resolve()

    try:
        candidate.relative_to(root.resolve())
    except ValueError:
        return candidate

    if candidate.is_dir():
        return candidate / "index.html"
    if candidate.exists():
        return candidate
    if candidate.suffix == "":
        return candidate / "index.html"
    return candidate


def validate_page(root: Path, page: Path, baseurl: str) -> list[str]:
    parser = PageParser()
    parser.feed(page.read_text(encoding="utf-8"))
    relative = page.relative_to(root).as_posix()
    failures: list[str] = []

    if parser.h1_count != 1:
        failures.append(f"{relative}: expected exactly one h1, found {parser.h1_count}")
    if parser.main_ids != ["main-content"]:
        failures.append(
            f"{relative}: expected exactly one <main id=\"main-content\">, found {parser.main_ids}"
        )

    for destination in parser.links:
        target = resolve_local_target(root, page, destination, baseurl)
        if target is not None and not target.exists():
            failures.append(f"{relative}: broken local reference {destination!r}")

    return failures


def main() -> int:
    root = Path(sys.argv[1] if len(sys.argv) > 1 else "_site").resolve()
    baseurl = sys.argv[2] if len(sys.argv) > 2 else ""
    failures: list[str] = []

    if not root.is_dir():
        print(f"Generated site directory does not exist: {root}", file=sys.stderr)
        return 1

    for relative in REQUIRED_OUTPUTS:
        if not (root / relative).is_file():
            failures.append(f"missing required output: {relative}")

    for page in sorted(root.rglob("*.html")):
        failures.extend(validate_page(root, page, baseurl))

    if failures:
        for failure in failures:
            print(f"ERROR: {failure}", file=sys.stderr)
        print(f"Site output validation failed with {len(failures)} error(s).", file=sys.stderr)
        return 1

    page_count = sum(1 for _ in root.rglob("*.html"))
    print(f"Site output validation passed for {page_count} HTML page(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
