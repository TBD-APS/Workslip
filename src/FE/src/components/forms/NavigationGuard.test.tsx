import { useState } from 'react';
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { createMemoryRouter, RouterProvider, useNavigate } from 'react-router-dom';
import { NavigationGuard } from './NavigationGuard';

type GuardedPageProps = {
  autoSaveOnLeave?: () => boolean | Promise<boolean>;
  autoSavePending?: boolean;
  onSave?: () => void | boolean | Promise<unknown>;
};

function GuardedPage({ autoSaveOnLeave, autoSavePending, onSave }: GuardedPageProps) {
  const navigate = useNavigate();

  return (
    <>
      <button type="button" onClick={() => navigate('/other')}>Forlad siden</button>
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
