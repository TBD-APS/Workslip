import axios from 'axios';
import { Camera, Check, Minus, Package, Plus, Printer, RotateCcw, ScanLine, Warehouse } from 'lucide-react';
import { useCallback, useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import type {
  InventoryLocationResponse,
  InventoryMaterialResponse,
  InventoryQrLabelDocumentResponse,
  InventoryScanResponse,
} from '../../../api/generated/models';
import {
  getApiInventoryLocations,
  postApiInventoryLocations,
  postApiInventoryMaterials,
  postApiInventoryMovements,
  postApiInventoryScanImage,
} from '../../../api/generated/inventory/inventory';
import { apiClient } from '../../../lib/axios';
import { useCan } from '../../../providers/permissions';
import './inventory.css';

const SCAN_INTERVAL_MS = 650;

function errorMessage(error: unknown, fallback: string) {
  if (!axios.isAxiosError(error)) return fallback;
  const data = error.response?.data as { message?: string; title?: string; errors?: Record<string, string[]> } | undefined;
  return data?.message || data?.title || Object.values(data?.errors ?? {})[0]?.[0] || fallback;
}

function parseDanishNumber(value: string) {
  const normalized = value.trim().replace(/\s/g, '').replace(/\.(?=\d{3}(?:\D|$))/g, '').replace(',', '.');
  const parsed = Number(normalized);
  return Number.isFinite(parsed) ? parsed : Number.NaN;
}

function populateSingleLabelWindow(printWindow: Window, label: InventoryQrLabelDocumentResponse) {
  const document = printWindow.document;
  document.open();
  document.write('<!doctype html><html><head><meta charset="utf-8"><title>Workslip QR-label</title><style>body{font-family:system-ui;text-align:center;padding:24px}.label{border:2px solid #111;border-radius:16px;padding:20px;display:inline-block}.qr svg{width:260px;height:260px}h1{font-size:24px;margin:12px 0 4px}.sku{font-size:16px}</style></head><body><div class="label"><div class="qr"></div><h1></h1><div class="sku"></div></div></body></html>');
  document.close();
  const qr = document.querySelector<HTMLElement>('.qr');
  const name = document.querySelector<HTMLElement>('h1');
  const sku = document.querySelector<HTMLElement>('.sku');
  if (qr) qr.innerHTML = label.svg;
  if (name) name.textContent = label.name;
  if (sku) sku.textContent = label.sku;
  document.title = label.name;
  printWindow.focus();
  printWindow.print();
}

export function InventoryPage() {
  const canManage = useCan('user:manage');
  const videoRef = useRef<HTMLVideoElement>(null);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const scanTimerRef = useRef<number | null>(null);
  const scanBusyRef = useRef(false);
  const scanningRef = useRef(false);
  const scanSessionRef = useRef(0);
  const locationsRef = useRef<InventoryLocationResponse[]>([]);
  const movementCommandRef = useRef<{ key: string; commandId: string } | null>(null);

  const [locations, setLocations] = useState<InventoryLocationResponse[]>([]);
  const [scan, setScan] = useState<InventoryScanResponse | null>(null);
  const [selectedLocationId, setSelectedLocationId] = useState('');
  const [quantity, setQuantity] = useState('1');
  const [scanning, setScanning] = useState(false);
  const [scannerError, setScannerError] = useState('');
  const [posting, setPosting] = useState(false);
  const [success, setSuccess] = useState('');
  const [materials, setMaterials] = useState<InventoryMaterialResponse[]>([]);
  const [adminOpen, setAdminOpen] = useState(false);
  const [materialForm, setMaterialForm] = useState({ name: '', sku: '', unit: 'stk', unitCost: '0' });
  const [locationName, setLocationName] = useState('');
  const [adminBusy, setAdminBusy] = useState(false);

  const stopScanner = useCallback(() => {
    scanSessionRef.current += 1;
    scanningRef.current = false;
    setScanning(false);
    if (scanTimerRef.current !== null) window.clearTimeout(scanTimerRef.current);
    scanTimerRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    if (videoRef.current) videoRef.current.srcObject = null;
  }, []);

  useEffect(() => () => stopScanner(), [stopScanner]);

  const loadLocations = useCallback(async () => {
    const result = await getApiInventoryLocations();
    const activeLocations = result.filter((location) => location.isActive);
    locationsRef.current = activeLocations;
    setLocations(activeLocations);
    setSelectedLocationId((current) => current || activeLocations[0]?.id || '');
  }, []);

  const loadMaterials = useCallback(async () => {
    if (!canManage) return;
    const result = await apiClient.get<InventoryMaterialResponse[]>('/api/inventory/materials');
    setMaterials(result as unknown as InventoryMaterialResponse[]);
  }, [canManage]);

  useEffect(() => {
    // State changes happen after the asynchronous initial API hydration completes.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadLocations();
    void loadMaterials();
  }, [loadLocations, loadMaterials]);

  async function captureAndScan(sessionId: number) {
    if (!scanningRef.current || scanBusyRef.current || sessionId !== scanSessionRef.current) return;
    const video = videoRef.current;
    const canvas = canvasRef.current;
    if (!video || !canvas || video.readyState < HTMLMediaElement.HAVE_CURRENT_DATA) {
      scanTimerRef.current = window.setTimeout(() => void captureAndScan(sessionId), SCAN_INTERVAL_MS);
      return;
    }

    scanBusyRef.current = true;
    try {
      const maxWidth = 960;
      const scale = Math.min(1, maxWidth / video.videoWidth);
      canvas.width = Math.max(1, Math.round(video.videoWidth * scale));
      canvas.height = Math.max(1, Math.round(video.videoHeight * scale));
      const context = canvas.getContext('2d');
      if (!context) throw new Error('camera_canvas');
      context.drawImage(video, 0, 0, canvas.width, canvas.height);
      const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, 'image/jpeg', 0.72));
      if (!blob) throw new Error('camera_frame');
      const result = await postApiInventoryScanImage({ file: new File([blob], 'scan.jpg', { type: 'image/jpeg' }) }, { skipGlobalErrorToast: true });
      if (!scanningRef.current || sessionId !== scanSessionRef.current) return;
      setScannerError('');
      setScan(result);
      setSuccess('');
      movementCommandRef.current = null;
      const stockedLocation = result.balances.find((balance) => Number(balance.quantity) > 0);
      setSelectedLocationId(stockedLocation?.locationId || result.balances[0]?.locationId || locationsRef.current[0]?.id || '');
      stopScanner();
      return;
    } catch (error) {
      if (axios.isAxiosError(error) && error.response?.status === 404) {
        // A frame without a QR code is normal while the camera is moving.
      } else if (scanningRef.current && sessionId === scanSessionRef.current) {
        setScannerError(errorMessage(error, 'QR-koden kunne ikke læses. Prøv igen.'));
      }
    } finally {
      scanBusyRef.current = false;
    }

    if (scanningRef.current && sessionId === scanSessionRef.current) {
      scanTimerRef.current = window.setTimeout(() => void captureAndScan(sessionId), SCAN_INTERVAL_MS);
    }
  }

  const startScanner = async () => {
    stopScanner();
    const sessionId = scanSessionRef.current;
    setScan(null);
    setSuccess('');
    setScannerError('');
    movementCommandRef.current = null;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: { ideal: 'environment' }, width: { ideal: 1280 }, height: { ideal: 720 } },
        audio: false,
      });
      if (sessionId !== scanSessionRef.current) {
        stream.getTracks().forEach((track) => track.stop());
        return;
      }
      streamRef.current = stream;
      scanningRef.current = true;
      setScanning(true);
      if (videoRef.current) {
        videoRef.current.srcObject = stream;
        await videoRef.current.play();
      }
      void captureAndScan(sessionId);
    } catch {
      if (sessionId !== scanSessionRef.current) return;
      stopScanner();
      setScannerError('Workslip kunne ikke åbne kameraet. Tillad kameraadgang og prøv igen.');
    }
  };

  const applyMovement = async (direction: 'in' | 'out') => {
    const parsedQuantity = parseDanishNumber(quantity);
    if (!scan || !selectedLocationId || !Number.isFinite(parsedQuantity) || parsedQuantity <= 0) {
      setScannerError('Indtast et gyldigt antal større end 0.');
      return;
    }

    const commandKey = `${scan.materialId}:${selectedLocationId}:${direction}:${parsedQuantity}`;
    if (movementCommandRef.current?.key !== commandKey) {
      movementCommandRef.current = { key: commandKey, commandId: crypto.randomUUID() };
    }

    setPosting(true);
    setSuccess('');
    setScannerError('');
    try {
      const movement = await postApiInventoryMovements({
        materialId: scan.materialId,
        locationId: selectedLocationId,
        direction,
        quantity: parsedQuantity,
        commandId: movementCommandRef.current.commandId,
        reason: 'QR-scanner',
      });
      setScan((current) => current ? {
        ...current,
        balances: current.balances.some((balance) => balance.locationId === selectedLocationId)
          ? current.balances.map((balance) => balance.locationId === selectedLocationId
            ? { ...balance, quantity: movement.balanceAfter }
            : balance)
          : [...current.balances, {
            materialId: current.materialId,
            locationId: selectedLocationId,
            locationName: locationsRef.current.find((location) => location.id === selectedLocationId)?.name || 'Lager',
            quantity: movement.balanceAfter,
          }],
      } : current);
      const formattedQuantity = parsedQuantity.toLocaleString('da-DK');
      setSuccess(direction === 'out' ? `${formattedQuantity} ${scan.unit} taget ud` : `${formattedQuantity} ${scan.unit} lagt på lager`);
      setQuantity('1');
      movementCommandRef.current = null;
    } catch (error) {
      setScannerError(errorMessage(error, 'Lagerhandlingen kunne ikke gennemføres. Tryk igen for at genbruge samme handling sikkert.'));
    } finally {
      setPosting(false);
    }
  };

  const createMaterial = async (event: FormEvent) => {
    event.preventDefault();
    const unitCost = parseDanishNumber(materialForm.unitCost);
    if (!Number.isFinite(unitCost) || unitCost < 0) return;
    setAdminBusy(true);
    try {
      await postApiInventoryMaterials({
        name: materialForm.name,
        sku: materialForm.sku,
        unit: materialForm.unit,
        unitCost,
      });
      setMaterialForm({ name: '', sku: '', unit: 'stk', unitCost: '0' });
      await loadMaterials();
    } finally {
      setAdminBusy(false);
    }
  };

  const createLocation = async (event: FormEvent) => {
    event.preventDefault();
    if (!locationName.trim()) return;
    setAdminBusy(true);
    try {
      await postApiInventoryLocations({ name: locationName });
      setLocationName('');
      await loadLocations();
    } finally {
      setAdminBusy(false);
    }
  };

  const printLabel = async (material: InventoryMaterialResponse) => {
    const printWindow = window.open('', '_blank', 'width=480,height=640');
    if (!printWindow) return;
    try {
      const label = await apiClient.get<InventoryQrLabelDocumentResponse>(`/api/inventory/materials/${material.id}/qr-label`) as unknown as InventoryQrLabelDocumentResponse;
      populateSingleLabelWindow(printWindow, label);
    } catch (error) {
      printWindow.close();
      throw error;
    }
  };

  const currentBalance = scan?.balances.find((balance) => balance.locationId === selectedLocationId);
  const parsedQuantity = parseDanishNumber(quantity);

  return (
    <section className="inventory-page" data-testid="inventory-page">
      <header className="inventory-hero">
        <div>
          <span className="inventory-eyebrow"><Warehouse size={15} /> Lager</span>
          <h1>Scan. Tag. Videre.</h1>
          <p>Scan QR-koden på varen og registrer lageret direkte fra telefonen.</p>
        </div>
        {canManage && (
          <button className="inventory-admin-toggle" type="button" onClick={() => setAdminOpen((open) => !open)}>
            <Package size={18} /> {adminOpen ? 'Til scanner' : 'Varer & labels'}
          </button>
        )}
      </header>

      {!adminOpen && (
        <div className="inventory-mobile-flow">
          {!scan && (
            <div className={`inventory-scanner ${scanning ? 'is-scanning' : ''}`}>
              <div className="inventory-camera-stage">
                {scanning ? (
                  <>
                    <video ref={videoRef} playsInline muted className="inventory-video" />
                    <div className="inventory-scan-frame" aria-hidden="true"><span /></div>
                  </>
                ) : (
                  <div className="inventory-camera-empty">
                    <ScanLine size={54} strokeWidth={1.6} />
                    <strong>Klar til at scanne</strong>
                    <span>Hold QR-koden inden for rammen</span>
                  </div>
                )}
              </div>
              <canvas ref={canvasRef} hidden />
              <button className="inventory-scan-button" type="button" onClick={() => void startScanner()} disabled={scanning}>
                <Camera size={22} /> {scanning ? 'Scanner…' : 'Scan vare'}
              </button>
              {scanning && <button className="inventory-stop-button" type="button" onClick={stopScanner}>Stop kamera</button>}
            </div>
          )}

          {scan && (
            <div className="inventory-result">
              <div className="inventory-result-head">
                <div className="inventory-package-icon"><Package size={28} /></div>
                <div><span>{scan.sku}</span><h2>{scan.name}</h2></div>
                <button type="button" className="inventory-rescan" onClick={() => void startScanner()} aria-label="Scan en anden vare"><RotateCcw size={19} /></button>
              </div>

              <label className="inventory-field">
                <span>Hvor?</span>
                <select value={selectedLocationId} onChange={(event) => { setSelectedLocationId(event.target.value); movementCommandRef.current = null; }}>
                  {locations.map((location) => <option key={location.id} value={location.id}>{location.name}</option>)}
                </select>
              </label>

              <div className="inventory-balance-card">
                <span>På lager her</span>
                <strong>{Number(currentBalance?.quantity ?? 0).toLocaleString('da-DK')} <small>{scan.unit}</small></strong>
              </div>

              <div className="inventory-quantity">
                <button type="button" onClick={() => { setQuantity(String(Math.max(1, (Number.isFinite(parsedQuantity) ? parsedQuantity : 1) - 1))); movementCommandRef.current = null; }} aria-label="Fjern én"><Minus size={22} /></button>
                <label><span>Antal</span><input inputMode="decimal" value={quantity} onChange={(event) => { setQuantity(event.target.value); movementCommandRef.current = null; }} /></label>
                <button type="button" onClick={() => { setQuantity(String((Number.isFinite(parsedQuantity) ? parsedQuantity : 0) + 1)); movementCommandRef.current = null; }} aria-label="Tilføj én"><Plus size={22} /></button>
              </div>

              <div className="inventory-actions">
                <button className="inventory-action inventory-action-out" type="button" disabled={posting || !selectedLocationId} onClick={() => void applyMovement('out')}>
                  <Minus size={25} /><span><strong>Tag ud</strong><small>Brugt på opgave</small></span>
                </button>
                <button className="inventory-action inventory-action-in" type="button" disabled={posting || !selectedLocationId} onClick={() => void applyMovement('in')}>
                  <Plus size={25} /><span><strong>Læg ind</strong><small>Fyld lager op</small></span>
                </button>
              </div>
            </div>
          )}

          {success && <div className="inventory-success" role="status"><Check size={20} /> {success}</div>}
          {scannerError && <div className="inventory-error" role="alert">{scannerError}</div>}
        </div>
      )}

      {adminOpen && canManage && (
        <div className="inventory-admin">
          <div className="inventory-admin-card">
            <h2>Ny vare</h2>
            <form onSubmit={(event) => void createMaterial(event)}>
              <input required placeholder="Varenavn" value={materialForm.name} onChange={(event) => setMaterialForm({ ...materialForm, name: event.target.value })} />
              <input required placeholder="Varenummer / SKU" value={materialForm.sku} onChange={(event) => setMaterialForm({ ...materialForm, sku: event.target.value })} />
              <div className="inventory-admin-row">
                <input required placeholder="Enhed" value={materialForm.unit} onChange={(event) => setMaterialForm({ ...materialForm, unit: event.target.value })} />
                <input required inputMode="decimal" placeholder="Kostpris" value={materialForm.unitCost} onChange={(event) => setMaterialForm({ ...materialForm, unitCost: event.target.value })} />
              </div>
              <button className="btn btn-primary" disabled={adminBusy}>Opret vare</button>
            </form>
          </div>

          <div className="inventory-admin-card">
            <h2>Lagerlokation</h2>
            <form className="inventory-location-form" onSubmit={(event) => void createLocation(event)}>
              <input required placeholder="Fx Bil 1 eller Lager Aarhus" value={locationName} onChange={(event) => setLocationName(event.target.value)} />
              <button className="btn btn-secondary" disabled={adminBusy}>Tilføj</button>
            </form>
            <div className="inventory-location-chips">{locations.map((location) => <span key={location.id}>{location.name}</span>)}</div>
          </div>

          <div className="inventory-admin-card inventory-material-list">
            <h2>QR-labels</h2>
            {materials.length === 0 ? <p>Opret den første vare for at få en QR-label.</p> : materials.map((material) => (
              <div className="inventory-material" key={material.id}>
                <div><strong>{material.name}</strong><span>{material.sku} · {material.unit}</span></div>
                <button type="button" onClick={() => void printLabel(material)}><Printer size={18} /> Print QR</button>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  );
}

export default InventoryPage;