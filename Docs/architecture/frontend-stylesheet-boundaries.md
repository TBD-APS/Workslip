# Frontend stylesheet boundaries

**Status:** Active  
**Owner:** Frontend owner  
**Tracking:** WOR-475, parent architecture work WOR-443

`src/FE/src/App.css` is a legacy compatibility stylesheet, not the preferred home for new feature styling.

## Ownership

- app shell, navigation, safe-area, header/account controls and responsive shell behavior: `src/FE/src/components/layouts/`
- shared form primitives: `src/FE/src/components/forms/`
- shared component styling: beside the shared component under `src/FE/src/components/`
- feature styling: inside the owning feature under `src/FE/src/features/<feature>/`
- semantic theme and brand tokens: the established theme/brand stylesheets

New selectors must be placed in the owning stylesheet. Moving selectors out of `App.css` only counts as migration when the legacy declaration is removed in the same change; duplicate selectors are not an architectural split.

## Migration guard

`npm run check:app-css-budget` enforces a shrinking byte ceiling for `App.css` before production builds. The ceiling prevents new work from growing the monolith while existing selectors are extracted incrementally.

The current ceiling is **117,000 bytes** after the latest WOR-475 ownership extraction. The guard is deliberately a ceiling, not a target. Each safe extraction should reduce the file size and lower the ceiling in the same change. Do not raise the ceiling to accommodate new feature styling.

## Completed boundaries

### App shell

WOR-475 moves the mobile-first authenticated shell, header, content gutter, bottom navigation and create FAB rules into `src/FE/src/components/layouts/AppLayout.shell.css`. `AppLayout.focus.css` remains the import boundary and composes the mobile shell with the existing desktop and focus-specific layout files.

Header/account `.user-avatar` controls now live with that shell because their lifecycle and responsive behavior are owned by `AppLayout`. This includes the notification/profile controls used by the authenticated header.

### Shared authenticated forms

The legacy `.form-input` primitive and its focus, readonly and placeholder states now live in `src/FE/src/components/forms/FormPrimitives.css`. `authenticated-base.css` imports that shared form boundary so existing authenticated consumers keep one common form contract without duplicating selectors.

### Profile-specific actions

Profile edit action layout now lives in `src/FE/src/features/settings/routes/Profile.css`. It is no longer coupled to the global compatibility stylesheet.

These moves remove the old declarations from `App.css`; there are no compatibility duplicates for the extracted selectors.

## Remaining extraction order

Prefer low-risk ownership moves first:

1. job-list and job-card selectors into the jobs feature, separating genuinely reusable page primitives before moving them;
2. customer-specific list/card selectors into the customers feature where ownership is unambiguous;
3. report/auditor-specific selectors into the auditor feature;
4. isolated shared-component selectors with clear import ownership;
5. broad page primitives and cascade-sensitive responsive rules last, after current consumers have been enumerated.

For every extraction, inspect current consumers before choosing an owner. Do not create generic catch-all stylesheets simply to make `App.css` smaller.

Every extraction must preserve day/night themes, mobile safe areas, focus visibility, reduced-motion behavior, 200% zoom/reflow and supported responsive layouts.
