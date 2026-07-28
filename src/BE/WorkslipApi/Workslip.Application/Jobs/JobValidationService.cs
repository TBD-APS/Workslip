using Ardalis.Result;
using Microsoft.Extensions.Logging;
using Workslip.Domain;

namespace Workslip.Application.Jobs
{
    public class JobValidationService
    {
        private readonly ILogger<JobValidationService> _logger;

        public JobValidationService(ILogger<JobValidationService> logger)
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

            // Skip submit validation for Diverse jobs
            if (report.JobType == JobType.Diverse)
            {
                return errors;
            }

            AddRequired(errors, nameof(JobReportResponse.ReportNumber), report.ReportNumber, "Rapportnummer er påkrævet.");
            AddRequired(errors, $"{nameof(JobReportResponse.Customer)}.{nameof(CustomerInfo.Name)}", report.Customer?.Name, "Kundenavn er påkrævet.");

            if (report.InstallationTypes.Count == 0)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.InstallationTypes), ErrorMessage = "Vælg mindst én installationstype." });
            }

            var workKindsByLabel = referenceData.WorkKinds
                .ToDictionary(w => w.NormalizedLabel, StringComparer.OrdinalIgnoreCase);

            if (report.WorkKind is null)
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Arbejdstype er påkrævet." });
            }
            else if (!workKindsByLabel.TryGetValue(report.WorkKind.NormalizedLabel, out var workKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = $"Ukendt arbejdstype '{report.WorkKind.NormalizedLabel}'." });
            }
            else if (workKind.RequiresCustomWorkKind && string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Denne arbejdstype kræver en brugerdefineret tekst." });
            }
            else if (!workKind.RequiresCustomWorkKind && !string.IsNullOrWhiteSpace(report.WorkKind.CustomWorkKind))
            {
                errors.Add(new ValidationError { Identifier = nameof(JobReportResponse.WorkKind), ErrorMessage = "Brugerdefineret tekst er kun tilladt for arbejdstyper, der kræver det." });
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
