import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useBlocker } from 'react-router-dom';
import { Loader2 } from 'lucide-react';

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
  const blocker = useBlocker(when);
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
    try {
      const saved = await save();
      if (saved === false) {
        blocker.reset?.();
        return;
      }
      blocker.proceed?.();
    } catch {
      blocker.reset?.();
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
    ) {
      return;
    }

    void handleAutoSaveAndLeave();
  }, [autoSavePending, blocker.state, handleAutoSaveAndLeave, isAutoSaveMode]);

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

  if (blocker.state !== 'blocked') return null;

  return createPortal(
    <div
      className="modal-backdrop"
      onClick={() => {
        if (!isSaving && !isAutoSaveMode) handleCancel();
      }}
    >
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={dialogTitle}
      >
        <h3>{dialogTitle}</h3>
        <p>{dialogMessage}</p>
        {saveFailed && (
          <p role="alert">
            Kunne ikke gemme ændringerne. Dine ændringer er stadig på siden.
          </p>
        )}
        <div className="modal-actions">
          {isSaving || isAutoSaveMode ? (
            <div className="saving-indicator">
              <Loader2 className="animate-spin" size={18} />
              <span>Gemmer...</span>
            </div>
          ) : (
            <>
              {onSave ? (
                <>
                  <div className="modal-actions--double">
                    <button type="button" className="btn btn-secondary" onClick={() => blocker.proceed?.()}>
                      Forlad uden at gemme
                    </button>
                    <button type="button" className="btn btn-primary" onClick={() => { void handleSaveAndLeave(); }}>
                      {saveFailed ? 'Prøv at gemme igen' : 'Gem og forlad'}
                    </button>
                  </div>
                  <button type="button" className="btn btn-secondary" onClick={handleCancel}>
                    Annuller
                  </button>
                </>
              ) : (
                <div className="modal-actions--double">
                  <button type="button" className="btn btn-secondary" onClick={handleCancel}>
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
