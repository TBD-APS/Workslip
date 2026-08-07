using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public sealed class CustomerCsvParser(ILogger<CustomerCsvParser> logger) : ICustomerImportFormatParser
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "text/csv",
        "text/plain",
        "application/vnd.ms-excel",
        "application/csv"
    ];

    public bool SupportsContentType(string? contentType) =>
        contentType is not null && AllowedContentTypes.Contains(contentType.ToLowerInvariant());

    public bool SupportsFileName(string? fileName) =>
        fileName is not null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    public CustomerImportParseResult Parse(Stream stream)
    {
        using var input = new StreamReader(stream, leaveOpen: true);
        var content = input.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new CustomerImportFormatException("CSV-filen er tom.");
        }

        try
        {
            using var reader = new StringReader(content);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = DetectDelimiter(content),
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = args => logger.LogWarning(
                    "Bad CSV data at row {Row}.",
                    args.Context.Parser?.Row)
            });

            if (!csv.Read())
            {
                throw new CustomerImportFormatException("CSV-filen mangler en overskriftsrække.");
            }

            csv.ReadHeader();

            var headers = CustomerImportHeaderMap.Create(csv.HeaderRecord ?? []);
            var customers = new List<ImportCustomerRow>();
            var sourceRows = 0;

            while (csv.Read())
            {
                sourceRows++;
                if (sourceRows > CustomerImportLimits.MaxRows)
                {
                    throw new CustomerImportFormatException(
                        $"For mange rækker. Maksimum er {CustomerImportLimits.MaxRows}.");
                }

                var row = CustomerImportRowFactory.Create(
                    csv.Context.Parser?.Row ?? 0,
                    headers,
                    index => csv.GetField(index));

                if (row is not null)
                {
                    customers.Add(row);
                }
            }

            return new CustomerImportParseResult(customers, 0);
        }
        catch (CustomerImportFormatException)
        {
            throw;
        }
        catch (CsvHelperException ex)
        {
            logger.LogWarning(
                "Failed to parse customer CSV. ExceptionType={ExceptionType}",
                ex.GetType().Name);
            throw new CustomerImportFormatException("CSV-filen kunne ikke læses.");
        }
    }

    private static string DetectDelimiter(string content)
    {
        var header = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var semicolons = header.Count(c => c == ';');
        var tabs = header.Count(c => c == '\t');
        var commas = header.Count(c => c == ',');
        if (tabs > semicolons && tabs > commas)
        {
            return "\t";
        }

        return semicolons > commas ? ";" : ",";
    }
}
