import { JobStatus } from '../../api/generated/models/jobStatus';
import { formatJobStatus } from './statusLabels';

const draftLabel = formatJobStatus(JobStatus.Draft);
const inReviewLabel = formatJobStatus(JobStatus.InReview);
const approvedLabel = formatJobStatus(JobStatus.Approved);
const rejectedLabel = formatJobStatus(JobStatus.Rejected);

void [draftLabel, inReviewLabel, approvedLabel, rejectedLabel];
