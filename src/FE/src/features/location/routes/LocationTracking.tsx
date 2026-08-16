import { useCallback, useEffect, useRef, useState } from 'react';
import { apiClient } from '../../../lib/axios';
import { useIsAdmin } from '../../../providers/permissions/usePermissions';

type MyTrackingStatus = {
  sessionId: string | null;
  active: boolean;
  capturedAt: string | null;
  latitude: number | null;
  longitude: number | null;
  accuracyMeters: number | null;
};

type CurrentEmployeeLocation = {
  userId: string;
  displayName: string;
  sessionId: string;
  capturedAt: string;
  ageSeconds: number;
  isStale: boolean;
  latitude: number;
  longitude: number;
  accuracyMeters: number | null;
  trackingActive: boolean;
};

const formatAge = (seconds: number) => {
  if (seconds < 60) return `${seconds} sek.`;
  return `${Math.floor(seconds / 60)} min.`;
};

const readCurrentPosition = () => new Promise<GeolocationPosition>((resolve, reject) => {
  navigator.geolocation.getCurrentPosition(resolve, reject, {
    enableHighAccuracy: true,
    maximumAge: 15_000,
    timeout: 20_000,
  });
});

const getGeolocationErrorMessage = (error: unknown): string => {
  if (typeof error === 'object' && error !== null && 'code' in error) {
    const geoError = error as GeolocationPositionError;
    return geoError.code === geoError.PERMISSION_DENIED
      ? 'GPS-tilladelse blev afvist. Tillad lokation i browseren og prøv igen.'
      : `GPS kunne ikke læses: ${geoError.message}`;
  }

  return 'GPS kunne ikke startes. Prøv igen.';
};

export function LocationTracking() {
  const isAdmin = useIsAdmin();
  const watchId = useRef<number | null>(null);
  const sessionId = useRef<string | null>(null);
  const [status, setStatus] = useState<MyTrackingStatus | null>(null);
  const [employees, setEmployees] = useState<CurrentEmployeeLocation[]>([]);
  const [permissionError, setPermissionError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [watching, setWatching] = useState(false);

  const loadStatus = useCallback(async () => {
    const data = await apiClient.get<MyTrackingStatus>('/api/location/me') as unknown as MyTrackingStatus;
    setStatus(data);
    sessionId.current = data.sessionId;
  }, []);

  const loadEmployees = useCallback(async () => {
    if (!isAdmin) return;
    const data = await apiClient.get<CurrentEmployeeLocation[]>('/api/location/current') as unknown as CurrentEmployeeLocation[];
    setEmployees(data);
  }, [isAdmin]);

  useEffect(() => {
    void loadStatus();
    void loadEmployees();
    const poll = window.setInterval(() => { void loadEmployees(); }, 25_000);
    return () => {
      window.clearInterval(poll);
      if (watchId.current !== null) navigator.geolocation.clearWatch(watchId.current);
    };
  }, [loadEmployees, loadStatus]);

  const sendPosition = useCallback(async (position: GeolocationPosition) => {
    if (!sessionId.current) return;
    await apiClient.post('/api/location/pings', {
      sessionId: sessionId.current,
      latitude: position.coords.latitude,
      longitude: position.coords.longitude,
      accuracyMeters: position.coords.accuracy,
      capturedAt: new Date(position.timestamp).toISOString(),
    });
    setPermissionError(null);
    await loadStatus();
  }, [loadStatus]);

  const startTracking = async () => {
    if (!('geolocation' in navigator)) {
      setPermissionError('Denne browser understøtter ikke GPS/geolocation.');
      return;
    }

    setBusy(true);
    setPermissionError(null);
    try {
      const initialPosition = await readCurrentPosition();
      const started = await apiClient.post('/api/location/sessions/start') as unknown as { sessionId: string; active: boolean };
      sessionId.current = started.sessionId;
      setStatus((current) => ({
        sessionId: started.sessionId,
        active: true,
        capturedAt: current?.capturedAt ?? null,
        latitude: current?.latitude ?? null,
        longitude: current?.longitude ?? null,
        accuracyMeters: current?.accuracyMeters ?? null,
      }));

      await sendPosition(initialPosition);
      if (watchId.current !== null) navigator.geolocation.clearWatch(watchId.current);
      watchId.current = navigator.geolocation.watchPosition(
        (position) => { void sendPosition(position); },
        (error) => {
          setWatching(false);
          setPermissionError(error.code === error.PERMISSION_DENIED
            ? 'GPS-tilladelsen blev fjernet. Genoptag tracking efter du har tilladt lokation igen.'
            : `GPS kunne ikke læses: ${error.message}`);
        },
        { enableHighAccuracy: true, maximumAge: 15_000, timeout: 20_000 },
      );
      setWatching(true);
    } catch (error) {
      setPermissionError(getGeolocationErrorMessage(error));
    } finally {
      setBusy(false);
    }
  };

  const stopTracking = async () => {
    if (!sessionId.current) return;
    setBusy(true);
    try {
      await apiClient.post(`/api/location/sessions/${sessionId.current}/stop`);
      if (watchId.current !== null) {
        navigator.geolocation.clearWatch(watchId.current);
        watchId.current = null;
      }
      setWatching(false);
      sessionId.current = null;
      await loadStatus();
      await loadEmployees();
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="page-container">
      <header className="page-header">
        <div>
          <h1>GPS / dispatch</h1>
          <p className="text-muted">Lokal prototype. Tracking kører kun, når du eksplicit starter den i denne browser.</p>
        </div>
      </header>

      <section className="card" style={{ padding: 20, marginBottom: 20 }}>
        <h2>Min tracking</h2>
        <p><strong>Status:</strong> {status?.active ? (watching ? 'Tracking aktiv' : 'Session aktiv – GPS skal genoptages') : 'Tracking stoppet'}</p>
        {status?.capturedAt && <p><strong>Sidste position:</strong> {new Date(status.capturedAt).toLocaleString('da-DK')}</p>}
        {status?.accuracyMeters != null && <p><strong>Accuracy:</strong> ±{Math.round(status.accuracyMeters)} m</p>}
        {permissionError && <div role="alert" className="alert alert-danger">{permissionError}</div>}
        <div style={{ display: 'flex', gap: 12 }}>
          <button className="btn btn-primary" type="button" onClick={() => { void startTracking(); }} disabled={busy || watching}>
            {status?.active ? 'Genoptag tracking' : 'Start tracking'}
          </button>
          <button className="btn btn-secondary" type="button" onClick={() => { void stopTracking(); }} disabled={busy || status?.active !== true}>
            Stop tracking
          </button>
        </div>
      </section>

      {isAdmin && (
        <section className="card" style={{ padding: 20 }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', gap: 12, alignItems: 'center' }}>
            <h2>Dispatch</h2>
            <button className="btn btn-secondary" type="button" onClick={() => { void loadEmployees(); }}>Opdater</button>
          </div>
          {employees.length === 0 ? (
            <p className="text-muted">Ingen medarbejdere har sendt en position endnu.</p>
          ) : (
            <div style={{ overflowX: 'auto' }}>
              <table className="table">
                <thead>
                  <tr><th>Medarbejder</th><th>Status</th><th>Sidst set</th><th>Accuracy</th><th>Position</th></tr>
                </thead>
                <tbody>
                  {employees.map((employee) => (
                    <tr key={employee.userId}>
                      <td>{employee.displayName}</td>
                      <td>{employee.trackingActive && !employee.isStale ? 'Live' : employee.isStale ? 'Forældet' : 'Stoppet'}</td>
                      <td>{formatAge(employee.ageSeconds)} siden</td>
                      <td>{employee.accuracyMeters == null ? '—' : `±${Math.round(employee.accuracyMeters)} m`}</td>
                      <td>
                        <a href={`https://www.google.com/maps?q=${employee.latitude},${employee.longitude}`} target="_blank" rel="noreferrer">
                          {employee.latitude.toFixed(5)}, {employee.longitude.toFixed(5)}
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}
    </div>
  );
}
