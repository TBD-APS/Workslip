import { JobStatus } from '../../api/generated/models/jobStatus';
import { formatJobStatus } from './statusLabels';

const draftLabel: 'Kladde' = formatJobStatus(JobStatus.Draft);
const inReviewLabel: 'Til gennemsyn' = formatJobStatus(JobStatus.InReview);
const approvedLabel: 'Godkendt' = formatJobStatus(JobStatus.Approved);
const rejectedLabel: 'Afvist' = formatJobStatus(JobStatus.Rejected);

void [draftLabel, inReviewLabel, approvedLabel, rejectedLabel];
