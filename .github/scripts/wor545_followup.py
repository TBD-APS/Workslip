from pathlib import Path

root = Path(__file__).resolve().parents[2]

# Keep the existing rejection test fake aligned with the expanded Jobs-owned port.
path = root / "src/BE/WorkslipApi/Workslip.Tests/Jobs/JobRejectionNotificationTests.cs"
source = path.read_text(encoding="utf-8")
needle = "        public Task<IReadOnlyList<JobListItemResponse>> GetMyAssignedJobsAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();\n"
replacement = """        public Task<IReadOnlyList<AssignedUserResponse>> GetOrganizationAdminsAsync(
            Guid requestedOrganizationId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(organizationId, requestedOrganizationId);
            return Task.FromResult<IReadOnlyList<AssignedUserResponse>>([]);
        }

""" + needle
if source.count(needle) != 1:
    raise RuntimeError("Expected one rejection-test assignment fake insertion point")
path.write_text(source.replace(needle, replacement, 1), encoding="utf-8")

# Preserve the previous 1000-recipient upper bound used by JobService's admin lookup.
path = root / "src/BE/WorkslipApi/Workslip.Infrastructure/Repositories/EfAssignmentRepository.cs"
source = path.read_text(encoding="utf-8")
needle = """            .Where(user => user.OrganizationId == organizationId && user.Role == Roles.Admin)
            .OrderBy(user => user.DisplayName)
            .Select(user => new AssignedUserResponse(user.Id, user.DisplayName))
"""
replacement = """            .Where(user => user.OrganizationId == organizationId && user.Role == Roles.Admin)
            .OrderBy(user => user.DisplayName)
            .Take(1000)
            .Select(user => new AssignedUserResponse(user.Id, user.DisplayName))
"""
if source.count(needle) != 1:
    raise RuntimeError("Expected one admin projection query")
path.write_text(source.replace(needle, replacement, 1), encoding="utf-8")
