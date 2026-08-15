# Ashfall prototype

This folder contains an isolated game prototype. It is intentionally kept outside the Workslip application and must not be imported by Workslip production code.

## Run locally

```bash
cd prototypes/ashfall
npm install
npm run dev
```

## Build

```bash
npm run build
```

## Scope

Current prototype capabilities:

- top-down canvas rendering
- keyboard and touch movement
- melee attacks with directional hit detection
- stamina-based dodge
- enemy chase AI and contact damage
- health/stamina HUD
- kill/ember progression
- death/restart and full-clear/restart states

## Review status

This prototype is for review only. It is not release-ready.

Known review concerns:

- enemy combat is still simplistic and lacks telegraphed attack states
- environment geometry is visual only and does not yet participate in collision
- no procedural run generation, loot/build system, quests, bosses or metaprogression yet
- mobile controls need real-device feel testing beyond automated browser interaction
- gameplay quality and visual direction still require product/gameplay review

All game code must remain under `prototypes/ashfall/` unless a later decision explicitly promotes it into a standalone product/repository.
