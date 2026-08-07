using ClosedXML.Excel;
using Workslip.Application.Customers;

namespace Workslip.Api.Endpoints;

public sealed class CustomerExcelParser : ICustomerImportFormatParser
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public bool SupportsFileName(string? fileName) =>
        fileName is not null && fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    public bool SupportsContentType(string? contentType) =>
        string.Equals(contentType, ContentType, StringComparison.OrdinalIgnoreCase);

    public CustomerImportParseResult Parse(Stream stream)
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
            sourceRows++;
            EnsureWithinRowLimit(sourceRows);

            var rowNumber = worksheetRow.RowNumber();
            var row = CustomerImportRowFactory.Create(
                rowNumber,
                headers,
                index => worksheetRow.Cell(index + 1).GetFormattedString());

            if (row is not null)
            {
                customers.Add(row);
            }
        }

        return new CustomerImportParseResult(customers, 0);
    }

    private static void EnsureWithinRowLimit(int sourceRows)
    {
        if (sourceRows > CustomerImportLimits.MaxRows)
        {
            throw new CustomerImportFormatException(
                $"For mange rækker. Maksimum er {CustomerImportLimits.MaxRows}.");
        }
    }
}
