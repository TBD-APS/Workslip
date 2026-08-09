namespace Workslip.Application.Jobs;

public sealed record JobAssignmentCandidateResponse(
    Guid Id,
    string DisplayName);
