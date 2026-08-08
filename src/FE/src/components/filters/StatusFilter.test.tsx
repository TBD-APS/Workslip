import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { getSavedStatusFilter, StatusFilter } from './StatusFilter';

afterEach(cleanup);

class ResizeObserverMock {
  observe() {}
  unobserve() {}
  disconnect() {}
}

beforeEach(() => {
  sessionStorage.clear();
  vi.stubGlobal('ResizeObserver', ResizeObserverMock);
});

const options = [
  { value: 'Aktiv', label: 'Aktiv' },
  { value: 'Gennemsyn', label: 'Til gennemsyn' },
  { value: 'Godkendt', label: 'Godkendt' },
  { value: 'Afvist', label: 'Afvist' },
];

describe('StatusFilter single-select', () => {
  it('marks only the selected option as active', () => {
    render(<StatusFilter options={options} selected={['Aktiv']} onChange={vi.fn()} />);

    expect(screen.getByRole('button', { name: 'Aktiv' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'Til gennemsyn' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Godkendt' })).toHaveAttribute('aria-pressed', 'false');
    expect(screen.getByRole('button', { name: 'Afvist' })).toHaveAttribute('aria-pressed', 'false');
  });

  it('replaces the previous selection when a new filter is selected', () => {
    const onChange = vi.fn();
    render(<StatusFilter options={options} selected={['Aktiv']} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: 'Til gennemsyn' }));

    expect(onChange).toHaveBeenCalledWith(['Gennemsyn']);
  });

  it('selects the first filter when none is active', () => {
    const onChange = vi.fn();
    render(<StatusFilter options={options} selected={[]} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: 'Afvist' }));

    expect(onChange).toHaveBeenCalledWith(['Afvist']);
  });

  it('deselects the active filter when it is clicked again', () => {
    const onChange = vi.fn();
    render(<StatusFilter options={options} selected={['Godkendt']} onChange={onChange} />);

    fireEvent.click(screen.getByRole('button', { name: 'Godkendt' }));

    expect(onChange).toHaveBeenCalledWith([]);
  });
});

describe('getSavedStatusFilter', () => {
  it('restores a single saved filter', () => {
    sessionStorage.setItem('statusFilter:lastActive', 'mine-jobs');
    sessionStorage.setItem('statusFilter:mine-jobs', JSON.stringify(['Aktiv']));

    expect(getSavedStatusFilter('mine-jobs', ['Aktiv'])).toEqual(['Aktiv']);
  });

  it('keeps only one filter when multiple were saved', () => {
    sessionStorage.setItem('statusFilter:lastActive', 'mine-jobs');
    sessionStorage.setItem('statusFilter:mine-jobs', JSON.stringify(['Aktiv', 'Gennemsyn']));

    expect(getSavedStatusFilter('mine-jobs', ['Aktiv'])).toEqual(['Aktiv']);
  });
});
