import { useCallback } from 'react';
import type { CustomerSnapshotData } from '../../../api/generated/models/customerSnapshotData';
import type { CustomerSearchViewModel } from '../../../api/generated/models/customerSearchViewModel';

export function hasSnapshotData(snapshot: CustomerSnapshotData | null | undefined): boolean {
  if (!snapshot) return false;
  return (
    snapshot.name !== null ||
    snapshot.email !== null ||
    snapshot.phone !== null ||
    snapshot.address !== null ||
    snapshot.contactPerson !== null
  );
}

export function trimSnapshot(snapshot: CustomerSnapshotData | null | undefined): CustomerSnapshotData | null {
  if (!snapshot) return null;
  return {
    name: snapshot.name?.trim() || null,
    email: snapshot.email?.trim() || null,
    phone: snapshot.phone?.trim() || null,
    address: snapshot.address?.trim() || null,
    contactPerson: snapshot.contactPerson?.trim() || null,
  };
}

export function useCustomerSnapshot<T extends {
  customerId: string | null;
  customerSnapshot: CustomerSnapshotData | null;
  editSnapshot: boolean;
  createCustomer: boolean;
}>(
  setForm: React.Dispatch<React.SetStateAction<T>> | ((updater: (prev: T) => T) => void),
) {
  const selectCustomer = useCallback(
    (customer: CustomerSearchViewModel) => {
      const snapshot: CustomerSnapshotData = {
        name: customer.name,
        email: customer.email,
        phone: customer.phone,
        address: customer.address,
        contactPerson: customer.contactPerson,
      };
      setForm((prev) => ({
        ...prev,
        customerId: customer.id,
        customerSnapshot: snapshot,
        editSnapshot: false,
        createCustomer: false,
      }));
    },
    [setForm],
  );

  const updateSnapshotField = useCallback(
    (field: keyof CustomerSnapshotData, value: string) => {
      setForm((prev) => ({
        ...prev,
        customerSnapshot: {
          ...(prev.customerSnapshot ?? {
            name: null,
            email: null,
            phone: null,
            address: null,
            contactPerson: null,
          }),
          [field]: value,
        },
        editSnapshot: true,
      }));
    },
    [setForm],
  );

  const updateEditSnapshot = useCallback(
    (edit: boolean) => {
      setForm((prev) => ({
        ...prev,
        editSnapshot: edit,
        createCustomer: edit ? prev.createCustomer : false,
      }));
    },
    [setForm],
  );

  return { selectCustomer, updateSnapshotField, updateEditSnapshot };
}
