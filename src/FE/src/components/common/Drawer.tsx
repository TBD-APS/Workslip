import { useEffect, useRef, type ReactNode } from 'react';
import { useBlocker, useLocation } from 'react-router-dom';
import { ChevronLeft } from 'lucide-react';
import { useModalAccessibility } from './useModalAccessibility';
import './Drawer.css';

const EDGE_SWIPE_WIDTH_PX = 16;
const MIN_CLOSE_DISTANCE_PX = 72;
const MAX_CLOSE_DISTANCE_PX = 120;
const CLOSE_DISTANCE_RATIO = 0.25;
const TRANSITION_CLEANUP_MS = 350;

type DrawerProps = {
  isOpen: boolean;
  onClose: () => void;
  title: string;
  ariaLabel?: string;
  icon?: ReactNode;
  className?: string;
  children: ReactNode;
};

type ActiveSwipe = {
  touchId: number;
  startX: number;
  dragX: number;
};

function findTouch(touches: TouchList, touchId: number): Touch | null {
  for (let index = 0; index < touches.length; index += 1) {
    const touch = touches[index];
    if (touch?.identifier === touchId) return touch;
  }

  return null;
}

function preventNativeGesture(event: TouchEvent) {
  if (event.cancelable) event.preventDefault();
}

function useDrawerEdgeSwipe(isOpen: boolean, onClose: () => void, drawerRef: React.RefObject<HTMLDivElement | null>) {
  useEffect(() => {
    const drawer = drawerRef.current;
    if (!drawer || !isOpen) return;

    let activeSwipe: ActiveSwipe | null = null;
    let cleanupTimer: number | null = null;

    const clearGestureStyles = () => {
      drawer.classList.remove('drawer-dragging', 'drawer-swipe-closing');
      drawer.style.removeProperty('--drawer-drag-x');
    };

    const scheduleGestureCleanup = () => {
      if (cleanupTimer !== null) window.clearTimeout(cleanupTimer);
      cleanupTimer = window.setTimeout(clearGestureStyles, TRANSITION_CLEANUP_MS);
    };

    const handleTouchStart = (event: TouchEvent) => {
      if (event.touches.length !== 1) return;

      const touch = event.touches[0];
      if (touch.clientX < 0 || touch.clientX > EDGE_SWIPE_WIDTH_PX) return;

      preventNativeGesture(event);
      if (cleanupTimer !== null) window.clearTimeout(cleanupTimer);
      drawer.classList.remove('drawer-swipe-closing');
      drawer.classList.add('drawer-dragging');
      drawer.style.setProperty('--drawer-drag-x', '0px');
      activeSwipe = {
        touchId: touch.identifier,
        startX: touch.clientX,
        dragX: 0,
      };
    };

    const handleTouchMove = (event: TouchEvent) => {
      if (!activeSwipe) return;

      const touch = findTouch(event.touches, activeSwipe.touchId);
      if (!touch) return;

      preventNativeGesture(event);
      const drawerWidth = drawer.getBoundingClientRect().width;
      const dragX = Math.min(drawerWidth, Math.max(0, touch.clientX - activeSwipe.startX));
      activeSwipe.dragX = dragX;
      drawer.style.setProperty('--drawer-drag-x', `${dragX}px`);
    };

    const finishSwipe = (event: TouchEvent) => {
      if (!activeSwipe) return;

      preventNativeGesture(event);
      const drawerWidth = drawer.getBoundingClientRect().width;
      const closeDistance = Math.min(
        MAX_CLOSE_DISTANCE_PX,
        Math.max(MIN_CLOSE_DISTANCE_PX, drawerWidth * CLOSE_DISTANCE_RATIO),
      );
      const shouldClose = activeSwipe.dragX >= closeDistance;
      activeSwipe = null;

      if (shouldClose) {
        drawer.classList.add('drawer-swipe-closing');
        drawer.classList.remove('drawer-dragging');
        onClose();
      } else {
        drawer.classList.remove('drawer-dragging');
      }

      scheduleGestureCleanup();
    };

    const cancelSwipe = (event: TouchEvent) => {
      if (!activeSwipe) return;

      preventNativeGesture(event);
      activeSwipe = null;
      drawer.classList.remove('drawer-dragging');
      scheduleGestureCleanup();
    };

    const listenerOptions: AddEventListenerOptions = { passive: false, capture: true };
    window.addEventListener('touchstart', handleTouchStart, listenerOptions);
    window.addEventListener('touchmove', handleTouchMove, listenerOptions);
    window.addEventListener('touchend', finishSwipe, listenerOptions);
    window.addEventListener('touchcancel', cancelSwipe, listenerOptions);

    return () => {
      window.removeEventListener('touchstart', handleTouchStart, listenerOptions);
      window.removeEventListener('touchmove', handleTouchMove, listenerOptions);
      window.removeEventListener('touchend', finishSwipe, listenerOptions);
      window.removeEventListener('touchcancel', cancelSwipe, listenerOptions);
      if (cleanupTimer !== null) window.clearTimeout(cleanupTimer);
      clearGestureStyles();
    };
  }, [drawerRef, isOpen, onClose]);
}

function DrawerNavigationBlocker({ onClose }: { onClose: () => void }) {
  const location = useLocation();
  const blocker = useBlocker(({ historyAction }) => historyAction === 'POP');

  useEffect(() => {
    if (blocker.state !== 'blocked') return;

    const isActualHistoryEntryChange = blocker.location.key !== location.key;
    blocker.reset();
    if (isActualHistoryEntryChange) onClose();
  }, [blocker, location.key, onClose]);

  return null;
}

export function Drawer({ isOpen, onClose, title, ariaLabel, icon, className, children }: DrawerProps) {
  const closeButtonRef = useRef<HTMLButtonElement>(null);
  const drawerRef = useModalAccessibility<HTMLDivElement>({
    open: isOpen,
    onClose,
    initialFocusRef: closeButtonRef,
  });
  useDrawerEdgeSwipe(isOpen, onClose, drawerRef);

  return (
    <>
      {isOpen && <DrawerNavigationBlocker onClose={onClose} />}
      <div
        className={`drawer-overlay ${isOpen ? 'open' : ''}`}
        onClick={onClose}
        aria-hidden="true"
      />
      <div
        ref={drawerRef}
        className={`drawer${className ? ` ${className}` : ''} ${isOpen ? 'open' : ''}`}
        role="dialog"
        aria-modal="true"
        aria-label={ariaLabel ?? title}
        aria-hidden={!isOpen}
        tabIndex={-1}
      >
        <div className="drawer-header">
          <button ref={closeButtonRef} className="btn-icon" type="button" onClick={onClose} aria-label={`Tilbage fra ${title.toLowerCase()}`}>
            <ChevronLeft size={26} aria-hidden="true" />
          </button>
          <div className="drawer-title">
            {icon}
            <h2>{title}</h2>
          </div>
        </div>

        <div className="drawer-content">
          {children}
        </div>
      </div>
    </>
  );
}
