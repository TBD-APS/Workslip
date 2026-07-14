using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.Extensions.Logging;
using Workslip.Application.Jobs;

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

    public static (IReadOnlyList<CustomerInfo> Customers, int Skipped, List<string> Errors) Parse(Stream stream, ILogger logger)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            HeaderValidated = null,
            MissingFieldFound = null,
            BadDataFound = args =>
            {
                logger.LogWarning("Bad CSV data at row {RawRecord}: {Context}", args.Context.Parser?.Row, args.RawRecord);
            }
        });

        csv.Context.RegisterClassMap<CustomerCsvRowMap>();

        var allRows = csv.GetRecords<CustomerCsvRow>().ToList();
        var errors = new List<string>();
        var customers = new List<CustomerInfo>();
        var skipped = 0;

        foreach (var row in allRows)
        {
            if (string.IsNullOrWhiteSpace(row.Name))
            {
                skipped++;
                continue;
            }

            customers.Add(new CustomerInfo(
                null,
                row.Name.Trim(),
                row.Address?.Trim(),
                row.Email?.Trim(),
                row.ContactPerson?.Trim(),
                row.Phone?.Trim()));
        }

        return (customers, skipped, errors);
    }
}

public sealed class CustomerCsvRow
{
    public string? Name { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? ContactPerson { get; set; }
    public string? Phone { get; set; }
}

public sealed class CustomerCsvRowMap : ClassMap<CustomerCsvRow>
{
    public CustomerCsvRowMap()
    {
        Map(m => m.Name).Name("Name", "Navn");
        Map(m => m.Address).Name("Address", "Adresse");
        Map(m => m.Email).Name("Email", "E-mail", "E-mailadresse");
        Map(m => m.ContactPerson).Name("ContactPerson", "Kontaktperson");
        Map(m => m.Phone).Name("Phone", "Telefon", "Telefonnummer");
    }
}
