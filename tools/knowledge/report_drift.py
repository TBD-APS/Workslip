#!/usr/bin/env python3
"""Report deterministic documentation-to-code drift signals.

The first version intentionally stays conservative: explicit code_refs/api_refs are authority for joins;
semantic matching can be added later as supporting evidence, never as an automatic rewrite trigger.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
MINIMAL_API_RE = re.compile(r'\.Map(?P<verb>Get|Post|Put|Delete|Patch)\(\s*"(?P<route>/api/[^"?#]*)"')
HTTP_ATTRIBUTE_RE = re.compile(r'\[Http(?P<verb>Get|Post|Put|Delete|Patch)(?:\(\s*"(?P<route>[^"?#]*)"\s*\))?\]')


def parse_inline_value(raw: str) -> Any:
    value = raw.strip()
    if value.startswith("[") and value.endswith("]"):
        inner = value[1:-1].strip()
        if not inner:
            return []
        return [item.strip().strip("'\"") for item in inner.split(",")]
    return value.strip("'\"")


def frontmatter(path: Path) -> dict[str, Any]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0].strip() != "---":
        return {}
    result: dict[str, Any] = {}
    for line in lines[1:]:
        if line.strip() == "---":
            break
        if not line.strip() or line.startswith((" ", "\t")) or ":" not in line:
            continue
        key, raw = line.split(":", 1)
        result[key.strip()] = parse_inline_value(raw)
    return result


def git_sha() -> str | None:
    try:
        return subprocess.check_output(
            ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True, stderr=subprocess.DEVNULL
        ).strip()
    except (OSError, subprocess.CalledProcessError):
        return None


def file_sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def load_manifest() -> tuple[Path, dict[str, Any]]:
    candidates = [ROOT / "Docs" / "rag-manifest.json", ROOT / "docs" / "rag-manifest.json"]
    path = next(candidate for candidate in candidates if candidate.is_file())
    return path, json.loads(path.read_text(encoding="utf-8"))


def structured_documents(manifest: dict[str, Any]) -> list[tuple[Path, dict[str, Any]]]:
    result: list[tuple[Path, dict[str, Any]]] = []
    for prefix_raw in manifest.get("metadataRequiredPrefixes", []):
        prefix = ROOT / prefix_raw
        if not prefix.exists():
            continue
        for path in prefix.rglob("*.md"):
            metadata = frontmatter(path)
            if metadata:
                result.append((path, metadata))
    return sorted(result, key=lambda item: item[0].as_posix())


def source_files() -> list[Path]:
    # apps/ holds the prompt worker, whose /api/saassy routes are not Map* calls under src/.
    roots = [ROOT / "src", ROOT / "apps"]
    extensions = {".cs", ".ts", ".tsx", ".js", ".mjs"}
    ignored = {"bin", "obj", "node_modules", "dist", "artifacts"}
    files: list[Path] = []
    for source_root in roots:
        if not source_root.exists():
            continue
        for path in source_root.rglob("*"):
            if not path.is_file() or path.suffix.lower() not in extensions:
                continue
            if any(part in ignored for part in path.parts):
                continue
            files.append(path)
    return files


def openapi_routes() -> dict[str, list[str]]:
    """Routes composed from a MapGroup are real once the published contract names them."""
    path = ROOT / "src/MR.SAASy.ControlCenter/OpenApi/control-center.openapi.json"
    if not path.is_file():
        return {}
    document = json.loads(path.read_text(encoding="utf-8"))
    relative = path.relative_to(ROOT).as_posix()
    routes: dict[str, list[str]] = {}
    for route in document.get("paths", {}):
        if isinstance(route, str) and route.startswith("/api/"):
            routes.setdefault(route, []).append(relative)
    return routes


def implementation_routes(files: list[Path]) -> dict[str, list[str]]:
    routes: dict[str, list[str]] = {}
    for path in files:
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        relative = path.relative_to(ROOT).as_posix()
        for match in MINIMAL_API_RE.finditer(text):
            route = match.group("route")
            routes.setdefault(route, []).append(relative)
        # Attribute routes are fragments until controller prefixes are composed; keep them as evidence,
        # but only compare absolute /api fragments deterministically.
        for match in HTTP_ATTRIBUTE_RE.finditer(text):
            route = match.group("route")
            if route and route.startswith("/api/"):
                routes.setdefault(route, []).append(relative)
    return routes


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    parser.add_argument("--include-undocumented-routes", action="store_true")
    args = parser.parse_args()

    manifest_path, manifest = load_manifest()
    docs = structured_documents(manifest)
    files = source_files()
    routes = implementation_routes(files)
    for route, owners in openapi_routes().items():
        routes.setdefault(route, []).extend(owners)
    code_blob = "\n".join(
        path.read_text(encoding="utf-8", errors="ignore") for path in files
    )

    findings: list[dict[str, Any]] = []
    documented_api_refs: set[str] = set()

    for path, metadata in docs:
        relative = path.relative_to(ROOT).as_posix()
        for ref in metadata.get("code_refs", []) if isinstance(metadata.get("code_refs", []), list) else []:
            target = ROOT / str(ref)
            if not target.exists() and not any(ROOT.glob(str(ref))):
                findings.append(
                    {
                        "code": "STALE_DOC_REFERENCE",
                        "document": relative,
                        "knowledgeId": metadata.get("id"),
                        "evidence": {"codeRef": ref, "documentSha256": file_sha(path)},
                        "confidence": 1.0,
                        "proposedAction": "Review or replace the stale explicit code_refs mapping."
                    }
                )

        api_refs = metadata.get("api_refs", [])
        if isinstance(api_refs, list):
            for ref in api_refs:
                ref = str(ref)
                if not ref:
                    continue
                documented_api_refs.add(ref)
                if ref not in routes and ref not in code_blob:
                    findings.append(
                        {
                            "code": "DOCUMENTED_NOT_IMPLEMENTED",
                            "document": relative,
                            "knowledgeId": metadata.get("id"),
                            "evidence": {"apiRef": ref, "documentSha256": file_sha(path)},
                            "confidence": 0.95,
                            "proposedAction": "Verify whether the API moved, was removed, or the documentation is ahead of implementation."
                        }
                    )

    if args.include_undocumented_routes:
        for route, owners in sorted(routes.items()):
            if route not in documented_api_refs:
                findings.append(
                    {
                        "code": "IMPLEMENTED_NOT_DOCUMENTED",
                        "document": None,
                        "knowledgeId": None,
                        "evidence": {"apiRoute": route, "codeOwners": sorted(set(owners))},
                        "confidence": 0.75,
                        "proposedAction": "Map this route to an existing capability/API document or explicitly classify it as internal/non-documentable."
                    }
                )

    report = {
        "schemaVersion": 1,
        "repositorySha": git_sha(),
        "manifest": manifest_path.relative_to(ROOT).as_posix(),
        "product": manifest.get("product"),
        "structuredDocuments": len(docs),
        "implementationRoutes": len(routes),
        "findings": findings,
    }
    rendered = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
