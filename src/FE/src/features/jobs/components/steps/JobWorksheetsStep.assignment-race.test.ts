import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';

function readSource(relativePath: string) {
  return readFileSync(fileURLToPath(new URL(relativePath, import.meta.url)), 'utf8');
}

describe('worksheet admin assignment loading regression', () => {
  it('backfills an initially empty worksheet assignee when async job assignments arrive', () => {
    const source = readSource('./JobWorksheetsStep.tsx');

    expect(source).toContain("if (!defaultUserId || addDraft.userId) return;");
    expect(source).toContain("dispatch({ type: 'setAddDraft', draft: { ...addDraft, userId: defaultUserId } });");
  });

  it('does not allow opening the add form while admin assignees are still loading', () => {
    const source = readSource('../WorksheetsSection.tsx');

    expect(source).toContain('const addDisabled = canPickUser && isLoadingUsers;');
    expect(source).toContain('disabled={addDisabled}');
    expect(source).toContain("addDisabled ? 'Henter montører...' : 'Tilføj timeseddel'");
  });
});
