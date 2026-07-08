import { useCallback, useState } from 'react';
import { useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useBlocker } from 'react-router-dom';
import { Loader2 } from 'lucide-react';

type NavigationGuardProps = {
  when: boolean;
  title?: string;
  message?: string;
  onSave?: () => void | Promise<unknown>;
};

export function NavigationGuard({
  when,
  title = 'Ugemte ændringer',
  message = 'Der er ugemte ændringer. Er du sikker på, at du vil forlade denne side?',
  onSave,
}: NavigationGuardProps) {
  const blocker = useBlocker(when);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    if (!when) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener('beforeunload', handler);
    return () => window.removeEventListener('beforeunload', handler);
  }, [when]);

  const handleSaveAndLeave = useCallback(async () => {
    if (!onSave) {
      blocker.proceed();
      return;
    }
    setIsSaving(true);
    try {
      await onSave();
    } catch {
      // proceed anyway
    }
    blocker.proceed();
  }, [blocker, onSave]);

  if (blocker.state !== 'blocked') return null;

  return createPortal(
    <div className="modal-backdrop" onClick={() => { if (!isSaving) blocker.reset(); }}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label={title}
      >
        <h3>{title}</h3>
        <p>{message}</p>
        <div className={`modal-actions ${onSave ? 'modal-actions--triple' : 'modal-actions--double'}`}>
          {isSaving ? (
            <div className="saving-indicator">
              <Loader2 className="animate-spin" size={18} />
              <span>Gemmer...</span>
            </div>
          ) : (
            <>
              <button type="button" className="btn btn-secondary" onClick={() => blocker.reset()}>
                Annuller
              </button>
              {onSave && (
                <button type="button" className="btn btn-secondary" onClick={() => blocker.proceed()}>
                  Forlad uden at gemme
                </button>
              )}
              <button type="button" className="btn btn-primary" onClick={handleSaveAndLeave}>
                {onSave ? 'Gem og forlad' : 'Forlad siden'}
              </button>
            </>
          )}
        </div>
      </div>
    </div>,
    document.getElementById('portal-root') ?? document.body,
  );
}
