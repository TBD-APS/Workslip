from pathlib import Path

readme_path = Path('src/FE/README.md')
content = readme_path.read_text(encoding='utf-8-sig')
old = (
    'The service worker claims clients but never navigates window clients. '
    'First-time installation is excluded from all update-banner and reload paths.'
)
new = (
    'The service worker claims clients. Push-notification clicks reuse a focused or visible '
    'Workslip window when available, await navigation to the same-origin notification target, '
    'and then focus the navigated client. A new window is opened only when no application client '
    'exists. First-time installation is excluded from all update-banner and reload paths.'
)
count = content.count(old)
if count != 1:
    raise SystemExit(f'Expected one service-worker navigation paragraph, found {count}')
readme_path.write_text(content.replace(old, new, 1), encoding='utf-8')
