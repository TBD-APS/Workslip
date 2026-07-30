from pathlib import Path

path = Path('src/FE/src/features/jobs/routes/CompletedJobReport.tsx')
text = path.read_text(encoding='utf-8')
old = "onGoToJob={() => navigate(`/app/completed/${job.id}`, { replace: true, state: { from: '/app' } })}"
new = "onGoToJob={() => setUndoRejectionCompleted(false)}"

if text.count(old) != 1:
    raise SystemExit(f'Expected exactly one same-route navigation, found {text.count(old)}')

path.write_text(text.replace(old, new, 1), encoding='utf-8')
