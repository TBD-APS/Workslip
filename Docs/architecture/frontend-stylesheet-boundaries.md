# Frontend stylesheet boundaries

**Status:** Active  
**Owner:** Frontend owner  
**Tracking:** WOR-475, parent architecture work WOR-443

`src/FE/src/App.css` is a legacy compatibility stylesheet, not the preferred home for new feature styling.

## Ownership

- app shell, navigation, safe-area and responsive shell behavior: `src/FE/src/components/layouts/`
- shared component styling: beside the shared component under `src/FE/src/components/`
- feature styling: inside the owning feature under `src/FE/src/features/<feature>/`
- semantic theme and brand tokens: the established theme/brand stylesheets

New selectors must be placed in the owning stylesheet. Moving selectors out of `App.css` only counts as migration when the legacy declaration is removed in the same change; duplicate selectors are not an architectural split.

## Migration guard

`npm run check:app-css-budget` enforces a shrinking byte ceiling for `App.css` before production builds. The ceiling prevents new work from growing the monolith while existing selectors are extracted incrementally.

`npm run check:color-budget` applies the same shrinking-ceiling pattern to colour literals that bypass the token layer; see [`figma-design-environment.md`](figma-design-environment.md).

The guard is deliberately a ceiling, not a target. Each safe extraction should reduce the file size and lower the ceiling in the same change. Do not raise the ceiling to accommodate new feature styling.

## Completed boundaries

### App shell

WOR-475 moves the mobile-first authenticated shell, header, content gutter, bottom navigation and create FAB rules into `src/FE/src/components/layouts/AppLayout.shell.css`. `AppLayout.focus.css` remains the import boundary and composes the mobile shell with the existing desktop and focus-specific layout files.

The extraction deliberately leaves `user-avatar`, profile edit actions, forms and job-list selectors in `App.css`; those have different owners and must not be swept into the layout layer merely because they were adjacent in the legacy file.

### Help wizard / Clippy 2.0

WOR-762 adds the help-wizard feature styling to `src/FE/src/features/platformFlags/help-wizard.css`. The stylesheet defines:

- fixed positioning using `env(safe-area-inset-left)` and `env(safe-area-inset-bottom)` for mobile safe areas;
- a `z-index` of `120` for the wizard container;
- responsive desktop rail layout adjustments via `body:has(.app-shell)` and `body:has(.app-shell .desktop-rail-toggle-input:checked)` selectors, shifting the home position left of the rail and respecting collapsed rail state;
- CSS custom properties for brand tokens (`--brand-marine`, `--brand-cream`, `--primary`), surface colours (`--surface-floating`, `--overlay-subtle`), text (`--text`, `--text-muted`), borders (`--border`, `--focus-ring`), shadows (`--shadow-sm`) and radii (`--radius`);
- `prefers-reduced-motion` media-query rules that disable idle blink, wand idle animations, spring transitions and hover transforms;
- keyframe animations (`clippy-blink`, `clippy-wand-idle`) scoped to the wizard mascot SVG.

The feature does not add selectors to `App.css`. The wizard toggle is `72px` by `78px`, uses `pointer-events: none` on the container and `pointer-events: auto` on interactive children, and remains inside the viewport on both desktop and mobile viewports.

## Remaining extraction order

Prefer low-risk ownership moves first:

1. shared form/input selectors into the existing shared form-control ownership;
2. `user-avatar` styling beside the shared `ProfileAvatar` component and profile-only actions beside the settings/profile route;
3. job-list/page selectors into the jobs feature, separating reusable page primitives before moving them;
4. isolated shared-component selectors with clear import ownership;
5. broad legacy selectors and cascade-sensitive responsive rules last.

For every extraction, inspect current consumers before choosing an owner. Do not create generic catch-all stylesheets simply to make `App.css` smaller.

Every extraction must preserve day/night themes, mobile safe areas, focus visibility, reduced-motion behavior, 200% zoom/reflow and supported responsive layouts.
