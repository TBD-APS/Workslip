"""Contract regressions for the authored knowledge graph; no provider or HTML needed."""

import contextlib
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import validate_knowledge as knowledge


class KnowledgeGraphTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.addCleanup(patch.stopall)
        patch.object(knowledge, "ROOT", self.root).start()
        self.manifest = {
            "schemaVersion": 1,
            "product": "pilot",
            "source": "pilot-docs",
            "canonicalRoot": "docs",
            "include": ["docs/**/*.md"],
            "exclude": [],
            "metadataRequiredPrefixes": ["docs"],
            "frontmatterRequired": ["docs/**/*.md"],
        }

    def write(self, name, kind="capability", *, status="active", review="approved",
              visibility="internal", relations=(), extra=""):
        path = self.root / "docs" / "knowledge" / f"{name}.md"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(
            f"---\nid: pilot.{name}\nproduct: pilot\ntype: {kind}\nstatus: {status}\n"
            f"owner: platform/pilot\nvisibility: {visibility}\naudience: [agent, developer]\n"
            "last_reviewed: 2026-11-16\n"
            + (f"review_status: {review}\n" if review is not None else "")
            + "source_refs: [https://example.test/source]\n"
            + "relations: [" + ", ".join(f"'{edge}'" for edge in relations) + "]\n"
            + extra + f"---\n# {name}\nAuthored evidence.\n",
            encoding="utf-8",
        )
        return path

    def validate(self):
        graph = {}
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            failures = knowledge.validate_structured_docs(self.manifest, graph=graph)
        return failures, graph, output.getvalue()

    def test_workslip_cli_validates_uppercase_docs_without_platform_roster(self):
        docs = self.root / "Docs" / "workflows"
        docs.mkdir(parents=True)
        document = docs / "job-rejection.md"
        document.write_text(
            "---\nid: workslip.workflow.job-rejection\nproduct: workslip\n"
            "type: workflow\nstatus: active\nowner: product/workslip\n"
            "visibility: internal\naudience: [agent, developer]\n"
            "last_reviewed: 2026-11-16\nreview_status: approved\n"
            "source_refs: [https://example.test/reviewed-source]\nrelations: []\n"
            "---\n# Job rejection\nReviewed documentation only.\n",
            encoding="utf-8",
        )
        manifest = self.manifest | {
            "product": "workslip", "canonicalRoot": "Docs",
            "source": "workslip-docs", "excludedRoots": ["docs"],
            "include": ["Docs/workflows/job-rejection.md"],
            "metadataRequiredPrefixes": ["Docs/workflows"],
            "frontmatterRequired": ["Docs/**/*.md"],
        }
        (self.root / "docs").mkdir()
        (self.root / "Docs" / "rag-manifest.json").write_text(json.dumps(manifest))
        graph_path = self.root / "graph.json"
        with patch("sys.argv", ["validate_knowledge.py", "--relations-json", str(graph_path)]), \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, knowledge.main())
        graph = json.loads(graph_path.read_text())
        self.assertEqual("workslip", graph["product"])
        self.assertEqual("workslip.workflow.job-rejection", graph["nodes"][0]["id"])
        self.assertEqual("Docs/workflows/job-rejection.md", graph["nodes"][0]["path"])
        document.write_text("# Missing required metadata\n")
        with patch("sys.argv", ["validate_knowledge.py"]), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, knowledge.main())

    def test_platform_cli_keeps_operating_roster_gate(self):
        (self.root / "docs").mkdir()
        manifest = self.manifest | {"product": "mr-saassy"}
        (self.root / "docs" / "rag-manifest.json").write_text(json.dumps(manifest))
        with patch("sys.argv", ["validate_knowledge.py"]), \
                patch.object(knowledge, "validate_operating_roster", return_value=1) as roster, \
                contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, knowledge.main())
        roster.assert_called_once_with()

    def test_complete_typed_history_is_queryable_without_html(self):
        self.write("platform", relations=["implemented_by:pilot.product"])
        self.write("decision", "decision", relations=["affects:pilot.platform"])
        self.write("product", "product-capability")
        self.write("release", "release", relations=["contains:pilot.product", "fixes:pilot.bug"])
        self.write("bug", "bug", relations=["affects:pilot.product", "affects:pilot.release"])
        self.write("guide", "guide", relations=["documents:pilot.product"])
        self.write("problem", "problem", relations=["motivates:pilot.platform"])
        self.write("observation", "agent-observation", status="draft", review="pending",
                   relations=["supports:pilot.guide"])
        failures, graph, output = self.validate()
        self.assertEqual(0, failures, output)
        self.assertEqual("1.1.0", graph["contractVersion"])
        self.assertEqual(8, len(graph["nodes"]))
        self.assertEqual(["pilot.product"], [edge["target"] for edge in graph["relations"]
                         if edge["source"] == "pilot.platform" and edge["type"] == "implemented_by"])
        observation = next(node for node in graph["nodes"] if node["id"] == "pilot.observation")
        self.assertEqual("pending", observation["reviewStatus"])
        self.assertEqual("draft", observation["status"])
        self.assertEqual("internal", observation["visibility"])
        self.assertEqual("platform/pilot", observation["owner"])
        self.assertEqual(64, len(observation["checksum"]))
        self.assertEqual(["https://example.test/source"], observation["sourceRefs"])

    def test_new_version_changes_checksum_and_keeps_identity_and_relations(self):
        self.write("product", "product-capability")
        path = self.write("platform", relations=["implemented_by:pilot.product"])
        before = self.validate()[1]
        path.write_text(path.read_text() + "A reviewed correction.\n")
        after = self.validate()[1]
        self.assertEqual(before["nodes"][0]["id"], after["nodes"][0]["id"])
        self.assertNotEqual(before["nodes"][0]["checksum"], after["nodes"][0]["checksum"])
        self.assertEqual(before["relations"], after["relations"])

    def test_unreviewed_and_rejected_observations_cannot_be_active_or_public(self):
        for review, status, visibility in [
            (None, "draft", "internal"), ("unknown", "draft", "internal"),
            ("pending", "active", "internal"), ("rejected", "active", "internal"),
            ("pending", "draft", "public"), ("rejected", "historical", "public"),
        ]:
            with self.subTest(review=review, status=status, visibility=visibility):
                self.write("observation", "agent-observation", review=review,
                           status=status, visibility=visibility)
                failures, graph, _ = self.validate()
                self.assertGreater(failures, 0)
                self.assertEqual({}, graph)

    def test_reviewed_observation_retains_explicit_provenance(self):
        self.write("observation", "agent-observation")
        self.assertEqual(0, self.validate()[0])
        self.write("observation", "agent-observation", extra="source_refs: []\n")
        self.assertGreater(self.validate()[0], 0)

    def test_invalid_relation_graph_never_exports_partial_knowledge(self):
        self.write("target", "product-capability")
        for relations in [
            ["unknown:pilot.target"], ["implemented_by:pilot.missing"],
            ["implemented_by:pilot.source"], ["implemented_by:../target"],
            ["implemented_by:pilot.target", "implemented_by:pilot.target"],
            ["contains:pilot.target"], ["pilot.target"],
        ]:
            with self.subTest(relations=relations):
                self.write("source", relations=relations)
                failures, graph, _ = self.validate()
                self.assertGreater(failures, 0)
                self.assertEqual({}, graph)

    def test_approved_claim_cannot_promote_a_pending_observation(self):
        self.write("observation", "agent-observation", status="draft", review="pending")
        self.write("approved", "agent-observation", relations=["supports:pilot.observation"])
        self.assertGreater(self.validate()[0], 0)

    def test_edges_cannot_widen_publication_or_cross_the_manifest_product(self):
        self.write("target", "product-capability")
        self.write("source", visibility="public", relations=["implemented_by:pilot.target"])
        self.assertGreater(self.validate()[0], 0)
        self.write("source", relations=["implemented_by:pilot.target"])
        self.write("target", "product-capability", extra="product: other-product\n")
        self.assertGreater(self.validate()[0], 0)

    def test_malformed_classifications_are_errors_not_exceptions(self):
        for extra in ["type: []\n", "review_status: []\n", "visibility: []\n", "status: []\n", "owner: ''\n"]:
            with self.subTest(extra=extra):
                self.write("source", extra=extra)
                failures, graph, _ = self.validate()
                self.assertGreater(failures, 0)
                self.assertEqual({}, graph)

    def test_legacy_documents_need_no_implicit_review_migration(self):
        path = self.write("legacy", review=None)
        path.write_text(path.read_text().replace("source_refs: [https://example.test/source]\n", "")
                        .replace("relations: []\n", ""))
        failures, graph, output = self.validate()
        self.assertEqual(0, failures, output)
        self.assertIsNone(graph["nodes"][0]["reviewStatus"])

    def test_block_list_and_unknown_visibility_fail_closed(self):
        self.write("source", extra="relations:\n  - implemented_by:pilot.target\n")
        self.assertGreater(self.validate()[0], 0)
        self.write("source", visibility="unknown")
        failures, graph, _ = self.validate()
        self.assertGreater(failures, 0)
        self.assertEqual({}, graph)

    def test_duplicate_identity_and_wrong_target_type_are_rejected(self):
        self.write("target", "guide")
        self.write("source", relations=["implemented_by:pilot.target"])
        self.assertGreater(self.validate()[0], 0)
        self.write("source", extra="id: pilot.target\n")
        self.assertGreater(self.validate()[0], 0)

    def test_cli_writes_only_a_fully_validated_graph(self):
        self.write("source")
        manifest = self.root / "docs" / "rag-manifest.json"
        manifest.write_text(json.dumps(self.manifest))
        output = self.root / "graph.json"
        args = ["validate", "--manifest", "docs/rag-manifest.json", "--relations-json", str(output)]
        with patch("sys.argv", args), patch.object(knowledge, "validate_operating_roster", return_value=0), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(0, knowledge.main())
        self.assertEqual("pilot.source", json.loads(output.read_text())["nodes"][0]["id"])
        output.unlink()
        self.write("source", relations=["implemented_by:pilot.missing"])
        with patch("sys.argv", args), patch.object(knowledge, "validate_operating_roster", return_value=0), contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(1, knowledge.main())
        self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
