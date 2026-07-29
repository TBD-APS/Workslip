import { Shield } from 'lucide-react';
import './UserRoleBadge.css';

type UserRoleBadgeProps = {
  role: string;
  displayName?: string | null;
};

function getRoleClassName(role: string): string {
  switch (role.trim().toLowerCase()) {
    case 'superadmin':
      return 'superadmin';
    case 'admin':
      return 'admin';
    case 'auditor':
      return 'auditor';
    default:
      return 'user';
  }
}

export function UserRoleBadge({ role, displayName }: UserRoleBadgeProps) {
  return (
    <span className={`user-role-badge user-role-badge--${getRoleClassName(role)}`}>
      <Shield size={13} aria-hidden="true" />
      <span>{displayName || role}</span>
    </span>
  );
}
