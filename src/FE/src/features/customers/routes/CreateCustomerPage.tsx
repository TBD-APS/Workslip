import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { apiClient } from '../../../lib/axios';
import { useQueryClient } from '@tanstack/react-query';
import { getGetApiCustomersQueryKey } from '../../../api/generated/customers/customers';
import type { CustomerDetailViewModel } from '../../../api/generated/models';
import { validateCustomer, type CustomerFieldErrors } from '../validation';
import { AddressAutocomplete } from '../../jobs/components/AddressAutocomplete';
import type { AddressSuggestion } from '../../jobs/hooks/useAddressAutocomplete';

export const CreateCustomerPage = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

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
  const [createdId, setCreatedId] = useState<string | null>(null);
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
      const created = await apiClient.post('/api/customers', {
        name: name.trim(),
        customerNumber: customerNumber.trim() || null,
        address: address.trim() || null,
        zipCode: zipCode.trim() || null,
        city: city.trim() || null,
        email: email.trim() || null,
        contactPerson: contactPerson.trim() || null,
        phone: phone.trim() || null,
      }) as CustomerDetailViewModel;

      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      setCreatedId(created.id);
    } catch {
      // Toast handled by axios interceptor.
    } finally {
      setIsSaving(false);
    }
  };

  const handleCreateAnother = () => {
    setCustomerNumber('');
    setName('');
    setAddress('');
    setZipCode('');
    setCity('');
    setEmail('');
    setContactPerson('');
    setPhone('');
    setFieldErrors({});
    setCreatedId(null);
    nameRef.current?.focus();
  };

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div>
          <h2>Opret kunde</h2>
          <p className="subtitle">Opret en ny kunde</p>
        </div>
      </div>

      <div className="customer-edit-form">
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-number">Kundenummer</label>
          <input
            id="create-customer-number"
            className="form-input"
            type="text"
            value={customerNumber}
            onChange={(e) => setCustomerNumber(e.target.value)}
            maxLength={80}
            placeholder="Indtast kundenummer"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-name">Kundenavn *</label>
          <input
            ref={nameRef}
            id="create-customer-name"
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
          <label className="form-label" htmlFor="create-customer-email">E-mail</label>
          <input
            ref={emailRef}
            id="create-customer-email"
            className={`form-input${fieldErrors.email ? ' form-input-invalid' : ''}`}
            type="email"
            value={email}
            onChange={(e) => { setEmail(e.target.value); clearError('email'); }}
            placeholder="Indtast e-mail"
          />
          {fieldErrors.email && <p className="form-error-text">{fieldErrors.email}</p>}
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-contact">Kontaktperson</label>
          <input id="create-customer-contact" className="form-input" type="text" value={contactPerson} onChange={(e) => setContactPerson(e.target.value)} maxLength={200} />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-phone">Telefon</label>
          <input
            ref={phoneRef}
            id="create-customer-phone"
            className={`form-input${fieldErrors.phone ? ' form-input-invalid' : ''}`}
            type="tel"
            value={phone}
            onChange={(e) => { setPhone(e.target.value); clearError('phone'); }}
            maxLength={80}
            placeholder="Indtast telefonnummer"
          />
          {fieldErrors.phone && <p className="form-error-text">{fieldErrors.phone}</p>}
        </div>
      </div>

      <div className="modal-actions">
        <button type="button" className="btn btn-primary" onClick={() => void handleSave()} disabled={isSaving}>
          {isSaving && <Loader2 className="animate-spin" size={16} />}
          <span>{isSaving ? 'Opretter...' : 'Opret'}</span>
        </button>
        <button type="button" className="btn btn-secondary" onClick={() => navigate('/app/customers')} disabled={isSaving}>Annuller</button>
      </div>

      {createdId && <CreateCustomerSuccessDialog onCreateAnother={handleCreateAnother} onGoToCustomerList={() => navigate('/app/customers')} />}
    </div>
  );
};

function CreateCustomerSuccessDialog({ onCreateAnother, onGoToCustomerList }: { onCreateAnother: () => void; onGoToCustomerList: () => void }) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Enter') onGoToCustomerList();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [onGoToCustomerList]);

  return createPortal(
    <div className="modal-backdrop" role="dialog" aria-modal="true" aria-labelledby="create-customer-success-title">
      <div className="modal-card">
        <h3 id="create-customer-success-title">Kunden er oprettet</h3>
        <div className="modal-actions">
          <button className="btn btn-secondary" onClick={onCreateAnother}>Opret en mere</button>
          <button className="btn btn-primary" onClick={onGoToCustomerList}>Til kundelisten</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
