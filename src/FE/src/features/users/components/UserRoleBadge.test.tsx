import { afterEach, describe, expect, it } from 'vitest';
import { cleanup, render, screen } from '@testing-library/react';
import { Crown, SearchCheck, Shield, User } from 'lucide-react';
import { UserRoleBadge } from './UserRoleBadge';

const iconByRole = {
  superadmin: Crown,
  admin: Shield,
  auditor: SearchCheck,
  user: User,
};

function renderIcon(Icon: typeof Crown) {
  const { container } = render(<Icon size={13} aria-hidden="true" />);
  return container.querySelector('svg')?.outerHTML;
}

describe('UserRoleBadge', () => {
  afterEach(() => cleanup());

  it.each(Object.entries(iconByRole))('renders the %s icon and display name', (role, Icon) => {
    const expectedSvg = renderIcon(Icon);
    cleanup();

    const { container } = render(<UserRoleBadge role={role} displayName="Rolle" />);

    expect(container.querySelector('svg')?.outerHTML).toBe(expectedSvg);
    expect(screen.getByText('Rolle')).toBeInTheDocument();
  });

  it('falls back to the raw role when no display name is provided', () => {
    render(<UserRoleBadge role="auditor" />);

    expect(screen.getByText('auditor')).toBeInTheDocument();
  });
});
