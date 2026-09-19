import { fireEvent, render } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { CalendarPicker } from './CalendarPicker';

describe('CalendarPicker', () => {
  it('maps every September 2026 date to the matching Monday-first weekday column', () => {
    const { container } = render(
      <CalendarPicker idPrefix="calendar-test" value="2026-09-29" onChange={vi.fn()} />,
    );

    fireEvent.click(container.querySelector('#calendar-test-trigger')!);

    const weekdayCenters = Array.from({ length: 7 }, (_, column) =>
      container.querySelector(`#calendar-test-weekday-${column}`),
    );
    expect(weekdayCenters.every(Boolean)).toBe(true);

    const expectedColumns = new Map([
      ['2026-09-01', '1'],
      ['2026-09-07', '0'],
      ['2026-09-13', '6'],
      ['2026-09-29', '1'],
      ['2026-09-30', '2'],
    ]);

    for (const [date, column] of expectedColumns) {
      expect(container.querySelector(`#calendar-test-day-${date}`))
        .toHaveAttribute('data-calendar-column', column);
    }

    expect(container.querySelector('#calendar-test-day-2026-09-29'))
      .toHaveAttribute('aria-pressed', 'true');
  });
});
