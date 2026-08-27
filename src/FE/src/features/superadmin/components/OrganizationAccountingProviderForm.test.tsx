import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { OrganizationAccountingProviderForm } from './OrganizationAccountingProviderForm';
import type { Organization } from '../types';

describe('OrganizationAccountingProviderForm', () => {
  const organizations: Organization[] = [
    {
      id: 'org-1',
      name: 'Alpha Byg',
      cvr: '12345678',
      accountingProviderId: 'economics',
    },
    {
      id: 'org-2',
      name: 'Beta VVS',
      cvr: '87654321',
      accountingProviderId: null,
    },
  ];

  it('renders with current accounting provider preselected', () => {
    render(
      <OrganizationAccountingProviderForm
        organizations={organizations}
        selectedOrganizationId="org-1"
        isSubmitting={false}
        onOrganizationChange={vi.fn()}
        onSubmit={vi.fn()}
      />,
    );

    const orgSelect = screen.getByRole('combobox', { name: /Organisation/i }) as HTMLSelectElement;
    const providerSelect = screen.getByRole('combobox', { name: /Regnskabssystem/i }) as HTMLSelectElement;

    expect(orgSelect.value).toBe('org-1');
    expect(providerSelect.value).toBe('economics');
  });

  it('submits updated accounting provider', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    render(
      <OrganizationAccountingProviderForm
        organizations={organizations}
        selectedOrganizationId="org-2"
        isSubmitting={false}
        onOrganizationChange={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    const providerSelect = screen.getByRole('combobox', { name: /Regnskabssystem/i }) as HTMLSelectElement;
    fireEvent.change(providerSelect, { target: { value: 'mock' } });

    const submitBtn = screen.getByRole('button', { name: /Gem integration/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        organizationId: 'org-2',
        providerId: 'mock',
      });
    });
  });

  it('submits null provider when "Ingen integration" is selected', async () => {
    const onSubmit = vi.fn().mockResolvedValue(undefined);

    render(
      <OrganizationAccountingProviderForm
        organizations={organizations}
        selectedOrganizationId="org-1"
        isSubmitting={false}
        onOrganizationChange={vi.fn()}
        onSubmit={onSubmit}
      />,
    );

    const providerSelect = screen.getByRole('combobox', { name: /Regnskabssystem/i }) as HTMLSelectElement;
    fireEvent.change(providerSelect, { target: { value: '' } });

    const submitBtn = screen.getByRole('button', { name: /Gem integration/i });
    fireEvent.click(submitBtn);

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        organizationId: 'org-1',
        providerId: null,
      });
    });
  });
});
