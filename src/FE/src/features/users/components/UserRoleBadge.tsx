import { Crown, SearchCheck, Shield, User } from 'lucide-react';
import './UserRoleBadge.css';

type UserRoleBadgeProps = {
  role: string;
  displayName?: string | null;
};

function renderRoleIcon(role: string) {
  switch (role.trim().toLowerCase()) {
    case 'superadmin':
      return <Crown size={13} aria-hidden="true" />;
    case 'admin':
      return <Shield size={13} aria-hidden="true" />;
    case 'auditor':
      return <SearchCheck size={13} aria-hidden="true" />;
    default:
      return <User size={13} aria-hidden="true" />;
  }
}

export function UserRoleBadge({ role, displayName }: UserRoleBadgeProps) {
  return (
    <span className="user-role-badge">
      {renderRoleIcon(role)}
      <span>{displayName || role}</span>
    </span>
  );
}
