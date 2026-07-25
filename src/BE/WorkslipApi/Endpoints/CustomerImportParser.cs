using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public sealed record CustomerImportParseResult(IReadOnlyList<ImportCustomerRow> Customers, int Skipped);

public sealed class CustomerImportFormatException(string message) : Exception(message);

internal sealed record CustomerImportHeaderMap(
    int CustomerNumber,
    int Name,
    int Address,
    int ZipCode,
    int City,
    int Country,
    int Phone,
    int ContactPerson,
    int Email)
{
    public static CustomerImportHeaderMap Create(IReadOnlyList<string?> headers)
    {
        var normalized = headers
            .Select((header, index) => new { Header = Normalize(header), Index = index })
            .Where(x => x.Header.Length > 0)
            .GroupBy(x => x.Header)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);

        var name = Find(normalized, "name", "navn");
        if (name < 0)
        {
            throw new CustomerImportFormatException("Filen mangler kolonnen 'Navn'.");
        }

        return new CustomerImportHeaderMap(
            Find(normalized, "customernumber", "kundenummer", "nr"),
            name,
            Find(normalized, "address", "adresse", "adresse1"),
            Find(normalized, "zipcode", "postalcode", "postnummer", "postnr"),
            Find(normalized, "city", "by"),
            Find(normalized, "country", "land"),
            Find(normalized, "phone", "telefon", "telefonnummer", "telefonfax", "telfonfax"),
            Find(normalized, "contactperson", "kontaktperson", "attention"),
            Find(normalized, "email", "emailadresse"));
    }

    private static int Find(IReadOnlyDictionary<string, int> headers, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            if (headers.TryGetValue(Normalize(alias), out var index))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Normalize(string? value) => new(
        (value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}

internal static class CustomerImportRowFactory
{
    public static ImportCustomerRow? Create(
        int rowNumber,
        CustomerImportHeaderMap headers,
        Func<int, string?> getValue)
    {
        var row = new ImportCustomerRow(
            rowNumber,
            Read(headers.CustomerNumber, getValue),
            Read(headers.Name, getValue),
            Read(headers.Address, getValue),
            Read(headers.ZipCode, getValue),
            Read(headers.City, getValue),
            Read(headers.Country, getValue),
            Read(headers.Email, getValue),
            Read(headers.ContactPerson, getValue),
            Read(headers.Phone, getValue));

        return HasImportedValue(row) ? row : null;
    }

    private static string? Read(int index, Func<int, string?> getValue)
    {
        if (index < 0)
        {
            return null;
        }

        var value = getValue(index);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool HasImportedValue(ImportCustomerRow row) =>
        row.CustomerNumber is not null ||
        row.Name is not null ||
        row.Address is not null ||
        row.ZipCode is not null ||
        row.City is not null ||
        row.Country is not null ||
        row.Email is not null ||
        row.ContactPerson is not null ||
        row.Phone is not null;
}
