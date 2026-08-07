import { Bell, BellOff, CheckCircle2, HelpCircle, WifiOff } from 'lucide-react';

type PushRuntimeStatus = {
  supported: boolean;
  permission: NotificationPermission | 'unsupported';
  subscribed: boolean;
};

type PushRuntimeDiagnosticsProps = {
  status: PushRuntimeStatus;
};

function getStatusCopy(status: PushRuntimeStatus) {
  if (!status.supported) {
    return {
      label: 'Ikke understøttet',
      detail: 'Browseren understøtter ikke Web Push i denne kontekst.',
      tone: 'is-stale',
      Icon: WifiOff,
    };
  }

  if (status.permission === 'denied') {
    return {
      label: 'Blokeret',
      detail: 'iOS/browseren har blokeret notifikationer for Workslip.',
      tone: 'is-stale',
      Icon: BellOff,
    };
  }

  if (status.permission === 'default') {
    return {
      label: 'Ikke godkendt',
      detail: 'Brugeren har endnu ikke givet tilladelse til notifikationer.',
      tone: 'is-stale',
      Icon: HelpCircle,
    };
  }

  if (!status.subscribed) {
    return {
      label: 'Ingen subscription',
      detail: 'Tilladelsen er givet, men pushManager har ingen aktiv subscription.',
      tone: 'is-stale',
      Icon: BellOff,
    };
  }

  return {
    label: 'Klar',
    detail: 'Tilladelsen er givet, og browseren har en aktiv Web Push-subscription.',
    tone: 'is-fresh',
    Icon: CheckCircle2,
  };
}

export function PushRuntimeDiagnostics({ status }: PushRuntimeDiagnosticsProps) {
  const copy = getStatusCopy(status);
  const Icon = copy.Icon;

  return (
    <div className="cache-diagnostics-list">
      <div>
        <span className="cache-diagnostics-list-icon"><Icon size={16} aria-hidden="true" /></span>
        <div>
          <strong>{copy.label}</strong>
          <span>{copy.detail}</span>
        </div>
        <span className={`cache-diagnostics-state-badge ${copy.tone}`}>
          {status.subscribed ? 'subscribed' : 'not subscribed'}
        </span>
      </div>
      <div>
        <span className="cache-diagnostics-list-icon"><Bell size={16} aria-hidden="true" /></span>
        <div>
          <strong>Permission</strong>
          <span>{status.permission}</span>
        </div>
      </div>
    </div>
  );
}

export type { PushRuntimeStatus };
