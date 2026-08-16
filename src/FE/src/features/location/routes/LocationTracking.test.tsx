import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocationTracking } from './LocationTracking';

const mocks = vi.hoisted(() => ({
  isAdmin: false,
  get: vi.fn(),
  post: vi.fn(),
}));

vi.mock('../../../lib/axios', () => ({
  apiClient: {
    get: mocks.get,
    post: mocks.post,
  },
}));

vi.mock('../../../providers/permissions/usePermissions', () => ({
  useIsAdmin: () => mocks.isAdmin,
}));

const position = {
  coords: {
    latitude: 56.1629,
    longitude: 10.2039,
    accuracy: 8,
    altitude: null,
    altitudeAccuracy: null,
    heading: null,
    speed: null,
  },
  timestamp: Date.parse('2026-08-16T15:45:00Z'),
} as GeolocationPosition;

describe('LocationTracking', () => {
  afterEach(cleanup);

  beforeEach(() => {
    mocks.isAdmin = false;
    mocks.get.mockReset();
    mocks.post.mockReset();
    mocks.get.mockResolvedValue({
      sessionId: null,
      active: false,
      capturedAt: null,
      latitude: null,
      longitude: null,
      accuracyMeters: null,
    });
    mocks.post.mockResolvedValue({});
  });

  it('does not create a backend session when location permission is denied', async () => {
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        getCurrentPosition: (_success: PositionCallback, error: PositionErrorCallback) => error({
          code: 1,
          message: 'denied',
          PERMISSION_DENIED: 1,
          POSITION_UNAVAILABLE: 2,
          TIMEOUT: 3,
        } as GeolocationPositionError),
        watchPosition: vi.fn(),
        clearWatch: vi.fn(),
      },
    });

    render(<LocationTracking />);
    await screen.findByText('Tracking stoppet');
    fireEvent.click(screen.getByRole('button', { name: 'Start tracking' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('GPS-tilladelse blev afvist');
    expect(mocks.post).not.toHaveBeenCalled();
  });

  it('creates a session, sends the initial position and starts watching after permission succeeds', async () => {
    const watchPosition = vi.fn().mockReturnValue(17);
    const clearWatch = vi.fn();
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: {
        getCurrentPosition: (success: PositionCallback) => success(position),
        watchPosition,
        clearWatch,
      },
    });

    mocks.post.mockImplementation(async (url: string) => {
      if (url === '/api/location/sessions/start') {
        return { sessionId: '11111111-1111-1111-1111-111111111111', active: true };
      }
      return {};
    });

    render(<LocationTracking />);
    await screen.findByText('Tracking stoppet');
    fireEvent.click(screen.getByRole('button', { name: 'Start tracking' }));

    await waitFor(() => {
      expect(mocks.post).toHaveBeenCalledWith('/api/location/sessions/start');
      expect(mocks.post).toHaveBeenCalledWith('/api/location/pings', expect.objectContaining({
        sessionId: '11111111-1111-1111-1111-111111111111',
        latitude: 56.1629,
        longitude: 10.2039,
        accuracyMeters: 8,
      }));
      expect(watchPosition).toHaveBeenCalledTimes(1);
    });

    expect(await screen.findByText('Tracking aktiv')).toBeInTheDocument();
  });
});
