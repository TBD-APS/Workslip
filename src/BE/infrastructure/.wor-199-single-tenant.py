from pathlib import Path


def replace_exact(path: str, old: str, new: str, expected_count: int = 1) -> None:
    file_path = Path(path)
    content = file_path.read_text(encoding="utf-8-sig")
    count = content.count(old)
    if count != expected_count:
        raise RuntimeError(
            f"Expected {expected_count} match(es) in {path}, found {count}:\n{old}"
        )
    file_path.write_text(
        content.replace(old, new),
        encoding="utf-8",
        newline="\n",
    )


deploy_entra = "src/BE/infrastructure/deploy-entra.ps1"
replace_exact(
    deploy_entra,
    "    signInAudience = 'AzureADandPersonalMicrosoftAccount'",
    "    # Workslip authenticates members and invited B2B guests in this tenant.\n"
    "    # Single-tenant registration is required for the login_hint optional claim\n"
    "    # used by promptless Microsoft logout.\n"
    "    signInAudience = 'AzureADMyOrg'",
    expected_count=2,
)

infra_readme = "src/BE/infrastructure/README.md"
replace_exact(
    infra_readme,
    "The script preserves existing managed role/scope IDs and does not create an OAuth client secret. The browser authenticates with authorization code + PKCE; the API validates bearer tokens. The client registration also requests the `login_hint` optional ID-token claim so explicit logout can identify the active Microsoft session and return directly to Workslip without a logout account picker.",
    "The script preserves existing managed role/scope IDs and does not create an OAuth client secret. Both registrations are single-tenant (`AzureADMyOrg`): all member and invited B2B guest accounts in the Workslip tenant can sign in, while accounts that have not been invited into the tenant cannot. The browser authenticates with authorization code + PKCE; the API validates bearer tokens. The client registration requests the `login_hint` optional ID-token claim so explicit logout can identify the active Microsoft session and return directly to Workslip without a logout account picker. Microsoft does not support optional claims for registrations that combine Entra and direct personal Microsoft-account audiences, so do not restore `AzureADandPersonalMicrosoftAccount` without redesigning logout.",
)

frontend_readme = "src/FE/README.md"
replace_exact(
    frontend_readme,
    "The Entra client registration must expose the `login_hint` optional ID-token claim; `deploy-entra.ps1` reconciles it. Sessions created before that configuration is deployed and the user signs in again have no stored hint and can still see Microsoft's account picker once. Microsoft logout does not remove remembered account tiles from the operating system, Outlook, Authenticator or Microsoft's sign-in account chooser.",
    "The Entra client registration must be single-tenant and expose the `login_hint` optional ID-token claim; `deploy-entra.ps1` reconciles both requirements. Invited external users remain supported as B2B guests in the Workslip tenant. Sessions created before that configuration is deployed and the user signs in again have no stored hint and can still see Microsoft's account picker once. Microsoft logout does not remove remembered account tiles from the operating system, Outlook, Authenticator or Microsoft's sign-in account chooser.",
)

content = Path(deploy_entra).read_text(encoding="utf-8")
if content.count("signInAudience = 'AzureADMyOrg'") != 2:
    raise RuntimeError("Both Entra registrations must be single-tenant.")
if "AzureADandPersonalMicrosoftAccount" in content:
    raise RuntimeError("Legacy mixed-account sign-in audience remains in deploy-entra.ps1.")
