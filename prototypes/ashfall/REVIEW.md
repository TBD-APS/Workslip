# Ashfall adversarial review

## Agentic-loop handoff

Issue: prototype-only change (no Linear issue created)
Goal: put the Ashfall game prototype under source control in an isolated folder and send it to review without touching Workslip production behavior.
Verified facts: all added files are under `prototypes/ashfall/`; the prototype is a standalone Vite/React canvas game; no Workslip application files are modified.
Scope: prototype source, local run/build instructions, review evidence and known gaps.
Non-scope: Workslip integration, production deployment, shared dependencies, authentication, backend, database, customer-facing release.
Risks: mobile input feel, simplistic enemy combat, missing collision/world systems, incomplete roguelike loop, gameplay/visual quality.
Artifacts: branch `rbj--ashfall-game-prototype`, this review note, AppDeploy browser QA from the source prototype.
Next allowed action: adversarial code/gameplay review and isolated build/browser validation.
Stop/escalate if: the prototype requires changes outside `prototypes/ashfall/` or someone proposes merging it as release-ready without closing the named gaps.

## Adversarial findings

### High — combat model is too shallow for the intended product
Enemy behavior currently consists primarily of distance-based chasing and contact damage. There are no explicit wind-up, attack, recovery or readable telegraph states. This risks combat feeling arbitrary rather than learnable.

Recommended correction: introduce explicit enemy state machines and visible telegraphs before treating combat quality as acceptable.

### High — environment has no collision semantics
World structures are rendered but do not block the player or enemies. Visual geometry therefore lies about navigation and can break spatial expectations.

Recommended correction: define a minimal obstacle/collision model and test both player and enemy navigation around it.

### Medium — mobile controls require physical-device validation
The prototype includes pointer cancel/leave cleanup for movement and touch-safe CSS, but automated browser QA cannot establish control feel, accidental gesture behavior, latency or thumb ergonomics.

Recommended correction: run focused iPhone/Android device testing before any gameplay acceptance decision.

### Medium — roguelike loop is incomplete
The current run is a fixed five-enemy encounter. There is no procedural variation, loot/build choice, quest variation, boss progression or persistent metaprogression.

Recommended correction: define the smallest complete repeatable run loop before expanding visuals.

### Medium — visual quality is still programmer-art level
The canvas presentation is cleaner than the first prototype but is still abstract geometry rather than a coherent production art direction.

Recommended correction: establish one representative polished encounter with deliberate character/enemy/environment silhouettes and animation language before scaling content.

## Release gate

**BLOCKED** for release/product acceptance.

The branch is suitable for source review and iteration only. Passing basic automated browser interaction does not close the gameplay-quality, collision, combat-state or real-device validation gaps above.
