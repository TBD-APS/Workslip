#!/usr/bin/env python3
"""Validate the repository knowledge/RAG corpus contract using only the stdlib."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from datetime import date
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[2]
KNOWLEDGE_CONTRACT_VERSION = "1.1.1"
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
    "code-reference",
    "code",
    "infrastructure",
}
REVIEWED_TYPES = {"product-capability", "problem", "bug", "agent-observation", "guide"}
ALLOWED_TYPES |= REVIEWED_TYPES
ALLOWED_REVIEW_STATUS = {"pending", "approved", "rejected"}
CAPABILITY_TYPES = {"capability", "product-capability"}
RELATION_TYPES = {
    "implemented_by": ({"capability"}, {"product-capability"}),
    "affects": ({"decision", "bug"}, CAPABILITY_TYPES | {"architecture", "release", "workflow", "api"}),
    "contains": ({"release"}, CAPABILITY_TYPES),
    "fixes": ({"release"}, {"bug"}),
    "supports": ({"agent-observation"}, ALLOWED_TYPES),
    "challenges": ({"agent-observation"}, ALLOWED_TYPES),
    "documents": ({"guide", "runbook", "workflow", "api"}, CAPABILITY_TYPES | {"architecture", "decision", "release"}),
    "motivates": ({"problem"}, CAPABILITY_TYPES),
}
ALLOWED_STATUS = {"active", "draft", "historical", "generated"}
ALLOWED_VISIBILITY = {"internal", "restricted", "public"}
ID_RE = re.compile(r"[a-z0-9][a-z0-9._-]{0,159}")
H1_RE = re.compile(r"^#\s+\S", re.MULTILINE)
# Keep these parser bounds aligned with AuthoredKnowledgePolicy.TryRead. Other
# authoring metadata (for example code_refs) is not interpreted by that runtime.
RUNTIME_METADATA_FIELDS = REQUIRED_METADATA_FIELDS | {"review_status", "source_refs", "relations"}
RUNTIME_LIST_FIELDS = {"audience", "source_refs", "relations"}
# .NET String.Trim/Char.IsWhiteSpace do not include Python's C0 separators 1C-1F.
RUNTIME_WHITESPACE = "\t\n\v\f\r \u0085\u00a0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000"


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


def utf16_length(value: str) -> int:
    """C# string.Length counts UTF-16 code units, including surrogate pairs."""
    return len(value.encode("utf-16-le")) // 2


def parse_runtime_scalar(raw: str) -> str:
    if not raw or utf16_length(raw) > 512 or raw[0] in "[{" or raw in {"null", "~"}:
        raise ValueError("Runtime metadata scalars must be non-null and at most 512 UTF-16 characters")
    if raw[0] in "'\"":
        if len(raw) < 2 or raw[-1] != raw[0]:
            raise ValueError("Runtime metadata quoted scalars must end with the matching quote")
        return raw[1:-1]
    return raw


def parse_runtime_value(key: str, raw: str) -> str | list[str]:
    value = raw.strip(RUNTIME_WHITESPACE)
    if key not in RUNTIME_LIST_FIELDS:
        return parse_runtime_scalar(value)
    if not value.startswith("[") or not value.endswith("]"):
        raise ValueError(f"{key} must be an inline list")
    if not value[1:-1].strip(RUNTIME_WHITESPACE):
        return []
    items = []
    start = 1
    quote = None
    first = True
    for index in range(1, len(value) - 1):
        character = value[index]
        if quote:
            if character == quote:
                quote = None
        elif character == ",":
            items.append(value[start:index])
            start = index + 1
            first = True
        elif first and character not in RUNTIME_WHITESPACE:
            quote = character if character in "'\"" else None
            first = False
    if quote:
        raise ValueError(f"{key} contains an unterminated quoted item")
    items.append(value[start:-1])
    if len(items) > 32:
        raise ValueError(f"{key} cannot contain more than 32 items")
    values = [parse_runtime_scalar(item.strip(RUNTIME_WHITESPACE)) for item in items]
    if any(not item.strip(RUNTIME_WHITESPACE) for item in values):
        raise ValueError(f"{key} cannot contain empty list items")
    return values


def parse_frontmatter(path: Path) -> tuple[dict[str, Any] | None, str]:
    # Read without universal-newline conversion: the runtime normalizes CRLF only.
    with path.open(encoding="utf-8", newline="") as source:
        text = source.read().replace("\r\n", "\n")
    if not text.startswith("---\n"):
        return None, text
    end = text.find("\n---\n", 4)
    if end < 0 or utf16_length(text[:end]) > 16_384:
        raise ValueError("Authored frontmatter must close within 16384 normalized UTF-16 characters")

    metadata: dict[str, Any] = {}
    for line in text[4:end].split("\n"):
        if not line or line[0] in RUNTIME_WHITESPACE or line.startswith("#"):
            continue
        if line.find(":") <= 0:
            continue
        key, raw = line.split(":", 1)
        key = key.strip(RUNTIME_WHITESPACE)
        if key in RUNTIME_METADATA_FIELDS:
            if key in metadata:
                raise ValueError(f"Duplicate runtime metadata field: {key}")
            metadata[key] = parse_runtime_value(key, raw)
        else:
            metadata[key] = parse_inline_value(raw)
    return metadata, text[end + 5:]


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


def validate_code_ref(ref: str) -> bool:
    if any(token in ref for token in ("*", "?", "[")):
        return any(ROOT.glob(ref))
    return (ROOT / ref).exists()


def should_exclude(path: Path, manifest: dict[str, Any]) -> bool:
    """Check if a path matches any exclude pattern from the manifest."""
    try:
        rel = path.relative_to(ROOT)
    except ValueError:
        return False

    rel_str = str(rel).replace("\\", "/")
    for pattern in manifest.get("exclude", []):
        # Convert glob pattern to regex
        regex_pattern = pattern.replace(".", r"\.").replace("**", ".*").replace("*", "[^/]*")
        if re.search(regex_pattern, rel_str):
            return True
    return False


def validate_knowledge_graph(documents: dict[str, tuple[Path, dict[str, Any]]]) -> tuple[int, list[dict[str, str]]]:
    """Validate authored edges by durable ID; no HTML, retrieval or provider is involved."""
    failures = 0
    edges = []
    visibility_rank = {"public": 0, "internal": 1, "restricted": 2}
    for knowledge_id, (path, metadata) in sorted(documents.items()):
        review = metadata.get("review_status")
        source_type = metadata.get("type")
        source_type = source_type if isinstance(source_type, str) else ""
        uses_graph = source_type in REVIEWED_TYPES or any(
            field in metadata for field in ("review_status", "source_refs", "relations")
        )
        if uses_graph:
            if not isinstance(metadata.get("owner"), str) or not metadata["owner"].strip(RUNTIME_WHITESPACE):
                fail(path, "Graph knowledge requires a non-empty owner")
                failures += 1
            if not isinstance(review, str) or review not in ALLOWED_REVIEW_STATUS:
                fail(path, "Graph knowledge requires review_status: pending, approved or rejected")
                failures += 1
            sources = metadata.get("source_refs")
            if not isinstance(sources, list) or not sources or any(
                not isinstance(source, str) or not source.strip(RUNTIME_WHITESPACE) for source in sources
            ):
                fail(path, "Graph knowledge requires non-empty source_refs as an inline list")
                failures += 1
            if review in ("pending", "rejected"):
                required_status = "draft" if review == "pending" else "historical"
                if metadata.get("status") != required_status or metadata.get("visibility") == "public":
                    fail(path, f"{review} knowledge must be {required_status} and non-public")
                    failures += 1

        relations = metadata.get("relations", [])
        if not isinstance(relations, list):
            fail(path, "relations must be an inline list of 'type:knowledge-id' strings")
            failures += 1
            continue
        seen = set()
        for relation in relations:
            kind, separator, target_id = relation.partition(":") if isinstance(relation, str) else ("", "", "")
            if not separator or kind not in RELATION_TYPES or not ID_RE.fullmatch(target_id):
                fail(path, f"Invalid typed relation: {relation!r}")
                failures += 1
                continue
            if relation in seen or target_id == knowledge_id:
                fail(path, f"Duplicate or self-referential relation: {relation!r}")
                failures += 1
                continue
            seen.add(relation)
            target = documents.get(target_id)
            if target is None:
                fail(path, f"Relation target is not in the validated corpus: {target_id}")
                failures += 1
                continue
            target_metadata = target[1]
            source_types, target_types = RELATION_TYPES[kind]
            if source_type not in source_types or str(target_metadata.get("type")) not in target_types:
                fail(path, f"Relation {kind!r} does not allow {metadata.get('type')!r} -> {target_metadata.get('type')!r}")
                failures += 1
            if review == "approved" and target_metadata.get("review_status") in ("pending", "rejected"):
                fail(path, f"Approved knowledge cannot promote an unapproved target: {target_id}")
                failures += 1
            source_visibility = visibility_rank.get(str(metadata.get("visibility")), -1)
            target_visibility = visibility_rank.get(str(target_metadata.get("visibility")), -1)
            if source_visibility < target_visibility:
                fail(path, f"Relation target has narrower visibility than its source: {target_id}")
                failures += 1
            edges.append({"source": knowledge_id, "type": kind, "target": target_id})
    return failures, edges


def validate_structured_docs(manifest: dict[str, Any], *, graph: dict[str, Any] | None = None) -> int:
    if graph is not None:
        graph.clear()
    failures = 0
    product = str(manifest["product"])
    seen_ids: dict[str, Path] = {}
    documents: dict[str, tuple[Path, dict[str, Any]]] = {}
    checked = 0

    frontmatter_required_globs = manifest.get("frontmatterRequired", ["docs/**/*.md", "src/**/README.md"])

    for raw_prefix in manifest.get("metadataRequiredPrefixes", []):
        prefix = ROOT / raw_prefix
        if not prefix.exists():
            continue
        for path in sorted(prefix.rglob("*.md")):
            if should_exclude(path, manifest):
                continue
            checked += 1
            try:
                metadata, body = parse_frontmatter(path)
            except ValueError as exc:
                fail(path, f"Invalid authored frontmatter: {exc}")
                failures += 1
                continue

            # Check if this file requires frontmatter
            rel = path.relative_to(ROOT)
            rel_str = str(rel).replace("\\", "/")
            requires_fm = False
            for pattern in frontmatter_required_globs:
                regex = pattern.replace(".", r"\.").replace("**", ".*").replace("*", "[^/]*")
                if re.search(regex, rel_str):
                    requires_fm = True
                    break

            if metadata is None:
                if requires_fm:
                    fail(path, "Structured knowledge document must start with YAML frontmatter")
                    failures += 1
                continue

            missing = sorted(REQUIRED_METADATA_FIELDS - metadata.keys())
            if missing:
                fail(path, f"Missing knowledge metadata fields: {', '.join(missing)}")
                failures += 1

            knowledge_id = str(metadata.get("id", ""))
            if not ID_RE.fullmatch(knowledge_id):
                fail(path, f"Invalid durable knowledge id: {knowledge_id!r}")
                failures += 1
            elif knowledge_id in seen_ids:
                fail(path, f"Duplicate knowledge id also used by {seen_ids[knowledge_id].relative_to(ROOT)}")
                failures += 1
            else:
                seen_ids[knowledge_id] = path
                documents[knowledge_id] = (path, metadata)

            if metadata.get("product") != product:
                fail(path, f"product must match manifest product {product!r}")
                failures += 1
            if not ID_RE.fullmatch(str(metadata.get("product", ""))):
                fail(path, "product must be a valid identifier of at most 160 characters")
                failures += 1
            if not isinstance(metadata.get("owner"), str) or not metadata["owner"].strip(RUNTIME_WHITESPACE):
                fail(path, "owner must be a non-empty scalar")
                failures += 1
            if not isinstance(metadata.get("type"), str) or metadata["type"] not in ALLOWED_TYPES:
                fail(path, f"Unknown knowledge type {metadata.get('type')!r}")
                failures += 1
            if not isinstance(metadata.get("status"), str) or metadata["status"] not in ALLOWED_STATUS:
                fail(path, f"Unknown status {metadata.get('status')!r}")
                failures += 1
            if not isinstance(metadata.get("visibility"), str) or metadata["visibility"] not in ALLOWED_VISIBILITY:
                fail(path, f"Unknown visibility {metadata.get('visibility')!r}")
                failures += 1

            audience = metadata.get("audience")
            if not isinstance(audience, list) or not audience:
                fail(path, "audience must be a non-empty inline list")
                failures += 1

            try:
                reviewed = str(metadata.get("last_reviewed", ""))
                if not re.fullmatch(r"[0-9]{4}-[0-9]{2}-[0-9]{2}", reviewed):
                    raise ValueError("Date format differs from runtime yyyy-MM-dd")
                date.fromisoformat(reviewed)
            except ValueError:
                fail(path, "last_reviewed must be an ISO date (YYYY-MM-DD)")
                failures += 1

            code_refs = metadata.get("code_refs", [])
            if code_refs and not isinstance(code_refs, list):
                fail(path, "code_refs must be an inline list")
                failures += 1
            elif isinstance(code_refs, list):
                for ref in code_refs:
                    if ref and not validate_code_ref(str(ref)):
                        fail(path, f"Stale code_ref does not resolve: {ref}")
                        failures += 1

            if not H1_RE.search(body):
                fail(path, "Structured knowledge document must contain an H1 heading after frontmatter")
                failures += 1

    graph_failures, edges = validate_knowledge_graph(documents)
    failures += graph_failures
    if graph is not None and failures == 0:
        graph.update({
            "contractVersion": KNOWLEDGE_CONTRACT_VERSION,
            "product": product,
            "source": manifest.get("source"),
            "tenant": manifest.get("tenant"),
            "nodes": [{
                "id": knowledge_id,
                "type": metadata["type"],
                "path": path.relative_to(ROOT).as_posix(),
                "checksum": hashlib.sha256(path.read_bytes()).hexdigest(),
                "status": metadata["status"],
                "reviewStatus": metadata.get("review_status"),
                "visibility": metadata["visibility"],
                "owner": metadata["owner"],
                "sourceRefs": metadata.get("source_refs", []),
            } for knowledge_id, (path, metadata) in sorted(documents.items())],
            "relations": edges,
        })
    print(f"Validated {checked} structured knowledge document(s) and {len(edges)} typed relation(s).")
    return failures


# Operating pages that describe the live MOTOR roster. Historical ADRs may name a
# retired seat only when they also say the catalog no longer has it. The desk
# archive and git-handle lists are not in this set.
OPERATING_ROSTER_DOCS = (
    "docs/architecture/motor-v1-foundation.md",
    "docs/architecture/agent-role-binding.md",
    "docs/architecture/structurizr-mcp.md",
    "docs/architecture/opencode-runtime.md",
    "docs/architecture/security-boundary.md",
    "docs/prd/agent-access-collaboration.md",
    "docs/prd/motor-orchestration.md",
    "docs/runtime/temporal-execution.md",
    "docs/control-center.md",
    "docs/architecture/agent-access-center.md",
)
OPERATING_ROSTER_REQUIRED = {
    "docs/architecture/motor-v1-foundation.md": (
        "ten agents bound to seven roles",
        "are not seats",
        "version 8",
    ),
    "docs/architecture/agent-role-binding.md": (
        "Ten agents, seven roles",
        "are not in the catalog",
    ),
    "docs/architecture/structurizr-mcp.md": ("Librarian (Cerebras)", "version 8"),
    "docs/architecture/opencode-runtime.md": ("Codex, Claude and Kimi hold this grant",),
    "docs/architecture/security-boundary.md": ("Self-appointed review or approval identity",),
    "docs/prd/agent-access-collaboration.md": (
        "10 agents bound to 7 roles",
        "are not seats",
        "version 8",
    ),
    "docs/prd/motor-orchestration.md": (
        "10 agents bound to 7 roles",
        "are not seats",
        "version 8",
    ),
    "docs/runtime/temporal-execution.md": ("no Critic seat",),
    "docs/control-center.md": ("Narrator and Grok", "agent:codex"),
    "docs/architecture/agent-access-center.md": ("ten catalog agents", "are not seats"),
}
# Phrases that put a removed seat back into the operating roster. Naming the
# seats in "are not seats" is allowed; these are the sentences that treat them
# as current members.
OPERATING_ROSTER_FORBIDDEN = (
    "14 agents",
    "fourteen agents",
    "Fourteen agents",
    "ten roles",
    "10 roles",
    "Atlas / Beacon",
    "Critic and Grok",
    "Forge review/approval",
    "agent:Forge",
    "Forge has this grant",
    "Forge for product",
    "Dommeren for experiments",
    "AdversarialCritic",
    "Critic for adversarial",
    "stay off the team until",
    "fourteen catalog",
    "six existing SASSY specialists",
)
JUDGE_DECISION = "docs/adr/ADR-009-branch-experiments-and-independent-judge.md"
JUDGE_CURRENT = "The catalog no longer has a judge seat"


def validate_operating_roster() -> int:
    failures = 0
    for relative in OPERATING_ROSTER_DOCS:
        path = ROOT / relative
        if not path.is_file():
            fail(path, "Operating roster document is missing")
            failures += 1
            continue
        text = path.read_text(encoding="utf-8")
        for phrase in OPERATING_ROSTER_FORBIDDEN:
            if phrase in text:
                fail(path, f"Operating roster must not say {phrase!r}")
                failures += 1
        for phrase in OPERATING_ROSTER_REQUIRED[relative]:
            if phrase not in text:
                fail(path, f"Operating roster must say {phrase!r}")
                failures += 1

    decision = ROOT / JUDGE_DECISION
    if not decision.is_file():
        fail(decision, "Judge decision record is missing")
        return failures + 1
    decision_text = decision.read_text(encoding="utf-8")
    if JUDGE_CURRENT not in decision_text:
        fail(decision, f"Judge decision must say {JUDGE_CURRENT!r} so the retired seat is not restored from the record")
        failures += 1
    print(f"Validated operating roster in {len(OPERATING_ROSTER_DOCS)} document(s).")
    return failures


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", help="Path relative to repo root")
    parser.add_argument("--relations-json", type=Path, help="Write the validated authored graph as JSON (offline tooling, not an authorized retrieval API)")
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
    graph: dict[str, Any] = {}
    if manifest is not None:
        failures += validate_structured_docs(manifest, graph=graph)
        # The artifact is shared with product repositories. Platform roster rules
        # belong only to MR SAAS'y; consumers still run the full generic contract.
        if manifest.get("product") == "mr-saassy":
            failures += validate_operating_roster()

    if failures:
        print(f"Knowledge validation failed with {failures} error(s).")
        return 1

    if args.relations_json:
        args.relations_json.write_text(json.dumps(graph, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Knowledge validation passed: {manifest_path.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
