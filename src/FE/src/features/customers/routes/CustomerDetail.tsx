import { useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, Hash, Heart, Mail, MapPin, MoreHorizontal, Phone, PlusCircle, Users } from 'lucide-react';
import { Can } from '../../../providers/permissions/Can';
import { FeatureGate } from '../../../providers/moduleAccess';
import { ErrorState } from '../../../components/ErrorState';
import { CopyAddressButton } from '../../../components/CopyAddressButton';
import { CopyableValue } from '../../../components/CopyableValue';
import {
  getGetApiCustomersIdQueryKey,
  getGetApiCustomersQueryKey,
  useGetApiCustomersId,
} from '../../../api/generated/customers/customers';
import { patchApiCustomersIdFavorite } from '../../../api/generated/customers/customers';
import { JobCard } from '../../../components/JobCard';
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
      <div id="customer-detail-error" className="page-container">
        <ErrorState message="Kunne ikke hente kundeoplysninger.">
          <button className="btn btn-primary" onClick={() => navigate('/app/customers')}>Tilbage til kunder</button>
        </ErrorState>
      </div>
    );
  }

  const locality = [customer.zipCode, customer.city].filter(Boolean).join(' ');
  const fullAddress = [customer.address, locality, customer.country].filter(Boolean).join(', ');

  return (
    <div id="customer-detail-page" className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div className="flex-1">
          <h2>
            <CopyableValue id="customer-detail-name" field="customer.name" value={customer.name} />
          </h2>
          <p className="subtitle">{customer.customerNumber ? `${customer.customerNumber} · ` : ''}{customer.jobCount} {customer.jobCount === 1 ? 'sag' : 'sager'}</p>
        </div>
        <Can permission="customer:edit">
          <button
            id="customer-favorite-button"
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
        <Can permission="customer:edit">
          <div className="worksheet-actions-menu-root">
            <button
              id="customer-actions-button"
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

      <FeatureGate module="compliance-evidence">
      <Can permission="job:create">
        <button
          id="customer-create-job-button"
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
      </FeatureGate>

      <section className="detail-section">
        <div className="customer-detail-info">
          {customer.customerNumber && (
            <div className="detail-row">
              <Hash size={16} />
              <CopyableValue id="customer-detail-number" field="customer.number" value={customer.customerNumber} />
            </div>
          )}
          {fullAddress && (
            <div className="detail-row">
              <MapPin size={16} />
              <CopyableValue id="customer-detail-address" field="address.full" value={fullAddress} />
              <CopyAddressButton id="customer-detail-address-actions" address={fullAddress} />
            </div>
          )}
          {customer.email && (
            <div className="detail-row">
              <Mail size={16} />
              <CopyableValue id="customer-detail-email" field="customer.email" value={customer.email} />
            </div>
          )}
          {customer.contactPerson && (
            <div className="detail-row">
              <Users size={16} />
              <CopyableValue id="customer-detail-contact" field="customer.contactPerson" value={customer.contactPerson} />
            </div>
          )}
          {customer.phone && (
            <div className="detail-row">
              <Phone size={16} />
              <CopyableValue id="customer-detail-phone" field="customer.phone" value={customer.phone} />
            </div>
          )}
        </div>
      </section>

      <div className="job-list">
        {customer.jobs.map((job) => {
          const destinationAddress = (job as typeof job & CustomerJobWithDestination).destinationAddress;
          return (
            <JobCard
              key={job.id}
              id={job.id}
              reportNumber={job.reportNumber}
              status={job.status}
              customerName={customer.name}
              address={destinationAddress}
              updatedAt={job.updatedAt}
              onOpen={() => navigate(`/app/completed/${job.id}`, { state: { from: `/app/customers/${customer.id}` } })}
            />
          );
        })}

        {customer.jobs.length === 0 && <div className="empty-state"><p>Ingen sager for denne kunde.</p></div>}
      </div>

      {ActionMenuPortal}
      {DeleteDialog}
    </div>
  );
};