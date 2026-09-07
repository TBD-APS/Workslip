#!/usr/bin/env python3
"""Render the Workslip agent instruction set into one self-contained HTML page.

The generated page is a consumption artifact for LLM agents and humans:
one file, no JavaScript, no external assets, deterministic output. The
markdown documents remain the only source of truth; this generator never
becomes one.

Usage:
    python tools/docs/build_agent_docs_html.py            # (re)generate
    python tools/docs/build_agent_docs_html.py --check    # exit 1 on drift
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
import unicodedata
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
TEMPLATE_PATH = Path(__file__).resolve().parent / "template" / "agent_docs.template.html"
OUTPUT_PATH = ROOT / "Docs" / "agents" / "agent-docs.html"

GENERATOR_NAME = "tools/docs/build_agent_docs_html.py"
GENERATOR_VERSION = 2
PAGE_TITLE = "Workslip agent documentation"
PAGE_INTRO = (
    "Self-contained rendering of the Workslip agent instruction set. "
    "The embedded JSON manifest (id: agent-docs-manifest) lists every source "
    "document with its content hash for freshness verification."
)

# Ordered bootstrap instruction set: root rules first, then scoped rules,
# then the required handbook documents from AGENT_CONTEXT_MANIFEST.json.
DOCUMENTS = (
    ("AGENTS.md", "root"),
    ("Docs/AGENTS.md", "directory"),
    ("src/FE/AGENTS.md", "directory"),
    ("src/BE/WorkslipApi/AGENTS.md", "directory"),
    ("src/BE/infrastructure/AGENTS.md", "directory"),
    ("Docs/agents/AGENT_HANDBOOK.md", "handbook"),
    ("Docs/agents/CONTROL_CENTER_OPERATING_MODEL.md", "handbook"),
)

HEADING_RE = re.compile(r"^(#{1,4})\s+(.+?)\s*#*\s*$")
FENCE_OPEN_RE = re.compile(r"^(\s*)(`{3,}|~{3,})\s*(\S*)\s*$")
FENCE_CLOSE_RE = re.compile(r"^\s*(`{3,}|~{3,})\s*$")
TABLE_ROW_RE = re.compile(r"^\s*\|.*\|\s*$")
TABLE_DIVIDER_RE = re.compile(r"^\s*\|?\s*:?-{3,}.*\|.*$")
UL_ITEM_RE = re.compile(r"^(\s*)[-*]\s+(.+)$")
OL_ITEM_RE = re.compile(r"^(\s*)(\d+)[.)]\s+(.+)$")
LIST_ITEM_RE = re.compile(r"^(\s*)(?:[-*]|\d+[.)])\s+(.+)$")
HR_RE = re.compile(r"^\s*(?:-{3,}|\*{3,})\s*$")
STATE_RE = re.compile(r"\*\*(?:State|Status):\*\*\s*(\w+)")
IMAGE_RE = re.compile(r"!\[([^\]]*)]\(([^)\s]+)\)")


def slugify(text: str) -> str:
    folded = (
        unicodedata.normalize("NFKD", text)
        .encode("ascii", "ignore")
        .decode("ascii")
        .lower()
    )
    folded = folded.replace("æ", "ae").replace("ø", "oe").replace("å", "aa")
    slug = re.sub(r"[^a-z0-9]+", "-", folded).strip("-")
    return slug or "section"


def escape_html(text: str) -> str:
    return (
        text.replace("&", "&amp;")
        .replace("<", "&lt;")
        .replace(">", "&gt;")
        .replace('"', "&quot;")
    )


def split_table_row(line: str) -> list[str]:
    """Split a markdown table row on pipes, respecting code spans and escapes."""
    stripped = line.strip().strip("|")
    cells: list[str] = []
    current: list[str] = []
    in_code = False
    index = 0
    while index < len(stripped):
        char = stripped[index]
        if char == "\\" and index + 1 < len(stripped) and stripped[index + 1] == "|":
            current.append("|")
            index += 2
            continue
        if char == "`":
            in_code = not in_code
        if char == "|" and not in_code:
            cells.append("".join(current).strip())
            current = []
        else:
            current.append(char)
        index += 1
    cells.append("".join(current).strip())
    return cells


def fence_aware_lines(lines: list[str]):
    """Yield (line, in_fence) so metadata scanners skip fenced content."""
    fence_marker = None
    for line in lines:
        if fence_marker is None:
            opening = FENCE_OPEN_RE.match(line)
            if opening:
                fence_marker = opening.group(2)[0]
                yield line, True
            else:
                yield line, False
        else:
            closing = FENCE_CLOSE_RE.match(line)
            if closing and closing.group(1)[0] == fence_marker:
                fence_marker = None
            yield line, True


class DocumentRenderer:
    """Render one markdown document to semantic HTML with stable anchors."""

    def __init__(self, element_id: str, resolve_link=None):
        self.element_id = element_id
        self.resolve_link = resolve_link
        self.anchor_counts: dict[str, int] = {}
        self.anchors: dict[str, str] = {}
        self.heading_ids_by_slug: dict[str, str] = {}

    def anchor_base(self, text: str) -> str:
        return f"{self.element_id}-{slugify(text)}"

    def anchor_for(self, text: str) -> str:
        base = self.anchor_base(text)
        count = self.anchor_counts.get(base, 0)
        self.anchor_counts[base] = count + 1
        anchor = base if count == 0 else f"{base}-{count + 1}"
        self.anchors[anchor] = text
        return anchor

    def prescan_headings(self, markdown: str) -> None:
        """Register heading anchors before rendering so fragment links resolve."""
        for line, in_fence in fence_aware_lines(markdown.splitlines()):
            if in_fence:
                continue
            heading = HEADING_RE.match(line)
            if heading:
                self.anchor_for(heading.group(2))

    def inline(self, text: str) -> str:
        text = IMAGE_RE.sub(lambda m: f"(image: {m.group(1)})", text)
        escaped = escape_html(text)

        # Protect inline code spans from further substitution.
        code_spans: list[str] = []

        def stash(match: re.Match) -> str:
            code_spans.append(f"<code>{match.group(1)}</code>")
            return f"\x00{len(code_spans) - 1}\x00"

        escaped = re.sub(r"`([^`]+)`", stash, escaped)

        escaped = re.sub(
            r"&lt;(https?://[^&\s]+)&gt;",
            lambda m: f'<a href="{m.group(1)}">{m.group(1)}</a>',
            escaped,
        )
        escaped = re.sub(
            r"\[([^\]]+)\]\(([^)\s]+)\)",
            lambda m: f'<a href="{self.link_target(m.group(2))}">{m.group(1)}</a>',
            escaped,
        )
        escaped = re.sub(r"\*\*([^*]+)\*\*", r"<strong>\1</strong>", escaped)
        escaped = re.sub(r"(?<!\*)\*([^*\s][^*]*)\*(?!\*)", r"<em>\1</em>", escaped)

        return re.sub(r"\x00(\d+)\x00", lambda m: code_spans[int(m.group(1))], escaped)

    def link_target(self, target: str) -> str:
        if target.startswith("#"):
            slug = target[1:]
            mapped = self.heading_ids_by_slug.get(slug)
            return mapped if mapped else target
        if self.resolve_link is None:
            return target
        return self.resolve_link(target)

    def render(self, markdown: str) -> str:
        return "\n".join(self.render_blocks(markdown.splitlines()))

    def render_blocks(self, lines: list[str]) -> list[str]:
        out: list[str] = []
        index = 0
        while index < len(lines):
            line = lines[index]

            opening = FENCE_OPEN_RE.match(line)
            if opening:
                language, body, index = self._consume_fence(lines, index)
                css = f' class="language-{escape_html(language)}"' if language else ""
                out.append(f"<pre><code{css}>{escape_html(body)}</code></pre>")
                continue

            heading = HEADING_RE.match(line)
            if heading:
                level = min(len(heading.group(1)) + 1, 5)
                text = heading.group(2)
                anchor = self.anchor_for(text)
                out.append(f'<h{level} id="{anchor}">{self.inline(text)}</h{level}>')
                index += 1
                continue

            if TABLE_ROW_RE.match(line) and index + 1 < len(lines) and TABLE_DIVIDER_RE.match(lines[index + 1]):
                table_html, index = self._consume_table(lines, index)
                out.append(table_html)
                continue

            if re.match(r"^\s*>", line):
                quote_lines = []
                while index < len(lines) and re.match(r"^\s*>", lines[index]):
                    quote_lines.append(re.sub(r"^\s*>\s?", "", lines[index]))
                    index += 1
                inner = "\n".join(self.render_blocks(quote_lines))
                out.append(f"<blockquote>{inner}</blockquote>")
                continue

            if LIST_ITEM_RE.match(line):
                list_html, index = self._consume_list(lines, index)
                out.append(list_html)
                continue

            if HR_RE.match(line):
                out.append("<hr>")
                index += 1
                continue

            if not line.strip():
                index += 1
                continue

            paragraph_lines = [line]
            index += 1
            while index < len(lines):
                nxt = lines[index]
                if (
                    not nxt.strip()
                    or HEADING_RE.match(nxt)
                    or FENCE_OPEN_RE.match(nxt)
                    or LIST_ITEM_RE.match(nxt)
                    or re.match(r"^\s*>", nxt)
                    or TABLE_ROW_RE.match(nxt)
                ):
                    break
                paragraph_lines.append(nxt)
                index += 1
            text = " ".join(part.strip() for part in paragraph_lines)
            out.append(f"<p>{self.inline(text)}</p>")

        return out

    def _consume_fence(self, lines: list[str], start: int) -> tuple[str, str, int]:
        opener = FENCE_OPEN_RE.match(lines[start])
        marker = opener.group(2)
        language = opener.group(3)
        body: list[str] = []
        index = start + 1
        while index < len(lines):
            closer = FENCE_CLOSE_RE.match(lines[index])
            if closer and closer.group(1)[0] == marker[0] and len(closer.group(1)) >= len(marker):
                return language, "\n".join(body), index + 1
            body.append(lines[index])
            index += 1
        raise ValueError(f"Unclosed fenced code block in {self.element_id}")

    def _consume_table(self, lines: list[str], start: int) -> tuple[str, int]:
        header = split_table_row(lines[start])
        index = start + 2
        rows: list[list[str]] = []
        while index < len(lines) and TABLE_ROW_RE.match(lines[index]):
            rows.append(split_table_row(lines[index]))
            index += 1
        head_cells = "".join(f'<th scope="col">{self.inline(cell)}</th>' for cell in header)
        body_rows = "".join(
            "<tr>" + "".join(f"<td>{self.inline(cell)}</td>" for cell in row) + "</tr>"
            for row in rows
        )
        return f"<table><thead><tr>{head_cells}</tr></thead><tbody>{body_rows}</tbody></table>", index

    def _consume_list(self, lines: list[str], start: int) -> tuple[str, int]:
        items: list[tuple[int, bool, str]] = []

        def item_level(raw_indent: str) -> int:
            width = len(raw_indent.expandtabs(2))
            return 0 if width < 2 else (1 if width < 4 else 2)

        index = start
        while index < len(lines):
            line = lines[index]
            ul_match = UL_ITEM_RE.match(line)
            ol_match = OL_ITEM_RE.match(line)
            if not ul_match and not ol_match:
                if not line.strip():
                    peek = index + 1
                    if peek < len(lines) and LIST_ITEM_RE.match(lines[peek]):
                        index += 1
                        continue
                break
            ordered = ol_match is not None
            content = ol_match.group(3) if ol_match else ul_match.group(2)
            items.append((item_level((ol_match or ul_match).group(1)), ordered, content))
            index += 1

        html: list[str] = []
        stack: list[dict] = []  # open lists: {"level", "ordered", "liOpen"}

        def close_top() -> None:
            top = stack.pop()
            if top["liOpen"]:
                html.append("</li>")
            html.append("</ol>" if top["ordered"] else "</ul>")

        for level, ordered, content in items:
            # Close deeper lists so the depth-`level` list is topmost.
            while len(stack) > level + 1:
                close_top()
            if len(stack) == level + 1 and stack[-1]["ordered"] != ordered:
                close_top()
            # Open missing levels (first item here, or indentation jump > 1).
            # A deeper list nests inside the still-open parent <li>.
            while len(stack) < level + 1:
                stack.append({"level": len(stack), "ordered": ordered, "liOpen": False})
                html.append("<ol>" if ordered else "<ul>")
            if stack[-1]["liOpen"]:
                html.append("</li>")

            html.append(f"<li>{self.inline(content)}")
            stack[-1]["liOpen"] = True

        while stack:
            close_top()
        return "".join(html), index


def document_title(markdown: str) -> str:
    for line, in_fence in fence_aware_lines(markdown.splitlines()):
        if in_fence:
            continue
        match = HEADING_RE.match(line)
        if match and len(match.group(1)) == 1:
            return match.group(2).strip()
    raise ValueError("Document has no H1 heading")


def strip_title_line(markdown: str) -> str:
    removed = False
    kept: list[str] = []
    for line, in_fence in fence_aware_lines(markdown.splitlines()):
        if not removed and not in_fence and HEADING_RE.match(line) and len(HEADING_RE.match(line).group(1)) == 1:
            removed = True
            continue
        kept.append(line)
    return "\n".join(kept)


def document_state(markdown: str) -> str | None:
    for line, in_fence in fence_aware_lines(markdown.splitlines()[:20]):
        if in_fence:
            continue
        match = STATE_RE.search(line)
        if match:
            return match.group(1)
    return None


def element_id_for(source_path: str) -> str:
    stem = re.sub(r"\.md$", "", source_path)
    return "doc-" + slugify(stem.replace("/", "-"))


def link_resolver(source_path: str):
    """Rewrite markdown-relative links to posix paths relative to the output file."""

    def resolve(target: str) -> str:
        if target.startswith("#") or re.match(r"^[a-z][a-z0-9+.-]*:", target, re.IGNORECASE):
            return target
        resolved = ((ROOT / source_path).parent / target).resolve()
        try:
            relative = resolved.relative_to(ROOT.resolve())
        except ValueError:
            return target
        base_parts = OUTPUT_PATH.parent.resolve().relative_to(ROOT.resolve()).parts
        target_parts = relative.parts
        common = 0
        for a, b in zip(base_parts, target_parts):
            if a != b:
                break
            common += 1
        depth = len(base_parts) - common
        suffix = "/".join(target_parts[common:]) or "."
        return "/".join([".."] * depth + [suffix]) if depth else suffix

    return resolve


def build_page() -> str:
    template = TEMPLATE_PATH.read_text(encoding="utf-8")
    articles: list[str] = []
    toc_entries: list[str] = []
    manifest_documents: list[dict] = []
    manifest_anchors: dict[str, dict[str, str]] = {}

    for source_path, scope in DOCUMENTS:
        path = ROOT / source_path
        if not path.is_file():
            raise FileNotFoundError(f"Required agent document is missing: {source_path}")
        markdown = path.read_text(encoding="utf-8")
        title = document_title(markdown)
        state = document_state(markdown)
        element_id = element_id_for(source_path)
        source_sha256 = hashlib.sha256(markdown.encode()).hexdigest()

        renderer = DocumentRenderer(element_id, resolve_link=link_resolver(source_path))
        renderer.prescan_headings(strip_title_line(markdown))
        doc_anchor = renderer.anchor_for(title)
        body = renderer.render(strip_title_line(markdown))

        state_html = (
            f'\n<p><span class="doc-state">{escape_html(state)}</span></p>'
            if state
            else ""
        )
        articles.append(
            f'<article id="{element_id}" data-source-path="{source_path}" '
            f'data-source-sha256="{source_sha256}">'
            f'\n<h2 id="{doc_anchor}">{renderer.inline(title)}</h2>'
            f'{state_html}\n{body}\n</article>'
        )
        toc_entries.append(f'<li><a href="#{element_id}">{escape_html(title)}</a></li>')
        manifest_documents.append(
            {
                "id": element_id,
                "sourcePath": source_path,
                "scope": scope,
                "title": title,
                "state": state,
                "sourceSha256": source_sha256,
            }
        )
        for anchor, heading_text in renderer.anchors.items():
            manifest_anchors[anchor] = {"document": element_id, "title": heading_text}

    page_anchor = slugify(PAGE_TITLE)
    manifest_anchors[page_anchor] = {"document": "page", "title": PAGE_TITLE}

    manifest_json = json.dumps(
        {
            "schemaVersion": 1,
            "generator": {"name": GENERATOR_NAME, "version": GENERATOR_VERSION},
            "documents": manifest_documents,
            "anchors": manifest_anchors,
        },
        sort_keys=True,
        ensure_ascii=False,
    ).replace("</", "<\\/")

    # Substitute via sentinels so document content can never collide with
    # template placeholders.
    sentinel_page_title = "\x00PAGE-TITLE\x00"
    page = template.replace("{{page_title}}", sentinel_page_title)
    page = page.replace("{{generator_name}}", escape_html(GENERATOR_NAME))
    page = page.replace("{{page_title_id}}", slugify(PAGE_TITLE))
    page = page.replace("{{page_intro}}", escape_html(PAGE_INTRO))
    page = page.replace("{{toc_entries}}", "\n".join(toc_entries))
    page = page.replace("{{documents}}", "\n".join(articles))
    page = page.replace("{{manifest}}", manifest_json)
    page = page.replace(sentinel_page_title, escape_html(PAGE_TITLE))
    return page


def main() -> int:
    check_only = "--check" in sys.argv[1:]
    try:
        page = build_page()
    except (FileNotFoundError, ValueError) as exc:
        print(f"agent-docs generation failed: {exc}")
        return 1

    if check_only:
        if not OUTPUT_PATH.is_file():
            print(f"{OUTPUT_PATH.relative_to(ROOT).as_posix()} is missing; run the generator.")
            return 1
        if OUTPUT_PATH.read_text(encoding="utf-8") != page:
            print(
                f"{OUTPUT_PATH.relative_to(ROOT).as_posix()} is stale; "
                "run python tools/docs/build_agent_docs_html.py"
            )
            return 1
        print(f"agent-docs.html is up to date ({len(DOCUMENTS)} documents).")
        return 0

    OUTPUT_PATH.write_text(page, encoding="utf-8", newline="\n")
    print(f"Wrote {OUTPUT_PATH.relative_to(ROOT).as_posix()} ({len(page)} bytes, {len(DOCUMENTS)} documents).")
    return 0


if __name__ == "__main__":
    sys.exit(main())
