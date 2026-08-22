import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { JobCard } from '../../../components/JobCard';

vi.mock('../../../lib/toast', () => ({
  notify: {
    success: vi.fn(),
    error: vi.fn(),
  },
}));

describe('JobCard copyability', () => {
  const writeText = vi.fn<() => Promise<void>>();

  beforeEach(() => {
    vi.clearAllMocks();
    writeText.mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    });
  });

  it('copies nested domain values without opening the card', async () => {
    const onOpen = vi.fn();
    const { container } = render(
      <JobCard
        id="job-1234"
        reportNumber="1234"
        status="Draft"
        customerName="Test Kunde"
        address="Testvej 1"
        onOpen={onOpen}
      />,
    );
    const card = container.firstElementChild;
    expect(card).toHaveAttribute('role', 'link');

    fireEvent.click(screen.getByRole('button', { name: /kopiér kundenavn/i }));

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('Test Kunde'));
    expect(onOpen).not.toHaveBeenCalled();

    fireEvent.click(card!);
    expect(onOpen).toHaveBeenCalledTimes(1);
  });
});
