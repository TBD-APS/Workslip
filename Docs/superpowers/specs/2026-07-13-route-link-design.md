# Route Link Design — Open in Maps

**Date:** 2026-07-13
**Status:** Approved

## Summary

Add a navigation icon button to the `DestinationAddressBlock` component that opens Google Maps with the job's destination address as a query. Single component change, no platform detection, universal Google Maps URL.

## Scope

- **One component:** `DestinationAddressBlock` in `src/FE/src/features/jobs/components/JobDetailBlocks.tsx`
- **No changes** to JobList, JobAttestationStep, or other components

## URL Construction

```
https://maps.google.com/?q={encodedAddress}
```

The address is composed from three fields joined with `, `:
1. `destinationAddress` (e.g., "Vesterbrogade 100")
2. `destinationZipCode` (e.g., "1620")
3. `destinationCity` (e.g., "København V")

Example: `https://maps.google.com/?q=Vesterbrogade%20100%2C%201620%20K%C3%B8benhavn%20V`

## Visual Layout

The Navigation icon (from `lucide-react`) is placed at the **top-right** of the `DestinationAddressBlock`, aligned with the address text line.

```
┌─────────────────────────────────────────────────┐
│ 📍 Destination Address                    🧭 │
│                                                 │
│ [Address field]  [Zip field]  [City field]      │
└─────────────────────────────────────────────────┘
```

The icon is a clickable `<a>` element opening the Google Maps URL in a new tab (`target="_blank"`).

## States

| Condition | Behavior |
|---|---|
| All three address fields empty | Navigation icon hidden |
| Any address field filled | Icon visible |
| Read-only mode (`readOnly` prop) | Icon still visible — users may want to navigate from view-only |
| Icon clicked | Opens Google Maps in new browser tab |

## Implementation Details

### Imports

Add `Navigation` to the existing `lucide-react` import in `JobDetailBlocks.tsx`. The `Navigation` icon is already used elsewhere in the app and is available.

### Button Styling

- Inline styles consistent with existing icon buttons in the component
- `cursor: pointer`, transparent background, no border
- `text-decoration: none` (it's an `<a>` tag)
- Same size as the existing `MapPin` icon (16px or 18px)
- Muted color by default, subtle hover effect

### Helper Function

Create a small helper `getMapsUrl(address, zipCode, city)` that:
1. Joins non-empty parts with `", "`
2. URL-encodes the full string
3. Returns the Google Maps URL
4. Returns `null` if all parts are empty/blank

### Component Changes

The `DestinationAddressBlock` component signature remains unchanged. The route link is purely additive — no prop changes, no interface changes.

In the header row (where `MapPin` + address text are rendered), add the Navigation icon button at the end. Position it with `justify-content: space-between` or absolute positioning depending on the current layout.

## What NOT to Do

- No platform detection (no Apple Maps URI scheme branching)
- No tooltip, label, or text — just the icon
- No changes to the address fields or their layout
- No changes to other components that display addresses
- No new dependencies beyond the already-available `Navigation` icon
