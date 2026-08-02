import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Activity, ArrowRight, Building2, CheckCircle2, Gauge, RefreshCw, ShieldCheck } from 'lucide-react';
import { lazy, Suspense, useMemo, useState } from 'react';
import { ErrorBoundary } from 'react-error-boundary';
import { useNavigate } from 'react-router-dom';
import { reportFrontendError } from '../../../applicationInsights';
import { notify } from '../../../lib/toast';
import {
  createOrganization,
  createOrganizationSession,
  getOrganizations,
  getSuperadminErrorMessage,
  inviteOrganizationAdmin,
  superadminOrganizationQueryKey,
} from '../api';
import { AdminInviteForm } from '../components/AdminInviteForm';
import { OrganizationCreateForm } from '../components/OrganizationCreateForm';
import {
  activateOrganizationSession,
  getOrganizationSession,
} from '../organizationSession';
import type {
  CreateOrganizationInput,
  InviteOrganizationAdminInput,
  Organization,
  OrganizationAdmin,
} from '../types';
import './SuperAdmin.css';
import './SuperAdminDiagnosticsEntry.css';

const ErrorDiagnosticsDashboard = lazy(() =>
  import('../diagnostics/ErrorDiagnosticsDashboard').then((module) => ({
    default: module.ErrorDiagnosticsDashboard,
  })),
);

export function SuperAdmin() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [requestedOrganizationId, setRequestedOrganizationId] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [sessionError, setSessionError] = useState<string | null>(null);
  const [lastAdminResult, setLastAdminResult] = useState<OrganizationAdmin | null>(null);
  const [showDiagnostics, setShowDiagnostics] = useState(false);

  const organizationsQuery = useQuery({
    queryKey: superadminOrganizationQueryKey,
    queryFn: getOrganizations,
  });

  const organizations = useMemo(
    () => [...(organizationsQuery.data ?? [])].sort((left, right) => left.name.localeCompare(right.name, 'da')),
    [organizationsQuery.data],
  );

  const selectedOrganizationId = organizations.some(
    (organization) => organization.id === requestedOrganizationId,
  )
    ? requestedOrganizationId
    : organizations[0]?.id ?? '';

  const activeOrganizationSession = getOrganizationSession();

  const createMutation = useMutation({
    mutationFn: createOrganization,
  });

  const inviteMutation = useMutation({
    mutationFn: inviteOrganizationAdmin,
  });

  const sessionMutation = useMutation({
    mutationFn: createOrganizationSession,
  });

  const handleCreateOrganization = async (input: CreateOrganizationInput) => {
    setCreateError(null);
    setLastAdminResult(null);

    try {
      const created = await createMutation.mutateAsync(input);
      queryClient.setQueryData<Organization[]>(superadminOrganizationQueryKey, (current = []) => {
        const withoutCreated = current.filter((organization) => organization.id !== created.organization.id);
        return [...withoutCreated, created.organization];
      });
      setRequestedOrganizationId(created.organization.id);
      notify.success(`${created.organization.name} er oprettet`);
    } catch (error) {
      const message = getSuperadminErrorMessage(error);
      setCreateError(message);
      throw error;
    }
  };

  const handleInviteAdmin = async (input: InviteOrganizationAdminInput) => {
    setInviteError(null);
    setLastAdminResult(null);

    try {
      const admin = await inviteMutation.mutateAsync(input);
      setLastAdminResult(admin);
      notify.success(
        admin.entraInvitationSent
          ? `Entra-invitation sendt til ${admin.email}`
          : `${admin.displayName} er opdateret som administrator`,
      );
    } catch (error) {
      const message = getSuperadminErrorMessage(error);
      setInviteError(message);
      throw error;
    }
  };

  const selectedOrganization = organizations.find(
    (organization) => organization.id === selectedOrganizationId,
  );

  const handleOpenOrganization = async () => {
    if (!selectedOrganization) return;

    setSessionError(null);
    try {
      const session = await sessionMutation.mutateAsync(selectedOrganization.id);
      activateOrganizationSession(
        {
          id: selectedOrganization.id,
          name: selectedOrganization.name,
        },
        session.token,
      );

      // Tenant query keys are not consistently organization-prefixed. A full
      // cache clear and navigation prevents data from the previous effective
      // organization from appearing during the switch.
      queryClient.clear();
      window.location.assign('/app');
    } catch (error) {
      setSessionError(getSuperadminErrorMessage(error));
    }
  };

  return (
    <div className="page-container superadmin-page">
      <header className="superadmin-page-header">
        <div className="superadmin-page-heading">
          <span className="superadmin-page-icon" aria-hidden="true">
            <ShieldCheck size={27} />
          </span>
          <div>
            <h1>Superadmin</h1>
            <p>Administrér organisationer, og åbn en tidsbegrænset organisationssession.</p>
          </div>
        </div>
        <div className="superadmin-header-actions">
          <button
            type="button"
            className="btn btn-secondary superadmin-refresh"
            onClick={() => navigate('/superadmin/cache')}
          >
            <Gauge size={16} aria-hidden="true" />
            <span>Cache</span>
          </button>
          <button
            type="button"
            className="btn btn-secondary superadmin-refresh"
            onClick={() => { void organizationsQuery.refetch(); }}
            disabled={organizationsQuery.isFetching}
          >
            <RefreshCw
              size={16}
              className={organizationsQuery.isFetching ? 'animate-spin' : undefined}
              aria-hidden="true"
            />
            <span>Genindlæs</span>
          </button>
        </div>
      </header>

      {organizationsQuery.isError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          <span>{getSuperadminErrorMessage(organizationsQuery.error)}</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void organizationsQuery.refetch(); }}>
            Prøv igen
          </button>
        </div>
      )}

      <div className="superadmin-overview" aria-live="polite">
        <div>
          <span className="superadmin-overview-label">Organisationer</span>
          <strong>{organizationsQuery.isLoading ? '—' : organizations.length}</strong>
        </div>
        <div>
          <span className="superadmin-overview-label">Valgt organisation</span>
          <strong>{selectedOrganization?.name ?? 'Ingen valgt'}</strong>
        </div>
        <div>
          <span className="superadmin-overview-label">Aktiv session</span>
          <strong>{activeOrganizationSession?.name ?? 'Superadmin-hjemmeorganisation'}</strong>
        </div>
      </div>

      <section className="superadmin-diagnostics-entry" aria-labelledby="diagnostics-entry-title">
        <span className="superadmin-card-icon" aria-hidden="true">
          <Activity size={21} />
        </span>
        <div>
          <h2 id="diagnostics-entry-title">Fejl og driftshændelser</h2>
          <p>Se sanitiserede frontend- og backendfejl fra Application Insights.</p>
        </div>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => setShowDiagnostics((current) => !current)}
          aria-expanded={showDiagnostics}
          aria-controls="superadmin-error-dashboard"
        >
          {showDiagnostics ? 'Skjul dashboard' : 'Åbn dashboard'}
          <ArrowRight size={16} aria-hidden="true" />
        </button>
      </section>

      {showDiagnostics && (
        <section id="superadmin-error-dashboard" className="superadmin-diagnostics-dashboard">
          <ErrorBoundary
            onError={(error, info) => reportFrontendError(
              error,
              'superadmin.diagnostics-boundary',
              { componentStack: info.componentStack ?? '' },
            )}
            fallbackRender={({ resetErrorBoundary }) => (
              <div className="superadmin-alert superadmin-alert-error" role="alert">
                <span>Fejldashboardet kunne ikke vises. Resten af Superadmin fungerer stadig.</span>
                <div>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => {
                      resetErrorBoundary();
                      setShowDiagnostics(false);
                    }}
                  >
                    Luk dashboard
                  </button>
                  <button
                    type="button"
                    className="btn btn-secondary"
                    onClick={() => window.location.reload()}
                  >
                    Genindlæs appen
                  </button>
                </div>
              </div>
            )}
          >
            <Suspense fallback={<div className="superadmin-empty" role="status">Indlæser fejldashboard...</div>}>
              <ErrorDiagnosticsDashboard />
            </Suspense>
          </ErrorBoundary>
        </section>
      )}

      <div className="superadmin-grid">
        <div>
          <OrganizationCreateForm
            isSubmitting={createMutation.isPending}
            onSubmit={handleCreateOrganization}
          />
          {createError && (
            <div className="superadmin-alert superadmin-alert-error" role="alert">
              {createError}
            </div>
          )}
        </div>

        <div>
          <AdminInviteForm
            organizations={organizations}
            selectedOrganizationId={selectedOrganizationId}
            isSubmitting={inviteMutation.isPending}
            onOrganizationChange={(organizationId) => {
              setRequestedOrganizationId(organizationId);
              setInviteError(null);
              setSessionError(null);
              setLastAdminResult(null);
            }}
            onSubmit={handleInviteAdmin}
          />
          {inviteError && (
            <div className="superadmin-alert superadmin-alert-error" role="alert">
              {inviteError}
            </div>
          )}
          {lastAdminResult && (
            <div className="superadmin-alert superadmin-alert-success" role="status">
              <CheckCircle2 size={18} aria-hidden="true" />
              <span>
                {lastAdminResult.entraInvitationSent
                  ? `Invitationen er sendt til ${lastAdminResult.email}.`
                  : `${lastAdminResult.displayName} havde allerede en Entra-konto og er nu administrator.`}
              </span>
            </div>
          )}
        </div>
      </div>

      <section className="superadmin-organization-list" aria-labelledby="organization-list-title">
        <div className="superadmin-list-header">
          <div>
            <h2 id="organization-list-title">Organisationer</h2>
            <p>Den valgte organisation åbnes med et kortlivet token. Din Superadmin-identitet bevares.</p>
          </div>
          <button
            type="button"
            className="btn btn-primary superadmin-open-organization"
            onClick={() => { void handleOpenOrganization(); }}
            disabled={!selectedOrganization || sessionMutation.isPending}
          >
            <span>{sessionMutation.isPending ? 'Åbner...' : 'Åbn organisation'}</span>
            <ArrowRight size={16} aria-hidden="true" />
          </button>
        </div>

        {sessionError && (
          <div className="superadmin-alert superadmin-alert-error" role="alert">
            {sessionError}
          </div>
        )}

        {organizationsQuery.isLoading ? (
          <div className="superadmin-empty" role="status">Indlæser organisationer...</div>
        ) : organizations.length === 0 ? (
          <div className="superadmin-empty">
            <Building2 size={24} aria-hidden="true" />
            <span>Der er endnu ikke oprettet organisationer.</span>
          </div>
        ) : (
          <div className="superadmin-organization-cards">
            {organizations.map((organization) => {
              const isSelected = organization.id === selectedOrganizationId;
              const isActive = organization.id === activeOrganizationSession?.id;
              return (
                <button
                  key={organization.id}
                  type="button"
                  className={`superadmin-organization-card${isSelected ? ' selected' : ''}`}
                  onClick={() => {
                    setRequestedOrganizationId(organization.id);
                    setInviteError(null);
                    setSessionError(null);
                    setLastAdminResult(null);
                  }}
                  aria-pressed={isSelected}
                >
                  <span className="superadmin-organization-name">{organization.name}</span>
                  <span className="superadmin-organization-cvr">
                    CVR {organization.cvr}{isActive ? ' · Aktiv session' : ''}
                  </span>
                </button>
              );
            })}
          </div>
        )}
      </section>
    </div>
  );
}
