import { useEffect, useState } from 'react';
import {
  PWA_UPDATE_APPLYING_EVENT,
  PWA_UPDATE_READY_EVENT,
  requestPwaUpdate,
} from '../../lib/pwaUpdateEvents';

type UpdateState = 'hidden' | 'ready' | 'applying';

export function PwaUpdateBanner() {
  const [updateState, setUpdateState] = useState<UpdateState>('hidden');

  useEffect(() => {
    const handleUpdateReady = () => setUpdateState('ready');
    const handleUpdateApplying = () => setUpdateState('applying');

    window.addEventListener(PWA_UPDATE_READY_EVENT, handleUpdateReady);
    window.addEventListener(PWA_UPDATE_APPLYING_EVENT, handleUpdateApplying);

    return () => {
      window.removeEventListener(PWA_UPDATE_READY_EVENT, handleUpdateReady);
      window.removeEventListener(PWA_UPDATE_APPLYING_EVENT, handleUpdateApplying);
    };
  }, []);

  if (updateState === 'hidden') return null;

  const isApplying = updateState === 'applying';

  return (
    <aside
      className="pwa-update-banner"
      role="region"
      aria-label="Appopdatering"
      aria-live="polite"
      aria-atomic="true"
    >
      <div className="pwa-update-banner-copy">
        <strong>Ny version klar</strong>
        <span>{isApplying ? 'Opdaterer appen...' : 'Appen opdateres automatisk om få sekunder.'}</span>
      </div>
      <button
        type="button"
        className="btn btn-primary btn-sm pwa-update-banner-button"
        disabled={isApplying}
        onClick={requestPwaUpdate}
      >
        {isApplying ? 'Opdaterer...' : 'Opdater nu'}
      </button>
    </aside>
  );
}
