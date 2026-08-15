import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react';

const FOCUSABLE_SELECTOR = [
  'button:not([disabled])',
  '[href]',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ');

type ModalStackEntry = {
  id: symbol;
  getDialog: () => HTMLElement | null;
  close: () => void;
  canCloseOnEscape: () => boolean;
};

const modalStack: ModalStackEntry[] = [];

type UseModalAccessibilityOptions = {
  open: boolean;
  onClose: () => void;
  initialFocusRef?: RefObject<HTMLElement | null>;
  closeOnEscape?: boolean;
};

function getFocusableElements(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
    .filter((element) => !element.hasAttribute('hidden') && element.getAttribute('aria-hidden') !== 'true');
}

function handleModalKeyDown(event: KeyboardEvent) {
  const activeModal = modalStack.at(-1);
  const dialog = activeModal?.getDialog();
  if (!activeModal || !dialog) return;

  if (event.key === 'Escape' && activeModal.canCloseOnEscape()) {
    event.preventDefault();
    event.stopImmediatePropagation();

    // Keep the current top modal on the stack until this keydown dispatch has
    // fully finished. Closing synchronously can unmount a nested modal while the
    // same Escape event is still being processed, which allows the parent modal
    // to become topmost and react to that same key press in some environments.
    queueMicrotask(() => {
      if (modalStack.at(-1)?.id === activeModal.id) activeModal.close();
    });
    return;
  }

  if (event.key !== 'Tab') return;

  const focusable = getFocusableElements(dialog);
  if (focusable.length === 0) {
    event.preventDefault();
    dialog.focus();
    return;
  }

  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (!dialog.contains(document.activeElement)) {
    event.preventDefault();
    (event.shiftKey ? last : first).focus();
  } else if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

function addModal(entry: ModalStackEntry) {
  removeModal(entry.id);
  const wasEmpty = modalStack.length === 0;
  modalStack.push(entry);
  if (wasEmpty) document.addEventListener('keydown', handleModalKeyDown, true);
}

function removeModal(modalId: symbol) {
  const index = modalStack.findIndex((entry) => entry.id === modalId);
  if (index >= 0) modalStack.splice(index, 1);
  if (modalStack.length === 0) document.removeEventListener('keydown', handleModalKeyDown, true);
}

export function useModalAccessibility<T extends HTMLElement>({
  open,
  onClose,
  initialFocusRef,
  closeOnEscape = true,
}: UseModalAccessibilityOptions) {
  const dialogRef = useRef<T>(null);
  const modalIdRef = useRef(Symbol('workslip-modal'));
  const onCloseRef = useRef(onClose);
  const closeOnEscapeRef = useRef(closeOnEscape);

  useEffect(() => {
    onCloseRef.current = onClose;
    closeOnEscapeRef.current = closeOnEscape;
  }, [closeOnEscape, onClose]);

  // Stack ownership is synchronous with DOM layout so a newly rendered nested
  // dialog becomes the sole keyboard target before the browser can dispatch input.
  useLayoutEffect(() => {
    if (!open) return undefined;

    const entry: ModalStackEntry = {
      id: modalIdRef.current,
      getDialog: () => dialogRef.current,
      close: () => onCloseRef.current(),
      canCloseOnEscape: () => closeOnEscapeRef.current,
    };
    addModal(entry);

    return () => removeModal(entry.id);
  }, [open]);

  // Focus lifecycle stays passive so closing a nested modal can restore focus to
  // its trigger without competing with the parent's layout lifecycle.
  useEffect(() => {
    if (!open) return undefined;

    const modalId = modalIdRef.current;
    const previouslyFocused = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null;
    const frame = window.requestAnimationFrame(() => {
      const dialog = dialogRef.current;
      if (!dialog || modalStack.at(-1)?.id !== modalId) return;
      const requestedInitialFocus = initialFocusRef?.current;
      const firstFocusable = getFocusableElements(dialog)[0];
      (requestedInitialFocus ?? firstFocusable ?? dialog).focus();
    });

    return () => {
      window.cancelAnimationFrame(frame);
      if (previouslyFocused?.isConnected) previouslyFocused.focus();
    };
  }, [initialFocusRef, open]);

  return dialogRef;
}
