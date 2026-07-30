import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowRight, Building2, CheckCircle2, RefreshCw, ShieldCheck } from 'lucide-react';
import { useMemo, useState } from 'react';
import { notify } from '../../../lib/toast';
import { isDesktopPlatform } from '../../../lib/platform';
import { useAuth } from '../../../providers/useAuth';
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
  clearOrganizationSession,
  getOrganizationSession,
} from '../organizationSession';
import type {
  CreateOrganizationInput,
  InviteOrganizationAdminInput,
  Organization,
  OrganizationAdmin,
} from '../types';
import './SuperAdmin.css';
import { DesktopOnlySuperadminScreen } from '../components/DesktopOnlySuperadmin';

export function SuperAdmin() {
  const queryClient = useQueryClient();
  const { logout } = useAuth();
  const canUseSuperadmin = isDesktopPlatform();
  const [requestedOrganizationId, setRequestedOrganizationId] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [inviteError, setInviteError] = useState<string | null>(null);
  const [sessionError, setSessionError] = useState<string | null>(null);
  const [lastAdminResult, setLastAdminResult] = useState<OrganizationAdmin | null>(null);

  const organizationsQuery = useQuery({
    queryKey: superadminOrganizationQueryKey,
    queryFn: getOrganizations,
    enabled: canUseSuperadmin,
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
    if (!canUseSuperadmin) return;

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
    if (!canUseSuperadmin) return;

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
    if (!canUseSuperadmin || !selectedOrganization) return;

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

  if (!canUseSuperadmin) {
    return (
      <DesktopOnlySuperadminScreen
        onLogout={() => {
          clearOrganizationSession();
          logout();
        }}
      />
    );
  }

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
