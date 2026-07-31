from pathlib import Path

changed = False

backend_test = Path('src/BE/WorkslipApi/Workslip.Tests/Jobs/JobViewTypesTests.cs')
content = backend_test.read_text(encoding='utf-8-sig')
if 'using Xunit;' not in content:
    content = content.replace('using Workslip.Domain;\n', 'using Workslip.Domain;\nusing Xunit;\n', 1)
    backend_test.write_text(content, encoding='utf-8')
    changed = True

report = Path('src/FE/src/features/jobs/routes/CompletedJobReport.tsx')
content = report.read_text(encoding='utf-8-sig')
old = '''  useEffect(() => {
    if (!id || !jobStatus) return;
    const viewType = jobStatus === JobStatus.Approved ? COMPLETED_JOB_VIEW_TYPE : undefined;
    markJobAsSeen(id, queryClient, viewType);
  }, [id, jobStatus, queryClient]);
'''
new = '''  useEffect(() => {
    if (!id || !jobStatus) return;
    if (jobStatus === JobStatus.Approved) {
      markJobAsSeen(id, queryClient, COMPLETED_JOB_VIEW_TYPE);
      return;
    }
    markJobAsSeen(id, queryClient);
  }, [id, jobStatus, queryClient]);
'''
if old in content:
    report.write_text(content.replace(old, new, 1), encoding='utf-8')
    changed = True
elif new not in content:
    raise SystemExit('Unexpected CompletedJobReport seen effect')

frontend_test = Path('src/FE/src/features/jobs/routes/CompletedJobReport.seen-state.test.tsx')
content = frontend_test.read_text(encoding='utf-8-sig')
old = '''vi.mock('../utils/markJobSeen', () => ({
  markJobAsSeen: mocks.markJobAsSeen,
}));
'''
new = '''vi.mock('../utils/markJobSeen', () => ({
  COMPLETED_JOB_VIEW_TYPE: 'Completed',
  markJobAsSeen: mocks.markJobAsSeen,
}));
'''
if old in content:
    frontend_test.write_text(content.replace(old, new, 1), encoding='utf-8')
    changed = True
elif new not in content:
    raise SystemExit('Unexpected markJobSeen test mock')

if not changed:
    raise SystemExit('WOR-246 compiler/test repairs are already applied')
