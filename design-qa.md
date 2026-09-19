# Design QA — JH EL/KØL create flow

## Reference

- Existing Workslip desktop overview supplied with WOR-458.
- Approved target interaction: the orange overview action opens a focused four-step flow (`Grundlag`, `Fagspor`, `Bemanding`, `Kontrol`) without changing the shared case workflow.
- Verification viewport: 1440 × 1000, Danish locale.

## Acceptance checks

| Check | Expected | Evidence/status |
|---|---|---|
| Entry point | Existing orange `+` action opens the EL/KØL flow only for the isolated catalogue | Automated browser assertion |
| Surrounding shell | Workslip navigation, organization session banner, and overview remain unchanged | Automated screenshot `jh-el-koel-01-foundation.png` |
| Visual hierarchy | One centered modal, clear title, four-step progress, primary action at lower right | Pending CI screenshot review |
| Scope choice | `Kun EL`, `Kun KØL`, and `EL + KØL` are mutually exclusive and readable | Automated browser assertion |
| Competency gate | Combined work cannot continue until both EL and KØL competencies are covered | Automated browser assertion |
| Review | Selected asset, disciplines, staff, and generated control package are visible before creation | Automated screenshot `jh-el-koel-02-review.png` |
| Responsive safety | Modal collapses to one column at 720 px and uses a bottom-aligned mobile presentation | CSS/build validation |
| Existing branches | NP/VVS continues to use the existing create sheet | Existing authenticated Playwright suite runs before the isolated profile suite |

## Final result

Pending authenticated CI browser evidence. The PR must not be handed off as complete until this section records either a passed visual review or a specific blocker.
