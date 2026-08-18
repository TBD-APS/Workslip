import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';

const routeSource = readFileSync(
  fileURLToPath(new URL('./JobEntryRoute.tsx', import.meta.url)),
  'utf8',
);
const viewSource = readFileSync(
  fileURLToPath(new URL('./AdminCompletedJobReport.tsx', import.meta.url)),
  'utf8',
);

describe('WOR-701 admin case overview scope', () => {
  it('routes only admins to the isolated reference view', () => {
    expect(routeSource).toContain('return isAdmin ? <AdminCompletedJobReport /> : <CompletedJobReport />;');
    expect(routeSource).toContain("import { AdminCompletedJobReport } from './AdminCompletedJobReport';");
  });

  it('keeps the reference composition local to the admin case view', () => {
    expect(viewSource).toContain('Tilbage til sager');
    expect(viewSource).toContain('Sagsinformation');
    expect(viewSource).toContain('Sagshistorik');
    expect(viewSource).toContain('Kommentar fra leder');
    expect(viewSource).toContain("import './AdminCompletedJobReport.css';");
  });
});
