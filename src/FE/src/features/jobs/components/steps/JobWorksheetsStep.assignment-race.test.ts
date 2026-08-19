import { describe, expect, it } from 'vitest';
import { initialWorksheetUiState, worksheetUiReducer } from '../worksheetUtils';

describe('worksheet admin assignment loading regression', () => {
  it('uses the resolved current-job assignee whenever the add form opens', () => {
    const staleState = initialWorksheetUiState('stale-user');

    const opened = worksheetUiReducer(staleState, {
      type: 'openAdd',
      defaultUserId: 'current-job-admin',
    });

    expect(opened.isAddOpen).toBe(true);
    expect(opened.addDraft.userId).toBe('current-job-admin');
  });

  it('keeps an empty assignee only when no resolved default exists yet', () => {
    const state = initialWorksheetUiState('');

    const opened = worksheetUiReducer(state, {
      type: 'openAdd',
      defaultUserId: '',
    });

    expect(opened.isAddOpen).toBe(true);
    expect(opened.addDraft.userId).toBe('');
  });
});
