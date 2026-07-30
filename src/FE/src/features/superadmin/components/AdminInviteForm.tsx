import { zodResolver } from '@hookform/resolvers/zod';
import { Loader2, MailPlus } from 'lucide-react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import type { InviteOrganizationAdminInput, Organization } from '../types';

const inviteAdminSchema = z.object({
  organizationId: z.string().min(1, 'Vælg en organisation.'),
  email: z.string()
    .trim()
    .min(1, 'Administratorens e-mailadresse er påkrævet.')
    .email('Administratorens e-mailadresse er ugyldig.')
    .max(320, 'E-mailadressen må højst være 320 tegn.'),
  displayName: z.string()
    .trim()
    .min(1, 'Administratorens navn er påkrævet.')
    .max(200, 'Administratorens navn må højst være 200 tegn.'),
  phone: z.string()
    .trim()
    .max(20, 'Telefonnummeret må højst være 20 tegn.'),
});

type InviteAdminFormValues = z.infer<typeof inviteAdminSchema>;

interface AdminInviteFormProps {
  organizations: Organization[];
  selectedOrganizationId: string;
  isSubmitting: boolean;
  onOrganizationChange: (organizationId: string) => void;
  onSubmit: (input: InviteOrganizationAdminInput) => Promise<void>;
}

export function AdminInviteForm({
  organizations,
  selectedOrganizationId,
  isSubmitting,
  onOrganizationChange,
  onSubmit,
}: AdminInviteFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    setValue,
    formState: { errors },
  } = useForm<InviteAdminFormValues>({
    resolver: zodResolver(inviteAdminSchema),
    defaultValues: {
      organizationId: selectedOrganizationId,
      email: '',
      displayName: '',
      phone: '',
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
      });
    } catch {
      // The parent displays the API error and keeps the entered values.
    }
  });

  return (
    <section className="superadmin-card" aria-labelledby="invite-admin-title">
      <div className="superadmin-card-header">
        <span className="superadmin-card-icon" aria-hidden="true">
          <MailPlus size={21} />
        </span>
        <div>
          <h2 id="invite-admin-title">Tildel administrator</h2>
          <p>Send en Microsoft Entra-invitation og tildel Admin-rollen.</p>
        </div>
      </div>

      <form className="superadmin-form" onSubmit={(event) => { void submit(event); }} noValidate>
        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-organization-select">
            Organisation *
          </label>
          <select
            id="superadmin-organization-select"
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
          <label className="form-label" htmlFor="superadmin-admin-name">
            Navn *
          </label>
          <input
            id="superadmin-admin-name"
            className={`form-input${errors.displayName ? ' form-input-invalid' : ''}`}
            type="text"
            autoComplete="name"
            maxLength={200}
            {...register('displayName')}
          />
          {errors.displayName && <p className="form-error-text">{errors.displayName.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-admin-email">
            E-mail *
          </label>
          <input
            id="superadmin-admin-email"
            className={`form-input${errors.email ? ' form-input-invalid' : ''}`}
            type="email"
            autoComplete="email"
            maxLength={320}
            {...register('email')}
          />
          {errors.email && <p className="form-error-text">{errors.email.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-admin-phone">
            Telefon
          </label>
          <input
            id="superadmin-admin-phone"
            className={`form-input${errors.phone ? ' form-input-invalid' : ''}`}
            type="tel"
            autoComplete="tel"
            maxLength={20}
            {...register('phone')}
          />
          {errors.phone && <p className="form-error-text">{errors.phone.message}</p>}
        </div>

        <button
          type="submit"
          className="btn btn-primary superadmin-submit"
          disabled={isSubmitting || organizations.length === 0}
        >
          {isSubmitting && <Loader2 className="animate-spin" size={17} aria-hidden="true" />}
          <span>{isSubmitting ? 'Sender invitation...' : 'Send Entra-invitation'}</span>
        </button>
      </form>
    </section>
  );
}
