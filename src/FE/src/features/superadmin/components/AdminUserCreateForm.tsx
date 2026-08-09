import { zodResolver } from '@hookform/resolvers/zod';
import { Loader2, UserPlus } from 'lucide-react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { ROLES } from '../../../providers/permissions';
import type { CreateAdminUserInput, Organization } from '../types';

const createUserSchema = z.object({
  organizationId: z.string().min(1, 'Vælg en organisation.'),
  email: z.string()
    .trim()
    .min(1, 'E-mailadresse er påkrævet.')
    .email('E-mailadressen er ugyldig.')
    .max(256, 'E-mailadressen må højst være 256 tegn.'),
  displayName: z.string()
    .trim()
    .min(1, 'Navn er påkrævet.')
    .max(256, 'Navn må højst være 256 tegn.'),
  phone: z.string()
    .trim()
    .max(20, 'Telefonnummeret må højst være 20 tegn.'),
  role: z.string().min(1, 'Vælg en rolle.'),
});

type CreateUserFormValues = z.infer<typeof createUserSchema>;

const ROLE_OPTIONS: Array<{ value: string; label: string }> = [
  { value: ROLES.User, label: 'Medarbejder' },
  { value: ROLES.Auditor, label: 'Auditør' },
  { value: ROLES.Admin, label: 'Administrator' },
  { value: ROLES.Superadmin, label: 'Superadministrator' },
];

interface AdminUserCreateFormProps {
  organizations: Organization[];
  selectedOrganizationId: string;
  isSubmitting: boolean;
  onOrganizationChange: (organizationId: string) => void;
  onSubmit: (input: CreateAdminUserInput) => Promise<void>;
}

export function AdminUserCreateForm({
  organizations,
  selectedOrganizationId,
  isSubmitting,
  onOrganizationChange,
  onSubmit,
}: AdminUserCreateFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<CreateUserFormValues>({
    resolver: zodResolver(createUserSchema),
    defaultValues: {
      organizationId: selectedOrganizationId,
      email: '',
      displayName: '',
      phone: '',
      role: ROLES.User,
    },
  });

  useEffect(() => {
    setValue('organizationId', selectedOrganizationId, { shouldValidate: false });
  }, [selectedOrganizationId, setValue]);

  const submit = handleSubmit(async (values) => {
    try {
      await onSubmit(values);
      reset({
        organizationId: values.organizationId,
        email: '',
        displayName: '',
        phone: '',
        role: ROLES.User,
      });
    } catch {
      // The parent displays the API error and keeps the entered values.
    }
  });

  return (
    <section className="superadmin-card" aria-labelledby="create-user-title">
      <div className="superadmin-card-header">
        <span className="superadmin-card-icon" aria-hidden="true">
          <UserPlus size={21} />
        </span>
        <div>
          <h2 id="create-user-title">Opret bruger</h2>
          <p>Opret en bruger i en valgt organisation med en given rolle.</p>
        </div>
      </div>

      <form className="superadmin-form" onSubmit={(event) => { void submit(event); }} noValidate>
        <div className="form-group">
          <label className="form-label" htmlFor="admin-user-organization-select">
            Organisation *
          </label>
          <select
            id="admin-user-organization-select"
            className={`form-input superadmin-select${errors.organizationId ? ' form-input-invalid' : ''}`}
            disabled={organizations.length === 0}
            {...register('organizationId', {
              onChange: (event) => onOrganizationChange(event.target.value),
            })}
          >
            <option value="">Vælg organisation</option>
            {organizations.map((organization) => (
              <option key={organization.id} value={organization.id}>
                {organization.name} · CVR {organization.cvr}
              </option>
            ))}
          </select>
          {errors.organizationId && <p className="form-error-text">{errors.organizationId.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="admin-user-name">
            Navn *
          </label>
          <input
            id="admin-user-name"
            className={`form-input${errors.displayName ? ' form-input-invalid' : ''}`}
            type="text"
            autoComplete="name"
            maxLength={256}
            {...register('displayName')}
          />
          {errors.displayName && <p className="form-error-text">{errors.displayName.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="admin-user-email">
            E-mail *
          </label>
          <input
            id="admin-user-email"
            className={`form-input${errors.email ? ' form-input-invalid' : ''}`}
            type="email"
            autoComplete="email"
            maxLength={256}
            {...register('email')}
          />
          {errors.email && <p className="form-error-text">{errors.email.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="admin-user-phone">
            Telefon
          </label>
          <input
            id="admin-user-phone"
            className={`form-input${errors.phone ? ' form-input-invalid' : ''}`}
            type="tel"
            autoComplete="tel"
            maxLength={20}
            {...register('phone')}
          />
          {errors.phone && <p className="form-error-text">{errors.phone.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="admin-user-role">
            Rolle *
          </label>
          <select
            id="admin-user-role"
            className={`form-input superadmin-select${errors.role ? ' form-input-invalid' : ''}`}
            {...register('role')}
          >
            {ROLE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
          {errors.role && <p className="form-error-text">{errors.role.message}</p>}
        </div>

        <button
          type="submit"
          className="btn btn-primary superadmin-submit"
          disabled={isSubmitting || organizations.length === 0}
        >
          {isSubmitting && <Loader2 className="animate-spin" size={17} aria-hidden="true" />}
          <span>{isSubmitting ? 'Opretter...' : 'Opret bruger'}</span>
        </button>
      </form>
    </section>
  );
}

export { ROLE_OPTIONS };
