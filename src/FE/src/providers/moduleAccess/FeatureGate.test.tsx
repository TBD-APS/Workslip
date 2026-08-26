import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { FeatureGate } from './FeatureGate';
import { ModuleAccessProvider } from './ModuleAccessProvider';

function renderGate(enabled: ReadonlySet<'work-management' | 'compliance-evidence'> | 'all', isLoading = false) {
  return render(
    <ModuleAccessProvider enabled={enabled} isLoading={isLoading}>
      <FeatureGate module="compliance-evidence" fallback={<span>fallback</span>}>
        <span>installations-content</span>
      </FeatureGate>
    </ModuleAccessProvider>,
  );
}

describe('FeatureGate module gating', () => {
  it('renders children when the module is entitled', () => {
    renderGate(new Set(['compliance-evidence']));
    expect(screen.getByText('installations-content')).toBeInTheDocument();
    expect(screen.queryByText('fallback')).not.toBeInTheDocument();
  });

  it('renders children when the tenant has the all-modules sentinel', () => {
    renderGate('all');
    expect(screen.getByText('installations-content')).toBeInTheDocument();
  });

  it('hides children and shows the fallback when the module is not entitled', () => {
    renderGate(new Set(['work-management']));
    expect(screen.queryByText('installations-content')).not.toBeInTheDocument();
    expect(screen.getByText('fallback')).toBeInTheDocument();
  });

  it('renders nothing while the entitlement summary is loading', () => {
    renderGate(new Set(['work-management']), true);
    expect(screen.queryByText('installations-content')).not.toBeInTheDocument();
    expect(screen.queryByText('fallback')).not.toBeInTheDocument();
  });
});
