import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { useJobCreateWithAuditorScope } from './useJobCreateWithAuditorScope';

const mocks = vi.hoisted(() => ({
  baseOnCreated: undefined as undefined | ((jobIds: string[]) => void),
  baseSave: vi.fn(),
  baseReset: vi.fn(),
  setJobAuditorScope: vi.fn(),
}));

vi.mock('./useJobCreate', () => ({
  useJobCreate: (onCreated: (jobIds: string[]) => void) => {
    mocks.baseOnCreated = onCreated;
    return {
      form: {},
      linkedJobIds: [],
      assignedUserIds: [],
      duplicatePerAssignedUser: false,
      assignableUsers: [],
      isSaving: false,
      canSave: true,
      linksStatus: 'idle',
      assignmentStatus: 'idle',
      referenceData: null,
      isLoadingReferenceData: false,
      isLoadingUsers: false,
      fieldErrors: {},
      save: mocks.baseSave,
      saveWithTimesheets: vi.fn(),
      reset: mocks.baseReset,
    };
  },
}));

vi.mock('../api/auditorScopeApi', () => ({
  setJobAuditorScope: mocks.setJobAuditorScope,
}));

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

describe('useJobCreateWithAuditorScope', () => {
  beforeEach(() => {
    mocks.baseOnCreated = undefined;
    mocks.baseSave.mockReset();
    mocks.baseReset.mockReset();
    mocks.setJobAuditorScope.mockReset();
  });

  it('forwards visible jobs without an auditor-scope mutation', async () => {
    const onCreated = vi.fn();
    renderHook(() => useJobCreateWithAuditorScope(onCreated));

    act(() => {
      mocks.baseOnCreated?.(['job-1']);
    });

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(['job-1']));
    expect(mocks.setJobAuditorScope).not.toHaveBeenCalled();
  });

  it('keeps a failed internal-scope write in the create flow and retries only failed jobs', async () => {
    const onCreated = vi.fn();
    mocks.setJobAuditorScope
      .mockResolvedValueOnce({ isInAuditorScope: false, reason: 'Intern opgave' })
      .mockRejectedValueOnce(new Error('500'))
      .mockResolvedValueOnce({ isInAuditorScope: false, reason: 'Intern opgave' });

    const { result } = renderHook(() => useJobCreateWithAuditorScope(onCreated));

    act(() => {
      result.current.updateAuditorScope({ isInAuditorScope: false, reason: 'Intern opgave' });
    });
    act(() => {
      mocks.baseOnCreated?.(['job-1', 'job-2']);
    });

    await waitFor(() => expect(result.current.auditorScopeError).toBe(true));
    expect(result.current.hasPendingAuditorScope).toBe(true);
    expect(onCreated).not.toHaveBeenCalled();
    expect(mocks.setJobAuditorScope).toHaveBeenCalledTimes(2);

    act(() => {
      result.current.save();
    });

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(['job-1', 'job-2']));
    expect(mocks.baseSave).not.toHaveBeenCalled();
    expect(mocks.setJobAuditorScope).toHaveBeenCalledTimes(3);
    expect(mocks.setJobAuditorScope.mock.calls[2]?.[0]).toBe('job-2');
  });
});
