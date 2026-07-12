import { useCallback, useEffect, useRef, useState, type MouseEvent } from 'react';
import { createPortal } from 'react-dom';
import { Loader2, Pencil, Trash2 } from 'lucide-react';
import { useQueryClient } from '@tanstack/react-query';
import { notify } from '../../../lib/toast';
import type { CustomerListItemViewModel, CustomerDetailViewModel } from '../../../api/generated/models';
import { getGetApiCustomersQueryKey, getGetApiCustomersIdQueryKey } from '../../../api/generated/customers/customers';
import { apiClient } from '../../../lib/axios';

type ActionMenuState = {
  customerId: string;
  top: number;
  right: number;
};

type CustomerDraft = {
  name: string;
  address: string;
  email: string;
  contactPerson: string;
  phone: string;
};

function toDraft(customer: CustomerListItemViewModel | CustomerDetailViewModel): CustomerDraft {
  return {
    name: customer.name ?? '',
    address: customer.address ?? '',
    email: customer.email ?? '',
    contactPerson: customer.contactPerson ?? '',
    phone: customer.phone ?? '',
  };
}

// ─── Context Menu ────────────────────────────────────────────────────────────

type CustomerActionMenuPortalProps = {
  openActionMenu: ActionMenuState | null;
  openCustomer: CustomerListItemViewModel | null;
  isDeleting: boolean;
  onStartEdit: (customer: CustomerListItemViewModel) => void;
  onDelete: (customer: CustomerListItemViewModel) => void;
};

function CustomerActionMenuPortal({
  openActionMenu,
  openCustomer,
  isDeleting,
  onStartEdit,
  onDelete,
}: CustomerActionMenuPortalProps) {
  if (!openActionMenu || !openCustomer) return null;

  return createPortal(
    <div
      className="worksheet-actions-menu"
      role="menu"
      style={{ top: openActionMenu.top, right: openActionMenu.right }}
    >
      <button type="button" role="menuitem" onClick={() => onStartEdit(openCustomer)}>
        <Pencil size={15} />
        <span>Rediger</span>
      </button>
      <button
        type="button"
        className="danger"
        role="menuitem"
        onClick={() => onDelete(openCustomer)}
        disabled={isDeleting}
      >
        <Trash2 size={15} />
        <span>Slet</span>
      </button>
    </div>,
    document.body,
  );
}

// ─── Edit Dialog ─────────────────────────────────────────────────────────────

type EditCustomerDialogProps = {
  customer: CustomerDetailViewModel | null;
  onClose: () => void;
};

function EditCustomerDialog({ customer, onClose }: EditCustomerDialogProps) {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<CustomerDraft>({ name: '', address: '', email: '', contactPerson: '', phone: '' });
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const nameRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (customer) {
      setDraft(toDraft(customer));
      setError(null);
      setTimeout(() => nameRef.current?.focus(), 50);
    }
  }, [customer]);

  useEffect(() => {
    if (!customer) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [customer, onClose]);

  if (!customer) return null;

  const updateDraft = (patch: Partial<CustomerDraft>) => setDraft((d) => ({ ...d, ...patch }));

  const handleSave = async () => {
    if (!draft.name.trim()) {
      setError('Kundenavn er påkrævet.');
      return;
    }

    setIsSaving(true);
    setError(null);

    try {
      await apiClient.put(`/api/customers/${customer.id}`, {
        name: draft.name.trim(),
        address: draft.address.trim() || null,
        email: draft.email.trim() || null,
        contactPerson: draft.contactPerson.trim() || null,
        phone: draft.phone.trim() || null,
      });

      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersIdQueryKey(customer.id) });
      notify.success('Kunden er opdateret.');
      onClose();
    } catch {
      notify.error('Kunne ikke opdatere kunden. Prøv igen.');
    } finally {
      setIsSaving(false);
    }
  };

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label="Rediger kunde"
      >
        <h3>Rediger kunde</h3>
        <div className="customer-edit-form">
          <div className="form-group">
            <label className="form-label" htmlFor="edit-customer-name">Kundenavn *</label>
            <input
              ref={nameRef}
              id="edit-customer-name"
              className="form-input"
              type="text"
              value={draft.name}
              onChange={(e) => updateDraft({ name: e.target.value })}
              maxLength={240}
            />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="edit-customer-address">Adresse</label>
            <input
              id="edit-customer-address"
              className="form-input"
              type="text"
              value={draft.address}
              onChange={(e) => updateDraft({ address: e.target.value })}
              maxLength={500}
            />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="edit-customer-email">E-mail</label>
            <input
              id="edit-customer-email"
              className="form-input"
              type="email"
              value={draft.email}
              onChange={(e) => updateDraft({ email: e.target.value })}
            />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="edit-customer-contact">Kontaktperson</label>
            <input
              id="edit-customer-contact"
              className="form-input"
              type="text"
              value={draft.contactPerson}
              onChange={(e) => updateDraft({ contactPerson: e.target.value })}
              maxLength={200}
            />
          </div>
          <div className="form-group">
            <label className="form-label" htmlFor="edit-customer-phone">Telefon</label>
            <input
              id="edit-customer-phone"
              className="form-input"
              type="tel"
              value={draft.phone}
              onChange={(e) => updateDraft({ phone: e.target.value })}
              maxLength={80}
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
            {isSaving && <Loader2 className="animate-spin" size={16} />}
            <span>{isSaving ? 'Gemmer...' : 'Gem'}</span>
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isSaving}
          >
            Annuller
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

// ─── Delete Confirmation ─────────────────────────────────────────────────────

type DeleteCustomerDialogProps = {
  customer: CustomerListItemViewModel | null;
  onClose: () => void;
  onDeleted?: (customer: CustomerListItemViewModel) => void;
};

function DeleteCustomerDialog({ customer, onClose, onDeleted }: DeleteCustomerDialogProps) {
  const queryClient = useQueryClient();
  const deleteLockRef = useRef(false);
  const [isDeleting, setIsDeleting] = useState(false);

  useEffect(() => {
    if (!customer) return;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [customer, onClose]);

  if (!customer) return null;

  const handleDelete = async () => {
    if (deleteLockRef.current) return;
    deleteLockRef.current = true;
    setIsDeleting(true);
    try {
      await apiClient.delete(`/api/customers/${customer.id}`);
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersQueryKey() });
      await queryClient.invalidateQueries({ queryKey: getGetApiCustomersIdQueryKey(customer.id) });
      notify.success('Kunden er slettet.');
      onDeleted?.(customer);
      onClose();
    } catch {
      notify.error('Kunne ikke slette kunden. Prøv igen.');
      deleteLockRef.current = false;
      setIsDeleting(false);
    }
  };

  return createPortal(
    <div className="modal-backdrop" onClick={onClose}>
      <div
        className="modal-card"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-label="Slet kunde"
      >
        <h3>Slet kunde</h3>
        <p>
          Er du sikker på, du vil slette <strong>{customer.name}</strong>?
        </p>

        <div className="modal-actions">
          <button
            type="button"
            className="btn btn-danger"
            onClick={() => void handleDelete()}
            disabled={isDeleting}
          >
            {isDeleting && <Loader2 className="animate-spin" size={16} />}
            <span>{isDeleting ? 'Sletter...' : 'Slet'}</span>
          </button>
          <button
            type="button"
            className="btn btn-secondary"
            onClick={onClose}
            disabled={isDeleting}
          >
            Annuller
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}

// ─── Main Component ──────────────────────────────────────────────────────────

type CustomerActionsProps = {
  customers: CustomerListItemViewModel[];
  onEditCustomer?: (customer: CustomerListItemViewModel) => void;
  onDeletedCustomer?: (customer: CustomerListItemViewModel) => void;
};

export function useCustomerActions({ customers, onEditCustomer, onDeletedCustomer }: CustomerActionsProps) {
  const [openActionMenu, setOpenActionMenu] = useState<ActionMenuState | null>(null);
  const [editingCustomer, setEditingCustomer] = useState<CustomerDetailViewModel | null>(null);
  const [deletingCustomer, setDeletingCustomer] = useState<CustomerListItemViewModel | null>(null);

  const openCustomer = openActionMenu
    ? customers.find((c) => c.id === openActionMenu.customerId) ?? null
    : null;

  // Close menu on outside click
  useEffect(() => {
    if (!openActionMenu) return;
    const handlePointerDown = (event: PointerEvent) => {
      if (event.target instanceof Element && event.target.closest('.worksheet-actions-menu-root, .worksheet-actions-menu')) return;
      setOpenActionMenu(null);
    };
    document.addEventListener('pointerdown', handlePointerDown);
    return () => document.removeEventListener('pointerdown', handlePointerDown);
  }, [openActionMenu]);

  // Close menu on scroll/resize
  useEffect(() => {
    if (!openActionMenu) return;
    const closeMenu = () => setOpenActionMenu(null);
    const scrollContainer = document.querySelector('.app-shell');
    scrollContainer?.addEventListener('scroll', closeMenu, { passive: true });
    window.addEventListener('resize', closeMenu);
    return () => {
      scrollContainer?.removeEventListener('scroll', closeMenu);
      window.removeEventListener('resize', closeMenu);
    };
  }, [openActionMenu]);

  const toggleActionMenu = useCallback((event: MouseEvent<HTMLButtonElement>, customerId: string) => {
    const rect = event.currentTarget.getBoundingClientRect();
    setOpenActionMenu((prev) =>
      prev?.customerId === customerId
        ? null
        : { customerId, top: rect.bottom + 6, right: window.innerWidth - rect.right },
    );
  }, []);

  const handleStartEdit = useCallback((customer: CustomerListItemViewModel) => {
    setOpenActionMenu(null);
    onEditCustomer?.(customer);
  }, [onEditCustomer]);

  const handleDelete = useCallback((customer: CustomerListItemViewModel) => {
    setOpenActionMenu(null);
    setDeletingCustomer(customer);
  }, []);

  return {
    toggleActionMenu,
    openActionMenu,
    ActionMenuPortal: (
      <CustomerActionMenuPortal
        openActionMenu={openActionMenu}
        openCustomer={openCustomer}
        isDeleting={false}
        onStartEdit={handleStartEdit}
        onDelete={handleDelete}
      />
    ),
    EditDialog: (
      <EditCustomerDialog
        customer={editingCustomer}
        onClose={() => setEditingCustomer(null)}
      />
    ),
    DeleteDialog: (
      <DeleteCustomerDialog
        customer={deletingCustomer}
        onClose={() => setDeletingCustomer(null)}
        onDeleted={onDeletedCustomer}
      />
    ),
  };
}
