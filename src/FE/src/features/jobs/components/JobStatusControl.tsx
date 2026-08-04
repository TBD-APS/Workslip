import { useRef, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  getGetApiJobsIdQueryKey,
  getGetApiJobsQueryKey,
  usePostApiJobsIdStatus,
} from '../../../api/generated/jobs/jobs';
import { JobStatus } from '../../../api/generated/models/jobStatus';
import { notify } from '../../../lib/toast';
import { formatJobStatus } from '../statusLabels';
import { ConfirmActionDialog } from './ConfirmActionDialog';
import './JobStatusControl.css';

const STATUS_OPTIONS: readonly JobStatus[] = [
  JobStatus.Draft,
  JobStatus.InReview,
  JobStatus.Approved,
  JobStatus.Rejected,
];

type JobStatusControlProps = {
  jobId: string;
  reportNumber: string;
  status: JobStatus;
  editable?: boolean;
  allowedStatuses?: readonly JobStatus[];
  beforeChange?: (status: JobStatus) => Promise<boolean>;
  onChanged?: (status: JobStatus) => void;
};

export function JobStatusControl(props: JobStatusControlProps) {
  if (!props.editable) {
    return <JobStatusDots status={props.status} />;
  }

  return <EditableJobStatusControl {...props} />;
}

function EditableJobStatusControl({
  jobId,
  reportNumber,
  status,
  allowedStatuses = [],
  beforeChange,
  onChanged,
}: JobStatusControlProps) {
  const queryClient = useQueryClient();
  const inFlightRef = useRef(false);
  const [pendingStatus, setPendingStatus] = useState<JobStatus | null>(null);
  const [isChanging, setIsChanging] = useState(false);
  const mutation = usePostApiJobsIdStatus({
    request: { skipGlobalErrorToast: true },
  });

  const changeStatus = async (targetStatus: JobStatus) => {
    if (
      targetStatus === status
      || inFlightRef.current
      || mutation.isPending
      || !allowedStatuses.includes(targetStatus)
    ) {
      return;
    }

    inFlightRef.current = true;
    setIsChanging(true);
    let changed = false;

    try {
      if (beforeChange && !(await beforeChange(targetStatus))) {
        return;
      }

      const updatedJob = await mutation.mutateAsync({
        id: jobId,
        data: { status: targetStatus },
      });

      queryClient.setQueryData(getGetApiJobsIdQueryKey(jobId), updatedJob);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: getGetApiJobsIdQueryKey(jobId) }),
        queryClient.invalidateQueries({ queryKey: getGetApiJobsQueryKey() }),
      ]);

      notify.success(`Status ændret til ${formatJobStatus(targetStatus).toLowerCase()}`);
      changed = true;
    } catch {
      notify.error(`Kunne ikke ændre status til ${formatJobStatus(targetStatus).toLowerCase()}`);
    } finally {
      inFlightRef.current = false;
      setIsChanging(false);
      setPendingStatus(null);
    }

    if (changed) {
      onChanged?.(targetStatus);
    }
  };

  const isPending = isChanging || mutation.isPending;

  return (
    <>
      <JobStatusDots
        status={status}
        allowedStatuses={allowedStatuses}
        disabled={isPending}
        onChange={setPendingStatus}
      />
      {pendingStatus && (
        <ConfirmActionDialog
          action="submit"
          reportNumber={reportNumber}
          isPending={isPending}
          onConfirm={() => void changeStatus(pendingStatus)}
          onClose={() => setPendingStatus(null)}
        />
      )}
    </>
  );
}

type JobStatusDotsProps = {
  status: JobStatus;
  allowedStatuses?: readonly JobStatus[];
  disabled?: boolean;
  onChange?: (status: JobStatus) => void;
};

function JobStatusDots({
  status,
  allowedStatuses = [],
  disabled = false,
  onChange,
}: JobStatusDotsProps) {
  return (
    <div
      className="job-status-dots"
      role="group"
      aria-label={`Sagsstatus: ${formatJobStatus(status)}`}
      aria-busy={disabled}
    >
      {STATUS_OPTIONS.map((option) => {
        const label = formatJobStatus(option);
        const isCurrent = option === status;
        const canChange = Boolean(onChange) && allowedStatuses.includes(option) && !isCurrent;
        const className = `job-status-dot job-status-dot--${option}${isCurrent ? ' is-current' : ''}`;
        const title = isCurrent
          ? `${label} (nuværende status)`
          : canChange
            ? `Skift status: ${label}`
            : label;

        if (!canChange) {
          return (
            <span
              key={option}
              className={className}
              role="img"
              aria-label={title}
              aria-current={isCurrent ? 'true' : undefined}
              title={title}
            />
          );
        }

        return (
          <button
            key={option}
            className={className}
            type="button"
            onClick={() => onChange(option)}
            disabled={disabled}
            aria-label={title}
            title={title}
          />
        );
      })}
    </div>
  );
}
