# Route Link Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Navigation icon button to `DestinationAddressBlock` that opens Google Maps with the job's destination address.

**Architecture:** Single-component change in `JobDetailBlocks.tsx`. A helper function constructs the Google Maps URL from the three address fields. The icon is an `<a>` tag positioned at the right of the section header row.

**Tech Stack:** React, lucide-react (`Navigation` icon), existing CSS variables

---

### Task 1: Add Navigation icon and maps URL helper

**Files:**
- Modify: `src/FE/src/features/jobs/components/JobDetailBlocks.tsx:2-81`
- Modify: `src/FE/src/App.css` (near line 1144)

- [ ] **Step 1: Add `Navigation` to the lucide-react import**

In `JobDetailBlocks.tsx`, line 2, add `Navigation` to the existing import:

```tsx
import { Building2, FileText, Link2, Lock, Navigation, Users } from 'lucide-react';
```

- [ ] **Step 2: Add the `getMapsUrl` helper function**

Add this function after the imports (before line 18, the `CustomerBlockProps` type):

```tsx
function getMapsUrl(address: string, zipCode: string, city: string): string | null {
  const parts = [address, zipCode, city].filter((p) => p.trim().length > 0);
  if (parts.length === 0) return null;
  return `https://maps.google.com/?q=${encodeURIComponent(parts.join(', '))}`;
}
```

- [ ] **Step 3: Add the Navigation icon button to `DestinationAddressBlock`**

Replace the `section-header-row` div (lines 66-69) to include the navigation link:

```tsx
<div className="section-header-row">
  <FileText size={18} />
  <h3>Adresse (destination){required && <span className="required-asterisk">*</span>}</h3>
  {(() => {
    const mapsUrl = getMapsUrl(value, zipCode, city);
    return mapsUrl ? (
      <a
        href={mapsUrl}
        target="_blank"
        rel="noopener noreferrer"
        className="nav-maps-link"
        title="Åbn i Google Maps"
        onClick={(e) => e.stopPropagation()}
      >
        <Navigation size={16} />
      </a>
    ) : null;
  })()}
</div>
```

- [ ] **Step 4: Add CSS for `.nav-maps-link`**

In `src/FE/src/App.css`, after the `.section-header-row h3` rule (line 1155), add:

```css
.nav-maps-link {
  margin-left: auto;
  color: var(--text-muted, #6b7280);
  text-decoration: none;
  display: flex;
  align-items: center;
  padding: 4px;
  border-radius: 4px;
  transition: color 0.15s ease;
}

.nav-maps-link:hover {
  color: var(--primary, #2563eb);
}
```

- [ ] **Step 5: Verify the build compiles**

Run from `src/FE`:
```bash
npx vite build --mode development 2>&1 | head -20
```
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add src/FE/src/features/jobs/components/JobDetailBlocks.tsx src/FE/src/App.css
git commit -m "feat: add Open in Maps link to DestinationAddressBlock"
```
