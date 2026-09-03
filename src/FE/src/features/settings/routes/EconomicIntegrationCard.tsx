import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { useSearchParams } from 'react-router-dom';
import {
  AlertCircle,
  CheckCircle2,
  ExternalLink,
  Landmark,
  Loader2,
  Unplug,
} from 'lucide-react';
import { ConfirmDialog } from '../../../components/common/ConfirmDialog';
import { ErrorState } from '../../../components/ErrorState';
import { notify } from '../../../lib/toast';
import {
  useDisconnectEconomic,
  useEconomicConnection,
  useStartEconomicConnection,
} from '../api';

export const EconomicIntegrationCard = () => {
  const queryClient = useQueryClient();
  const [searchParams, setSearchParams] = useSearchParams();
  const [confirmDisconnect, setConfirmDisconnect] = useState(false);
  const connection = useEconomicConnection();
  const connectMutation = useStartEconomicConnection();
  const disconnectMutation = useDisconnectEconomic();
  const callbackResult = searchParams.get('economic');

  useEffect(() => {
    if (!callbackResult) return;

    if (callbackResult === 'connected') {
      notify.success('e-conomic er forbundet');
      void queryClient.invalidateQueries({ queryKey: ['/api/accounting/economic/connection'] });
      void queryClient.invalidateQueries({ queryKey: ['/api/accounting/status'] });
    } else if (callbackResult === 'error') {
      notify.error('e-conomic kunne ikke forbindes. Prøv igen.');
    }

    const next = new URLSearchParams(searchParams);
    next.delete('economic');
    setSearchParams(next, { replace: true });
  }, [callbackResult, queryClient, searchParams, setSearchParams]);

  const connect = async () => {
    try {
      const start = await connectMutation.mutateAsync();
      if (!start.installationUrl) {
        notify.error('e-conomic forbindelseslink mangler');
        return;
      }
      window.location.assign(start.installationUrl);
    } catch {
      notify.error('Kunne ikke starte e-conomic forbindelsen');
    }
  };

  const disconnect = async () => {
    try {
      await disconnectMutation.mutateAsync();
      setConfirmDisconnect(false);
      await queryClient.invalidateQueries({ queryKey: ['/api/accounting/economic/connection'] });
      await queryClient.invalidateQueries({ queryKey: ['/api/accounting/status'] });
      notify.success('e-conomic forbindelsen er afbrudt');
    } catch {
      notify.error('Kunne ikke afbryde e-conomic forbindelsen');
    }
  };

  const data = connection.data;
  const isBusy = connectMutation.isPending || disconnectMutation.isPending;

  return (
    <>
      <div className="section-card" style={{ marginTop: '1rem' }}>
        <div
          style={{
            display: 'flex',
            alignItems: 'flex-start',
            justifyContent: 'space-between',
            gap: '1rem',
            flexWrap: 'wrap',
          }}
        >
          <div>
            <h3 className="section-card-title" style={{ marginBottom: '0.35rem' }}>
              <Landmark size={18} aria-hidden="true" />
              Integrationer
            </h3>
            <p className="subtitle" style={{ margin: 0 }}>
              Kobl Workslip direkte til økonomisystemet. Bogføring, moms og bank bliver fortsat i e-conomic.
            </p>
          </div>

          {data?.connected && (
            <span
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '0.4rem',
                padding: '0.35rem 0.65rem',
                borderRadius: '999px',
                fontSize: '0.82rem',
                fontWeight: 650,
                background: 'var(--success-soft, rgba(22, 163, 74, 0.1))',
              }}
            >
              <CheckCircle2 size={15} aria-hidden="true" />
              Forbundet
            </span>
          )}
        </div>

        {connection.isLoading && (
          <div style={{ padding: '1rem 0', display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <Loader2 size={16} className="spin" aria-hidden="true" />
            <span className="subtitle">Henter integrationsstatus...</span>
          </div>
        )}

        {connection.isError && <ErrorState message="Kunne ikke hente e-conomic status" />}

        {data && (
          <div
            style={{
              marginTop: '1rem',
              padding: '1rem',
              border: '1px solid var(--border-color, rgba(0,0,0,0.1))',
              borderRadius: '0.85rem',
              display: 'grid',
              gap: '0.8rem',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.7rem' }}>
              <div
                aria-hidden="true"
                style={{
                  width: 38,
                  height: 38,
                  borderRadius: 10,
                  display: 'grid',
                  placeItems: 'center',
                  fontWeight: 800,
                  background: 'var(--surface-secondary, rgba(0,0,0,0.04))',
                }}
              >
                e
              </div>
              <div style={{ minWidth: 0 }}>
                <strong>e-conomic</strong>
                <div className="subtitle" style={{ margin: 0, fontSize: '0.85rem' }}>
                  {data.connected
                    ? data.companyName || (data.agreementNumber ? `Aftale ${data.agreementNumber}` : 'Forbundet til e-conomic')
                    : 'Kunder, fakturakladder, fakturastatus og bilag'}
                </div>
              </div>
            </div>

            {data.connected ? (
              <>
                <div
                  style={{
                    display: 'grid',
                    gridTemplateColumns: 'repeat(auto-fit, minmax(150px, 1fr))',
                    gap: '0.75rem',
                  }}
                >
                  {data.agreementNumber && (
                    <div>
                      <div className="subtitle" style={{ margin: 0, fontSize: '0.75rem' }}>Aftalenummer</div>
                      <strong>{data.agreementNumber}</strong>
                    </div>
                  )}
                  {data.connectedAt && (
                    <div>
                      <div className="subtitle" style={{ margin: 0, fontSize: '0.75rem' }}>Forbundet</div>
                      <strong>
                        {new Date(data.connectedAt).toLocaleDateString('da-DK', {
                          day: 'numeric',
                          month: 'short',
                          year: 'numeric',
                        })}
                      </strong>
                    </div>
                  )}
                </div>

                <div style={{ display: 'flex', gap: '0.65rem', flexWrap: 'wrap' }}>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={connect}
                    disabled={isBusy || !data.available}
                  >
                    {connectMutation.isPending
                      ? <Loader2 size={16} className="spin" aria-hidden="true" />
                      : <ExternalLink size={16} aria-hidden="true" />}
                    Forbind igen
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => setConfirmDisconnect(true)}
                    disabled={isBusy}
                  >
                    <Unplug size={16} aria-hidden="true" />
                    Afbryd forbindelse
                  </button>
                </div>
              </>
            ) : data.available ? (
              <>
                <p className="subtitle" style={{ margin: 0 }}>
                  Du sendes til e-conomic for at godkende Workslip og kommer automatisk tilbage hertil. Du skal ikke kopiere tokens eller konfigurere felter manuelt.
                </p>
                <div>
                  <button
                    type="button"
                    className="btn btn-primary"
                    onClick={connect}
                    disabled={isBusy}
                  >
                    {connectMutation.isPending
                      ? <Loader2 size={16} className="spin" aria-hidden="true" />
                      : <ExternalLink size={16} aria-hidden="true" />}
                    {connectMutation.isPending ? 'Åbner e-conomic...' : 'Forbind e-conomic'}
                  </button>
                </div>
              </>
            ) : (
              <div
                role="status"
                style={{
                  display: 'flex',
                  alignItems: 'flex-start',
                  gap: '0.55rem',
                  padding: '0.8rem',
                  borderRadius: '0.7rem',
                  background: 'var(--warning-soft, rgba(245, 158, 11, 0.1))',
                }}
              >
                <AlertCircle size={17} aria-hidden="true" style={{ flex: '0 0 auto', marginTop: 2 }} />
                <span>
                  e-conomic app-forbindelsen mangler den globale MR Software-konfiguration. Ingen kunde-token skal indtastes her.
                </span>
              </div>
            )}
          </div>
        )}
      </div>

      <ConfirmDialog
        open={confirmDisconnect}
        title="Afbryd e-conomic"
        message="Workslip mister adgang til e-conomic, men ingen data eller bogføring slettes i e-conomic. Du kan forbinde igen senere."
        confirmLabel="Afbryd forbindelse"
        pendingLabel="Afbryder…"
        variant="danger"
        onConfirm={disconnect}
        onClose={() => setConfirmDisconnect(false)}
      />
    </>
  );
};
