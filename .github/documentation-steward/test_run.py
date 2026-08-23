from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


SCRIPT = Path(__file__).with_name('run.py')
SPEC = importlib.util.spec_from_file_location('documentation_steward', SCRIPT)
assert SPEC is not None and SPEC.loader is not None
steward = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = steward
SPEC.loader.exec_module(steward)


class DocumentationStewardPolicyTest(unittest.TestCase):
    def test_allows_an_existing_technical_document_not_changed_by_the_pr(self) -> None:
        parsed = steward.parse_classification(
            {
                'state': 'DRAFT_UPDATE',
                'summary': 'The operations runbook needs the new endpoint prerequisite.',
                'confidence': 0.9,
                'evidence_paths': ['src/BE/WorkslipApi/Program.cs', 'src/BE/WorkslipApi/README.md'],
                'target_path': 'Docs/operations/local-development.md',
            },
            {'src/BE/WorkslipApi/Program.cs', 'src/BE/WorkslipApi/README.md'},
            {'Docs/operations/local-development.md'},
        )

        self.assertEqual('DRAFT_UPDATE', parsed.state)
        self.assertEqual('Docs/operations/local-development.md', parsed.target_path)

    def test_rejects_protected_governance_document(self) -> None:
        with self.assertRaisesRegex(ValueError, 'allowed existing document'):
            steward.parse_classification(
                {
                    'state': 'DRAFT_UPDATE',
                    'summary': 'Update governance.',
                    'confidence': 1,
                    'evidence_paths': ['platform/mr-saasy-control-plane/config/agent-routing.php'],
                    'target_path': 'Docs/agents/AGENT_HANDBOOK.md',
                },
                {'platform/mr-saasy-control-plane/config/agent-routing.php'},
                {'Docs/agents/AGENT_HANDBOOK.md'},
            )

    def test_rejects_unverified_evidence_path(self) -> None:
        with self.assertRaisesRegex(ValueError, 'changed paths'):
            steward.parse_classification(
                {
                    'state': 'DRAFT_UPDATE',
                    'summary': 'Update an operations document.',
                    'confidence': 0.8,
                    'evidence_paths': ['invented/source.cs'],
                    'target_path': 'Docs/operations/local-development.md',
                },
                {'src/BE/WorkslipApi/Program.cs'},
                {'Docs/operations/local-development.md'},
            )

    def test_refuses_to_overwrite_a_document_already_changed_by_the_pr(self) -> None:
        with self.assertRaisesRegex(ValueError, 'must not overwrite'):
            steward.parse_classification(
                {
                    'state': 'DRAFT_UPDATE',
                    'summary': 'Update an operations document.',
                    'confidence': 0.8,
                    'evidence_paths': ['src/BE/WorkslipApi/Program.cs'],
                    'target_path': 'Docs/operations/local-development.md',
                },
                {'src/BE/WorkslipApi/Program.cs', 'Docs/operations/local-development.md'},
                {'Docs/operations/local-development.md'},
            )

    def test_trusted_pr_requires_same_repository_main_target_and_member(self) -> None:
        trusted = {
            'author_association': 'MEMBER',
            'base': {'ref': 'main'},
            'head': {'repo': {'full_name': 'rasm105k/Workslip-v2.0'}},
        }
        self.assertTrue(steward.is_trusted_pr(trusted, 'rasm105k/Workslip-v2.0', 'main'))
        trusted['head']['repo']['full_name'] = 'outside/contributor'
        self.assertFalse(steward.is_trusted_pr(trusted, 'rasm105k/Workslip-v2.0', 'main'))

    def test_rejects_unbounded_document_update(self) -> None:
        current = '# Current\n\nShort content.\n'
        with self.assertRaisesRegex(ValueError, 'bounded update size'):
            steward.validate_updated_markdown('# Updated\n\n' + ('new fact\n' * 3000), current)


if __name__ == '__main__':
    unittest.main()
