import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useBlocker } from 'react-router-dom';
import { Loader2 } from 'lucide-react';

type NavigationGuardProps = {
  when: boolean;
  title?: string;
  message?: string;
  onSave?: () => void | Promise<unknown>;
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
  const autoSaveOnLeaveRef = useRef(autoSaveOnLeave);
  const autoSaveStartedRef = useRef(false);
  const mountedRef = useRef(true);
  const isAutoSaveMode = Boolean(autoSaveOnLeave);
  const dialogTitle = isAutoSaveMode ? 'Gemmer ændringer' : title;
  const dialogMessage = isAutoSaveMode
    ? 'Dine ændringer gemmes automatisk, før du forlader siden.'
    : message;

  useEffect(() => {
    autoSaveOnLeaveRef.current = autoSaveOnLeave;
  }, [autoSaveOnLeave]);

  useEffect(() => () => {
    mountedRef.current = false;
  }, []);

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
    setIsSaving(true);
    try {
      const saved = await save();
      if (saved === false) {
        blocker.reset();
        return;
      }
      blocker.proceed?.();
    } catch {
      blocker.reset();
    } finally {
      if (mountedRef.current) setIsSaving(false);
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
    setIsSaving(true);
    try {
      await onSave();
    } catch {
      // proceed anyway
    }
    blocker.proceed?.();
  }, [blocker, onSave]);

  if (blocker.state !== 'blocked') return null;

  return createPortal(
    <div
      className="modal-backdrop"
      onClick={() => {
        if (!isSaving && !isAutoSaveMode) blocker.reset();
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
                    <button type="button" className="btn btn-secondary" onClick={() => blocker.proceed()}>
                      Forlad uden at gemme
                    </button>
                    <button type="button" className="btn btn-primary" onClick={handleSaveAndLeave}>
                      Gem og forlad
                    </button>
                  </div>
                  <button type="button" className="btn btn-secondary" onClick={() => blocker.reset()}>
                    Annuller
                  </button>
                </>
              ) : (
                <div className="modal-actions--double">
                  <button type="button" className="btn btn-secondary" onClick={() => blocker.reset()}>
                    Annuller
                  </button>
                  <button type="button" className="btn btn-primary" onClick={handleSaveAndLeave}>
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
