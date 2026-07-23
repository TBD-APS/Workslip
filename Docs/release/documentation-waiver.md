# Documentation waiver

Use this only when a release cannot safely wait for the normal documentation update. A waiver is temporary, visible debt.

## Required fields

```text
Documentation waiver

Missing artifact:
Reason:
Risk:
Waiver owner:
Waiver expires: YYYY-MM-DD
Follow-up: WOR-###
Release/PR:
```

## Rules

- One named person owns closure.
- The expiry date is mandatory and should be short.
- The follow-up issue contains acceptance criteria for removing the waiver.
- Security, tenant-isolation, destructive migration and recovery gaps cannot be waived without explicit release-owner approval.
- An expired waiver blocks the next production release.
- Closing the follow-up updates the missing artifact and removes the waiver reference.
