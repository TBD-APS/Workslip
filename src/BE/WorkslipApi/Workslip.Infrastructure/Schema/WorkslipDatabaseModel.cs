using System.Text;
using Workslip.Domain.Models;

namespace Workslip.Infrastructure.Schema;

/// <summary>
/// Code-level source of truth for SQL Server schema generation.
/// Keep table definitions aligned with row models and repository SQL.
/// </summary>
public static class WorkslipDatabaseModel
{
    public const string Schema = "dbo";

    public static IReadOnlyList<TableDefinition> Tables { get; } =
    [
        new(
            "Organizations",
            typeof(OrganizationRow),
            [
                Column.RequiredGuid(nameof(OrganizationRow.Id)),
                Column.RequiredString(nameof(OrganizationRow.Name), 200),
                Column.RequiredString(nameof(OrganizationRow.Cvr), 8),
                Column.RequiredDateTimeOffset(nameof(OrganizationRow.CreatedAt), "sysutcdatetime()"),
                Column.RequiredDateTimeOffset(nameof(OrganizationRow.UpdatedAt), "sysutcdatetime()")
            ],
            [
                "constraint CK_Organizations_Cvr_8Digits check (Cvr like '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]')"
            ],
            [
                "create unique index UX_Organizations_Cvr on dbo.Organizations (Cvr);"
            ]),

        new(
            "Users",
            typeof(UserDataRow),
            [
                Column.RequiredGuid(nameof(UserDataRow.Id)),
                Column.RequiredGuid(nameof(UserDataRow.OrganizationId)),
                Column.RequiredString(nameof(UserDataRow.DisplayName), 200),
                Column.OptionalString(nameof(UserDataRow.Email), 320),
                Column.OptionalString(nameof(UserDataRow.Phone), 80),
                Column.OptionalString(nameof(UserDataRow.EntraEmail), 200),
                Column.OptionalString(nameof(UserDataRow.EntraId), 80),
                Column.RequiredString(nameof(UserDataRow.Role), 80),
                Column.RequiredDateTimeOffset(nameof(UserDataRow.CreatedAt), "sysutcdatetime()"),
                Column.RequiredDateTimeOffset(nameof(UserDataRow.UpdatedAt), "sysutcdatetime()")
            ],
            [
                "constraint FK_Users_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)"
            ]),

        new(
            "Customers",
            typeof(CustomerRow),
            [
                Column.RequiredGuid(nameof(CustomerRow.Id)),
                Column.RequiredGuid(nameof(CustomerRow.OrganizationId)),
                Column.RequiredString(nameof(CustomerRow.Name), 240),
                Column.OptionalString(nameof(CustomerRow.Address), 500),
                Column.OptionalString(nameof(CustomerRow.Email), 320),
                Column.OptionalString(nameof(CustomerRow.ContactPerson), 200),
                Column.OptionalString(nameof(CustomerRow.Phone), 80),
                Column.RequiredDateTimeOffset(nameof(CustomerRow.CreatedAt), "sysutcdatetime()"),
                Column.RequiredDateTimeOffset(nameof(CustomerRow.UpdatedAt), "sysutcdatetime()")
            ],
            [
                "constraint FK_Customers_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)"
            ]),

        new(
            "JobWorkKinds",
            typeof(JobWorkKindRow),
            [
                Column.RequiredString(nameof(JobWorkKindRow.Id), 80),
                Column.RequiredString(nameof(JobWorkKindRow.Label), 160),
                Column.RequiredBit(nameof(JobWorkKindRow.RequiresCustomWorkKind)),
                Column.RequiredBit(nameof(JobWorkKindRow.IsActive), "1"),
                Column.RequiredInt(nameof(JobWorkKindRow.SortOrder), "0"),
                Column.RequiredDateTimeOffset(nameof(JobWorkKindRow.UpdatedAt), "sysutcdatetime()")
            ],
            [],
            [
                "create unique index UX_JobWorkKinds_Label on dbo.JobWorkKinds (Label);"
            ]),

        new(
            "JobClosureFlags",
            typeof(JobClosureFlagRow),
            [
                Column.RequiredString(nameof(JobClosureFlagRow.Id), 80),
                Column.RequiredString(nameof(JobClosureFlagRow.Label), 160),
                Column.RequiredBit(nameof(JobClosureFlagRow.IsExclusive)),
                Column.RequiredBit(nameof(JobClosureFlagRow.IsActive), "1"),
                Column.RequiredInt(nameof(JobClosureFlagRow.SortOrder), "0"),
                Column.RequiredDateTimeOffset(nameof(JobClosureFlagRow.UpdatedAt), "sysutcdatetime()")
            ],
            [],
            [
                "create unique index UX_JobClosureFlags_Label on dbo.JobClosureFlags (Label);"
            ]),

        new(
            "JobReports",
            typeof(JobReportRow),
            [
                Column.RequiredGuid(nameof(JobReportRow.Id)),
                Column.RequiredGuid(nameof(JobReportRow.OrganizationId)),
                Column.OptionalGuid(nameof(JobReportRow.CustomerId)),
                Column.RequiredString(nameof(JobReportRow.ReportNumber), 80),
                Column.RequiredString(nameof(JobReportRow.Status), 40),
                Column.RequiredString(nameof(JobReportRow.CustomerName), 240),
                Column.RequiredString(nameof(JobReportRow.CustomerAddress), 500),
                Column.OptionalString(nameof(JobReportRow.CustomerEmail), 320),
                Column.OptionalString(nameof(JobReportRow.ContactPerson), 200),
                Column.OptionalString(nameof(JobReportRow.Phone), 80),
                Column.OptionalDate(nameof(JobReportRow.ReportDate)),
                Column.RequiredString(nameof(JobReportRow.TaskDescription)),
                Column.OptionalString(nameof(JobReportRow.CustomerObservations)),
                Column.OptionalString(nameof(JobReportRow.TechnicalObservations)),
                Column.RequiredString(nameof(JobReportRow.InstallationTypesJson), defaultSql: "'[]'"),
                Column.RequiredString(nameof(JobReportRow.WorkKind), 80),
                Column.OptionalString(nameof(JobReportRow.CustomWorkKind), 160),
                Column.OptionalString(nameof(JobReportRow.Remarks)),
                Column.RequiredString(nameof(JobReportRow.ClosureFlagsJson), defaultSql: "'[]'"),
                 Column.OptionalString(nameof(JobReportRow.PayloadJson)),
                 Column.OptionalGuid(nameof(JobReportRow.AssignedUserId)),
                 Column.RequiredDateTimeOffset(nameof(JobReportRow.CreatedAt)),
                 Column.RequiredDateTimeOffset(nameof(JobReportRow.UpdatedAt)),
                 Column.OptionalDateTimeOffset(nameof(JobReportRow.SubmittedAt))
            ],
            [
                "constraint FK_JobReports_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)",
                "constraint FK_JobReports_Customers foreign key (CustomerId) references dbo.Customers(Id)",
                "constraint FK_JobReports_JobWorkKinds foreign key (WorkKind) references dbo.JobWorkKinds(Id)",
                 "constraint CK_JobReports_Status check (Status in ('Draft', 'Submitted', 'InReview', 'Approved', 'Rejected', 'Archived'))",
                 "constraint CK_JobReports_InstallationTypesJson_IsJson check (isjson(InstallationTypesJson) = 1)",
                 "constraint CK_JobReports_ClosureFlagsJson_IsJson check (isjson(ClosureFlagsJson) = 1)",
                 "constraint CK_JobReports_PayloadJson_IsJson check (PayloadJson is null or isjson(PayloadJson) = 1)",
                 "constraint FK_JobReports_AssignedUsers foreign key (AssignedUserId) references dbo.Users(Id)"
            ],
            [
                "create index IX_JobReports_Organization_Status_UpdatedAt on dbo.JobReports (OrganizationId, Status, UpdatedAt desc);"
            ]),

        new(
            "JobReportLinks",
            typeof(JobReportLinkRow),
            [
                Column.RequiredGuid(nameof(JobReportLinkRow.Id)),
                Column.RequiredGuid(nameof(JobReportLinkRow.SourceReportId)),
                Column.RequiredGuid(nameof(JobReportLinkRow.TargetReportId)),
                Column.RequiredString(nameof(JobReportLinkRow.LinkType), 80),
                Column.RequiredDateTimeOffset(nameof(JobReportLinkRow.CreatedAt))
            ],
            [
                "constraint FK_JobReportLinks_SourceReport foreign key (SourceReportId) references dbo.JobReports(Id)",
                "constraint FK_JobReportLinks_TargetReport foreign key (TargetReportId) references dbo.JobReports(Id)",
                "constraint CK_JobReportLinks_NoSelfLink check (SourceReportId != TargetReportId)"
            ],
            [
                "create unique index UX_JobReportLinks_Pair on dbo.JobReportLinks (SourceReportId, TargetReportId);",
                "create index IX_JobReportLinks_TargetReport on dbo.JobReportLinks (TargetReportId);"
            ]),

        new(
            "JobControlSubcategoryDecisions",
            typeof(JobControlSubcategoryRow),
            [
                Column.RequiredGuid(nameof(JobControlSubcategoryRow.Id)),
                Column.RequiredGuid(nameof(JobControlSubcategoryRow.ReportId)),
                Column.RequiredString(nameof(JobControlSubcategoryRow.InstallationTypeId), 100),
                Column.RequiredString(nameof(JobControlSubcategoryRow.SubcategoryId), 100),
                Column.RequiredDateTimeOffset(nameof(JobControlSubcategoryRow.CreatedAt)),
                Column.RequiredDateTimeOffset(nameof(JobControlSubcategoryRow.UpdatedAt))
            ],
            [
                "constraint FK_JobControlSubcategoryDecisions_JobReports foreign key (ReportId) references dbo.JobReports(Id) on delete cascade"
            ],
            [
                "create unique index UX_JobControlSubcategoryDecisions_Report_Installation_Subcategory on dbo.JobControlSubcategoryDecisions (ReportId, InstallationTypeId, SubcategoryId);"
            ]),

        new(
            "JobControlChecks",
            typeof(JobControlCheckRow),
            [
                Column.RequiredGuid(nameof(JobControlCheckRow.Id)),
                Column.RequiredGuid(nameof(JobControlCheckRow.ReportId)),
                Column.RequiredGuid(nameof(JobControlCheckRow.SubcategoryDecisionId)),
                Column.RequiredString(nameof(JobControlCheckRow.InstallationTypeId), 100),
                Column.RequiredString(nameof(JobControlCheckRow.SubcategoryId), 100),
                Column.RequiredString(nameof(JobControlCheckRow.ItemId), 160),
                Column.RequiredBit(nameof(JobControlCheckRow.Checked)),
                Column.OptionalString(nameof(JobControlCheckRow.Note)),
                Column.RequiredDateTimeOffset(nameof(JobControlCheckRow.CreatedAt)),
                Column.RequiredDateTimeOffset(nameof(JobControlCheckRow.UpdatedAt))
            ],
            [
                "constraint FK_JobControlChecks_JobReports foreign key (ReportId) references dbo.JobReports(Id)",
                "constraint FK_JobControlChecks_JobControlSubcategoryDecisions foreign key (SubcategoryDecisionId) references dbo.JobControlSubcategoryDecisions(Id) on delete cascade"
            ],
            [
                "create unique index UX_JobControlChecks_Subcategory_Item on dbo.JobControlChecks (SubcategoryDecisionId, ItemId);"
            ]),

        new(
            "JobEvents",
            typeof(JobEventRow),
            [
                Column.RequiredGuid(nameof(JobEventRow.Id)),
                Column.RequiredGuid(nameof(JobEventRow.ReportId)),
                Column.OptionalGuid(nameof(JobEventRow.ActorId)),
                Column.RequiredString(nameof(JobEventRow.EventType), 80),
                Column.OptionalString(nameof(JobEventRow.BeforeJson)),
                Column.OptionalString(nameof(JobEventRow.AfterJson)),
                Column.RequiredDateTimeOffset(nameof(JobEventRow.CreatedAt))
            ],
            [
                "constraint FK_JobEvents_JobReports foreign key (ReportId) references dbo.JobReports(Id) on delete cascade",
                "constraint FK_JobEvents_Users foreign key (ActorId) references dbo.Users(Id)",
                "constraint CK_JobEvents_BeforeJson_IsJson check (BeforeJson is null or isjson(BeforeJson) = 1)",
                "constraint CK_JobEvents_AfterJson_IsJson check (AfterJson is null or isjson(AfterJson) = 1)"
            ],
            [
                "create index IX_JobEvents_Report_CreatedAt on dbo.JobEvents (ReportId, CreatedAt desc);"
            ]),

        new(
            "InviteTokens",
            typeof(InviteTokenRow),
            [
                Column.RequiredGuid(nameof(InviteTokenRow.Id)),
                Column.RequiredGuid(nameof(InviteTokenRow.OrganizationId)),
                Column.RequiredString(nameof(InviteTokenRow.Email), 320),
                Column.RequiredString(nameof(InviteTokenRow.Token), 64),
                Column.OptionalString(nameof(InviteTokenRow.Role), 80),
                Column.RequiredDateTimeOffset(nameof(InviteTokenRow.ExpiresAt)),
                Column.RequiredBit(nameof(InviteTokenRow.Consumed), "0"),
                Column.RequiredDateTimeOffset(nameof(InviteTokenRow.CreatedAt), "sysutcdatetime()")
            ],
            [
                "constraint FK_InviteTokens_Organizations foreign key (OrganizationId) references dbo.Organizations(Id)"
            ],
            [
                "create unique index UX_InviteTokens_Token on dbo.InviteTokens (Token);",
                "create index IX_InviteTokens_Email on dbo.InviteTokens (Email) where Consumed = 0;"
            ])
    ];

    public static string GenerateCreateScript() => GenerateCreateScript(Tables);

    public static string GenerateCreateScript(IEnumerable<string> tableNames)
    {
        var selected = tableNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return GenerateCreateScript(Tables.Where(table => selected.Contains(table.Name)));
    }

    private static string GenerateCreateScript(IEnumerable<TableDefinition> tables)
    {
        var sql = new StringBuilder();

        foreach (var table in tables)
        {
            sql.AppendLine($"create table {table.QualifiedName} (");

            var bodyLines = table.Columns
                .Select((column, index) => "    " + column.ToSql(table.Name) + (index == 0 && column.Name == "Id" ? $" constraint PK_{table.Name} primary key" : string.Empty))
                .Concat(table.Constraints.Select(constraint => "    " + constraint))
                .ToArray();

            sql.AppendLine(string.Join(",\n", bodyLines));
            sql.AppendLine(");");
            sql.AppendLine();

            foreach (var index in table.Indexes)
            {
                sql.AppendLine(index);
            }

            if (table.Indexes.Count > 0)
            {
                sql.AppendLine();
            }
        }

        return sql.ToString();
    }

    public sealed record TableDefinition(
        string Name,
        Type RowType,
        IReadOnlyList<Column> Columns,
        IReadOnlyList<string> Constraints,
        IReadOnlyList<string> Indexes)
    {
        public TableDefinition(string name, Type rowType, IReadOnlyList<Column> columns, IReadOnlyList<string> constraints)
            : this(name, rowType, columns, constraints, [])
        {
        }

        public string QualifiedName => $"{Schema}.{Name}";
    }

    public sealed record Column(string Name, string SqlType, bool IsRequired, string? DefaultSql = null)
    {
        public static Column RequiredGuid(string name) => new(name, "uniqueidentifier", true);
        public static Column OptionalGuid(string name) => new(name, "uniqueidentifier", false);
        public static Column RequiredString(string name, int? length = null, string? defaultSql = null) => new(name, ToStringType(length), true, defaultSql);
        public static Column OptionalString(string name, int? length = null) => new(name, ToStringType(length), false);
        public static Column RequiredBit(string name, string? defaultSql = null) => new(name, "bit", true, defaultSql);
        public static Column RequiredInt(string name, string? defaultSql = null) => new(name, "int", true, defaultSql);
        public static Column OptionalDate(string name) => new(name, "date", false);
        public static Column RequiredDateTimeOffset(string name, string? defaultSql = null) => new(name, "datetimeoffset", true, defaultSql);
        public static Column OptionalDateTimeOffset(string name) => new(name, "datetimeoffset", false);

        public string ToSql(string tableName)
        {
            var nullability = IsRequired ? "not null" : "null";
            var defaultClause = DefaultSql is null ? string.Empty : $" constraint DF_{tableName}_{Name} default {DefaultSql}";
            return $"{Name} {SqlType} {nullability}{defaultClause}";
        }

        private static string ToStringType(int? length) => length is null ? "nvarchar(max)" : $"nvarchar({length.Value})";
    }
}
