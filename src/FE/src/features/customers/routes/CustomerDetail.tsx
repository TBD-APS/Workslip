import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Clock, Hash, Heart, Mail, MapPin, MoreHorizontal, Phone, PlusCircle, Users } from 'lucide-react';
import { Can } from '../../../providers/permissions/Can';
import { ErrorState } from '../../../components/ErrorState';
import {
  getGetApiCustomersIdQueryKey,
  getGetApiCustomersQueryKey,
  useGetApiCustomersId,
} from '../../../api/generated/customers/customers';
import { formatDateLong } from '../../../lib/formatDate';
import { formatJobStatus } from '../../jobs/statusLabels';
import { patchApiCustomersIdFavorite } from '../../jobs/customerApi';
import { useCustomerActions } from '../components/CustomerActions';
import { useScrollRestore } from '../../../hooks/useScrollRestore';
import type { CustomerListItemViewModel } from '../../../api/generated/models';
import { notify } from '../../../lib/toast';

type CustomerFavoriteState = {
  isFavorite?: boolean;
};

type CustomerJobWithDestination = {
  destinationAddress?: string | null;
};

export const CustomerDetail = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;
  const isFavorite = (customer as CustomerFavoriteState | undefined)?.isFavorite ?? false;

  const favoriteMutation = useMutation({
    mutationFn: (nextIsFavorite: boolean) => patchApiCustomersIdFavorite(id!, { isFavorite: nextIsFavorite }),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: getGetApiCustomersIdQueryKey(id!) }),
        queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() }),
        queryClient.invalidateQueries({ queryKey: ['customers', 'favorite'] }),
      ]);
    },
    onError: () => notify.error('Kunne ikke opdatere favoritstatus. Prøv igen.'),
  });

  useScrollRestore(`customer:${id}`);

  const listItems: CustomerListItemViewModel[] = customer
    ? [{
        id: customer.id,
        customerNumber: customer.customerNumber ?? null,
        name: customer.name,
        address: customer.address ?? null,
        zipCode: customer.zipCode ?? null,
        city: customer.city ?? null,
        country: customer.country ?? null,
        email: customer.email ?? null,
        contactPerson: customer.contactPerson ?? null,
        phone: customer.phone ?? null,
        jobCount: customer.jobCount,
        isFavorite,
      }]
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

  const locality = [customer.zipCode, customer.city].filter(Boolean).join(' ');
  const fullAddress = [customer.address, locality, customer.country].filter(Boolean).join(', ');

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div className="flex-1">
          <h2>{customer.name}</h2>
          <p className="subtitle">{customer.customerNumber ? `${customer.customerNumber} · ` : ''}{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</p>
        </div>
        <Can permission="customer:edit">
          <button
            type="button"
            className={`btn-icon ${isFavorite ? 'text-red' : 'opacity-30'}`}
            onClick={() => favoriteMutation.mutate(!isFavorite)}
            disabled={favoriteMutation.isPending}
            aria-label={isFavorite ? 'Fjern kunde fra favoritter' : 'Tilføj kunde til favoritter'}
            aria-pressed={isFavorite}
            title={isFavorite ? 'Fjern fra favoritter' : 'Tilføj til favoritter'}
          >
            <Heart size={20} fill={isFavorite ? 'currentColor' : 'none'} />
          </button>
        </Can>
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

      <Can permission="job:create">
        <button
          type="button"
          className="fab-create"
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
          aria-label="Opret ny KLS-sag for kunde"
          title="Opret ny KLS-sag"
        >
          <PlusCircle size={22} />
        </button>
      </Can>

      <section className="detail-section">
        <div className="customer-detail-info">
          {customer.customerNumber && (
            <div className="detail-row"><Hash size={16} /><span>{customer.customerNumber}</span></div>
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

      <div className="job-list">
        {customer.jobs.map((job) => {
          const destinationAddress = (job as typeof job & CustomerJobWithDestination).destinationAddress;

          return (
            <button
              key={job.id}
              className="job-card"
              onClick={() => navigate(`/app/completed/${job.id}`, { state: { from: `/app/customers/${customer.id}` } })}
              type="button"
            >
              <div className="job-card-top">
                <div>
                  <span className="job-number">Sag-{job.reportNumber}</span>
                  <h3 className="job-customer">{customer.name}</h3>
                </div>
                <span className={`status-badge status-${job.status.toLowerCase()}`}>{formatJobStatus(job.status)}</span>
              </div>
              <p className="job-address-row">
                <MapPin size={14} />
                <span className="job-address">{destinationAddress || 'Ingen destinationsadresse angivet'}</span>
              </p>
              <div className="job-card-body">
                {job.contactPerson && <span className="meta-item"><Users size={14} /><span>{job.contactPerson}</span></span>}
                {job.contactPhone && <span className="meta-item"><Phone size={14} /><span>{job.contactPhone}</span></span>}
              </div>
              <div className="job-card-meta">
                <span className="meta-item"><Clock size={14} /><span className="meta-item">Sidst opdateret: {formatDateLong(job.updatedAt)}</span></span>
              </div>
            </button>
          );
        })}

        {customer.jobs.length === 0 && <div className="empty-state"><p>Ingen sager for denne kunde.</p></div>}
      </div>

      {ActionMenuPortal}
      {DeleteDialog}
    </div>
  );
};
