"""Explicit contradictions require concrete current facts; unknowns never become passing evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("report_drift", Path(__file__).with_name("report_drift.py"))
drift = importlib.util.module_from_spec(spec)
spec.loader.exec_module(drift)


class DriftClaimsTests(unittest.TestCase):
    def test_all_four_missing_codes_carry_versioned_reviewable_evidence(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(drift, "ROOT", Path(directory)):
            root = Path(directory)
            source = root / "src/Api.cs"
            source.parent.mkdir()
            source.write_text('namespace Example; enum State { Draft, Active }\napp.MapPost("/api/notes", Handler);')
            test = root / "tests/NotesTests.cs"
            test.parent.mkdir()
            test.write_text('public async Task Current_test() {}\n// public Task Removed_test() {}')
            contract = root / "src/MR.SAASy.ControlCenter/OpenApi/control-center.openapi.json"
            contract.parent.mkdir(parents=True)
            contract.write_text(json.dumps({"paths": {"/api/notes": {"post": {"security": []}}}}))
            doc = root / "note.md"
            doc.write_text('# An explicit authored claim\n')
            metadata = {"id": "example.notes", "status": "active", "type": "capability",
                        "code_claims": ["api:/api/notes=GET", "enum:Example.State=Draft|Retired"],
                        "permission_claims": ["permission:POST:/api/notes=authenticated"],
                        "test_refs": ["tests/NotesTests.cs#Removed_test"]}
            facts = drift.implementation_facts([source])
            findings, unknown = drift.explicit_claim_findings([(doc, metadata)], facts)
            self.assertFalse(unknown)
            self.assertEqual({"DOC_CODE_CONTRADICTION", "DOC_TEST_CONTRADICTION", "UNMAPPED_CAPABILITY", "PERMISSION_MISMATCH"}, {f["code"] for f in findings})
            for finding in findings:
                self.assertEqual("pending", finding["reviewStatus"])
                self.assertEqual(drift.file_sha(doc), finding["evidence"]["documentSha256"])
                for evidence in finding["evidence"]["sources"]:
                    self.assertEqual(drift.file_sha(root / evidence["path"]), evidence["sha256"])
            metadata.update(code_refs=["src/Api.cs"], code_claims=["api:/api/notes=POST", "enum:Example.State=Active|Draft"],
                            permission_claims=["permission:POST:/api/notes=public"], test_refs=["tests/NotesTests.cs#Current_test"])
            self.assertEqual(([], []), drift.explicit_claim_findings([(doc, metadata)], facts))

    def test_unknown_or_ambiguous_facts_are_not_assessed_and_drafts_are_not_current_claims(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(drift, "ROOT", Path(directory)):
            doc = Path(directory) / "note.md"
            doc.write_text("# Claim\n")
            meta = {"status": "active", "code_claims": ["enum:Missing=Active"], "test_refs": ["../outside.cs#Test"]}
            findings, unknown = drift.explicit_claim_findings([(doc, meta)], {})
            self.assertFalse(findings)
            self.assertEqual(2, len(unknown))
            self.assertEqual(([], []), drift.explicit_claim_findings([(doc, {**meta, "status": "draft"})], {}))


if __name__ == "__main__":
    unittest.main()
