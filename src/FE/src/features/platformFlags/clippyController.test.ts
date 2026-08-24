import { describe, expect, it, vi } from 'vitest';
import {
  calculateClippyTargetOffset,
  clippy,
  parseClippyCommand,
  subscribeClippyCommands,
} from './clippyController';

describe('parseClippyCommand', () => {
  it('accepts bounded target commands and rejects malformed targets', () => {
    expect(parseClippyCommand({ type: 'go-to', targetId: ' save-button ' })).toEqual({
      type: 'go-to',
      targetId: 'save-button',
    });
    expect(parseClippyCommand({ type: 'go-to', targetId: '' })).toBeNull();
    expect(parseClippyCommand({ type: 'go-to', targetId: 'x'.repeat(129) })).toBeNull();
  });

  it('clamps reaction durations to a small interaction window', () => {
    expect(parseClippyCommand({ type: 'react', reaction: 'success', durationMs: 90 })).toEqual({
      type: 'react',
      reaction: 'success',
      durationMs: 150,
    });
    expect(parseClippyCommand({ type: 'react', reaction: 'warning', durationMs: 20_000 })).toEqual({
      type: 'react',
      reaction: 'warning',
      durationMs: 5_000,
    });
    expect(parseClippyCommand({ type: 'react', reaction: 'dance' })).toBeNull();
  });
});

describe('calculateClippyTargetOffset', () => {
  const home = {
    left: 10,
    top: 600,
    right: 82,
    bottom: 678,
    width: 72,
    height: 78,
  };
  const viewport = { width: 1_000, height: 800 };

  it('places Clippy next to a visible target without leaving the viewport', () => {
    expect(calculateClippyTargetOffset(home, {
      left: 500,
      top: 200,
      right: 600,
      bottom: 240,
      width: 100,
      height: 40,
    }, viewport)).toEqual({ x: 406, y: -419 });
  });

  it('uses the opposite side for targets near the left edge', () => {
    expect(calculateClippyTargetOffset(home, {
      left: 100,
      top: 200,
      right: 200,
      bottom: 240,
      width: 100,
      height: 40,
    }, viewport)).toEqual({ x: 202, y: -419 });
  });

  it('does not chase targets outside the visible viewport', () => {
    expect(calculateClippyTargetOffset(home, {
      left: 200,
      top: 900,
      right: 300,
      bottom: 940,
      width: 100,
      height: 40,
    }, viewport)).toBeNull();
  });
});

describe('clippy commands', () => {
  it('publishes validated commands to mounted Clippy controllers', () => {
    const listener = vi.fn();
    const unsubscribe = subscribeClippyCommands(listener);

    clippy.goTo('job-save-button');
    clippy.react('success', 700);

    expect(listener).toHaveBeenNthCalledWith(1, { type: 'go-to', targetId: 'job-save-button' });
    expect(listener).toHaveBeenNthCalledWith(2, { type: 'react', reaction: 'success', durationMs: 700 });

    unsubscribe();
  });
});
