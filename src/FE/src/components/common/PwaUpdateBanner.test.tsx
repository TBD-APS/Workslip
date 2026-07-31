import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
  announcePwaUpdateApplying,
  announcePwaUpdateReady,
  PWA_UPDATE_APPLY_EVENT,
} from '../../lib/pwaUpdateEvents';
import { PwaUpdateBanner } from './PwaUpdateBanner';

describe('PwaUpdateBanner', () => {
  it('stays actionable until the service-worker coordinator accepts the update', () => {
    let applyRequests = 0;
    const handleApplyRequest = () => {
      applyRequests += 1;
    };

    window.addEventListener(PWA_UPDATE_APPLY_EVENT, handleApplyRequest);
    render(<PwaUpdateBanner />);

    act(() => announcePwaUpdateReady());

    const updateButton = screen.getByRole('button', { name: 'Opdater nu' });
    fireEvent.click(updateButton);

    expect(applyRequests).toBe(1);
    expect(updateButton).toBeEnabled();
    expect(updateButton).toHaveTextContent('Opdater nu');

    act(() => announcePwaUpdateApplying());

    expect(screen.getByRole('button', { name: 'Opdaterer...' })).toBeDisabled();

    act(() => announcePwaUpdateReady());

    expect(screen.getByRole('button', { name: 'Opdater nu' })).toBeEnabled();
    window.removeEventListener(PWA_UPDATE_APPLY_EVENT, handleApplyRequest);
  });
});
