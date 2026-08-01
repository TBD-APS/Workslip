import {
  createContext,
  createElement,
  useContext,
  useLayoutEffect,
  useState,
  type ReactNode,
  type RefObject,
} from 'react';
import { useLocation, useNavigationType } from 'react-router-dom';

const AppScrollRestoreKeyContext = createContext<string | null>(null);

export function AppScrollRestoreBoundary({
  restoreKey,
  children,
}: {
  restoreKey: string | null;
  children: ReactNode;
}) {
  return createElement(AppScrollRestoreKeyContext.Provider, { value: restoreKey }, children);
}

export function useAppScrollRestoreKey(): string | null {
  return useContext(AppScrollRestoreKeyContext);
}

export function useAppRouteScrollManager(
  scrollContainerRef: RefObject<HTMLElement | null>,
): string | null {
  const location = useLocation();
  const navigationType = useNavigationType();
  const [navigationState, setNavigationState] = useState(() => ({
    locationKey: location.key,
    restoreKey: null as string | null,
  }));

  let restoreKey = navigationState.restoreKey;
  if (navigationState.locationKey !== location.key) {
    restoreKey = navigationType === 'POP' && !location.hash ? location.key : null;
    setNavigationState({ locationKey: location.key, restoreKey });
  }

  useLayoutEffect(() => {
    const container = scrollContainerRef.current;

    // Reset every non-anchor destination first. A real POP may then restore
    // its saved position after the destination has mounted and loaded.
    if (!location.hash && container) {
      container.scrollTop = 0;
      container.scrollLeft = 0;
    }
  }, [location.hash, location.key, navigationType, scrollContainerRef]);

  return restoreKey;
}
