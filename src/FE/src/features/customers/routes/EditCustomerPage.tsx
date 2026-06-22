import { useParams, useNavigate } from 'react-router-dom';
import { AlertCircle, ArrowLeft } from 'lucide-react';
import { useGetApiCustomersId } from '../../../api/generated/customers/customers';
import { apiClient } from '../../../lib/axios';
import { useQueryClient } from '@tanstack/react-query';
import { getGetApiCustomersQueryKey, getGetApiCustomersIdQueryKey } from '../../../api/generated/customers/customers';
import { toast } from 'sonner';
import { useState, useEffect, useRef } from 'react';


export const EditCustomerPage = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;

  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [email, setEmail] = useState('');
  const [contactPerson, setContactPerson] = useState('');
  const [phone, setPhone] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (customer) {
      setName(customer.name);
      setAddress(customer.address ?? '');
      setEmail(customer.email ?? '');
      setContactPerson(customer.contactPerson ?? '');
      setPhone(customer.phone ?? '');
      setTimeout(() => nameRef.current?.focus(), 50);
    }
  }, [customer]);

  if (query.isLoading) {
    return (
      <div className="page-container">
        <div className="page-header">
          <div className="skeleton skeleton-title" />
          <div className="skeleton skeleton-subtitle" />
        </div>
      </div>
    );
  }

  if (query.isError || !customer) {
    return (
      <div className="page-container">
        <div className="error-state">
          <AlertCircle size={32} />
          <p>Kunne ikke hente kundeoplysninger.</p>
          <button className="btn btn-primary" onClick={() => navigate('/app/customers')}>
            Tilbage til kunder
          </button>
        </div>
      </div>
    );
  }

  const handleSave = async () => {
    if (!name.trim()) {
      setError('Kundenavn er påkrævet.');
      return;
    }

    setIsSaving(true);
    setError(null);

    try {
      await apiClient.put(`/api/customers/${customer.id}`, {
        name: name.trim(),
        address: address.trim() || null,
        email: email.trim() || null,
        contactPerson: contactPerson.trim() || null,
        phone: phone.trim() || null,
      });

      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersIdQueryKey(customer.id) });
      toast.success('Kunden er opdateret.');
      navigate(`/app/customers/${customer.id}`);
    } catch {
      toast.error('Kunne ikke opdatere kunden. Prøv igen.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div>
          <h2>Rediger kunde</h2>
          <p className="subtitle">Opdater kundeoplysninger</p>
        </div>
      </div>

      <div className="customer-edit-form">
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-name">Kundenavn *</label>
          <input
            ref={nameRef}
            id="edit-customer-name"
            className="form-input"
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            maxLength={240}
            placeholder="Indtast kundenavn"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-address">Adresse</label>
          <input
            id="edit-customer-address"
            className="form-input"
            type="text"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            maxLength={500}
            placeholder="Indtast adresse"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-email">E-mail</label>
          <input
            id="edit-customer-email"
            className="form-input"
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="Indtast e-mail"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-contact">Kontaktperson</label>
          <input
            id="edit-customer-contact"
            className="form-input"
            type="text"
            value={contactPerson}
            onChange={(e) => setContactPerson(e.target.value)}
            maxLength={200}
            placeholder="Indtast kontaktperson"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-phone">Telefon</label>
          <input
            id="edit-customer-phone"
            className="form-input"
            type="tel"
            value={phone}
            onChange={(e) => setPhone(e.target.value)}
            maxLength={80}
            placeholder="Indtast telefonnummer"
          />
        </div>
      </div>

      {error && <p className="form-error-text">{error}</p>}

      <div className="modal-actions">
        <button
          type="button"
          className="btn btn-primary"
          onClick={() => void handleSave()}
          disabled={isSaving}
        >
          {isSaving && <div className="animate-spin" style={{ width: 16, height: 16, border: '2px solid white', borderTopColor: 'transparent', borderRadius: '50%', marginRight: 8 }} />}
          <span>{isSaving ? 'Gemmer...' : 'Gem'}</span>
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          onClick={() => navigate(`/app/customers/${customer.id}`)}
          disabled={isSaving}
        >
          Annuller
        </button>
      </div>
    </div>
  );
};
