from pathlib import Path

path = Path("tools/temp-wor-244-apply.py")
text = path.read_text(encoding="utf-8")
start = text.index('replace(\n    "Docs/api/contract.md",')
end = text.index("\n\nbackend_test =", start)
replacement = '''replace(
    "Docs/api/contract.md",
    """Admin-authorized invitation status operations are:

```text
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```
""",
    """Admin-authorized invitation operations are:

```text
POST   /api/auth/invite
GET    /api/auth/invites
DELETE /api/auth/invites/{inviteId}
```

`POST /api/auth/invite` accepts one or more e-mail addresses and an invitation role. The only assignable roles are canonical `User` and `Auditor`; missing or blank roles retain the backward-compatible `User` default. Any other value, including `Admin` and `Superadmin`, is rejected before an invitation or e-mail side effect occurs. Resending a pending invitation replaces its role with the latest valid selection.
""",
)'''
path.write_text(text[:start] + replacement + text[end:], encoding="utf-8")
