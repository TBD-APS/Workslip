import { createContext, useContext, useEffect, useState, useCallback } from 'react';
import '../workslip-brand.css';

type Theme = 'night' | 'day';

interface ThemeContextValue {
  theme: Theme;
  toggle: () => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function getInitialTheme(): Theme {
  const stored = localStorage.getItem('theme');
  if (stored === 'day' || stored === 'night') return stored;
  if (window.matchMedia('(prefers-color-scheme: dark)').matches) return 'night';
  return 'day';
}

export const ThemeProvider = ({ children }: { children: React.ReactNode }) => {
  const [theme, setTheme] = useState<Theme>(getInitialTheme);

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme);
    localStorage.setItem('theme', theme);
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) {
      meta.setAttribute('content', theme === 'night' ? '#123B4A' : '#FFF7E8');
    }
  }, [theme]);

  const toggle = useCallback(() => {
    setTheme(prev => prev === 'night' ? 'day' : 'night');
  }, []);

  return (
    <ThemeContext.Provider value={{ theme, toggle }}>
      {children}
    </ThemeContext.Provider>
  );
};

export const useTheme = (): ThemeContextValue => {
  const ctx = useContext(ThemeContext);
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider');
  return ctx;
};