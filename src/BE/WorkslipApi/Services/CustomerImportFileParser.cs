using System.Globalization;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;
using Workslip.Api.Endpoints;
using Workslip.Application.Customers;

namespace Workslip.Api.Services;

public sealed class CustomerImportFileParser(ILogger<CustomerImportFileParser> logger)
{
    public const long MaxUploadSize = 10 * 1024 * 1024;
    public const int MaxRows = 10_000;

    private static readonly HashSet<string> CsvContentTypes =
    [
        "text/csv",
        "text/plain",
        "application/vnd.ms-excel",
        "application/csv"
    ];

    private const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public CustomerImportParseResult Parse(IFormFile file)
    {
        if (file is null or { Length: 0 })
            throw new CustomerImportFormatException("Der blev ikke uploadet en fil.");

        if (file.Length > MaxUploadSize)
            throw new CustomerImportFormatException($"Filen er for stor. Maksimal størrelse er {MaxUploadSize / 1024 / 1024} MB.");

        var format = ResolveFormat(file.FileName, file.ContentType)
            ?? throw new CustomerImportFormatException("Kun .csv- og .xlsx-filer accepteres.");

        try
        {
            using var stream = file.OpenReadStream();
            return format == CustomerImportFormat.Excel ? ParseExcel(stream) : ParseCsv(stream);
        }
        catch (CustomerImportFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Failed to parse customer import. Format={Format} ExceptionType={ExceptionType}", format, ex.GetType().Name);
            throw new CustomerImportFormatException("Filen kunne ikke læses som en gyldig kundeimport.");
        }
    }

    private CustomerImportParseResult ParseCsv(Stream stream)
    {
        using var input = new StreamReader(stream, leaveOpen: true);
        var content = input.ReadToEnd();
        if (string.IsNullOrWhiteSpace(content))
            throw new CustomerImportFormatException("CSV-filen er tom.");

        try
        {
            using var reader = new StringReader(content);
            using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = DetectDelimiter(content),
                HasHeaderRecord = true,
                HeaderValidated = null,
                MissingFieldFound = null,
                BadDataFound = args => logger.LogWarning("Bad CSV data at row {Row}.", args.Context.Parser?.Row)
            });

            if (!csv.Read())
                throw new CustomerImportFormatException("CSV-filen mangler en overskriftsrække.");

            csv.ReadHeader();
            var headers = CustomerImportHeaderMap.Create(csv.HeaderRecord ?? []);
            var customers = new List<ImportCustomerRow>();
            var sourceRows = 0;

            while (csv.Read())
            {
                EnsureRowLimit(++sourceRows);
                var row = CustomerImportRowFactory.Create(csv.Context.Parser?.Row ?? 0, headers, index => csv.GetField(index));
                if (row is not null)
                    customers.Add(row);
            }

            return new CustomerImportParseResult(customers, 0);
        }
        catch (CustomerImportFormatException)
        {
            throw;
        }
        catch (CsvHelperException ex)
        {
            logger.LogWarning("Failed to parse customer CSV. ExceptionType={ExceptionType}", ex.GetType().Name);
            throw new CustomerImportFormatException("CSV-filen kunne ikke læses.");
        }
    }

    private static CustomerImportParseResult ParseExcel(Stream stream)
    {
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.FirstOrDefault()
            ?? throw new CustomerImportFormatException("Excel-filen indeholder ingen regneark.");
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new CustomerImportFormatException("Excel-filen er tom.");
        var lastHeaderCell = headerRow.LastCellUsed()
            ?? throw new CustomerImportFormatException("Excel-filen mangler en overskriftsrække.");

        var headerValues = Enumerable.Range(1, lastHeaderCell.Address.ColumnNumber)
            .Select(column => headerRow.Cell(column).GetFormattedString())
            .ToArray();
        var headers = CustomerImportHeaderMap.Create(headerValues);
        var customers = new List<ImportCustomerRow>();
        var sourceRows = 0;

        foreach (var worksheetRow in worksheet.RowsUsed().Where(row => row.RowNumber() > headerRow.RowNumber()))
        {
            EnsureRowLimit(++sourceRows);
            var row = CustomerImportRowFactory.Create(
                worksheetRow.RowNumber(),
                headers,
                index => worksheetRow.Cell(index + 1).GetFormattedString());
            if (row is not null)
                customers.Add(row);
        }

        return new CustomerImportParseResult(customers, 0);
    }

    private static CustomerImportFormat? ResolveFormat(string? fileName, string? contentType)
    {
        if (fileName?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true)
            return CustomerImportFormat.Csv;
        if (fileName?.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) == true)
            return CustomerImportFormat.Excel;
        if (contentType is not null && CsvContentTypes.Contains(contentType.ToLowerInvariant()))
            return CustomerImportFormat.Csv;
        return string.Equals(contentType, ExcelContentType, StringComparison.OrdinalIgnoreCase)
            ? CustomerImportFormat.Excel
            : null;
    }

    private static void EnsureRowLimit(int sourceRows)
    {
        if (sourceRows > MaxRows)
            throw new CustomerImportFormatException($"For mange rækker. Maksimum er {MaxRows}.");
    }

    private static string DetectDelimiter(string content)
    {
        var header = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        var semicolons = header.Count(c => c == ';');
        var tabs = header.Count(c => c == '\t');
        var commas = header.Count(c => c == ',');
        if (tabs > semicolons && tabs > commas)
            return "\t";
        return semicolons > commas ? ";" : ",";
    }

    private enum CustomerImportFormat
    {
        Csv,
        Excel
    }
}
