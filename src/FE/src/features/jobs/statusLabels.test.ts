import { JobStatus } from '../../api/generated/models/jobStatus';
import { formatJobStatus } from './statusLabels';

const draftLabel = formatJobStatus(JobStatus.Draft);
const inReviewLabel = formatJobStatus(JobStatus.InReview);
const approvedLabel = formatJobStatus(JobStatus.Approved);
const rejectedLabel = formatJobStatus(JobStatus.Rejected);

if (draftLabel !== 'Kladde' || inReviewLabel !== 'Til gennemsyn' || approvedLabel !== 'Godkendt' || rejectedLabel !== 'Afvist') {
  throw new Error('Unexpected status labels');
}

void [draftLabel, inReviewLabel, approvedLabel, rejectedLabel];
