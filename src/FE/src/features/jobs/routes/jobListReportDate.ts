type JobListReportDateSource = {
  jobType?: string | null;
  reportDate?: string | null;
  createdAt: string;
};

export function getJobListReportDate(job: JobListReportDateSource): string | null | undefined {
  if (job.reportDate) return job.reportDate;
  return job.jobType === 'Diverse' ? job.createdAt : null;
}
