import { useDeferredValue, useState, type FormEvent } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Building2, Mail, Pencil, Trash2, UserPlus, Users } from 'lucide-react';
import { ConfirmDeleteDialog } from '../../../components/common/ConfirmDeleteDialog';
import { SearchBar } from '../../../components/filters/SearchBar';
import { UserRoleBadge } from '../../users/components/UserRoleBadge';
import {
  createSuperadminUser,
  deleteSuperadminUser,
  getSuperadminErrorMessage,
  getSuperadminUserOptions,
  getSuperadminUsers,
  superadminUserOptionsQueryKey,
  superadminUserQueryKey,
  updateSuperadminUser,
} from '../api';
import type {
  CreateSuperAdminUserInput,
  SuperAdminOrganizationOption,
  SuperAdminUser,
  UpdateSuperAdminUserInput,
} from '../types';
import './SuperAdminUsersPanel.css';

const PAGE_SIZE = 50;
const MEMBER_USER_KIND = 'Member';
const INTERNAL_TEST_USER_KIND = 'InternalTest';

type UserDraft = CreateSuperAdminUserInput;

const emptyDraft: UserDraft = {
  organizationId: '',
  filialId: '',
  email: '',
  displayName: '',
  phone: '',
  role: 'User',
  userKind: MEMBER_USER_KIND,
};

export function SuperAdminUsersPanel() {
  const queryClient = useQueryClient();
  const [search, setSearch] = useState('');
  const deferredSearch = useDeferredValue(search.trim());
  const [page, setPage] = useState(1);
  const [draft, setDraft] = useState<UserDraft>(emptyDraft);
  const [editingUser, setEditingUser] = useState<SuperAdminUser | null>(null);
  const [formOpen, setFormOpen] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<SuperAdminUser | null>(null);

  const optionsQuery = useQuery({
    queryKey: superadminUserOptionsQueryKey,
    queryFn: getSuperadminUserOptions,
  });

  const usersQuery = useQuery({
    queryKey: [...superadminUserQueryKey, { page, search: deferredSearch }],
    queryFn: () => getSuperadminUsers({
      limit: PAGE_SIZE,
      offset: (page - 1) * PAGE_SIZE,
      search: deferredSearch || undefined,
    }),
  });

  const createMutation = useMutation({
    mutationFn: createSuperadminUser,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: superadminUserQueryKey });
      closeForm();
    },
    onError: (error) => setFormError(getSuperadminErrorMessage(error)),
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, input }: { id: string; input: UpdateSuperAdminUserInput }) =>
      updateSuperadminUser(id, input),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: superadminUserQueryKey });
      closeForm();
    },
    onError: (error) => setFormError(getSuperadminErrorMessage(error)),
  });

  const deleteMutation = useMutation({
    mutationFn: deleteSuperadminUser,
    onSuccess: async () => {
      setDeleteTarget(null);
      await queryClient.invalidateQueries({ queryKey: superadminUserQueryKey });
    },
  });

  const organizations = optionsQuery.data?.organizations ?? [];
  const roles = optionsQuery.data?.roles ?? [];
  const userKinds = optionsQuery.data?.userKinds ?? [];
  const total = usersQuery.data?.total ?? 0;
  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  const beginCreate = () => {
    const organization = organizations[0];
    const filial = organization ? getPreferredFilial(organization) : undefined;
    setEditingUser(null);
    setDraft({
      ...emptyDraft,
      organizationId: organization?.id ?? '',
      filialId: filial?.id ?? '',
      role: roles[0] ?? 'User',
      userKind: userKinds.includes(MEMBER_USER_KIND)
        ? MEMBER_USER_KIND
        : userKinds[0] ?? MEMBER_USER_KIND,
    });
    setFormError(null);
    setFormOpen(true);
  };

  const beginEdit = (user: SuperAdminUser) => {
    setEditingUser(user);
    setDraft({
      organizationId: user.organizationId,
      filialId: user.filialId,
      email: user.email,
      displayName: user.displayName,
      phone: user.phone ?? '',
      role: user.role,
      userKind: user.userKind,
    });
    setFormError(null);
    setFormOpen(true);
  };

  const closeForm = () => {
    setFormOpen(false);
    setEditingUser(null);
    setDraft(emptyDraft);
    setFormError(null);
  };

  const handleOrganizationChange = (organizationId: string) => {
    const organization = organizations.find((item) => item.id === organizationId);
    const filial = organization ? getPreferredFilial(organization) : undefined;
    setDraft((current) => ({
      ...current,
      organizationId,
      filialId: filial?.id ?? '',
    }));
  };

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    setFormError(null);

    if (!draft.displayName.trim() || !draft.role || !draft.filialId || !draft.userKind) {
      setFormError('Udfyld navn, rolle, brugergruppe og filial.');
      return;
    }

    if (editingUser) {
      updateMutation.mutate({
        id: editingUser.id,
        input: {
          displayName: draft.displayName.trim(),
          phone: draft.phone.trim(),
          role: draft.role,
          filialId: draft.filialId,
          userKind: draft.userKind,
        },
      });
      return;
    }

    if (!draft.organizationId || !draft.email.trim()) {
      setFormError('Vælg organisation og udfyld e-mail.');
      return;
    }

    createMutation.mutate({
      ...draft,
      email: draft.email.trim(),
      displayName: draft.displayName.trim(),
      phone: draft.phone.trim(),
    });
  };

  const selectedOrganization = organizations.find((organization) => organization.id === draft.organizationId);
  const availableFilials = selectedOrganization?.filials ?? [];
  const isSaving = createMutation.isPending || updateMutation.isPending;

  return (
    <section className="superadmin-users-panel" aria-labelledby="superadmin-users-title">
      <div className="superadmin-users-header">
        <div className="superadmin-users-heading">
          <span className="superadmin-card-icon" aria-hidden="true">
            <Users size={21} />
          </span>
          <div>
            <h2 id="superadmin-users-title">Brugere</h2>
            <p>Administrér tenant-brugere på tværs af organisationer, filialer og brugergrupper.</p>
          </div>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          onClick={beginCreate}
          disabled={optionsQuery.isLoading || organizations.length === 0}
        >
          <UserPlus size={16} aria-hidden="true" />
          Ny bruger
        </button>
      </div>

      <div className="superadmin-users-toolbar">
        <SearchBar
          value={search}
          onChange={(value) => {
            setSearch(value);
            setPage(1);
          }}
          placeholder="Søg navn, e-mail, organisation, filial, rolle eller brugergruppe..."
        />
        <span className="superadmin-users-count">{total} {total === 1 ? 'bruger' : 'brugere'}</span>
      </div>

      {optionsQuery.isError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          <span>{getSuperadminErrorMessage(optionsQuery.error)}</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void optionsQuery.refetch(); }}>
            Prøv igen
          </button>
        </div>
      )}

      {formOpen && (
        <form className="superadmin-user-form" onSubmit={handleSubmit}>
          <div className="superadmin-user-form-header">
            <div>
              <h3>{editingUser ? 'Redigér bruger' : 'Opret bruger'}</h3>
              {editingUser && <p>{editingUser.email}</p>}
            </div>
            <button type="button" className="btn btn-secondary" onClick={closeForm} disabled={isSaving}>
              Annuller
            </button>
          </div>

          <div className="superadmin-user-form-grid">
            <label className="form-field">
              <span>Organisation</span>
              {editingUser ? (
                <input className="form-input" value={editingUser.organizationName} disabled />
              ) : (
                <select
                  className="form-input"
                  value={draft.organizationId}
                  onChange={(event) => handleOrganizationChange(event.target.value)}
                  required
                >
                  {organizations.map((organization) => (
                    <option key={organization.id} value={organization.id}>{organization.name}</option>
                  ))}
                </select>
              )}
            </label>

            <label className="form-field">
              <span>Filial</span>
              <select
                className="form-input"
                value={draft.filialId}
                onChange={(event) => setDraft((current) => ({ ...current, filialId: event.target.value }))}
                required
              >
                {availableFilials.map((filial) => (
                  <option key={filial.id} value={filial.id}>
                    {filial.name}{filial.isDefault ? ' · standard' : ''}
                  </option>
                ))}
              </select>
            </label>

            <label className="form-field">
              <span>Navn</span>
              <input
                className="form-input"
                type="text"
                value={draft.displayName}
                onChange={(event) => setDraft((current) => ({ ...current, displayName: event.target.value }))}
                maxLength={256}
                required
              />
            </label>

            <label className="form-field">
              <span>E-mail</span>
              <input
                className="form-input"
                type="email"
                value={draft.email}
                onChange={(event) => setDraft((current) => ({ ...current, email: event.target.value }))}
                disabled={Boolean(editingUser)}
                maxLength={256}
                required
              />
            </label>

            <label className="form-field">
              <span>Telefon</span>
              <input
                className="form-input"
                type="tel"
                value={draft.phone}
                onChange={(event) => setDraft((current) => ({ ...current, phone: event.target.value }))}
                maxLength={20}
              />
            </label>

            <label className="form-field">
              <span>Rolle</span>
              <select
                className="form-input"
                value={draft.role}
                onChange={(event) => setDraft((current) => ({ ...current, role: event.target.value }))}
                required
              >
                {roles.map((role) => <option key={role} value={role}>{role}</option>)}
              </select>
            </label>

            <label className="form-field">
              <span>Brugergruppe</span>
              <select
                className="form-input"
                value={draft.userKind}
                onChange={(event) => setDraft((current) => ({ ...current, userKind: event.target.value }))}
                required
              >
                {userKinds.map((userKind) => (
                  <option key={userKind} value={userKind}>{getUserKindLabel(userKind)}</option>
                ))}
              </select>
            </label>
          </div>

          {formError && <div className="superadmin-alert superadmin-alert-error" role="alert">{formError}</div>}

          <div className="superadmin-user-form-actions">
            <button type="submit" className="btn btn-primary" disabled={isSaving}>
              {isSaving ? 'Gemmer...' : editingUser ? 'Gem ændringer' : 'Opret bruger'}
            </button>
          </div>
        </form>
      )}

      {usersQuery.isError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          <span>{getSuperadminErrorMessage(usersQuery.error)}</span>
          <button type="button" className="btn btn-secondary" onClick={() => { void usersQuery.refetch(); }}>
            Prøv igen
          </button>
        </div>
      )}

      {deleteMutation.isError && (
        <div className="superadmin-alert superadmin-alert-error" role="alert">
          {getSuperadminErrorMessage(deleteMutation.error)}
        </div>
      )}

      {usersQuery.isLoading ? (
        <div className="superadmin-empty" role="status">Indlæser brugere...</div>
      ) : (usersQuery.data?.users.length ?? 0) === 0 ? (
        <div className="superadmin-empty">Ingen brugere matcher søgningen.</div>
      ) : (
        <div className="superadmin-user-cards">
          {usersQuery.data?.users.map((user) => (
            <article key={user.id} className="superadmin-user-card">
              <div className="superadmin-user-card-main">
                <div className="superadmin-user-card-name">
                  <strong>{user.displayName}</strong>
                  <UserRoleBadge role={user.role} />
                </div>
                <span className="superadmin-user-meta"><Mail size={14} aria-hidden="true" />{user.email}</span>
                <span className="superadmin-user-meta">
                  <Building2 size={14} aria-hidden="true" />
                  {user.organizationName} · {user.filialName}
                </span>
                {user.userKind === INTERNAL_TEST_USER_KIND && (
                  <span className="superadmin-user-meta">Brugergruppe: Intern test</span>
                )}
              </div>
              <div className="superadmin-user-card-actions">
                <button type="button" className="btn btn-secondary" onClick={() => beginEdit(user)}>
                  <Pencil size={15} aria-hidden="true" />
                  Redigér
                </button>
                <button
                  type="button"
                  className="btn btn-secondary danger"
                  onClick={() => setDeleteTarget(user)}
                  disabled={deleteMutation.isPending}
                >
                  <Trash2 size={15} aria-hidden="true" />
                  Slet
                </button>
              </div>
            </article>
          ))}
        </div>
      )}

      {totalPages > 1 && (
        <div className="superadmin-users-pagination">
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setPage((current) => Math.max(1, current - 1))}
            disabled={page === 1 || usersQuery.isFetching}
          >
            Forrige
          </button>
          <span>Side {page} af {totalPages}</span>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={() => setPage((current) => Math.min(totalPages, current + 1))}
            disabled={page >= totalPages || usersQuery.isFetching}
          >
            Næste
          </button>
        </div>
      )}

      <ConfirmDeleteDialog
        open={deleteTarget !== null}
        title="Slet bruger"
        message={deleteTarget
          ? `Er du sikker på, at ${deleteTarget.displayName} skal slettes fra Workslip? Brugere med sags- eller timeseddelhistorik kan ikke slettes.`
          : ''}
        onConfirm={() => {
          if (deleteTarget) deleteMutation.mutate(deleteTarget.id);
        }}
        onClose={() => setDeleteTarget(null)}
      />
    </section>
  );
}

function getPreferredFilial(organization: SuperAdminOrganizationOption) {
  return organization.filials.find((filial) => filial.isDefault) ?? organization.filials[0];
}

function getUserKindLabel(userKind: string) {
  return userKind === INTERNAL_TEST_USER_KIND ? 'Intern test' : 'Kunde';
}
