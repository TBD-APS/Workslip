from pathlib import Path

root = Path(__file__).resolve().parents[2]


def replace_once(source: str, old: str, new: str, label: str) -> str:
    count = source.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return source.replace(old, new, 1)


# Keep the existing rejection test fake aligned with the expanded Jobs-owned port
# and remove the obsolete IUserRepository constructor argument.
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
source = replace_once(source, needle, replacement, "rejection assignment fake")
source = replace_once(
    source,
    """            new EmptyReferenceDataRepository(),
            null!,
            new EmptyWorksheetRepository(),
""",
    """            new EmptyReferenceDataRepository(),
            new EmptyWorksheetRepository(),
""",
    "rejection JobService constructor",
)
path.write_text(source, encoding="utf-8")

# CreateJobServiceTests: IUserRepository was the sixth positional constructor argument.
path = root / "src/BE/WorkslipApi/Workslip.Tests/Jobs/CreateJobServiceTests.cs"
source = path.read_text(encoding="utf-8")
source = replace_once(
    source,
    """            new EmptyReferenceDataRepository(),
            null!,
            worksheets,
""",
    """            new EmptyReferenceDataRepository(),
            worksheets,
""",
    "create JobService constructor",
)
path.write_text(source, encoding="utf-8")

# JobListCacheIsolationTests uses null fakes positionally; remove the sixth argument.
path = root / "src/BE/WorkslipApi/Workslip.Tests/Jobs/JobListCacheIsolationTests.cs"
source = path.read_text(encoding="utf-8")
old = """            repository,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            cache,
"""
new = """            repository,
            null!,
            null!,
            null!,
            null!,
            null!,
            cache,
"""
source = replace_once(source, old, new, "list cache JobService constructor")
path.write_text(source, encoding="utf-8")

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
source = replace_once(source, needle, replacement, "admin projection query")
path.write_text(source, encoding="utf-8")
