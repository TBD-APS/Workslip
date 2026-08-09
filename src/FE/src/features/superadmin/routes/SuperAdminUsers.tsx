import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Trash2, Users as UsersIcon } from 'lucide-react';
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ConfirmDeleteDialog } from '../../../components/common/ConfirmDeleteDialog';
import { SearchBar } from '../../../components/filters/SearchBar';
import { PaginationControls } from '../../../components/pagination/PaginationControls';
import { notify } from '../../../lib/toast';
import {
  createAdminUser,
  deleteAdminUser,
  getAdminUsers,
  getOrganizations,
  getSuperadminErrorMessage,
  superadminOrganizationQueryKey,
  updateAdminUser,
} from '../api';
import { AdminUserCreateForm, ROLE_OPTIONS } from '../components/AdminUserCreateForm';
import { EditAdminUserDialog } from '../components/EditAdminUserDialog';
import type { AdminUser, CreateAdminUserInput, UpdateAdminUserInput } from '../types';
import './SuperAdminUsers.css';

const PAGE_SIZE = 20;

export function SuperAdminUsers() {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [organizationFilter, setOrganizationFilter] = useState('');
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [createOrganizationId, setCreateOrganizationId] = useState('');
  const [createError, setCreateError] = useState<string | null>(null);
  const [editingUser, setEditingUser] = useState<AdminUser | null>(null);
  const [editError, setEditError] = useState<string | null>(null);
  const [roleSwapError, setRoleSwapError] = useState<string | null>(null);
  const [deletingUser, setDeletingUser] = useState<AdminUser | null>(null);

  const organizationsQuery = useQuery({
    queryKey: superadminOrganizationQueryKey,
    queryFn: getOrganizations,
  });
  const organizations = organizationsQuery.data ?? [];

  const listQueryKey = ['superadmin', 'users', organizationFilter, search, page] as const;
  const usersQuery = useQuery({
    queryKey: listQueryKey,
    queryFn: () => getAdminUsers({
      organizationId: organizationFilter || undefined,
      limit: PAGE_SIZE,
      offset: (page - 1) * PAGE_SIZE,
      search: search || undefined,
      sortBy: 'displayName',
      sortDirection: 'asc',
    }),
  });

  const invalidateUsers = () => queryClient.invalidateQueries({ queryKey: ['superadmin', 'users'] });

  const createMutation = useMutation({ mutationFn: createAdminUser });
  const updateMutation = useMutation({ mutationFn: (args: { id: string; input: UpdateAdminUserInput }) => updateAdminUser(args.id, args.input) });
  const deleteMutation = useMutation({ mutationFn: deleteAdminUser });

  const handleCreate = async (input: CreateAdminUserInput) => {
    setCreateError(null);
    try {
      await createMutation.mutateAsync(input);
      await invalidateUsers();
      notify.success(`${input.displayName} er oprettet.`);
    } catch (error) {
      setCreateError(getSuperadminErrorMessage(error));
      throw error;
    }
  };

  const handleRoleSwap = async (user: AdminUser, role: string) => {
    if (role === user.role) return;
    setRoleSwapError(null);
    try {
      await updateMutation.mutateAsync({ id: user.id, input: { role } });
      await invalidateUsers();
      notify.success(`${user.displayName} har nu rollen ${role}.`);
    } catch (error) {
      setRoleSwapError(getSuperadminErrorMessage(error));
    }
  };

  const handleEditSubmit = async (input: UpdateAdminUserInput) => {
    if (!editingUser) return;
    setEditError(null);
    try {
      await updateMutation.mutateAsync({ id: editingUser.id, input });
      await invalidateUsers();
      notify.success(`${editingUser.displayName} er opdateret.`);
      setEditingUser(null);
    } catch (error) {
      setEditError(getSuperadminErrorMessage(error));
    }
  };

  const handleDelete = async () => {
    if (!deletingUser) return;
    try {
      await deleteMutation.mutateAsync(deletingUser.id);
      await invalidateUsers();
      notify.success(`${deletingUser.displayName} er slettet.`);
      setDeletingUser(null);
    } catch (error) {
      notify.error(getSuperadminErrorMessage(error));
    }
  };

  const users = usersQuery.data?.users ?? [];
  const total = usersQuery.data?.total ?? 0;

  return (
    <div className="page-container superadmin-page superadmin-users-page">
      <header className="superadmin-page-header">
        <div className="superadmin-page-heading">
          <button
            type="button"
            className="superadmin-back-button"
            onClick={() => navigate('/superadmin')}
            aria-label="Tilbage til Superadmin"
            title="Tilbage til Superadmin"
          >
            <ArrowLeft size={18} />
          </button>
          <span className="superadmin-page-icon" aria-hidden="true">
            <UsersIcon size={27} />
          </span>
          <div>
            <h1>Brugere</h1>
            <p>Administrér brugere på tværs af alle organisationer, inklusiv rolleskift.</p>
          </div>
        </div>
      </header>

      <div className="superadmin-grid">
        <AdminUserCreateForm
          organizations={organizations}
          selectedOrganizationId={createOrganizationId}
          isSubmitting={createMutation.isPending}
          onOrganizationChange={setCreateOrganizationId}
          onSubmit={handleCreate}
        />
        {createError && (
          <div className="superadmin-alert superadmin-alert-error" role="alert">
            {createError}
          </div>
        )}
      </div>

      <section className="superadmin-organization-list" aria-labelledby="admin-users-title">
        <div className="superadmin-list-header">
          <div>
            <h2 id="admin-users-title">Alle brugere</h2>
            <p>{total} {total === 1 ? 'bruger' : 'brugere'} fundet</p>
          </div>
        </div>

        <div className="superadmin-users-filters">
          <select
            className="form-input superadmin-select"
            value={organizationFilter}
            onChange={(event) => { setOrganizationFilter(event.target.value); setPage(1); }}
            aria-label="Filtrer på organisation"
          >
            <option value="">Alle organisationer</option>
            {organizations.map((organization) => (
              <option key={organization.id} value={organization.id}>
                {organization.name}
              </option>
            ))}
          </select>
          <SearchBar
            value={search}
            onChange={(value) => { setSearch(value); setPage(1); }}
            placeholder="Søg på navn, e-mail, telefon eller rolle..."
          />
        </div>

        {roleSwapError && (
          <div className="superadmin-alert superadmin-alert-error" role="alert">
            {roleSwapError}
          </div>
        )}

        {usersQuery.isError ? (
          <div className="superadmin-alert superadmin-alert-error" role="alert">
            <span>{getSuperadminErrorMessage(usersQuery.error)}</span>
            <button type="button" className="btn btn-secondary" onClick={() => { void usersQuery.refetch(); }}>
              Prøv igen
            </button>
          </div>
        ) : usersQuery.isLoading ? (
          <div className="superadmin-empty" role="status">Indlæser brugere...</div>
        ) : users.length === 0 ? (
          <div className="superadmin-empty">
            <UsersIcon size={24} aria-hidden="true" />
            <span>Ingen brugere fundet.</span>
          </div>
        ) : (
          <>
            <div className="data-table-wrap">
              <table className="data-table">
                <thead>
                  <tr>
                    <th>Organisation</th>
                    <th>Navn</th>
                    <th>E-mail</th>
                    <th>Rolle</th>
                    <th className="col-actions" />
                  </tr>
                </thead>
                <tbody>
                  {users.map((user) => (
                    <tr key={user.id}>
                      <td>{user.organizationName}</td>
                      <td><strong>{user.displayName}</strong></td>
                      <td>{user.email}</td>
                      <td>
                        <select
                          className="form-input superadmin-select superadmin-role-select"
                          value={user.role}
                          onChange={(event) => { void handleRoleSwap(user, event.target.value); }}
                          disabled={updateMutation.isPending}
                          aria-label={`Rolle for ${user.displayName}`}
                        >
                          {ROLE_OPTIONS.map((option) => (
                            <option key={option.value} value={option.value}>
                              {option.label}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td className="col-actions">
                        <button
                          type="button"
                          className="btn btn-secondary"
                          onClick={() => { setEditingUser(user); setEditError(null); }}
                        >
                          Rediger
                        </button>
                        <button
                          type="button"
                          className="btn btn-danger superadmin-delete-user"
                          onClick={() => setDeletingUser(user)}
                          aria-label={`Slet ${user.displayName}`}
                        >
                          <Trash2 size={16} aria-hidden="true" />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <PaginationControls
              page={page}
              totalCount={total}
              pageSize={PAGE_SIZE}
              onPrev={() => setPage((current) => Math.max(1, current - 1))}
              onNext={() => setPage((current) => current + 1)}
            />
          </>
        )}
      </section>

      <EditAdminUserDialog
        user={editingUser}
        isSubmitting={updateMutation.isPending}
        error={editError}
        onSubmit={handleEditSubmit}
        onClose={() => setEditingUser(null)}
      />

      <ConfirmDeleteDialog
        open={deletingUser !== null}
        title="Slet bruger"
        message={deletingUser ? `Er du sikker på, at du vil slette ${deletingUser.displayName} (${deletingUser.organizationName})? Handlingen kan ikke fortrydes.` : ''}
        onConfirm={handleDelete}
        onClose={() => setDeletingUser(null)}
      />
    </div>
  );
}
