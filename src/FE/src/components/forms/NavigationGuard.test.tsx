import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createMemoryRouter, RouterProvider, useLocation, useNavigate } from 'react-router-dom';
import { NavigationGuard } from './NavigationGuard';

type GuardedPageProps = {
  autoSaveOnLeave?: () => boolean | Promise<boolean>;
  autoSavePending?: boolean;
  onSave?: () => void | boolean | Promise<unknown>;
};

function GuardedPage({ autoSaveOnLeave, autoSavePending, onSave }: GuardedPageProps) {
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <>
      <button type="button" onClick={() => navigate('/other')}>Forlad siden</button>
      <button type="button" onClick={() => navigate('/edit?conversation=1')}>Åbn samtale</button>
      <p>Søgestreng: {location.search || 'ingen'}</p>
      <NavigationGuard
        when
        autoSaveOnLeave={autoSaveOnLeave}
        autoSavePending={autoSavePending}
        onSave={onSave}
      />
    </>
  );
}

function renderGuardedPage(element: React.ReactNode) {
  const router = createMemoryRouter([
    { path: '/edit', element },
    { path: '/other', element: <h1>Anden side</h1> },
  ], { initialEntries: ['/edit'] });

  render(<RouterProvider router={router} />);
}

function deferredBoolean() {
  let resolve!: (value: boolean) => void;
  const promise = new Promise<boolean>((promiseResolve) => {
    resolve = promiseResolve;
  });
  return { promise, resolve };
}

afterEach(() => {
  cleanup();
});

describe('NavigationGuard autosave navigation', () => {
  it('saves automatically without offering to discard changes', async () => {
    const deferred = deferredBoolean();
    const autoSaveOnLeave = vi.fn(() => deferred.promise);
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('dialog', { name: 'Gemmer ændringer' })).toBeInTheDocument();
    expect(screen.getByText('Dine ændringer gemmes automatisk, før du forlader siden.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Forlad uden at gemme' })).not.toBeInTheDocument();
    expect(autoSaveOnLeave).toHaveBeenCalledTimes(1);

    await act(async () => {
      deferred.resolve(true);
      await deferred.promise;
    });

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
  });

  it('lets the page write its own search params without raising the save dialog', async () => {
    const autoSaveOnLeave = vi.fn(() => Promise.resolve(true));
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Åbn samtale' }));

    // The navigation must actually land - "no dialog" is only meaningful if the
    // search param write was not silently swallowed by the blocker.
    await waitFor(() => expect(screen.getByText('Søgestreng: ?conversation=1')).toBeInTheDocument());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(autoSaveOnLeave).not.toHaveBeenCalled();
  });

  it('offers a retry and a way to stay when the automatic save fails', async () => {
    const autoSaveOnLeave = vi.fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Kunne ikke gemme ændringerne');
    expect(screen.getByRole('button', { name: 'Prøv igen' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Bliv på siden' })).toBeInTheDocument();
    // Autosave screens never offer to throw the draft away, failed or not.
    expect(screen.queryByRole('button', { name: 'Forlad uden at gemme' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Prøv igen' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
    expect(autoSaveOnLeave).toHaveBeenCalledTimes(2);
  });

  it('stays on the page with the draft intact when the automatic save keeps failing', async () => {
    const autoSaveOnLeave = vi.fn().mockResolvedValue(false);
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Bliv på siden' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Forlad siden' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
    expect(autoSaveOnLeave).toHaveBeenCalledTimes(1);
  });

  it('waits for an active autosave before flushing the latest draft', async () => {
    const deferred = deferredBoolean();
    const autoSaveOnLeave = vi.fn(() => deferred.promise);

    function PendingPage() {
      const [pending, setPending] = useState(true);
      return (
        <>
          <button type="button" onClick={() => setPending(false)}>Afslut aktiv gemning</button>
          <GuardedPage autoSaveOnLeave={autoSaveOnLeave} autoSavePending={pending} />
        </>
      );
    }

    renderGuardedPage(<PendingPage />);
    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('dialog', { name: 'Gemmer ændringer' })).toBeInTheDocument();
    expect(autoSaveOnLeave).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole('button', { name: 'Afslut aktiv gemning' }));
    await waitFor(() => expect(autoSaveOnLeave).toHaveBeenCalledTimes(1));

    await act(async () => {
      deferred.resolve(true);
      await deferred.promise;
    });

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
  });

  it('preserves the manual save and discard choices for explicit-save screens', async () => {
    renderGuardedPage(<GuardedPage onSave={vi.fn()} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('dialog', { name: 'Ugemte ændringer' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Forlad uden at gemme' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Gem og forlad' })).toBeInTheDocument();
  });

  it('keeps navigation blocked when save resolves false', async () => {
    const onSave = vi.fn().mockResolvedValue(false);
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Gem og forlad' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Kunne ikke gemme ændringerne');
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Prøv at gemme igen' })).toBeInTheDocument();
  });

  it('keeps navigation blocked when save rejects', async () => {
    const onSave = vi.fn().mockRejectedValue(new Error('save failed'));
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Gem og forlad' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Kunne ikke gemme ændringerne');
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
  });

  it('retries a failed save and proceeds only after success', async () => {
    const onSave = vi.fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Gem og forlad' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Prøv at gemme igen' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
    expect(onSave).toHaveBeenCalledTimes(2);
  });

  it('allows explicit discard after a failed save', async () => {
    const onSave = vi.fn().mockResolvedValue(false);
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Gem og forlad' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Forlad uden at gemme' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
  });

  it('treats Escape as staying on the page when the automatic save has failed', async () => {
    const autoSaveOnLeave = vi.fn().mockResolvedValue(false);
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Kunne ikke gemme ændringerne');
    expect(screen.getByRole('dialog', { name: 'Gemmer ændringer' })).toHaveAttribute('aria-modal', 'true');
    await waitFor(() => expect(screen.getByRole('button', { name: 'Prøv igen' })).toHaveFocus());

    fireEvent.keyDown(document, { key: 'Escape' });

    // Escape is the keyboard form of 'Bliv på siden': the exit is cancelled and the
    // draft is still on the page. It must never resolve to a retry or a discard.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Forlad siden' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
    expect(autoSaveOnLeave).toHaveBeenCalledTimes(1);
  });

  it('ignores Escape while the automatic save is still in flight', async () => {
    const deferred = deferredBoolean();
    const autoSaveOnLeave = vi.fn(() => deferred.promise);
    renderGuardedPage(<GuardedPage autoSaveOnLeave={autoSaveOnLeave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    const dialog = await screen.findByRole('dialog', { name: 'Gemmer ændringer' });

    fireEvent.keyDown(document, { key: 'Escape' });

    // Dismissing the blocker mid-save would hand the page back with a write still
    // running and a proceed() that can still fire underneath the user.
    expect(dialog).toBeInTheDocument();
    expect(screen.getByText('Gemmer...')).toBeInTheDocument();

    await act(async () => {
      deferred.resolve(true);
      await deferred.promise;
    });

    await waitFor(() => expect(screen.getByRole('heading', { name: 'Anden side' })).toBeInTheDocument());
  });

  it('cancels the explicit-save dialog on Escape and focuses the saving action', async () => {
    const onSave = vi.fn();
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));

    expect(await screen.findByRole('dialog', { name: 'Ugemte ændringer' })).toBeInTheDocument();
    // Initial focus must not land on the discard button that sits first in the DOM.
    await waitFor(() => expect(screen.getByRole('button', { name: 'Gem og forlad' })).toHaveFocus());

    fireEvent.keyDown(document, { key: 'Escape' });

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it('cancels navigation after a failed save without losing the form route', async () => {
    const onSave = vi.fn().mockResolvedValue(false);
    renderGuardedPage(<GuardedPage onSave={onSave} />);

    fireEvent.click(screen.getByRole('button', { name: 'Forlad siden' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Gem og forlad' }));
    fireEvent.click(await screen.findByRole('button', { name: 'Annuller' }));

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(screen.getByRole('button', { name: 'Forlad siden' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'Anden side' })).not.toBeInTheDocument();
  });
});
