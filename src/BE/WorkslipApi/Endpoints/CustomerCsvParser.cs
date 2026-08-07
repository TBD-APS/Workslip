using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

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
            var customers = new List<Application.Customers.ImportCustomerRow>();

            while (csv.Read())
            {
                var row = CustomerImportRowFactory.Create(
                    csv.Context.Parser?.Row ?? 0,
                    headers,
                    index => csv.GetField(index));

                if (row is not null)
                {
                    customers.Add(row);
                }
            }

            // Empty rows and rows containing values only in deliberately ignored columns
            // are structural noise, not customer records, and are therefore not reported.
            return new CustomerImportParseResult(customers, 0);
        }
        catch (CustomerImportFormatException)
        {
            throw;
        }
        catch (CsvHelperException ex)
        {
            logger.LogWarning(ex, "Failed to parse customer CSV.");
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
