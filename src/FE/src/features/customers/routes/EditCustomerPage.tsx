import { useParams, useNavigate } from 'react-router-dom';
import { ArrowLeft } from 'lucide-react';
import { ErrorState } from '../../../components/ErrorState';
import { useGetApiCustomersId } from '../../../api/generated/customers/customers';
import { apiClient } from '../../../lib/axios';
import { useQueryClient } from '@tanstack/react-query';
import { getGetApiCustomersQueryKey, getGetApiCustomersIdQueryKey } from '../../../api/generated/customers/customers';
import { notify } from '../../../lib/toast';
import { useState, useEffect, useRef, useCallback } from 'react';
import { validateCustomer, type CustomerFieldErrors } from '../validation';
import { AddressAutocomplete } from '../../jobs/components/AddressAutocomplete';
import type { AddressSuggestion } from '../../jobs/hooks/useAddressAutocomplete';

type ExtendedCustomerFields = {
  customerNumber?: string | null;
  zipCode?: string | null;
  city?: string | null;
};

export const EditCustomerPage = () => {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const query = useGetApiCustomersId(id!);
  const customer = query.data;

  const [customerNumber, setCustomerNumber] = useState('');
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [zipCode, setZipCode] = useState('');
  const [city, setCity] = useState('');
  const [email, setEmail] = useState('');
  const [contactPerson, setContactPerson] = useState('');
  const [phone, setPhone] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<CustomerFieldErrors>({});
  const nameRef = useRef<HTMLInputElement>(null);
  const emailRef = useRef<HTMLInputElement>(null);
  const phoneRef = useRef<HTMLInputElement>(null);

  const refs: Record<string, React.RefObject<HTMLInputElement | null>> = {
    name: nameRef,
    email: emailRef,
    phone: phoneRef,
  };

  const clearError = useCallback((field: string) => {
    setFieldErrors((prev) => {
      if (!prev[field]) return prev;
      const next = { ...prev };
      delete next[field];
      return next;
    });
  }, []);

  useEffect(() => {
    if (customer) {
      const extended = customer as typeof customer & ExtendedCustomerFields;
      setCustomerNumber(extended.customerNumber ?? '');
      setName(customer.name);
      setAddress(customer.address ?? '');
      setZipCode(extended.zipCode ?? '');
      setCity(extended.city ?? '');
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
        <ErrorState message="Kunne ikke hente kundeoplysninger.">
          <button className="btn btn-primary" onClick={() => navigate('/app/customers')}>Tilbage til kunder</button>
        </ErrorState>
      </div>
    );
  }

  const handleSave = async () => {
    const errors = validateCustomer({ name, email, phone });
    setFieldErrors(errors);

    const firstError = Object.keys(errors)[0];
    if (firstError) {
      refs[firstError]?.current?.focus();
      return;
    }

    setIsSaving(true);
    try {
      await apiClient.put(`/api/customers/${customer.id}`, {
        name: name.trim(),
        customerNumber: customerNumber.trim() || null,
        address: address.trim() || null,
        zipCode: zipCode.trim() || null,
        city: city.trim() || null,
        email: email.trim() || null,
        contactPerson: contactPerson.trim() || null,
        phone: phone.trim() || null,
      });

      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersIdQueryKey(customer.id) });
      notify.success('Kunden er opdateret.');
      navigate(`/app/customers/${customer.id}`);
    } catch {
      notify.error('Kunne ikke opdatere kunden. Prøv igen.');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate(`/app/customers/${customer.id}`)} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div>
          <h2>Rediger kunde</h2>
          <p className="subtitle">Opdater kundeoplysninger</p>
        </div>
      </div>

      <div className="customer-edit-form">
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-number">Kundenummer</label>
          <input id="edit-customer-number" className="form-input" type="text" value={customerNumber} onChange={(e) => setCustomerNumber(e.target.value)} maxLength={80} />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-name">Kundenavn *</label>
          <input
            ref={nameRef}
            id="edit-customer-name"
            className={`form-input${fieldErrors.name ? ' form-input-invalid' : ''}`}
            type="text"
            value={name}
            onChange={(e) => { setName(e.target.value); clearError('name'); }}
            maxLength={240}
            placeholder="Indtast kundenavn"
          />
          {fieldErrors.name && <p className="form-error-text">{fieldErrors.name}</p>}
        </div>
        <div className="form-group">
          <label className="form-label">Adresse</label>
          <AddressAutocomplete
            value={address}
            onTextChange={setAddress}
            onSelectSuggestion={(s: AddressSuggestion) => {
              setAddress(s.street);
              setZipCode(s.zipCode);
              setCity(s.city);
            }}
            onClear={() => { setAddress(''); setZipCode(''); setCity(''); }}
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-email">E-mail</label>
          <input
            ref={emailRef}
            id="edit-customer-email"
            className={`form-input${fieldErrors.email ? ' form-input-invalid' : ''}`}
            type="email"
            value={email}
            onChange={(e) => { setEmail(e.target.value); clearError('email'); }}
          />
          {fieldErrors.email && <p className="form-error-text">{fieldErrors.email}</p>}
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-contact">Kontaktperson</label>
          <input id="edit-customer-contact" className="form-input" type="text" value={contactPerson} onChange={(e) => setContactPerson(e.target.value)} maxLength={200} />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="edit-customer-phone">Telefon</label>
          <input
            ref={phoneRef}
            id="edit-customer-phone"
            className={`form-input${fieldErrors.phone ? ' form-input-invalid' : ''}`}
            type="tel"
            value={phone}
            onChange={(e) => { setPhone(e.target.value); clearError('phone'); }}
            maxLength={80}
          />
          {fieldErrors.phone && <p className="form-error-text">{fieldErrors.phone}</p>}
        </div>
      </div>

      <div className="modal-actions">
        <button type="button" className="btn btn-primary" onClick={() => void handleSave()} disabled={isSaving}>
          {isSaving && <div className="animate-spin spinner-white" />}
          <span>{isSaving ? 'Gemmer...' : 'Gem'}</span>
        </button>
        <button type="button" className="btn btn-secondary" onClick={() => navigate(`/app/customers/${customer.id}`)} disabled={isSaving}>Annuller</button>
      </div>
    </div>
  );
};
