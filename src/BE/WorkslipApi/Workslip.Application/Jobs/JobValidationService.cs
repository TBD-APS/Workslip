using Ardalis.Result;
using Microsoft.Extensions.Logging;

namespace Workslip.Application.Jobs
{
    public class JobValidationService
    {
        private readonly ILogger _logger;

        public JobValidationService(ILogger logger)
        {
            _logger = logger;
        }

        public Result ValidateSubmitReady(JobReportResponse job, ReferenceDataResponse refData)
        {
            var validationErrors = ValidateReadyForSubmission(job, refData);
            if (validationErrors.Count == 0)
                return Result.Success();

            _logger.LogWarning("Job submit validation failed. JobId: {JobId}. Fields: {ValidationFields}",
                job.Id, ValidationFields(validationErrors));

            return Result.Invalid(validationErrors);
        }

        private static List<ValidationError> ValidateReadyForSubmission(JobReportResponse report, ReferenceDataResponse referenceData)
        {
            var errors = new List<ValidationError>();
            AddRequired(errors, nameof(JobReportResponse.ReportNumber), report.ReportNumber, "Report number is required.");
            AddRequired(errors, $"{nameof(JobReportResponse.Customer)}.{nameof(CustomerInfo.Name)}", report.Customer?.Name, "Customer name is required.");
            AddRequired(errors, $"{nameof(JobReportResponse.Customer)}.{nameof(CustomerInfo.Address)}", report.Customer?.Address, "Customer address is required.");

            if (report.InstallationTypes.Count == 0)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.InstallationTypes), ErrorMessage = "Select at least one installation type." });
            }

            var workKindsByLabel = referenceData.WorkKinds
                .ToDictionary(w => w.NormalizedLabel, StringComparer.OrdinalIgnoreCase);

            if (report.WorkKind is null)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Work kind is required." });
            }
            else if (!workKindsByLabel.TryGetValue(report.WorkKind.NormalizedLabel, out var workKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Unknown work kind '{report.WorkKind.NormalizedLabel}'." });
            }
            else if (workKind.RequiresCustomWorkKind && string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Custom work kind is required for this work kind." });
            }
            else if (!workKind.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Custom work kind is only allowed for work kinds that require custom text." });
            }

            return errors;
        }

        private static string ValidationFields(IEnumerable<ValidationError> errors) =>
    string.Join(",", errors.Select(error => error.Identifier).Distinct());

        private static void AddRequired(List<ValidationError> errors, string identifier, string? value, string message)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add(new ValidationError { Identifier = identifier, ErrorMessage = message });
            }
        }
    }
}
