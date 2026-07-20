import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './registerSW'
import './index.css'
import App from './App.tsx'

if (typeof window !== 'undefined') {
  const originalFocus = HTMLInputElement.prototype.focus;

  HTMLInputElement.prototype.focus = function (options?: FocusOptions) {
    // Hvis der ikke eksplicit er angivet options, tvinger vi preventScroll: true
    const newOptions: FocusOptions = {
      preventScroll: true,
      ...options,
    };
    originalFocus.call(this, newOptions);
  };
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
