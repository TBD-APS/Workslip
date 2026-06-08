import { JobStatus } from '../../api/generated/models/jobStatus';
import { formatJobStatus } from './statusLabels';

const draftLabel: 'Kladde' = formatJobStatus(JobStatus.Draft);
const submittedLabel: 'Indsendt' = formatJobStatus(JobStatus.Submitted);
const inReviewLabel: 'Til gennemsyn' = formatJobStatus(JobStatus.InReview);
const approvedLabel: 'Godkendt' = formatJobStatus(JobStatus.Approved);
const rejectedLabel: 'Afvist' = formatJobStatus(JobStatus.Rejected);
const archivedLabel: 'Arkiveret' = formatJobStatus(JobStatus.Archived);

void [draftLabel, submittedLabel, inReviewLabel, approvedLabel, rejectedLabel, archivedLabel];
