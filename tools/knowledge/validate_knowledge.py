#!/usr/bin/env python3
"""Validate the repository knowledge/RAG corpus contract using only the stdlib."""

from __future__ import annotations

import argparse
import json
import re
import sys
from datetime import date
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
REQUIRED_MANIFEST_FIELDS = {
    "schemaVersion",
    "product",
    "source",
    "canonicalRoot",
    "include",
    "exclude",
    "metadataRequiredPrefixes",
}
REQUIRED_METADATA_FIELDS = {
    "id",
    "product",
    "type",
    "status",
    "owner",
    "visibility",
    "audience",
    "last_reviewed",
}
ALLOWED_TYPES = {
    "product",
    "capability",
    "workflow",
    "decision",
    "architecture",
    "api",
    "runbook",
    "compliance",
    "strategy",
    "release",
}
ALLOWED_STATUS = {"active", "draft", "historical", "generated"}
ALLOWED_VISIBILITY = {"internal", "restricted", "public"}
ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]*$")
H1_RE = re.compile(r"^#\s+\S", re.MULTILINE)


def fail(path: Path, message: str) -> None:
    try:
        relative = path.relative_to(ROOT)
    except ValueError:
        relative = path
    print(f"::error file={relative.as_posix()}::{message}")
    print(f"{relative.as_posix()}: {message}")


def parse_inline_value(raw: str) -> Any:
    value = raw.strip()
    if value.startswith("[") and value.endswith("]"):
        inner = value[1:-1].strip()
        if not inner:
            return []
        return [item.strip().strip("'\"") for item in inner.split(",")]
    if value in {"true", "false"}:
        return value == "true"
    if value in {"null", "~"}:
        return None
    return value.strip("'\"")


def parse_frontmatter(path: Path) -> tuple[dict[str, Any] | None, str]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    if not lines or lines[0].strip() != "---":
        return None, text

    metadata: dict[str, Any] = {}
    end = None
    for index in range(1, len(lines)):
        line = lines[index]
        if line.strip() == "---":
            end = index
            break
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if line.startswith((" ", "\t")):
            # The v1 contract deliberately keeps frontmatter flat and inline.
            continue
        if ":" not in line:
            continue
        key, raw = line.split(":", 1)
        metadata[key.strip()] = parse_inline_value(raw)

    if end is None:
        return None, text
    return metadata, "\n".join(lines[end + 1 :])


def validate_manifest(path: Path) -> tuple[dict[str, Any] | None, int]:
    failures = 0
    try:
        manifest = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(path, f"Cannot read valid JSON manifest: {exc}")
        return None, 1

    missing = sorted(REQUIRED_MANIFEST_FIELDS - manifest.keys())
    if missing:
        fail(path, f"Missing manifest fields: {', '.join(missing)}")
        failures += 1

    if manifest.get("schemaVersion") != 1:
        fail(path, "schemaVersion must currently be 1")
        failures += 1

    canonical = ROOT / str(manifest.get("canonicalRoot", ""))
    if not canonical.is_dir():
        fail(path, f"canonicalRoot does not exist: {canonical.relative_to(ROOT)}")
        failures += 1

    html = manifest.get("htmlProjection", {})
    if html.get("indexGeneratedHtml") is False:
        html_patterns = [pattern for pattern in manifest.get("include", []) if ".html" in pattern]
        if html_patterns:
            fail(path, "Generated HTML is configured as non-indexed but include contains HTML patterns")
            failures += 1

    canonical_name = str(manifest.get("canonicalRoot", ""))
    excluded_roots = set(manifest.get("excludedRoots", []))
    for sibling in ROOT.iterdir():
        if not sibling.is_dir() or sibling.name == canonical_name:
            continue
        if sibling.name.casefold() == canonical_name.casefold() and sibling.name not in excluded_roots:
            fail(path, f"Case-variant documentation root {sibling.name!r} must be explicitly excluded or migrated")
            failures += 1

    return manifest, failures


def validate_code_ref(path: Path, ref: str) -> bool:
    if any(token in ref for token in ("*", "?", "[")):
        return any(ROOT.glob(ref))
    return (ROOT / ref).exists()


def validate_structured_docs(manifest: dict[str, Any]) -> int:
    failures = 0
    product = str(manifest["product"])
    seen_ids: dict[str, Path] = {}
    checked = 0

    for raw_prefix in manifest.get("metadataRequiredPrefixes", []):
        prefix = ROOT / raw_prefix
        if not prefix.exists():
            continue
        for path in sorted(prefix.rglob("*.md")):
            checked += 1
            metadata, body = parse_frontmatter(path)
            if metadata is None:
                fail(path, "Structured knowledge document must start with YAML frontmatter")
                failures += 1
                continue

            missing = sorted(REQUIRED_METADATA_FIELDS - metadata.keys())
            if missing:
                fail(path, f"Missing knowledge metadata fields: {', '.join(missing)}")
                failures += 1

            knowledge_id = str(metadata.get("id", ""))
            if not ID_RE.match(knowledge_id):
                fail(path, f"Invalid durable knowledge id: {knowledge_id!r}")
                failures += 1
            elif knowledge_id in seen_ids:
                fail(path, f"Duplicate knowledge id also used by {seen_ids[knowledge_id].relative_to(ROOT)}")
                failures += 1
            else:
                seen_ids[knowledge_id] = path

            if metadata.get("product") != product:
                fail(path, f"product must match manifest product {product!r}")
                failures += 1

            if metadata.get("type") not in ALLOWED_TYPES:
                fail(path, f"Unknown knowledge type {metadata.get('type')!r}")
                failures += 1
            if metadata.get("status") not in ALLOWED_STATUS:
                fail(path, f"Unknown status {metadata.get('status')!r}")
                failures += 1
            if metadata.get("visibility") not in ALLOWED_VISIBILITY:
                fail(path, f"Unknown visibility {metadata.get('visibility')!r}")
                failures += 1

            audience = metadata.get("audience")
            if not isinstance(audience, list) or not audience:
                fail(path, "audience must be a non-empty inline list")
                failures += 1

            try:
                date.fromisoformat(str(metadata.get("last_reviewed", "")))
            except ValueError:
                fail(path, "last_reviewed must be an ISO date (YYYY-MM-DD)")
                failures += 1

            code_refs = metadata.get("code_refs", [])
            if code_refs and not isinstance(code_refs, list):
                fail(path, "code_refs must be an inline list")
                failures += 1
            elif isinstance(code_refs, list):
                for ref in code_refs:
                    if ref and not validate_code_ref(path, str(ref)):
                        fail(path, f"Stale code_ref does not resolve: {ref}")
                        failures += 1

            if not H1_RE.search(body):
                fail(path, "Structured knowledge document must contain an H1 heading after frontmatter")
                failures += 1

    print(f"Validated {checked} structured knowledge document(s).")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", help="Path relative to repo root")
    args = parser.parse_args()

    if args.manifest:
        manifest_path = ROOT / args.manifest
    else:
        candidates = [ROOT / "Docs" / "rag-manifest.json", ROOT / "docs" / "rag-manifest.json"]
        manifest_path = next((candidate for candidate in candidates if candidate.is_file()), candidates[0])

    if not manifest_path.is_file():
        fail(manifest_path, "Knowledge corpus manifest is missing")
        return 1

    manifest, failures = validate_manifest(manifest_path)
    if manifest is not None:
        failures += validate_structured_docs(manifest)

    if failures:
        print(f"Knowledge validation failed with {failures} error(s).")
        return 1

    print(f"Knowledge validation passed: {manifest_path.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
