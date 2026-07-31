from pathlib import Path

path = Path('src/FE/src/features/jobs/routes/CompletedJobReport.tsx')
content = path.read_text(encoding='utf-8-sig')
old = '''  const job = details.job;
  const isDiverseInReview = job?.jobType === 'Diverse' && job?.status === JobStatus.InReview;

  useEffect(() => {
    if (!id || !job) return;
    const viewType = job.status === JobStatus.Approved ? COMPLETED_JOB_VIEW_TYPE : undefined;
    markJobAsSeen(id, queryClient, viewType);
  }, [id, job?.status, queryClient]);
'''
new = '''  const job = details.job;
  const jobStatus = job?.status;
  const isDiverseInReview = job?.jobType === 'Diverse' && jobStatus === JobStatus.InReview;

  useEffect(() => {
    if (!id || !jobStatus) return;
    const viewType = jobStatus === JobStatus.Approved ? COMPLETED_JOB_VIEW_TYPE : undefined;
    markJobAsSeen(id, queryClient, viewType);
  }, [id, jobStatus, queryClient]);
'''
if content.count(old) != 1:
    raise SystemExit(f'Expected one CompletedJobReport effect, found {content.count(old)}')
path.write_text(content.replace(old, new, 1), encoding='utf-8')
