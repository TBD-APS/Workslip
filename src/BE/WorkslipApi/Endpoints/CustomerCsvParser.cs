using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

namespace Workslip.Api.Endpoints;

public static class CustomerCsvParser
{
    private static readonly HashSet<string> AllowedContentTypes =
    [
        "text/csv",
        "text/plain",
        "application/vnd.ms-excel",
        "application/csv"
    ];

    public static bool IsAllowedContentType(string? contentType) =>
        contentType is not null && AllowedContentTypes.Contains(contentType.ToLowerInvariant());

    public static bool HasAllowedExtension(string? fileName) =>
        fileName is not null && fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase);

    public static CustomerImportParseResult Parse(Stream stream, ILogger logger)
    {
        using var input = new StreamReader(stream, leaveOpen: true);
        var content = input.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new CustomerImportFormatException("CSV-filen er tom.");
        }

        using var reader = new StringReader(content);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = DetectDelimiter(content),
            HasHeaderRecord = true,
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = args => logger.LogWarning(
                "Bad CSV data at row {Row}: {RawRecord}",
                args.Context.Parser?.Row,
                args.RawRecord)
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
