/* eslint-disable react-refresh/only-export-components */
import { StrictMode, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { createMemoryRouter, RouterProvider, useLocation } from 'react-router-dom';
import './base.css';
import './App.css';
import { Drawer } from './components/common/Drawer';

function DrawerValidationPage() {
  const [isOpen, setIsOpen] = useState(true);
  const location = useLocation();

  return (
    <main className="app-shell" data-validation-route={location.pathname}>
      <div className="app-content">
        <h1>Aktuel appskærm</h1>
        <p data-testid="current-route">{location.pathname}</p>
        <button type="button" onClick={() => setIsOpen(true)}>
          Åbn drawer
        </button>
      </div>
      <Drawer
        isOpen={isOpen}
        onClose={() => setIsOpen(false)}
        title="Valideringsdrawer"
      >
        <p>Drawerindhold</p>
      </Drawer>
    </main>
  );
}

const router = createMemoryRouter(
  [
    { path: '/before', element: <div data-testid="previous-route">Forrige skærm</div> },
    { path: '/app', element: <DrawerValidationPage /> },
  ],
  {
    initialEntries: ['/before', '/app'],
    initialIndex: 1,
  },
);

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <RouterProvider router={router} />
  </StrictMode>,
);
