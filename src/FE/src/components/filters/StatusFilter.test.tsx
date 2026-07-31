import { fireEvent, render, screen } from '@testing-library/react';
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest';
import { StatusFilter } from './StatusFilter';

type TestStatus = 'Draft' | 'Rejected' | 'InReview';

const options = [
  { value: ['Draft', 'Rejected'] as const, label: 'Aktive og afviste' },
  { value: 'InReview' as const, label: 'Til gennemsyn' },
];

beforeAll(() => {
  vi.stubGlobal('ResizeObserver', class ResizeObserver {
    observe() {}
    unobserve() {}
    disconnect() {}
  });
});

afterAll(() => {
  vi.unstubAllGlobals();
});

describe('StatusFilter grouped options', () => {
  it('selects every status in a grouped option without removing other selections', () => {
    const onChange = vi.fn();

    render(
      <StatusFilter<TestStatus>
        options={options}
        selected={['InReview']}
        onChange={onChange}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Aktive og afviste' }));

    expect(onChange).toHaveBeenCalledWith(['InReview', 'Draft', 'Rejected']);
  });

  it('removes every grouped status while preserving unrelated selections', () => {
    const onChange = vi.fn();

    render(
      <StatusFilter<TestStatus>
        options={options}
        selected={['Draft', 'Rejected', 'InReview']}
        onChange={onChange}
      />,
    );

    const groupedButton = screen.getByRole('button', { name: 'Aktive og afviste' });
    expect(groupedButton).toHaveAttribute('aria-pressed', 'true');

    fireEvent.click(groupedButton);

    expect(onChange).toHaveBeenCalledWith(['InReview']);
  });
});