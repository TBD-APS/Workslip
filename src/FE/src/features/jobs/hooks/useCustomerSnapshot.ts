import { useCallback, useRef } from 'react';
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

function snapshotMatches(
  a: CustomerSnapshotData | null | undefined,
  b: CustomerSnapshotData | null | undefined,
): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return (
    a.name === b.name &&
    a.email === b.email &&
    a.phone === b.phone &&
    a.address === b.address &&
    a.contactPerson === b.contactPerson
  );
}

export function useCustomerSnapshot<T extends { customerId: string | null; customerSnapshot: CustomerSnapshotData | null; editSnapshot: boolean }>(
  setForm: React.Dispatch<React.SetStateAction<T>> | ((updater: (prev: T) => T) => void),
) {
  const originalRef = useRef<CustomerSnapshotData | null>(null);

  const selectCustomer = useCallback(
    (customer: CustomerSearchViewModel) => {
      const snapshot: CustomerSnapshotData = {
        name: customer.name,
        email: customer.email,
        phone: customer.phone,
        address: customer.address,
        contactPerson: customer.contactPerson,
      };
      originalRef.current = { ...snapshot };
      setForm((prev) => ({
        ...prev,
        customerId: customer.id,
        customerSnapshot: snapshot,
        editSnapshot: false,
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
      }));
    },
    [setForm],
  );

  const hasCustomerChanges = useCallback(
    (currentSnapshot: CustomerSnapshotData | null) => {
      return !snapshotMatches(originalRef.current, currentSnapshot);
    },
    [],
  );

  return { selectCustomer, updateSnapshotField, updateEditSnapshot, hasCustomerChanges };
}
