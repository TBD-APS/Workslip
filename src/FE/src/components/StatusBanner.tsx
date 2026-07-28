import type { ReactNode } from 'react';

type StatusBannerVariant = 'warning' | 'info';

interface StatusBannerProps {
  variant: StatusBannerVariant;
  title: string;
  children?: ReactNode;
}

const iconMap: Record<StatusBannerVariant, ReactNode> = {
  warning: (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z" />
      <path d="M12 9v4" />
      <path d="M12 17h.01" />
    </svg>
  ),
  info: (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <path d="M12 16v-4" />
      <path d="M12 8h.01" />
    </svg>
  ),
};

export const StatusBanner = ({ variant, title, children }: StatusBannerProps) => (
  <div className={`status-banner status-banner--${variant}`}>
    {iconMap[variant]}
    <div>
      <strong>{title}</strong>
      {children}
    </div>
  </div>
);
