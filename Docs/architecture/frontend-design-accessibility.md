# Frontend design and accessibility baseline

**Status:** Active  
**Owner:** Frontend owner  
**Review cadence:** When shared visual tokens, app-shell/navigation, interaction patterns or accessibility targets change

This page records the durable design and accessibility rules for the authenticated Workslip frontend. Runtime code remains the source of truth for implementation details.

## Design direction

Workslip uses the Farvelab direction as a visual system, not as page-specific decoration. Shared components should communicate the same hierarchy regardless of feature:

1. **Canvas** — one low-noise application background.
2. **Surface** — cards, panels, forms and floating content use semantic surface tokens rather than feature-specific greys.
3. **Primary action** — one clearly dominant action treatment; secondary actions stay visually quieter.
4. **Status** — success, warning, danger, info and neutral states use semantic text/background pairs and meaningful text or accessible names. Color is supplementary, not the only carrier of meaning.
5. **Focus and selection** — keyboard focus, selected state and hover are different states and must remain distinguishable.
6. **Overlay** — dialogs, action menus and other portals use the same tokens and interaction model as the authenticated shell.
7. **Motion** — animation is progressive enhancement. The workflow must remain understandable when motion is removed.

The semantic theme is scoped from `body:has(.app-shell)`. This is deliberate: existing React portals render some dialogs and menus into `document.body`, and they must inherit the same theme as the route that opened them.

## Accessibility target

The engineering baseline is **WCAG 2.2 Level AA** for the authenticated web/PWA experience, plus selected stronger product defaults where they improve field usability. This is an engineering target, not a claim of audited conformance or legal certification.

Official references:

- W3C WCAG 2.2: https://www.w3.org/TR/WCAG22/
- W3C overview of WCAG 2.2 changes: https://www.w3.org/WAI/standards-guidelines/wcag/new-in-22/
- W3C cognitive and learning accessibility: https://www.w3.org/WAI/people-use-web/abilities-barriers/cognitive/
- W3C predictable interfaces: https://www.w3.org/WAI/WCAG22/Understanding/predictable.html
- Danish Safety Technology Authority, general accessibility requirements: https://www.sik.dk/erhverv/tilgaengelighed-produkter-og-tjenester/vejledninger/generelt-om-kravene-tilgaengelighedsloven
- Danish Safety Technology Authority, e-commerce scope: https://www.sik.dk/erhverv/produkter/tilgaengelighed-produkter-og-tjenester/tilgaengelighed-e-handelstjenester

### Legal scope

Do not turn this engineering baseline into a blanket legal claim. Danish guidance states that consumer e-commerce services are within the accessibility-law scope and that services delivered only between businesses are not covered by those e-commerce requirements. Workslip's exact legal scope depends on the service and customer model in production and requires a separate compliance assessment.

## Functional-needs matrix

Accessibility is designed around functional needs rather than assuming everyone with the same diagnosis needs the same UI.

| Functional need | Conditions/users that may benefit | Workslip rule |
| --- | --- | --- |
| Low vision / reduced contrast sensitivity | Low vision, ageing, migraine-related visual sensitivity | Normal text targets at least 4.5:1; meaningful UI boundaries/focus target at least 3:1 where WCAG requires it; day/night palettes are both verified. |
| Color differentiation | Color-vision deficiency, low vision | Status always has text or an accessible name; red/green/cyan dots never carry unique meaning alone. |
| Keyboard-only operation | Blind users, motor impairment, repetitive strain injury | All primary workflows are operable without a mouse; visible `:focus-visible`; focused controls are not hidden behind sticky UI. |
| Reduced fine motor control | Tremor, cerebral palsy, arthritis, temporary injury | WCAG 2.2 AA minimum target-size rules are the floor; Workslip targets approximately 44×44 CSS px for primary touch controls where practical. Dragging cannot be the only way to perform an essential action. |
| Screen reader / non-visual navigation | Blind users, severe low vision | Native landmarks/headings/controls first; ARIA only supplements semantics; dialogs expose name and modal state; decorative icons are hidden from the accessibility tree. |
| Reading and language processing | Dyslexia, cognitive/learning disabilities | Clear labels, short actionable error text, stable terminology, meaningful headings and predictable control placement. |
| Attention / executive function | ADHD, brain injury, cognitive fatigue, some mental-health conditions | Avoid unnecessary visual competition and automatic movement; keep one dominant action per local context; preserve progress and make errors recoverable. |
| Predictability / orientation | Autism, cognitive/learning disabilities, screen-magnifier users | Repeated navigation and controls retain position, labels and behavior across routes; new design patterns require a concrete product need. |
| Motion sensitivity | Vestibular disorders, migraine | Non-essential transitions/animations respect `prefers-reduced-motion`; no workflow depends on animation. |
| Photosensitivity | Photosensitive epilepsy and related sensitivities | Do not introduce flashing/blinking content; decorative loading/motion stays below WCAG flash thresholds and is removed/reduced when the user requests reduced motion. |
| High-contrast adaptation | Windows High Contrast / forced-colors users | Focus remains visible in `forced-colors`; semantic meaning must survive when authored colors are replaced. |
| Zoom and reflow | Low vision, screen magnification | Long Danish labels and browser zoom must not remove functionality or cause page-level horizontal scrolling at supported narrow widths. |

## Current semantic token model

The authenticated theme owns these categories centrally:

- canvas and text: `--bg`, `--text`, `--text-muted`, `--text-dim`
- action: `--primary`, `--primary-hover`, `--primary-pressed`, `--on-primary`
- accents: `--accent-cyan`, `--accent-mint`, `--accent-amber`, `--accent-coral`
- surfaces: `--surface-*`
- borders/focus: `--border`, `--border-strong`, `--focus-ring`
- status: `--danger`, `--warning`, `--success`, `--status-*-bg`, `--status-*-text`
- overlays/elevation: `--overlay-*`, `--shadow-*`
- shape: `--radius-*`

Feature CSS should consume these tokens rather than introduce a second palette. Feature-specific data visualization may derive tones with `color-mix()` from the shared brand/status accents, but should not establish an independent set of hardcoded brand colors.

## Shared interaction primitives

Repeated interaction behavior belongs in shared components rather than feature-local implementations.

### Modal behavior

Modal dialogs use the shared modal-accessibility behavior. An open modal must:

- move focus inside the modal;
- contain Tab and Shift+Tab while open;
- support Escape when dismissal is allowed;
- restore focus to the invoking control when it closes;
- ensure only the topmost modal handles keyboard events when dialogs are nested.

`ConfirmDialog` is the standard confirmation primitive. `ConfirmDeleteDialog` is the destructive wrapper. Native `window.confirm()` is not used for authenticated Workslip workflows because it bypasses the app theme and shared focus behavior.

`Drawer` uses the same modal focus model. Feature dialogs such as action confirmation, create-success states, image preview and PDF preview use the same focus behavior rather than duplicating a partial modal implementation.

### Navigable rows

A visually clickable row must expose the same primary navigation to keyboard users. On existing desktop data tables, focusable rows handle Enter and Space without replacing native table semantics with `role="link"`. Keyboard events originating from nested buttons/links must not trigger the row action.

When a future table can model the primary destination as a normal link inside a cell without reducing usability, native link semantics are preferred over making the row itself interactive.

## Static contrast evidence for WOR-452

The following ratios are deterministic sRGB calculations for the token pairs changed in WOR-452. They are useful regression evidence but do not replace browser inspection because compositing, font size/weight and actual component backgrounds still matter.

| Pair | Previous | Current |
| --- | ---: | ---: |
| Night primary action text / primary | white on `#7c83ff`: ~3.21:1 | `#0b1020` on `#7c83ff`: ~5.90:1 |
| Day primary / canvas | `#5a63e8` on `#f6f7fb`: ~4.47:1 | `#5660df` on `#f6f7fb`: ~4.74:1 |
| Day dim text / canvas | `#8993aa` on `#f6f7fb`: ~2.88:1 | `#65708a` on `#f6f7fb`: ~4.63:1 |
| Day info status text / background | `#147f91` on `#e4f6f8`: ~4.21:1 | `#0d7183` on `#e4f6f8`: ~5.08:1 |
| Day danger status text / background | `#c34f4a` on `#ffebe9`: ~4.04:1 | `#ad3f3b` on `#ffebe9`: ~5.16:1 |
| Day focus / canvas | previous translucent ring: ~1.76:1 after compositing | `#4f59d4` on `#f6f7fb`: ~5.28:1 |

Filled controls on `--primary` use `--on-primary`; feature CSS must not reintroduce a fixed white foreground. This applies to primary buttons, wizard selected/active states and calendar selection.

## Interaction rules

### Focus

- Use `:focus-visible`; do not remove outlines without an equal or stronger replacement.
- Focus is independent of hover and selected state.
- Modal focus follows the shared behavior above.
- Focusable desktop table rows need an explicit visible row outline.

### Touch and pointer

- Primary icon buttons, header controls, form controls and status filters target approximately 44px hit areas.
- Small visual affordances may live inside a larger interactive box. Job-status controls deliberately keep a compact visual dot inside a 44×44 button.
- WCAG 2.2 AA's 24×24 target-size criterion remains the minimum conformance floor, including its spacing/essential exceptions.
- Hover effects are enabled only for hover-capable fine pointers; touch never depends on hover.

### Color and state

- Text labels remain the authoritative meaning for job status.
- Supplemental unread/new-rejection/unassigned dots expose accessible names.
- Decorative status dots that duplicate visible status text are hidden from assistive technology.
- Job-state visuals consume semantic status tokens; feature-specific copies of the blue/yellow/green/red status palette are not maintained.

### Motion

- `prefers-reduced-motion: reduce` applies to the entire authenticated body so body-portaled dialogs and menus are included.
- Avoid autoplay, parallax and flashing UI in core workflows.
- Gamification is decorative enhancement and must remain understandable when its motion is suppressed.

## Known gaps and required verification

WOR-452 is not complete until the acceptance evidence exists. Current known gaps after the semantic/accessibility convergence pass:

- desktop table column resizing still uses pointer dragging; this is non-essential presentation today, but it should either gain a keyboard/single-pointer alternative or remain explicitly treated as optional enhancement before claiming full WCAG 2.2 AA coverage of that control;
- real browser keyboard and screen-reader spot checks are still required even though shared modal behavior has unit regression coverage;
- day/night themes need real browser inspection at the defined mobile/tablet/desktop viewports;
- 200% text zoom and narrow-width reflow need browser evidence;
- forced-colors and reduced-motion need browser/OS preference verification;
- automated lint/build/tests and relevant Playwright flows remain required by the repository validation policy;
- `App.css` still contains some legacy declarations that are overridden by the active Farvelab/component layers. Runtime design ownership is now in the semantic/shared layers, but physical deletion of that legacy source should be done as a separate low-risk stylesheet decomposition rather than a large manual rewrite.

## Review checklist for new UI

Before a shared UI change is considered done:

- semantic token used instead of a one-off color when a token exists;
- shared interaction primitive reused instead of introducing another dialog/confirmation pattern;
- keyboard path tested, including Escape/Tab behavior for overlays;
- focus is visible and not obscured;
- status/error meaning survives without color;
- touch target is robust for the control's importance;
- 200% zoom and long Danish labels remain usable;
- reduced-motion does not remove required information;
- narrow viewport has no page-level horizontal overflow;
- day and night themes both inspected;
- relevant Playwright desktop and narrow flows recorded in the PR.
