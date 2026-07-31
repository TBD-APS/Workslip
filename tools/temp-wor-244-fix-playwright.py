from pathlib import Path

path = Path("src/FE/wor-244.validation.spec.ts")
text = path.read_text(encoding="utf-8")
old = "  await expect(page.getByText('1 invitation(er) sendt')).toBeVisible();"
new = "  await expect(page.getByRole('button', { name: 'Send invitation' })).toBeDisabled();"
if old not in text:
    raise RuntimeError("Expected toast assertion was not found in WOR-244 Playwright spec.")
path.write_text(text.replace(old, new, 1), encoding="utf-8")
