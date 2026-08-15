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

`npm run check:app-css-budget` enforces a 130 KB ceiling for `App.css` before production builds. The ceiling prevents new work from growing the monolith while existing selectors are extracted incrementally.

The guard is deliberately a ceiling, not a target. Each safe extraction should reduce the file size. Do not raise the ceiling to accommodate new feature styling.

## Extraction order

Prefer low-risk ownership moves first:

1. selectors already owned by an existing feature/layout stylesheet;
2. isolated shared-component selectors with clear import ownership;
3. responsive/layout selectors after browser regression checks;
4. broad legacy selectors and cascade-sensitive rules last.

Every extraction must preserve day/night themes, mobile safe areas, focus visibility, reduced-motion behavior, 200% zoom/reflow and supported responsive layouts.
