import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import { emitWorkslipUiFeedback } from '../../lib/uiFeedback';
import { clippy } from './clippyController';
import { HelpWizard } from './HelpWizard';

function setVisibleRect(element: HTMLElement, left: number, top: number, width: number, height: number) {
  element.getBoundingClientRect = () => ({
    x: left,
    y: top,
    left,
    top,
    right: left + width,
    bottom: top + height,
    width,
    height,
    toJSON: () => ({}),
  });
}

function enableClippy() {
  localStorage.setItem('workslip.flag.help-wizard', 'on');
}

describe('HelpWizard', () => {
  afterEach(() => {
    cleanup();
    localStorage.clear();
    window.history.replaceState({}, '', '/');
    document.querySelectorAll('[data-test-clippy-fixture]').forEach((element) => element.remove());
  });

  it('stays hidden by default when no assignment exists', () => {
    render(<HelpWizard />);
    expect(screen.queryByTestId('help-wizard')).toBeNull();
  });

  it('renders Clippy when explicitly enabled without opening a message', () => {
    enableClippy();
    render(<HelpWizard />);

    const toggle = screen.getByRole('button', { name: 'Hjælp' });
    expect(screen.getByTestId('help-wizard')).toBeInTheDocument();
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('keeps the original gold clip identity with wand and free-hand finger gun', () => {
    enableClippy();
    render(<HelpWizard />);

    const wizard = screen.getByTestId('help-wizard');
    expect(wizard.querySelector('.clippy-gold-clip')).not.toBeNull();
    expect(wizard.querySelector('.clippy-wizard-clip-outer')).not.toBeNull();
    expect(wizard.querySelector('.clippy-wizard-wand')).not.toBeNull();
    expect(wizard.querySelector('.clippy-wizard-finger-gun')).not.toBeNull();
    expect(wizard.querySelectorAll('.clippy-wizard-dust')).toHaveLength(3);
  });

  it('can still be turned off explicitly for the current identity', () => {
    localStorage.setItem('workslip.flag.help-wizard', 'off');
    render(<HelpWizard />);
    expect(screen.queryByTestId('help-wizard')).toBeNull();
  });

  it('opens and closes a concise contextual help prompt', () => {
    enableClippy();
    render(<HelpWizard />);

    const toggle = screen.getByRole('button', { name: 'Hjælp' });
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByRole('status')).toHaveTextContent('Hvad driller?');
    expect(screen.getByRole('status')).toHaveTextContent('Jeg holder mig i hjørnet, til du kalder.');

    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('uses route-specific copy without starting a tour', () => {
    enableClippy();
    window.history.replaceState({}, '', '/app/timer');
    render(<HelpWizard />);

    fireEvent.click(screen.getByRole('button', { name: 'Hjælp' }));
    expect(screen.getByRole('status')).toHaveTextContent('Timer uden bøvl.');
    expect(screen.getByRole('status')).toHaveTextContent('Du tager dig af arbejdet.');
  });

  it('moves to registered app targets and can return home', () => {
    enableClippy();
    const target = document.createElement('button');
    target.id = 'clippy-test-target';
    target.dataset.testClippyFixture = 'true';
    setVisibleRect(target, 500, 180, 120, 40);
    document.body.appendChild(target);

    render(<HelpWizard />);
    const wizard = screen.getByTestId('help-wizard');
    setVisibleRect(wizard, 10, 600, 72, 78);

    act(() => clippy.goTo('clippy-test-target'));
    expect(wizard).toHaveAttribute('data-clippy-mode', 'target');

    act(() => clippy.pointAt('clippy-test-target'));
    expect(wizard).toHaveAttribute('data-clippy-reaction', 'attention');

    act(() => clippy.goHome());
    expect(wizard).toHaveAttribute('data-clippy-mode', 'home');
  });

  it('uses real Workslip feedback to point at invalid fields and celebrate success', async () => {
    enableClippy();
    const input = document.createElement('input');
    input.id = 'feedback-invalid-field';
    input.setAttribute('aria-invalid', 'true');
    input.dataset.testClippyFixture = 'true';
    setVisibleRect(input, 520, 220, 180, 42);
    document.body.appendChild(input);

    render(<HelpWizard />);
    const wizard = screen.getByTestId('help-wizard');
    setVisibleRect(wizard, 10, 600, 72, 78);

    act(() => emitWorkslipUiFeedback({ kind: 'error' }));
    await waitFor(() => expect(wizard).toHaveAttribute('data-clippy-mode', 'target'));
    expect(wizard).toHaveAttribute('data-clippy-reaction', 'attention');

    act(() => emitWorkslipUiFeedback({ kind: 'success' }));
    expect(wizard).toHaveAttribute('data-clippy-mode', 'home');
    expect(wizard).toHaveAttribute('data-clippy-reaction', 'success');
  });

  it('points at a validation error after a form submit', async () => {
    enableClippy();
    const form = document.createElement('form');
    form.dataset.testClippyFixture = 'true';
    const input = document.createElement('input');
    input.id = 'submit-invalid-field';
    input.className = 'form-input-invalid';
    setVisibleRect(input, 440, 260, 160, 40);
    form.appendChild(input);
    document.body.appendChild(form);

    render(<HelpWizard />);
    const wizard = screen.getByTestId('help-wizard');
    setVisibleRect(wizard, 10, 600, 72, 78);

    fireEvent.submit(form);

    await waitFor(() => expect(wizard).toHaveAttribute('data-clippy-mode', 'target'));
    expect(wizard).toHaveAttribute('data-clippy-reaction', 'attention');
  });
});
