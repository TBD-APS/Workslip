import { useCallback, useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useNavigate } from 'react-router-dom';
import { ArrowLeft, Loader2, Upload } from 'lucide-react';
import { apiClient } from '../../../lib/axios';
import { useQueryClient } from '@tanstack/react-query';
import { getGetApiCustomersQueryKey } from '../../../api/generated/customers/customers';
import type { CustomerDetailViewModel } from '../../../api/generated/models';
import { NumericInput } from '../../../components/forms/NumericInput';
import { notify } from '../../../lib/toast';
import { validateCustomer, type CustomerFieldErrors } from '../validation';

type CustomerImportResult = {
  imported: number;
  duplicates: number;
  skipped: number;
  failed: number;
  errors: Array<{ rowNumber: number; field: string; message: string }>;
};

export const CreateCustomerPage = () => {
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const [customerNumber, setCustomerNumber] = useState('');
  const [name, setName] = useState('');
  const [address, setAddress] = useState('');
  const [zipCode, setZipCode] = useState('');
  const [city, setCity] = useState('');
  const [country, setCountry] = useState('');
  const [email, setEmail] = useState('');
  const [contactPerson, setContactPerson] = useState('');
  const [phone, setPhone] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<CustomerFieldErrors>({});
  const [createdId, setCreatedId] = useState<string | null>(null);
  const [pendingImport, setPendingImport] = useState<File | null>(null);
  const [isImporting, setIsImporting] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
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
        country: country.trim() || null,
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
    setCountry('');
    setEmail('');
    setContactPerson('');
    setPhone('');
    setFieldErrors({});
    setCreatedId(null);
    nameRef.current?.focus();
  };

  const handleImport = async () => {
    if (!pendingImport) return;
    setIsImporting(true);

    try {
      const formData = new FormData();
      formData.append('file', pendingImport);
      const result = await apiClient.post('/api/customers/import', formData) as CustomerImportResult;
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

  return (
    <div className="page-container">
      <div className="detail-header">
        <button className="btn-icon-back" onClick={() => navigate('/app/customers')} aria-label="Tilbage">
          <ArrowLeft size={20} />
        </button>
        <div>
          <h2>Opret kunde</h2>
          <p className="subtitle">Opret en kunde eller importér flere fra Excel/CSV</p>
        </div>
      </div>

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
          <label className="form-label" htmlFor="create-customer-address">Adresse</label>
          <input
            id="create-customer-address"
            className="form-input"
            type="text"
            value={address}
            onChange={(e) => setAddress(e.target.value)}
            maxLength={500}
            placeholder="Indtast adresse"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-zip">Postnummer</label>
          <NumericInput
            id="create-customer-zip"
            kind="integer"
            value={zipCode}
            onChange={setZipCode}
            placeholder="Indtast postnummer"
          />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-city">By</label>
          <input id="create-customer-city" className="form-input" type="text" value={city} onChange={(e) => setCity(e.target.value)} maxLength={120} />
        </div>
        <div className="form-group">
          <label className="form-label" htmlFor="create-customer-country">Land</label>
          <input id="create-customer-country" className="form-input" type="text" value={country} onChange={(e) => setCountry(e.target.value)} maxLength={120} />
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
