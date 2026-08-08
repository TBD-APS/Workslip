import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
  announcePwaUpdateApplying,
  announcePwaUpdateReady,
} from '../../lib/pwaUpdateEvents';
import { PwaUpdateBanner } from './PwaUpdateBanner';

describe('PwaUpdateBanner', () => {
  it('shows automatic update status without a manual update action', () => {
    render(<PwaUpdateBanner />);

    expect(screen.queryByRole('region', { name: 'Appopdatering' })).not.toBeInTheDocument();

    act(() => announcePwaUpdateReady());

    expect(screen.getByRole('region', { name: 'Appopdatering' })).toHaveTextContent('Ny version klar');
    expect(screen.getByText('Appen opdateres automatisk om få sekunder.')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();

    act(() => announcePwaUpdateApplying());

    expect(screen.getByText('Opdaterer appen...')).toBeInTheDocument();
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });
});
