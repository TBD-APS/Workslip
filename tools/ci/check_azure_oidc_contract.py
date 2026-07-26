from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/main_api-npteknik-prod.yml"
OIDC_TEMPLATE = ROOT / "src/BE/infrastructure/github-oidc-immutable.bicep"
README = ROOT / "src/BE/WorkslipApi/README.md"

EXPECTED_SUBJECT = (
    "repo:rasm105k@31623093/Workslip-v2.0@1245555609:environment:prod"
)


def require(text: str, expected: str, source: Path) -> None:
    if expected not in text:
        raise SystemExit(f"Missing expected contract in {source}: {expected}")


def forbid(text: str, unexpected: str, source: Path) -> None:
    if unexpected in text:
        raise SystemExit(f"Stale deployment expression remains in {source}: {unexpected}")


def main() -> None:
    workflow = WORKFLOW.read_text(encoding="utf-8")
    oidc_template = OIDC_TEMPLATE.read_text(encoding="utf-8")
    readme = README.read_text(encoding="utf-8")

    require(workflow, "environment: ${{ inputs.environment || 'prod' }}", WORKFLOW)
    require(workflow, "app-name: ${{ env.AZURE_WEBAPP_NAME }}", WORKFLOW)
    forbid(workflow, "github.event.inputs.environment", WORKFLOW)
    forbid(workflow, "github.event.inputs.app_name", WORKFLOW)

    require(oidc_template, "githubOwnerId string = '31623093'", OIDC_TEMPLATE)
    require(oidc_template, "githubRepositoryId string = '1245555609'", OIDC_TEMPLATE)
    require(oidc_template, "subject: immutableSubject", OIDC_TEMPLATE)
    require(readme, EXPECTED_SUBJECT, README)
    require(readme, "deploy-with-github-oidc.ps1", README)

    print("Azure OIDC deployment contract is consistent.")


if __name__ == "__main__":
    main()
