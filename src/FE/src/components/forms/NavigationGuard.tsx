import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useBlocker, type BlockerFunction } from 'react-router-dom';
import { Loader2 } from 'lucide-react';
import { useModalAccessibility } from '../common/useModalAccessibility';

type NavigationGuardProps = {
  when: boolean;
  title?: string;
  message?: string;
  onSave?: () => void | boolean | Promise<unknown>;
  autoSaveOnLeave?: () => boolean | Promise<boolean>;
  autoSavePending?: boolean;
};

export function NavigationGuard({
  when,
  title = 'Ugemte ændringer',
  message = 'Der er ugemte ændringer. Er du sikker på, at du vil forlade denne side?',
  onSave,
  autoSaveOnLeave,
  autoSavePending = false,
}: NavigationGuardProps) {
  // Only leaving the PAGE is an exit worth guarding. A same-pathname navigation
  // is this page writing its own URL - the Samtale drawer's ?conversation=1, a
  // wizard step param - and blocking those turned every in-page URL write into a
  // 'Gemmer ændringer' modal the user never asked for. Trade-off taken
  // deliberately: search-only and hash-only exits are no longer guarded.
  // The dependency list is exactly `when`, so the function identity stays stable
  // for as long as a block is live.
  const shouldBlock = useCallback<BlockerFunction>(
    ({ currentLocation, nextLocation }) => when && currentLocation.pathname !== nextLocation.pathname,
    [when],
  );
  const blocker = useBlocker(shouldBlock);
  const [isSaving, setIsSaving] = useState(false);
  const [saveFailed, setSaveFailed] = useState(false);
  const autoSaveOnLeaveRef = useRef(autoSaveOnLeave);
  const autoSaveStartedRef = useRef(false);
  const isAutoSaveMode = Boolean(autoSaveOnLeave);
  const dialogTitle = isAutoSaveMode ? 'Gemmer ændringer' : title;
  const dialogMessage = isAutoSaveMode
    ? 'Dine ændringer gemmes automatisk, før du forlader siden.'
    : message;

  useEffect(() => {
    autoSaveOnLeaveRef.current = autoSaveOnLeave;
  }, [autoSaveOnLeave]);

  useEffect(() => {
    if (!when) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [when]);

  const handleAutoSaveAndLeave = useCallback(async () => {
    const save = autoSaveOnLeaveRef.current;
    if (!save) return;

    autoSaveStartedRef.current = true;
    setSaveFailed(false);
    try {
      const saved = await save();
      if (saved === false) {
        setSaveFailed(true);
        return;
      }
      blocker.proceed?.();
    } catch {
      setSaveFailed(true);
    }
  }, [blocker]);

  useEffect(() => {
    if (blocker.state === 'unblocked') {
      autoSaveStartedRef.current = false;
      return;
    }

    if (
      blocker.state !== 'blocked'
      || !isAutoSaveMode
      || autoSavePending
      || autoSaveStartedRef.current
      || saveFailed
    ) {
      return;
    }

    void handleAutoSaveAndLeave();
  }, [autoSavePending, blocker.state, handleAutoSaveAndLeave, isAutoSaveMode, saveFailed]);

  const handleSaveAndLeave = useCallback(async () => {
    if (!onSave) {
      blocker.proceed?.();
      return;
    }

    setSaveFailed(false);
    setIsSaving(true);
    try {
      const result = await onSave();
      if (result === false) {
        setSaveFailed(true);
        return;
      }
      blocker.proceed?.();
    } catch {
      setSaveFailed(true);
    } finally {
      setIsSaving(false);
    }
  }, [blocker, onSave]);

  const handleCancel = () => {
    setSaveFailed(false);
    blocker.reset?.();
  };

  // A save in flight is exactly the state that renders a spinner and no controls;
  // every other state of this dialog renders a stay-on-the-page button.
  const isSaveInFlight = isSaving || (isAutoSaveMode && !saveFailed);
  // Escape resolves to the least destructive control the dialog is currently
  // showing - 'Bliv på siden' / 'Annuller' - so it cancels the exit and leaves the
  // draft untouched. While a save is genuinely running there is no such control:
  // dismissing the blocker then would hand the page back to the user with a write
  // still in flight and a proceed() that can still fire underneath them, so Escape
  // stays inert until the save settles into success (we navigate) or failure (the
  // retry/stay buttons appear).
  const initialFocusRef = useRef<HTMLButtonElement>(null);
  const dialogRef = useModalAccessibility<HTMLDivElement>({
    open: blocker.state === 'blocked',
    onClose: handleCancel,
    initialFocusRef,
    closeOnEscape: !isSaveInFlight,
  });

  if (blocker.state !== 'blocked') return null;

  return createPortal(
    <div
      className="modal-backdrop"
      onClick={() => {
        if (!isSaving && !isAutoSaveMode) handleCancel();
      }}
    >
      <div
        ref={dialogRef}
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-modal="true"
        aria-label={dialogTitle}
        tabIndex={-1}
      >
        <h3>{dialogTitle}</h3>
        <p>{dialogMessage}</p>
        {saveFailed && (
          <p role="alert">
            Kunne ikke gemme ændringerne. Dine ændringer er stadig på siden.
          </p>
        )}
        <div className="modal-actions">
          {isSaveInFlight ? (
            <div className="saving-indicator">
              <Loader2 className="animate-spin" size={18} />
              <span>Gemmer...</span>
            </div>
          ) : isAutoSaveMode ? (
            // Autosave failed. The navigation stays blocked and the draft stays on
            // the page, so the only two honest choices are retry and stay - never a
            // discard, which would throw away work the user never chose to lose.
            <div className="modal-actions--double">
              <button type="button" className="btn btn-secondary" onClick={handleCancel}>
                Bliv på siden
              </button>
              <button
                ref={initialFocusRef}
                type="button"
                className="btn btn-primary"
                onClick={() => { void handleAutoSaveAndLeave(); }}
              >
                Prøv igen
              </button>
            </div>
          ) : (
            <>
              {onSave ? (
                <>
                  <div className="modal-actions--double">
                    <button type="button" className="btn btn-secondary" onClick={() => blocker.proceed?.()}>
                      Forlad uden at gemme
                    </button>
                    {/* Focus lands on the saving action, not on the discard next to it. */}
                    <button
                      ref={initialFocusRef}
                      type="button"
                      className="btn btn-primary"
                      onClick={() => { void handleSaveAndLeave(); }}
                    >
                      {saveFailed ? 'Prøv at gemme igen' : 'Gem og forlad'}
                    </button>
                  </div>
                  <button type="button" className="btn btn-secondary" onClick={handleCancel}>
                    Annuller
                  </button>
                </>
              ) : (
                <div className="modal-actions--double">
                  {/* No save to offer here, so the primary button discards - focus the
                      cancel instead and keep Enter from throwing the draft away. */}
                  <button ref={initialFocusRef} type="button" className="btn btn-secondary" onClick={handleCancel}>
                    Annuller
                  </button>
                  <button type="button" className="btn btn-primary" onClick={() => { void handleSaveAndLeave(); }}>
                    Forlad siden
                  </button>
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>,
    document.getElementById('portal-root') ?? document.body,
  );
}
