using System;
using System.Collections.Generic;
using System.Text;

namespace Workslip.Domain.Models
{
    public sealed class InstallationControlPointRow
    {
        public Guid InstallationTypeId { get; set; }
        public InstallationTypeRow InstallationType { get; set; } = null!;

        public Guid ControlCategoryId { get; set; }
        public ControlCategoryRow ControlCategory { get; set; } = null!;

        public Guid ControlPointId { get; set; }
        public ControlPointRow ControlPoint { get; set; } = null!;

        public int SortOrder { get; set; }
        public bool IsRequired { get; set; }
    }
}
