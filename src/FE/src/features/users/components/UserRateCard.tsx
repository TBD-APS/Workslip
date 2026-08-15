import { useState, type KeyboardEvent, type MouseEvent } from 'react';
import { Banknote, Loader2, Pencil, Save, X } from 'lucide-react';
import { NumericInput } from '../../../components/forms/NumericInput';
import {
  formatBillableHourlyRate,
  normalizeBillableHourlyRate,
  useUpdateUserBillingRate,
  useUserBillingRate,
} from '../hooks/useUserBillingRate';
import { notify } from '../../../lib/toast';
import './UserRateCard.css';

type RateValue = number | string | null;

type UserRateEditorProps = {
  userId: string;
  rate: RateValue;
  isLoading?: boolean;
  isError?: boolean;
  variant?: 'detail' | 'inline';
  ariaLabel?: string;
};

export function UserRateEditor({
  userId,
  rate,
  isLoading = false,
  isError = false,
  variant = 'detail',
  ariaLabel = 'Fakturerbar timepris',
}: UserRateEditorProps) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const mutation = useUpdateUserBillingRate(userId);
  const normalizedRate = normalizeBillableHourlyRate(rate);

  const stopClickPropagation = (event: MouseEvent<HTMLElement>) => {
    event.stopPropagation();
  };

  const stopKeyboardPropagation = (event: KeyboardEvent<HTMLElement>) => {
    event.stopPropagation();
  };

  const beginEditing = () => {
    setValue(normalizedRate?.toString().replace('.', ',') ?? '');
    setEditing(true);
  };

  const save = () => {
    const normalized = value.trim().replace(',', '.');
    const parsed = normalized ? Number(normalized) : null;
    if (parsed != null && (!Number.isFinite(parsed) || parsed < 0 || parsed > 100000)) {
      notify.error('Timeprisen skal være mellem 0 og 100.000 kr.');
      return;
    }

    mutation.mutate(
      { id: userId, data: { billableHourlyRate: parsed } },
      { onSuccess: () => setEditing(false) },
    );
  };

  return (
    <div
      className={`user-rate-editor user-rate-editor--${variant}`}
      onClick={stopClickPropagation}
      onKeyDown={stopKeyboardPropagation}
      role="group"
      aria-label={ariaLabel}
    >
      <div className="user-rate-editor__icon" aria-hidden="true">
        <Banknote size={variant === 'inline' ? 16 : 18} />
      </div>

      <div className="user-rate-editor__content">
        {variant === 'detail' && <span className="user-rate-editor__label">Fakturerbar timepris</span>}

        {isLoading ? (
          <span className="user-rate-editor__value user-rate-editor__value--muted">
            <Loader2 size={15} className="animate-spin" /> Henter...
          </span>
        ) : isError ? (
          <span className="user-rate-editor__value user-rate-editor__value--error">Kunne ikke hentes</span>
        ) : editing ? (
          <div className="user-rate-editor__form">
            <label className="sr-only" htmlFor={`user-hourly-rate-${userId}`}>Kr. pr. time</label>
            <NumericInput
              id={`user-hourly-rate-${userId}`}
              value={value}
              kind="decimal"
              min={0}
              max={100000}
              onChange={setValue}
              disabled={mutation.isPending}
            />
            <div className="user-rate-editor__actions">
              <button
                className="btn-icon user-rate-editor__action"
                type="button"
                onClick={() => setEditing(false)}
                disabled={mutation.isPending}
                aria-label="Annuller redigering af timepris"
              >
                <X size={16} />
              </button>
              <button
                className="btn-icon user-rate-editor__action user-rate-editor__action--save"
                type="button"
                onClick={save}
                disabled={mutation.isPending}
                aria-label="Gem timepris"
              >
                {mutation.isPending ? <Loader2 size={16} className="animate-spin" /> : <Save size={16} />}
              </button>
            </div>
          </div>
        ) : (
          <span className="user-rate-editor__value">{formatBillableHourlyRate(rate)}</span>
        )}
      </div>

      {!editing && !isLoading && !isError && (
        <button
          className="btn-icon user-rate-editor__edit"
          type="button"
          onClick={beginEditing}
          aria-label={`Rediger timepris${variant === 'inline' ? `, ${formatBillableHourlyRate(rate)}` : ''}`}
        >
          <Pencil size={15} />
        </button>
      )}
    </div>
  );
}

export function UserRateCard({ userId }: { userId: string }) {
  const query = useUserBillingRate(userId);

  return (
    <section className="user-rate-card" aria-label="Fakturerbar timepris">
      <UserRateEditor
        userId={userId}
        rate={query.data?.billableHourlyRate ?? null}
        isLoading={query.isLoading}
        isError={query.isError}
        variant="detail"
      />
      <p className="user-rate-card__hint">Bruges i det administrative faktureringsgrundlag.</p>
    </section>
  );
}
