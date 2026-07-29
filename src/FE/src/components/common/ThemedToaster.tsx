import { Toaster } from 'sonner';
import { useTheme } from '../../providers/ThemeProvider';

export function ThemedToaster() {
  const { theme } = useTheme();

  return (
    <Toaster
      theme={theme === 'night' ? 'dark' : 'light'}
      position="top-center"
      toastOptions={{
        style: {
          background: 'var(--surface-color)',
          border: '1px solid var(--surface-border)',
          backdropFilter: 'blur(20px)',
          color: 'var(--text-primary)',
        },
      }}
    />
  );
}
