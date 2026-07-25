import { useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate, useParams } from 'react-router-dom';
import { ArrowLeft, Clock, Hash, Loader2, Mail, MapPin, MoreHorizontal, Phone, Plus, Upload, Users } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { Can } from '../../../providers/permissions/Can';
import { ErrorState } from '../../../components/ErrorState';
import { useGetApiCustomersId, getGetApiCustomersQueryKey } from '../../../api/generated/customers/customers';
import { apiClient } from '../../../lib/axios';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { useCustomerActions } from '../components/CustomerActions';
import { useScrollRestore } from '../../../hooks/useScrollRestore';
import { useMediaQuery } from '../../../hooks/useMediaQuery';
import { notify } from '../../../lib/toast';
import type { CustomerListItemViewModel } from '../../../api/generated/models';

type ExtendedCustomerFields = {
  customerNumber?: string | null;
  zipCode?: string | null;
  city?: string | null;
  country?: string | null;
};

export const CustomerDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;
  const queryClient = useQueryClient();
  const isDesktop = useMediaQuery('(min-width: 768px)');

  const [pendingImport, setPendingImport] = useState<File | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useScrollRestore(`customer:${id}`);

  const handleImport = async () => {
    if (!pendingImport) return;
    setIsImporting(true);
    try {
      const formData = new FormData();
      formData.append('file', pendingImport);
      const result = await apiClient.post('/api/customers/import', formData) as { imported: number; duplicates: number; skipped: number; failed: number };
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      notify.success(
        `${result.imported} importeret, ${result.duplicates} dubletter, ${result.skipped} sprunget over, ${result.failed} med fejl.`,
      );
      setPendingImport(null);
      if (fileInputRef.current) fileInputRef.current.value = '';
    } catch {
      // Toast handled by axios interceptor.
    } finally {
      setIsImporting(false);
    }
  };

  const listCustomer = customer as (typeof customer & ExtendedCustomerFields) | undefined;
  const listItems: CustomerListItemViewModel[] = customer
    ? [{
        id: customer.id,
        customerNumber: listCustomer?.customerNumber ?? null,
        name: customer.name,
        address: customer.address,
        zipCode: listCustomer?.zipCode ?? null,
        city: listCustomer?.city ?? null,
        country: listCustomer?.country ?? null,
        email: customer.email,
        contactPerson: customer.contactPerson,
        phone: customer.phone,
        jobCount: customer.jobCount,
        isTop: false,
      } as CustomerListItemViewModel]
    : [];

  const { toggleActionMenu, openActionMenu, ActionMenuPortal, DeleteDialog } = useCustomerActions({
    customers: listItems,
    onEditCustomer: (item) => navigate(`/app/customers/${item.id}/edit`),
    onDeletedCustomer: () => navigate('/app/customers'),
  });

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="detail-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !customer) {
    return (
      <div className="page-container">
        <ErrorState message="Kunne ikke hente kundeoplysninger.">
          <button className="btn btn-primary" onClick={() => navigate('/app/customers')}>Tilbage til kunder</button>
        </ErrorState>
      </div>
    );
  }

  const extended = customer as typeof customer & ExtendedCustomerFields;
  const locality = [extended.zipCode, extended.city].filter(Boolean).join(' ');
  const fullAddress = [customer.address, locality, extended.country].filter(Boolean).join(', ');

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div className="flex-1">
          <h2>{customer.name}</h2>
          <p className="subtitle">{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</p>
        </div>
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => navigate('/app/job/new', {
            state: {
              fromCustomer: true,
              customerId: customer.id,
              customerSnapshot: {
                name: customer.name,
                email: customer.email,
                phone: customer.phone,
                address: fullAddress || null,
                contactPerson: customer.contactPerson,
              },
            },
          })}
        >
          <Plus size={16} />
          <span>Ny sag</span>
        </button>
        <Can permission="user:manage">
          <div className="worksheet-actions-menu-root">
            <button
              type="button"
              className="btn-icon"
              onClick={(event) => toggleActionMenu(event, customer.id)}
              aria-label="Flere handlinger for kunde"
              aria-expanded={openActionMenu?.customerId === customer.id}
              title="Handlinger"
            >
              <MoreHorizontal size={18} />
            </button>
          </div>
        </Can>
      </div>

      <section className="detail-section">
        <div className="customer-detail-info">
          {extended.customerNumber && (
            <div className="detail-row"><Hash size={16} /><span>{extended.customerNumber}</span></div>
          )}
          {fullAddress && (
            <div className="detail-row"><MapPin size={16} /><span>{fullAddress}</span></div>
          )}
          {customer.email && (
            <div className="detail-row"><Mail size={16} /><span>{customer.email}</span></div>
          )}
          {customer.contactPerson && (
            <div className="detail-row"><Users size={16} /><span>{customer.contactPerson}</span></div>
          )}
          {customer.phone && (
            <div className="detail-row"><Phone size={16} /><span>{customer.phone}</span></div>
          )}
        </div>
      </section>

      {isDesktop && (
        <Can permission="customer:edit">
          <section className="detail-section">
            <h3>Importér kunder</h3>
            <p className="subtitle">Understøtter .xlsx og .csv. Eksisterende kundenumre importeres ikke igen.</p>
            <input
              ref={fileInputRef}
              type="file"
              accept=".xlsx,.csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet,text/csv"
              hidden
              onChange={(event) => setPendingImport(event.target.files?.[0] ?? null)}
            />
            <button type="button" className="btn btn-secondary" onClick={() => fileInputRef.current?.click()}>
              <Upload size={16} />
              <span>Vælg importfil</span>
            </button>
          </section>
        </Can>
      )}

      <div className="job-list">
        {customer.jobs.map((job) => (
          <button
            key={job.id}
            className="job-card"
            onClick={() => navigate(`/app/completed/${job.id}`, { state: { from: `/app/customers/${customer.id}` } })}
            type="button"
          >
            <div className="job-card-top">
              <span className="job-number">Sag-{job.reportNumber}</span>
              <span className={`status-badge status-${job.status.toLowerCase()}`}>{formatJobStatus(job.status)}</span>
            </div>
            <div className="job-card-body">
              {job.contactPerson && <span className="meta-item"><Users size={14} /><span>{job.contactPerson}</span></span>}
              {job.contactPhone && <span className="meta-item"><Phone size={14} /><span>{job.contactPhone}</span></span>}
            </div>
            <div className="job-card-meta">
              <span className="meta-item"><Clock size={14} /><span className="meta-item">Sidst opdateret: {formatDateLong(job.updatedAt)}</span></span>
            </div>
          </button>
        ))}

        {customer.jobs.length === 0 && <div className="empty-state"><p>Ingen sager for denne kunde.</p></div>}
      </div>

      {ActionMenuPortal}
      {DeleteDialog}
      {pendingImport && (
        <CustomerImportConfirmDialog
          file={pendingImport}
          isImporting={isImporting}
          onConfirm={() => void handleImport()}
          onClose={() => setPendingImport(null)}
        />
      )}
    </div>
  );
};

function CustomerImportConfirmDialog({ file, isImporting, onConfirm, onClose }: { file: File; isImporting: boolean; onConfirm: () => void; onClose: () => void }) {
  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="customer-import-title">
      <div className="modal-card">
        <h3 id="customer-import-title">Godkend kundeimport</h3>
        <p>Importér kunder fra <strong>{file.name}</strong>?</p>
        <p className="subtitle">Rækker med eksisterende kundenummer springes over. Importen kan ikke fortrydes samlet.</p>
        <div className="modal-actions">
          <button className="btn btn-primary" type="button" onClick={onConfirm} disabled={isImporting}>
            {isImporting && <Loader2 className="animate-spin" size={16} />}
            <span>{isImporting ? 'Importerer...' : 'Importér'}</span>
          </button>
          <button className="btn btn-secondary" type="button" onClick={onClose} disabled={isImporting}>Annuller</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
