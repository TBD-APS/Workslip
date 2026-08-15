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

The guard is deliberately a ceiling, not a target. Each safe extraction should reduce the file size and lower the ceiling in the same change. Do not raise the ceiling to accommodate new feature styling.

## Completed boundaries

### App shell

WOR-475 moves the mobile-first authenticated shell, header, content gutter, bottom navigation and create FAB rules into `src/FE/src/components/layouts/AppLayout.shell.css`. `AppLayout.focus.css` remains the import boundary and composes the mobile shell with the existing desktop and focus-specific layout files.

The extraction deliberately leaves `user-avatar`, profile edit actions, forms and job-list selectors in `App.css`; those have different owners and must not be swept into the layout layer merely because they were adjacent in the legacy file.

## Remaining extraction order

Prefer low-risk ownership moves first:

1. shared form/input selectors into the existing shared form-control ownership;
2. `user-avatar` styling beside the shared `ProfileAvatar` component and profile-only actions beside the settings/profile route;
3. job-list/page selectors into the jobs feature, separating reusable page primitives before moving them;
4. isolated shared-component selectors with clear import ownership;
5. broad legacy selectors and cascade-sensitive responsive rules last.

For every extraction, inspect current consumers before choosing an owner. Do not create generic catch-all stylesheets simply to make `App.css` smaller.

Every extraction must preserve day/night themes, mobile safe areas, focus visibility, reduced-motion behavior, 200% zoom/reflow and supported responsive layouts.
