import { createContext, useContext, useState, ReactNode } from 'react';

type DropdownContextValue = {
  openDropdowns: number;
  registerOpen: () => void;
  registerClose: () => void;
};

const DropdownContext = createContext<DropdownContextValue | null>(null);

export function DropdownProvider({ children }: { children: ReactNode }) {
  const [openDropdowns, setOpenDropdowns] = useState(0);

  const registerOpen = () => setOpenDropdowns((prev) => prev + 1);
  const registerClose = () => setOpenDropdowns((prev) => Math.max(0, prev - 1));

  return (
    <DropdownContext.Provider value={{ openDropdowns, registerOpen, registerClose }}>
      {children}
    </DropdownContext.Provider>
  );
}

export function useDropdownContext() {
  const context = useContext(DropdownContext);
  if (!context) {
    throw new Error('useDropdownContext must be used within DropdownProvider');
  }
  return context;
}