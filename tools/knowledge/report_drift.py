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
from datetime import datetime, timezone
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


def implementation_facts(files: list[Path]) -> dict[str, dict[str, Any]]:
    """Literal facts only: no inference that a test passed or an auth scheme works at runtime."""
    facts: dict[str, dict[str, Any]] = {}
    for path in files:
        text = path.read_text(encoding="utf-8", errors="ignore")
        relative = path.relative_to(ROOT).as_posix()
        evidence = {"path": relative, "sha256": file_sha(path)}
        for match in MINIMAL_API_RE.finditer(text):
            key = "api:" + match.group("route")
            fact = facts.setdefault(key, {"values": [], "sources": []})
            fact["values"] = sorted(set(fact["values"] + [match.group("verb").upper()]))
            if evidence not in fact["sources"]:
                fact["sources"].append(evidence)
        # Enum claims name their namespace. Handle ordinary literal C# enums conservatively;
        # complex/generated declarations are left unknown, not guessed.
        namespace = re.search(r"\bnamespace\s+([\w.]+)", text)
        clean = re.sub(r"/\*.*?\*/|//[^\n]*", "", text, flags=re.S)
        for match in re.finditer(r"\benum\s+(\w+)\s*(?::\s*\w+)?\s*\{([^{}]*)\}", clean):
            members = [part.strip() for part in match[2].split(",") if part.strip()]
            if not all(re.fullmatch(r"[A-Za-z_]\w*(?:\s*=\s*-?(?:\d+|0x[0-9a-fA-F]+))?", value) for value in members):
                continue
            name = (namespace[1] + "." if namespace else "") + match[1]
            key = "enum:" + name
            # Two definitions cannot prove a unique declaration.
            if key in facts:
                facts[key]["ambiguous"] = True
            else:
                facts[key] = {"values": sorted(value.split("=", 1)[0].strip() for value in members), "sources": [evidence]}
    contract = ROOT / "src/MR.SAASy.ControlCenter/OpenApi/control-center.openapi.json"
    if contract.is_file():
        document = json.loads(contract.read_text(encoding="utf-8"))
        evidence = {"path": contract.relative_to(ROOT).as_posix(), "sha256": file_sha(contract)}
        for route, operations in document.get("paths", {}).items():
            methods = sorted(verb.upper() for verb in operations if verb.lower() in {"get", "post", "put", "patch", "delete", "head", "options"})
            facts.setdefault("api:" + route, {"values": methods, "sources": [evidence]})
            for verb in methods:
                security = operations[verb.lower()].get("security", document.get("security"))
                if isinstance(security, list):
                    # An empty requirement object is an anonymous alternative.
                    public = not security or any(item == {} for item in security)
                    facts[f"permission:{verb}:{route}"] = {"values": ["public" if public else "authenticated"], "sources": [evidence]}
    return facts


def explicit_claim_findings(docs: list[tuple[Path, dict[str, Any]]], facts: dict[str, dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    findings: list[dict[str, Any]] = []
    unknown: list[dict[str, Any]] = []
    for path, metadata in docs:
        if metadata.get("status") != "active":
            continue
        relative = path.relative_to(ROOT).as_posix()

        def emit(code: str, claim: str, observed: Any, sources: list[dict[str, Any]]) -> None:
            findings.append({"code": code, "document": relative, "knowledgeId": metadata.get("id"),
                "evidence": {"claim": claim, "observed": observed, "documentSha256": file_sha(path), "sources": sources},
                "confidence": 1.0, "reviewStatus": "pending",
                "proposedAction": "Review the explicit claim against its versioned implementation evidence; do not rewrite canonical knowledge automatically."})

        for field, code in (("code_claims", "DOC_CODE_CONTRADICTION"), ("permission_claims", "PERMISSION_MISMATCH")):
            for claim in metadata.get(field, []) if isinstance(metadata.get(field), list) else []:
                key, separator, raw = str(claim).partition("=")
                fact = facts.get(key)
                if not separator or fact is None or fact.get("ambiguous"):
                    unknown.append({"document": relative, "claim": claim, "reason": "No unambiguous extracted fact; not assessed."})
                    continue
                if sorted(set(raw.split("|"))) != fact["values"]:
                    emit(code, str(claim), fact["values"], fact["sources"])
        for claim in metadata.get("test_refs", []) if isinstance(metadata.get("test_refs"), list) else []:
            reference, separator, method = str(claim).partition("#")
            target = (ROOT / reference).resolve()
            if not separator or not method or not target.is_relative_to(ROOT) or not target.is_file():
                unknown.append({"document": relative, "claim": claim, "reason": "No local test file/symbol mapping; not assessed."})
                continue
            text = re.sub(r"/\*.*?\*/|//[^\n]*", "", target.read_text(encoding="utf-8"), flags=re.S)
            declaration = r"\b(?:void|Task(?:<[^>]+>)?|ValueTask(?:<[^>]+>)?)\s+" + re.escape(method) + r"\s*\("
            if not re.search(declaration, text):
                emit("DOC_TEST_CONTRADICTION", str(claim), "Referenced test method is absent from the current file.",
                     [{"path": reference, "sha256": file_sha(target)}])
        relations = metadata.get("relations") if isinstance(metadata.get("relations"), list) else []
        if metadata.get("type") in {"capability", "product-capability"} and not any(metadata.get(field) for field in ("code_refs", "code_owners", "api_refs")) and not any(
                str(edge).startswith("implemented_by:") for edge in relations):
            emit("UNMAPPED_CAPABILITY", "explicit implementation mapping", "No code/API owner or implemented_by relation.", [])
    return findings, unknown


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path)
    parser.add_argument("--include-undocumented-routes", action="store_true")
    parser.add_argument("--facts-output", type=Path, help="Metadata-only extracted facts and source digests.")
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
    facts = implementation_facts(files)
    explicit_findings, unknown_claims = explicit_claim_findings(docs, facts)
    findings.extend(explicit_findings)
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

    for finding in findings:
        finding.setdefault("reviewStatus", "pending")
    report = {
        "schemaVersion": 1,
        "repositorySha": git_sha(),
        "generatedAt": datetime.now(timezone.utc).isoformat(),
        "manifest": manifest_path.relative_to(ROOT).as_posix(),
        "product": manifest.get("product"),
        "structuredDocuments": len(docs),
        "implementationRoutes": len(routes),
        "findings": findings,
        "unknownClaims": unknown_claims,
    }
    if args.facts_output:
        args.facts_output.write_text(json.dumps({"schemaVersion": 1, "repositorySha": git_sha(), "facts": facts}, indent=2) + "\n", encoding="utf-8")
    rendered = json.dumps(report, indent=2, ensure_ascii=False) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
