import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Loader2, Pencil, Save, X } from 'lucide-react';
import { NumericInput } from '../../../components/forms/NumericInput';
import { customAxiosInstance } from '../../../api/fetcherOrval';
import { notify } from '../../../lib/toast';

type RateResponse = { userId: string; billableHourlyRate: number | null };
const rateKey = (userId: string) => ['/api/job-costing/users', userId, 'rate'] as const;

export function UserRateCard({ userId }: { userId: string }) {
  const queryClient = useQueryClient();
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState('');
  const query = useQuery({
    queryKey: rateKey(userId),
    queryFn: () => customAxiosInstance<RateResponse>({
      url: `/api/job-costing/users/${userId}/rate`,
      method: 'GET',
    }),
  });

  const mutation = useMutation({
    mutationFn: (billableHourlyRate: number | null) => customAxiosInstance<void>({
      url: `/api/job-costing/users/${userId}/rate`,
      method: 'PATCH',
      data: { billableHourlyRate },
    }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: rateKey(userId) });
      setEditing(false);
      notify.success('Timepris er opdateret');
    },
    onError: () => notify.error('Timeprisen kunne ikke gemmes'),
  });

  const beginEditing = () => {
    setValue(query.data?.billableHourlyRate?.toString().replace('.', ',') ?? '');
    setEditing(true);
  };

  const save = () => {
    const normalized = value.trim().replace(',', '.');
    const parsed = normalized ? Number(normalized) : null;
    if (parsed != null && (!Number.isFinite(parsed) || parsed < 0 || parsed > 100000)) {
      notify.error('Timeprisen skal være mellem 0 og 100.000 kr.');
      return;
    }
    mutation.mutate(parsed);
  };

  return (
    <div className="section-card">
      <div className="section-card-header">
        <div>
          <h3>Fakturerbar timepris</h3>
          <p className="subtitle">Bruges i det administrative faktureringsgrundlag.</p>
        </div>
        {!editing && !query.isLoading && (
          <button className="btn-icon" type="button" onClick={beginEditing} aria-label="Rediger timepris">
            <Pencil size={16} />
          </button>
        )}
      </div>
      {query.isLoading ? (
        <span className="meta-item"><Loader2 size={16} className="animate-spin" /> Henter...</span>
      ) : query.isError ? (
        <p className="form-error">Timeprisen kunne ikke hentes.</p>
      ) : editing ? (
        <div className="form-group">
          <label htmlFor="user-hourly-rate">Kr. pr. time</label>
          <NumericInput
            id="user-hourly-rate"
            value={value}
            kind="decimal"
            min={0}
            max={100000}
            onChange={setValue}
            disabled={mutation.isPending}
          />
          <div className="profile-edit-actions">
            <button className="btn btn-secondary" type="button" onClick={() => setEditing(false)} disabled={mutation.isPending}>
              <X size={16} /> Annuller
            </button>
            <button className="btn btn-primary" type="button" onClick={save} disabled={mutation.isPending}>
              {mutation.isPending ? <Loader2 size={16} className="animate-spin" /> : <Save size={16} />} Gem
            </button>
          </div>
        </div>
      ) : (
        <strong>{query.data?.billableHourlyRate == null
          ? 'Ikke angivet'
          : `${query.data.billableHourlyRate.toLocaleString('da-DK', { minimumFractionDigits: 2, maximumFractionDigits: 2 })} kr./time`}</strong>
      )}
    </div>
  );
}
