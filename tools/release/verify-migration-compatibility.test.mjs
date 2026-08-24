import test from 'node:test';
import assert from 'node:assert/strict';
import { inspectMigration } from './verify-migration-compatibility.mjs';

test('allows additive expand migrations', () => {
  const result = inspectMigration('expand.sql', 'ALTER TABLE dbo.Job ADD NewColumn nvarchar(100) NULL;');
  assert.equal(result.destructive, false);
});

test('blocks destructive migration without explicit contract marker', () => {
  const result = inspectMigration('contract.sql', 'ALTER TABLE dbo.Job DROP COLUMN LegacyValue;');
  assert.equal(result.destructive, true);
  assert.equal(result.approvedContract, false);
});

test('recognizes explicit later contract migration marker', () => {
  const result = inspectMigration('contract.sql', '-- WORKSLIP-CONTRACT-MIGRATION: approved\nALTER TABLE dbo.Job DROP COLUMN LegacyValue;');
  assert.equal(result.destructive, true);
  assert.equal(result.approvedContract, true);
});
