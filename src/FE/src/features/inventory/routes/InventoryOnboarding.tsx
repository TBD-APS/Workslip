import axios from 'axios';
import { Check, FileSpreadsheet, Package, Printer, Upload, X } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import type { ChangeEvent } from 'react';
import type { InventoryMaterialResponse, InventoryQrLabelDocumentResponse } from '../../../api/generated/models';
import { apiClient } from '../../../lib/axios';
import './inventory-onboarding.css';

type CsvRow = {
  row: number;
  name: string;
  sku: string;
  unit: string;
  unitCost: number;
  error?: string;
};

const normalizeHeader = (value: string) => value.trim().toLocaleLowerCase('da-DK').replace(/[ _-]/g, '');

const aliases = {
  name: ['navn', 'varenavn', 'name', 'produkt'],
  sku: ['varenummer', 'sku', 'varenr', 'itemnumber', 'artikelnummer'],
  unit: ['enhed', 'unit'],
  unitCost: ['kostpris', 'pris', 'unitcost', 'cost'],
};

function detectSeparator(line: string) {
  const semicolons = (line.match(/;/g) ?? []).length;
  const commas = (line.match(/,/g) ?? []).length;
  return semicolons >= commas ? ';' : ',';
}

function parseCsvLine(line: string, separator: string): string[] {
  const cells: string[] = [];
  let current = '';
  let quoted = false;
  for (let index = 0; index < line.length; index += 1) {
    const char = line[index];
    if (char === '"') {
      if (quoted && line[index + 1] === '"') {
        current += '"';
        index += 1;
      } else {
        quoted = !quoted;
      }
    } else if (char === separator && !quoted) {
      cells.push(current.trim());
      current = '';
    } else {
      current += char;
    }
  }
  cells.push(current.trim());
  return cells;
}

function parsePrice(raw: string) {
  const normalized = raw.trim().replace(/\s/g, '').replace(/\.(?=\d{3}(?:\D|$))/g, '').replace(',', '.');
  if (!normalized) return 0;
  const value = Number(normalized);
  return Number.isFinite(value) ? value : Number.NaN;
}

function findColumn(headers: string[], candidates: string[]) {
  return headers.findIndex((header) => candidates.includes(normalizeHeader(header)));
}

function parseCsv(text: string): CsvRow[] {
  const lines = text.replace(/^\uFEFF/, '').split(/\r?\n/).filter((line) => line.trim().length > 0);
  if (lines.length < 2) return [];
  const separator = detectSeparator(lines[0]);
  const headers = parseCsvLine(lines[0], separator);
  const nameIndex = findColumn(headers, aliases.name);
  const skuIndex = findColumn(headers, aliases.sku);
  const unitIndex = findColumn(headers, aliases.unit);
  const costIndex = findColumn(headers, aliases.unitCost);
  if (nameIndex < 0) throw new Error('CSV-filen mangler en kolonne med varenavn. Brug fx “Varenavn”.');

  const seenSkus = new Set<string>();
  return lines.slice(1).map((line, index) => {
    const cells = parseCsvLine(line, separator);
    const name = cells[nameIndex]?.trim() ?? '';
    const rawSku = skuIndex >= 0 ? cells[skuIndex]?.trim() ?? '' : '';
    const sku = rawSku || `WS-${String(index + 1).padStart(5, '0')}`;
    const unit = (unitIndex >= 0 ? cells[unitIndex]?.trim() : '') || 'stk';
    const unitCost = costIndex >= 0 ? parsePrice(cells[costIndex] ?? '') : 0;
    let error = '';
    if (!name) error = 'Mangler varenavn';
    else if (!sku) error = 'Mangler varenummer';
    else if (!Number.isFinite(unitCost) || unitCost < 0) error = 'Ugyldig kostpris';
    else if (seenSkus.has(sku.toLocaleLowerCase('da-DK'))) error = 'Dubleret varenummer i filen';
    seenSkus.add(sku.toLocaleLowerCase('da-DK'));
    return { row: index + 2, name, sku, unit, unitCost, error: error || undefined };
  });
}

function apiError(error: unknown) {
  if (!axios.isAxiosError(error)) return 'Varen kunne ikke oprettes.';
  const data = error.response?.data as { message?: string; title?: string } | undefined;
  return data?.message || data?.title || 'Varen kunne ikke oprettes.';
}

export function InventoryOnboarding() {
  const [rows, setRows] = useState<CsvRow[]>([]);
  const [fileName, setFileName] = useState('');
  const [parseError, setParseError] = useState('');
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<{ created: number; failed: number } | null>(null);
  const [materials, setMaterials] = useState<InventoryMaterialResponse[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [printing, setPrinting] = useState(false);

  const loadMaterials = async () => {
    const result = await apiClient.get<InventoryMaterialResponse[]>('/api/inventory/materials');
    setMaterials(result as unknown as InventoryMaterialResponse[]);
  };

  useEffect(() => { void loadMaterials(); }, []);

  const validRows = useMemo(() => rows.filter((row) => !row.error), [rows]);
  const invalidRows = rows.length - validRows.length;

  const handleFile = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (!file) return;
    setFileName(file.name);
    setParseError('');
    setImportResult(null);
    try {
      setRows(parseCsv(await file.text()));
    } catch (error) {
      setRows([]);
      setParseError(error instanceof Error ? error.message : 'CSV-filen kunne ikke læses.');
    }
  };

  const importRows = async () => {
    if (!validRows.length) return;
    setImporting(true);
    let created = 0;
    let failed = 0;
    const queue = [...validRows];
    const workers = Array.from({ length: Math.min(4, queue.length) }, async () => {
      while (queue.length) {
        const row = queue.shift();
        if (!row) return;
        try {
          await apiClient.post('/api/inventory/materials', {
            name: row.name,
            sku: row.sku,
            unit: row.unit,
            unitCost: row.unitCost,
          }, { skipGlobalErrorToast: true });
          created += 1;
        } catch (error) {
          failed += 1;
          setRows((current) => current.map((item) => item.row === row.row ? { ...item, error: apiError(error) } : item));
        }
      }
    });
    await Promise.all(workers);
    setImportResult({ created, failed });
    setImporting(false);
    await loadMaterials();
  };

  const toggleMaterial = (id: string) => {
    setSelected((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const printLabels = async (ids: string[]) => {
    if (!ids.length) return;
    setPrinting(true);
    try {
      const labels = await Promise.all(ids.map(async (id) => {
        const label = await apiClient.get<InventoryQrLabelDocumentResponse>(`/api/inventory/materials/${id}/qr-label`);
        return label as unknown as InventoryQrLabelDocumentResponse;
      }));
      const printWindow = window.open('', '_blank', 'width=1000,height=800');
      if (!printWindow) return;
      const cards = labels.map((label) => `<article class="label"><div class="qr">${label.svg}</div><strong>${label.name}</strong><span>${label.sku}</span></article>`).join('');
      printWindow.document.write(`<!doctype html><html><head><meta charset="utf-8"><title>Workslip QR-labels</title><style>@page{size:A4;margin:9mm}*{box-sizing:border-box}body{margin:0;font-family:system-ui;color:#111}.sheet{display:grid;grid-template-columns:repeat(3,1fr);gap:5mm}.label{height:58mm;border:1px solid #aaa;border-radius:4mm;padding:4mm;display:grid;grid-template-rows:1fr auto auto;place-items:center;text-align:center;break-inside:avoid}.qr svg{width:34mm;height:34mm}.label strong{font-size:11pt;max-width:100%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.label span{font-size:9pt}@media print{.label{border-color:#bbb}}</style></head><body><main class="sheet">${cards}</main><script>window.onload=()=>window.print()</script></body></html>`);
      printWindow.document.close();
    } finally {
      setPrinting(false);
    }
  };

  return (
    <section className="inventory-onboarding" data-testid="inventory-onboarding">
      <header>
        <span className="inventory-onboarding-eyebrow"><Package size={16} /> Lageropsætning</span>
        <h1>Få varerne ind uden tastearbejde</h1>
        <p>Importér en vareliste, tjek den før oprettelse og print QR-labels på ét A4-ark.</p>
      </header>

      <div className="inventory-onboarding-card">
        <div className="inventory-onboarding-card-title">
          <FileSpreadsheet size={23} />
          <div><h2>1. Importér CSV</h2><p>Kolonnen “Varenavn” er nødvendig. Varenummer, enhed og kostpris er valgfri.</p></div>
        </div>
        <label className="inventory-upload">
          <Upload size={22} />
          <span>{fileName || 'Vælg CSV-fil'}</span>
          <input type="file" accept=".csv,text/csv" onChange={(event) => void handleFile(event)} />
        </label>
        {parseError && <div className="inventory-import-error" role="alert"><X size={18} /> {parseError}</div>}
        {rows.length > 0 && (
          <>
            <div className="inventory-import-summary">
              <strong>{validRows.length}</strong> klar til import
              {invalidRows > 0 && <span>{invalidRows} kræver rettelse</span>}
            </div>
            <div className="inventory-preview-wrap">
              <table className="inventory-preview">
                <thead><tr><th>Vare</th><th>Varenr.</th><th>Enhed</th><th>Pris</th><th>Status</th></tr></thead>
                <tbody>{rows.slice(0, 100).map((row) => (
                  <tr key={row.row} className={row.error ? 'has-error' : ''}>
                    <td>{row.name || '—'}</td><td>{row.sku}</td><td>{row.unit}</td><td>{row.unitCost.toLocaleString('da-DK')} kr.</td>
                    <td>{row.error ? <span className="bad">{row.error}</span> : <span className="good"><Check size={14} /> Klar</span>}</td>
                  </tr>
                ))}</tbody>
              </table>
            </div>
            {rows.length > 100 && <p className="inventory-preview-note">Viser de første 100 af {rows.length} varer.</p>}
            <button className="btn btn-primary inventory-import-button" type="button" disabled={importing || !validRows.length} onClick={() => void importRows()}>
              {importing ? 'Importerer…' : `Opret ${validRows.length} varer`}
            </button>
          </>
        )}
        {importResult && <div className="inventory-import-result"><Check size={19} /> {importResult.created} varer oprettet{importResult.failed ? ` · ${importResult.failed} fejlede` : ''}</div>}
      </div>

      <div className="inventory-onboarding-card">
        <div className="inventory-onboarding-card-title">
          <Printer size={23} />
          <div><h2>2. Print QR-labels</h2><p>Vælg varer eller print hele kataloget. Layoutet er lavet til almindeligt A4-labelpapir.</p></div>
        </div>
        <div className="inventory-label-actions">
          <button type="button" className="btn btn-secondary" disabled={printing || !selected.size} onClick={() => void printLabels([...selected])}>Print valgte ({selected.size})</button>
          <button type="button" className="btn btn-secondary" disabled={printing || !materials.length} onClick={() => void printLabels(materials.map((item) => item.id))}>Print alle ({materials.length})</button>
        </div>
        <div className="inventory-label-list">
          {materials.map((material) => (
            <label key={material.id} className="inventory-label-row">
              <input type="checkbox" checked={selected.has(material.id)} onChange={() => toggleMaterial(material.id)} />
              <span><strong>{material.name}</strong><small>{material.sku} · {material.unit}</small></span>
            </label>
          ))}
          {!materials.length && <p>Ingen varer endnu. Importér listen ovenfor først.</p>}
        </div>
      </div>
    </section>
  );
}
