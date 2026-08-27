import { zodResolver } from '@hookform/resolvers/zod';
import { Landmark, Loader2 } from 'lucide-react';
import { useEffect } from 'react';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import type { Organization } from '../types';

const accountingProviderSchema = z.object({
  organizationId: z.string().min(1, 'Vælg en organisation.'),
  providerId: z.string().optional(),
});

type AccountingProviderFormValues = z.infer<typeof accountingProviderSchema>;

interface OrganizationAccountingProviderFormProps {
  organizations: Organization[];
  selectedOrganizationId: string;
  isSubmitting: boolean;
  onOrganizationChange: (organizationId: string) => void;
  onSubmit: (input: { organizationId: string; providerId: string | null }) => Promise<void>;
}

export function OrganizationAccountingProviderForm({
  organizations,
  selectedOrganizationId,
  isSubmitting,
  onOrganizationChange,
  onSubmit,
}: OrganizationAccountingProviderFormProps) {
  const selectedOrg = organizations.find((org) => org.id === selectedOrganizationId);

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<AccountingProviderFormValues>({
    resolver: zodResolver(accountingProviderSchema),
    defaultValues: {
      organizationId: selectedOrganizationId,
      providerId: selectedOrg?.accountingProviderId ?? '',
    },
  });

  useEffect(() => {
    setValue('organizationId', selectedOrganizationId, { shouldValidate: false });
    setValue('providerId', selectedOrg?.accountingProviderId ?? '', { shouldValidate: false });
  }, [selectedOrganizationId, selectedOrg?.accountingProviderId, setValue]);

  const submit = handleSubmit(async (values) => {
    try {
      await onSubmit({
        organizationId: values.organizationId,
        providerId: values.providerId?.trim() ? values.providerId.trim() : null,
      });
    } catch {
      // The parent handles error display
    }
  });

  return (
    <section className="superadmin-card" aria-labelledby="accounting-provider-title">
      <div className="superadmin-card-header">
        <span className="superadmin-card-icon" aria-hidden="true">
          <Landmark size={21} />
        </span>
        <div>
          <h2 id="accounting-provider-title">Regnskabsintegration</h2>
          <p>Vælg ekstern regnskabsudbyder til automatisk bilags- og fakturaspejling.</p>
        </div>
      </div>

      <form
        id="accounting-provider-form"
        className="superadmin-form"
        onSubmit={(event) => { void submit(event); }}
        noValidate
      >
        <div className="form-group">
          <label className="form-label" htmlFor="superadmin-accounting-org-select">
            Organisation *
          </label>
          <select
            id="superadmin-accounting-org-select"
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
          <label className="form-label" htmlFor="superadmin-accounting-provider-select">
            Regnskabssystem
          </label>
          <select
            id="superadmin-accounting-provider-select"
            className="form-input superadmin-select"
            disabled={organizations.length === 0}
            {...register('providerId')}
          >
            <option value="">Ingen integration (Standard)</option>
            <option value="economics">e-conomic (Visma e-conomic)</option>
            <option value="mock">Mock Regnskab (Dev / Test)</option>
          </select>
          <p className="form-help-text" style={{ fontSize: '0.85rem', color: 'var(--text-secondary)', marginTop: '0.25rem' }}>
            Bilag og fakturaer fra den valgte udbyder bliver automatisk synkroniseret og vist i medarbejder- og sagsdokumenter.
          </p>
        </div>

        <button
          id="superadmin-accounting-provider-submit"
          type="submit"
          className="btn btn-primary superadmin-submit"
          disabled={isSubmitting || organizations.length === 0}
        >
          {isSubmitting && <Loader2 className="animate-spin" size={17} aria-hidden="true" />}
          <span>{isSubmitting ? 'Gemmer...' : 'Gem integration'}</span>
        </button>
      </form>
    </section>
  );
}
