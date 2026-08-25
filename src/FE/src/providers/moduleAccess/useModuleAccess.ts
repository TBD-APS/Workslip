/**
 * React hook over the tenant module-access context. Mirrors `useCan` from the
 * permissions layer, but answers "is this module entitled for the tenant?"
 * rather than "may this user perform this action?". Effective UI access is the
 * intersection of both — combine with `useCan` at the call site.
 */

import { useContext } from 'react';
import { ModuleAccessContext, type ModuleAccessValue } from './ModuleAccessContext';

export function useModuleAccess(): ModuleAccessValue {
  return useContext(ModuleAccessContext);
}
