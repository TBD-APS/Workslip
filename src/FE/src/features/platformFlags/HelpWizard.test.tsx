import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { HelpWizard } from './HelpWizard';

describe('HelpWizard', () => {
  it('renders nothing on the off-path', () => {
    render(<HelpWizard />);
    expect(screen.queryByTestId('help-wizard')).toBeNull();
  });
});
