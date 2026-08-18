# Figma design environment

**Status:** Active  
**Owner:** Frontend owner  
**Review cadence:** When the authenticated palette, the token export contract or the Figma plan/seat changes

This page records how the Workslip Figma environment is connected to the frontend implementation. Runtime code remains the source of truth for the palette; Figma is a downstream consumer of it.

## Colour ownership

Two stylesheets contribute to the authenticated visual system, and their order matters:

| Layer | File | Owns |
|---|---|---|
| Shape and structure | [`../../src/FE/src/farvelab-theme.css`](../../src/FE/src/farvelab-theme.css) | Radii, spacing rhythm, elevation shape, component structure, focus and motion rules |
| Colour semantics | [`../../src/FE/src/workslip-brand.css`](../../src/FE/src/workslip-brand.css) | Every semantic colour token for the authenticated app |

`workslip-brand.css` is imported after the Farvelab layer and scopes its declarations to `html body:has(.app-shell)`, which outranks the Farvelab `body:has(.app-shell)` selector on specificity. **The brand file therefore defines the colours the application actually renders.** Any token export, design file or contrast calculation must read it rather than the Farvelab palette.

The trade-neutral core palette is Marine `#123b4a`, Petrol `#147a7e`, Signal orange `#f47a24` and Warm cream `#fff7e8`. Orange is reserved for the single dominant action; petrol carries selection and navigation.

## Token export

```bash
cd src/FE
npm run export:design-tokens
```

The script parses both theme blocks from `workslip-brand.css`, resolves `var(--brand-*)` indirection to concrete values, and writes `src/FE/design-tokens/workslip-tokens.json`. The output is committed so palette drift is visible in review rather than discovered in Figma.

Each exported variable carries the CSS property it came from, its Figma scope and a `var(--token)` code syntax entry, so Figma Dev Mode emits the real Workslip custom property instead of a raw hex value.

Re-run the export whenever `workslip-brand.css` changes, and import the result into Figma in the same change.

## Figma file layout

The design file mirrors the export:

| Collection | Modes | Contents |
|---|---|---|
| `Primitives` | Value | The four `--brand-*` core colours |
| `Color` | Night | Semantic tokens for the default theme |
| `Color Day` | Day | The same variable names with day-theme values |
| `Scale` | Value | Radii, spacing steps and the 44px touch-target size |

Semantic tokens alias a primitive only where the CSS itself aliases one (`--primary: var(--brand-orange)`); every other value is exported literally. This keeps the Figma alias graph identical to the stylesheet.

Pages are `1 · Foundations` (palette, type ramp, scale, elevation), `2 · Current design` (component library) and `3 · Improvements` (design proposals).

## Plan constraints

The environment is currently on a Figma Starter plan with a View seat, which imposes limits that shaped the layout above:

- **One mode per collection.** Night and Day cannot be modes of a single collection, so they are paired collections sharing identical variable names. On a paid plan they merge into one two-mode collection without renaming anything.
- **Three pages maximum.** Components and screens share one page using Figma Sections instead of a page per component.
- **20 MCP tool calls per month.** Automated authoring against the file is exhausted quickly; a Full or Dev seat on a paid plan raises this to 200/day.

Upgrading the seat removes all three constraints without invalidating the token names or the export contract.

## Accessibility note

Contrast ratios must be recalculated against the brand palette, not the Farvelab values recorded in [`frontend-design-accessibility.md`](frontend-design-accessibility.md). The accessibility baseline itself — WCAG 2.2 AA, the functional-needs matrix and the interaction rules — is unchanged and remains authoritative.
