import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Banknote, Link2, Plus, RefreshCw, ReceiptText, Trash2 } from 'lucide-react';
import { apiClient } from '../../../lib/axios';
import { notify } from '../../../lib/toast';
import { CollapsibleSection } from '../../../components/forms/CollapsibleSection';

interface AccountingStatus {
  providerId: string;
  providerDisplayName: string;
  configured: boolean;
  connected: boolean;
}

interface BillableItem {
  id: string;
  jobId: string;
  kind: 'material' | 'outlay';
  description: string;
  quantity: number | string;
  unitNetPrice: number | string;
  lineNetAmount: number | string;
  source: string;
}

interface InvoiceState {
  jobId: string;
  providerId: string;
  draftInvoiceNumber?: number | null;
  bookedInvoiceNumber?: number | null;
  status: 'Draft' | 'Booked' | 'Paid' | 'Overdue' | 'Unknown' | string;
  externalReference: string;
  externalUrl?: string | null;
  netAmount: number | string;
  lastSyncedAt: string;
}

interface CustomerSyncResult {
  pulled: number;
  pushed: number;
  linked: number;
  totalLocal: number;
  totalExternal: number;
}

type Props = {
  jobId: string;
  jobStatus: string;
};

const currency = (value: number | string) => new Intl.NumberFormat('da-DK', {
  style: 'currency',
  currency: 'DKK',
  maximumFractionDigits: 2,
}).format(Number(value) || 0);

const decimal = (value: string) => Number(value.replace(',', '.'));

export function JobAccountingPanel({ jobId, jobStatus }: Props) {
  const queryClient = useQueryClient();
  const [invoice, setInvoice] = useState<InvoiceState | null>(null);
  const [kind, setKind] = useState<'material' | 'outlay'>('material');
  const [description, setDescription] = useState('');
  const [quantity, setQuantity] = useState('1');
  const [unitNetPrice, setUnitNetPrice] = useState('');

  const statusQuery = useQuery({
    queryKey: ['accounting', 'status'],
    queryFn: async () => await apiClient.get('/api/accounting/status', { skipGlobalErrorToast: true }) as unknown as AccountingStatus,
    staleTime: 30_000,
    retry: false,
  });

  const itemsQuery = useQuery({
    queryKey: ['accounting', 'jobs', jobId, 'billable-items'],
    queryFn: async () => await apiClient.get(`/api/accounting/jobs/${jobId}/billable-items`, { skipGlobalErrorToast: true }) as unknown as BillableItem[],
    enabled: Boolean(jobId),
    staleTime: 10_000,
  });

  const customerSync = useMutation({
    mutationFn: async () => await apiClient.post('/api/accounting/customers/sync', undefined, { skipGlobalErrorToast: true }) as unknown as CustomerSyncResult,
    onSuccess: (result) => notify.success(`Kundesynk færdig: ${result.pulled} hentet, ${result.pushed} sendt, ${result.linked} linket.`),
    onError: () => notify.error('Kundesynk kunne ikke gennemføres.'),
  });

  const addItem = useMutation({
    mutationFn: async () => {
      const parsedQuantity = decimal(quantity);
      const parsedPrice = decimal(unitNetPrice);
      if (!description.trim() || !Number.isFinite(parsedQuantity) || parsedQuantity <= 0 || !Number.isFinite(parsedPrice) || parsedPrice < 0) {
        throw new Error('Udfyld beskrivelse, antal og pris korrekt.');
      }
      return await apiClient.post(`/api/accounting/jobs/${jobId}/billable-items`, {
        kind,
        description: description.trim(),
        quantity: parsedQuantity,
        unitNetPrice: parsedPrice,
        source: 'manual',
      }, { skipGlobalErrorToast: true }) as unknown as BillableItem;
    },
    onSuccess: async () => {
      setDescription('');
      setQuantity('1');
      setUnitNetPrice('');
      await queryClient.invalidateQueries({ queryKey: ['accounting', 'jobs', jobId, 'billable-items'] });
      notify.success(kind === 'material' ? 'Materiale tilføjet.' : 'Udlæg tilføjet.');
    },
    onError: (error) => notify.error(error instanceof Error ? error.message : 'Linjen kunne ikke gemmes.'),
  });

  const deleteItem = useMutation({
    mutationFn: async (itemId: string) => await apiClient.delete(`/api/accounting/jobs/${jobId}/billable-items/${itemId}`, { skipGlobalErrorToast: true }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['accounting', 'jobs', jobId, 'billable-items'] });
    },
    onError: () => notify.error('Linjen kunne ikke slettes.'),
  });

  const createInvoice = useMutation({
    mutationFn: async () => await apiClient.post(`/api/accounting/jobs/${jobId}/invoice-draft`, undefined, { skipGlobalErrorToast: true }) as unknown as InvoiceState,
    onSuccess: (result) => {
      setInvoice(result);
      notify.success(result.status === 'Draft' ? 'Fakturakladde er klar i regnskabssystemet.' : 'Fakturastatus er hentet.');
    },
    onError: (error: unknown) => {
      const message = (error as { response?: { data?: { error?: string } } })?.response?.data?.error;
      notify.error(message ?? 'Fakturakladden kunne ikke oprettes.');
    },
  });

  const refreshInvoice = useMutation({
    mutationFn: async () => await apiClient.post(`/api/accounting/jobs/${jobId}/invoice-refresh`, undefined, { skipGlobalErrorToast: true }) as unknown as InvoiceState,
    onSuccess: setInvoice,
    onError: () => notify.error('Fakturastatus kunne ikke opdateres.'),
  });

  const items = itemsQuery.data ?? [];
  const extrasTotal = useMemo(() => items.reduce((sum, item) => sum + Number(item.lineNetAmount || 0), 0), [items]);
  const status = statusQuery.data;
  const approved = jobStatus.toLowerCase() === 'approved';

  return (
    <CollapsibleSection
      icon={<Banknote size={18} />}
      title="Økonomi & fakturering"
      defaultOpen={false}
      scrollOnOpen={false}
    >
      <div id="job-accounting-panel" style={{ display: 'grid', gap: '14px' }}>
        <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between', gap: '10px' }}>
          <div>
            <strong>{status?.providerDisplayName ?? 'Regnskabsintegration'}</strong>
            <div style={{ fontSize: '13px', color: 'var(--text-secondary)', marginTop: '2px' }}>
              {statusQuery.isPending
                ? 'Kontrollerer forbindelse…'
                : status?.connected
                  ? 'Forbundet · Workslip kan synkronisere kunder og fakturakladder'
                  : status?.configured
                    ? 'Konfigureret, men forbindelsen svarer ikke'
                    : 'Ikke konfigureret for organisationen'}
            </div>
          </div>
          <button
            id="job-accounting-sync-customers"
            type="button"
            className="btn btn-secondary"
            disabled={!status?.connected || customerSync.isPending}
            onClick={() => customerSync.mutate()}
          >
            <RefreshCw size={15} aria-hidden="true" /> {customerSync.isPending ? 'Synkroniserer…' : 'Synkronisér kunder'}
          </button>
        </div>

        <div style={{ borderTop: '1px solid var(--border-color)', paddingTop: '12px' }}>
          <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: '12px', marginBottom: '8px' }}>
            <strong>Materialer & udlæg</strong>
            <span style={{ color: 'var(--text-secondary)', fontSize: '13px' }}>{currency(extrasTotal)} ekskl. moms</span>
          </div>

          <div style={{ display: 'grid', gap: '7px' }}>
            {items.length === 0 ? (
              <span style={{ color: 'var(--text-secondary)', fontSize: '13px' }}>Ingen materialer eller udlæg med beløb endnu.</span>
            ) : items.map((item) => (
              <div key={item.id} style={{ display: 'grid', gridTemplateColumns: 'minmax(0,1fr) auto auto', alignItems: 'center', gap: '10px', padding: '8px 0', borderBottom: '1px solid var(--border-color)' }}>
                <div>
                  <strong style={{ fontSize: '13px' }}>{item.description}</strong>
                  <div style={{ color: 'var(--text-secondary)', fontSize: '12px' }}>{item.kind === 'material' ? 'Materiale' : 'Udlæg'} · {Number(item.quantity)} × {currency(item.unitNetPrice)}</div>
                </div>
                <strong style={{ fontVariantNumeric: 'tabular-nums' }}>{currency(item.lineNetAmount)}</strong>
                <button type="button" className="btn btn-secondary" aria-label={`Slet ${item.description}`} onClick={() => deleteItem.mutate(item.id)} disabled={deleteItem.isPending}>
                  <Trash2 size={14} aria-hidden="true" />
                </button>
              </div>
            ))}
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '140px minmax(180px,1fr) 100px 130px auto', gap: '8px', marginTop: '12px', alignItems: 'end' }}>
            <label className="form-group" style={{ margin: 0 }}>
              <span className="form-label">Type</span>
              <select className="form-input" value={kind} onChange={(event) => setKind(event.target.value as 'material' | 'outlay')}>
                <option value="material">Materiale</option>
                <option value="outlay">Udlæg</option>
              </select>
            </label>
            <label className="form-group" style={{ margin: 0 }}>
              <span className="form-label">Beskrivelse</span>
              <input className="form-input" value={description} onChange={(event) => setDescription(event.target.value)} placeholder="Fx cirkulationspumpe" />
            </label>
            <label className="form-group" style={{ margin: 0 }}>
              <span className="form-label">Antal</span>
              <input className="form-input" inputMode="decimal" value={quantity} onChange={(event) => setQuantity(event.target.value)} />
            </label>
            <label className="form-group" style={{ margin: 0 }}>
              <span className="form-label">Pris ekskl. moms</span>
              <input className="form-input" inputMode="decimal" value={unitNetPrice} onChange={(event) => setUnitNetPrice(event.target.value)} placeholder="0,00" />
            </label>
            <button id="job-accounting-add-item" type="button" className="btn btn-secondary" onClick={() => addItem.mutate()} disabled={addItem.isPending}>
              <Plus size={15} aria-hidden="true" /> Tilføj
            </button>
          </div>
        </div>

        <div style={{ borderTop: '1px solid var(--border-color)', paddingTop: '12px', display: 'grid', gap: '10px' }}>
          <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: '12px', flexWrap: 'wrap' }}>
            <div>
              <strong style={{ display: 'flex', alignItems: 'center', gap: '6px' }}><ReceiptText size={15} aria-hidden="true" /> Fakturakladde</strong>
              <div style={{ color: 'var(--text-secondary)', fontSize: '13px', marginTop: '2px' }}>
                {!approved
                  ? 'Sagen skal være godkendt før Workslip må oprette en fakturakladde.'
                  : 'Timer med fakturerbar sats samt materialer og udlæg bliver overført som kladdelinjer.'}
              </div>
            </div>
            <button
              id="job-accounting-create-invoice"
              type="button"
              className="btn btn-primary"
              disabled={!approved || !status?.connected || createInvoice.isPending}
              onClick={() => createInvoice.mutate()}
            >
              <Link2 size={15} aria-hidden="true" /> {createInvoice.isPending ? 'Arbejder…' : 'Hent / opret kladde'}
            </button>
          </div>

          {invoice && (
            <div id="job-accounting-invoice-state" style={{ padding: '10px 12px', border: '1px solid var(--border-color)', borderRadius: '12px', display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: '12px', flexWrap: 'wrap' }}>
              <div>
                <strong>{invoice.status} · {invoice.draftInvoiceNumber ? `Kladde #${invoice.draftInvoiceNumber}` : invoice.bookedInvoiceNumber ? `Faktura #${invoice.bookedInvoiceNumber}` : invoice.externalReference}</strong>
                <div style={{ color: 'var(--text-secondary)', fontSize: '12px', marginTop: '2px' }}>{currency(invoice.netAmount)} ekskl. moms · reference {invoice.externalReference}</div>
              </div>
              <div style={{ display: 'flex', gap: '8px' }}>
                <button type="button" className="btn btn-secondary" onClick={() => refreshInvoice.mutate()} disabled={refreshInvoice.isPending}>
                  <RefreshCw size={14} aria-hidden="true" /> Opdatér
                </button>
                {invoice.externalUrl && (
                  <a className="btn btn-secondary" href={invoice.externalUrl} target="_blank" rel="noopener noreferrer">Åbn</a>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    </CollapsibleSection>
  );
}
