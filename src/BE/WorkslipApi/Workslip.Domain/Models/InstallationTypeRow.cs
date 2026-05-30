using System;
using System.Collections.Generic;
using System.Text;

namespace Workslip.Domain.Models
{
    public sealed class InstallationTypeRow
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
        public int SortOrder { get; set; }

        public DateTimeOffset CreatedAt { get; set; }
        public JobReportRow JobReport { get; set; } = new();
        public Guid JobReportId { get; set; }
        public ICollection<InstallationControlPointRow> ControlPoints { get; set; } = [];
    }
}
