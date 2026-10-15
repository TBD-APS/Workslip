# Live resource locks

**Status:** Active  
**Owner:** Workslip repository owner  
**Source of truth:** `src/BE/infrastructure/aca/move-locks-to-data-resources.ps1`, `src/BE/infrastructure/run-database-migrations.ps1` and the locks in the Azure subscription  
**Review cadence:** When a lock is added, or when the migration step stops on a lock

## Why the locks sit on the data

`run-database-migrations.ps1` opens a temporary SQL firewall rule for the runner, applies the migrations and deletes the rule again. An Azure lock is inherited by everything below its scope. A `CanNotDelete` lock on the subscription, on the resource group or on the SQL server therefore also blocks deleting that firewall rule.

That happened on 2026-10-15 (live deploy run 38772712937):
- the Auditor v1 migration was applied;
- the rule cleanup then failed with `ScopeLocked`, so the deploy stopped before the new app revision;
- the rule `AllowDatabaseMigration-2c762d3e3454` was left open on `db-mrsoftwarev2-live-server`.

Locks are kept on the resources that hold data:
- every SQL database except `master`;
- every storage account;
- every key vault.

A lock on a database does not cover the server's firewall rules, which are its siblings, so deploys can clean up again. Container Apps, App Configuration and the registry are rebuilt from IaC, so they carry no lock.

## What the migration step does now

Before it adds the firewall rule, the migration step:
1. lists the locks on the resource group and the subscription;
2. **stops before any database change** if a `CanNotDelete` or `ReadOnly` lock covers the SQL server, and names the lock;
3. deletes leftover `AllowDatabaseMigration-*` rules from earlier failed runs.

If the migration identity may not read subscription-wide locks, the step warns and continues as before.

## Moving the locks

Run this as a user who may manage locks (Owner or User Access Administrator):

```powershell
./src/BE/infrastructure/aca/move-locks-to-data-resources.ps1          # dry run: shows what it would change
./src/BE/infrastructure/aca/move-locks-to-data-resources.ps1 -Apply   # makes the changes
```

With `-Apply` the script:
1. creates a `protect-data` `CanNotDelete` lock on each unlocked database, storage account and key vault;
2. reads the locks back and stops before removing anything if a data resource is still unlocked;
3. removes the `CanNotDelete` locks on the subscription and on resource groups;
4. deletes leftover `AllowDatabaseMigration-*` firewall rules.

`ReadOnly` locks are reported and never changed. They also block deploys and need a decision of their own.

After the script has run, rerun **Deploy Workslip Live App (Container Apps)**. Already applied migrations are skipped.
