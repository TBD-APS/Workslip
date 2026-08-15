import { fireEvent, render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { UserRateEditor } from './UserRateCard';
import { useUpdateUserBillingRate } from '../hooks/useUserBillingRate';

vi.mock('../hooks/useUserBillingRate', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../hooks/useUserBillingRate')>();
  return {
    ...actual,
    useUpdateUserBillingRate: vi.fn(),
    useUserBillingRate: vi.fn(),
  };
});

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

describe('UserRateEditor', () => {
  const mutate = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    mutate.mockImplementation((_variables, options) => options?.onSuccess?.());
    vi.mocked(useUpdateUserBillingRate).mockReturnValue({
      mutate,
      isPending: false,
    } as unknown as ReturnType<typeof useUpdateUserBillingRate>);
  });

  it('edits a Danish decimal rate without triggering the parent row navigation', () => {
    const parentClick = vi.fn();

    render(
      <div onClick={parentClick}>
        <UserRateEditor
          userId="user-1"
          rate={725}
          variant="inline"
          ariaLabel="Fakturerbar timepris for Test Person"
        />
      </div>,
    );

    fireEvent.click(screen.getByRole('button', { name: /Rediger timepris/ }));
    expect(parentClick).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText('Kr. pr. time'), {
      target: { value: '800,50' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Gem timepris' }));

    expect(mutate).toHaveBeenCalledWith(
      {
        id: 'user-1',
        data: { billableHourlyRate: 800.5 },
      },
      expect.objectContaining({ onSuccess: expect.any(Function) }),
    );
    expect(parentClick).not.toHaveBeenCalled();
  });
});
