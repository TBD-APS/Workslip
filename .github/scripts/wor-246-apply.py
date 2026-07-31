from pathlib import Path

path = Path('src/FE/src/features/jobs/routes/JobList.tsx')
content = path.read_text(encoding='utf-8-sig')
old = 'function JobCard({ job, onOpen, isAdmin }: { job: JobListItemViewModel; onOpen: () => void; isAdmin: boolean }) {'
new = 'export function JobCard({ job, onOpen, isAdmin }: { job: JobListItemViewModel; onOpen: () => void; isAdmin: boolean }) {'
if content.count(old) != 1:
    raise SystemExit(f'Expected one JobCard declaration, found {content.count(old)}')
path.write_text(content.replace(old, new, 1), encoding='utf-8')
