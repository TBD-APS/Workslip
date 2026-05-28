using System;
using System.Collections.Generic;
using System.Text;

namespace Workslip.Domain.Models
{
    public sealed class JobLinkReportProjection
    {
        public Guid Id { get; init; }
        public string? ReportNumber { get; init; }
        public string? Status { get; init; }
        public string? CustomerName { get; init; }
    }
}
