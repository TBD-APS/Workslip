from pathlib import Path

path = Path('src/FE/src/features/jobs/routes/JobList.tsx')
content = path.read_text(encoding='utf-8-sig')
if 'export export function JobCard' in content:
    content = content.replace('export export function JobCard', 'export function JobCard', 1)
elif 'export function JobCard' not in content:
    old = 'function JobCard({ job, onOpen, isAdmin }: { job: JobListItemViewModel; onOpen: () => void; isAdmin: boolean }) {'
    new = 'export function JobCard({ job, onOpen, isAdmin }: { job: JobListItemViewModel; onOpen: () => void; isAdmin: boolean }) {'
    if content.count(old) != 1:
        raise SystemExit(f'Expected one JobCard declaration, found {content.count(old)}')
    content = content.replace(old, new, 1)
else:
    raise SystemExit('JobCard is already exported correctly')
path.write_text(content, encoding='utf-8')
