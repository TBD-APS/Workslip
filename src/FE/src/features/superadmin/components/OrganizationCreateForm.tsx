import { zodResolver } from '@hookform/resolvers/zod';
import { Building2, Loader2 } from 'lucide-react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import type { CreateOrganizationInput } from '../types';

const createOrganizationSchema = z.object({
  name: z.string()
    .trim()
    .min(1, 'Organisationsnavn er påkrævet.')
    .max(200, 'Organisationsnavnet må højst være 200 tegn.'),
  cvr: z.string()
    .trim()
    .regex(/^\d{8}$/, 'CVR-nummer skal bestå af præcis 8 cifre.'),
  adminDisplayName: z.string()
    .trim()
    .min(1, 'Administratorens navn er påkrævet.')
    .max(200, 'Administratorens navn må højst være 200 tegn.'),
});

type CreateOrganizationFormValues = z.infer<typeof createOrganizationSchema>;

interface OrganizationCreateFormProps {
  isSubmitting: boolean;
  onSubmit: (input: CreateOrganizationInput) => Promise<void>;
}

export function OrganizationCreateForm({ isSubmitting, onSubmit }: OrganizationCreateFormProps) {
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors },
  } = useForm<CreateOrganizationFormValues>({
    resolver: zodResolver(createOrganizationSchema),
    defaultValues: {
      name: '',
      cvr: '',
      adminDisplayName: '',
    },
  });

  const submit = handleSubmit(async (values) => {
    try {
      await onSubmit(values);
      reset();
    } catch {
      // The parent displays the API error and keeps the entered values.
    }
  });

  return (
    <section className="superadmin-card" aria-labelledby="create-organization-title">
      <div className="superadmin-card-header">
        <span className="superadmin-card-icon" aria-hidden="true">
          <Building2 size={21} />
        </span>
        <div>
          <h2 id="create-organization-title">Opret organisation</h2>
          <p>Opret organisationen og dens første lokale administratorplads.</p>
        </div>
      </div>

      <form className="superadmin-form" onSubmit={(event) => { void submit(event); }} noValidate>
        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-organization-name">
            Organisationsnavn *
          </label>
          <input
            id="superadmin-organization-name"
            className={`form-input${errors.name ? ' form-input-invalid' : ''}`}
            type="text"
            autoComplete="organization"
            maxLength={200}
            {...register('name')}
          />
          {errors.name && <p className="form-error-text">{errors.name.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-organization-cvr">
            CVR-nummer *
          </label>
          <input
            id="superadmin-organization-cvr"
            className={`form-input${errors.cvr ? ' form-input-invalid' : ''}`}
            type="text"
            inputMode="numeric"
            autoComplete="off"
            maxLength={8}
            {...register('cvr')}
          />
          {errors.cvr && <p className="form-error-text">{errors.cvr.message}</p>}
        </div>

        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-placeholder-name">
            Administratorens navn *
          </label>
          <input
            id="superadmin-placeholder-name"
            className={`form-input${errors.adminDisplayName ? ' form-input-invalid' : ''}`}
            type="text"
            autoComplete="name"
            maxLength={200}
            {...register('adminDisplayName')}
          />
          <p className="form-help-text">
            E-mail og Entra-invitation tilknyttes i næste trin.
          </p>
          {errors.adminDisplayName && <p className="form-error-text">{errors.adminDisplayName.message}</p>}
        </div>

        <button type="submit" className="btn btn-primary superadmin-submit" disabled={isSubmitting}>
          {isSubmitting && <Loader2 className="animate-spin" size={17} aria-hidden="true" />}
          <span>{isSubmitting ? 'Opretter...' : 'Opret organisation'}</span>
        </button>
      </form>
    </section>
  );
}
