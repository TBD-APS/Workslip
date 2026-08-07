using ClosedXML.Excel;

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
        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        var customers = new List<Application.Customers.ImportCustomerRow>();

        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = CustomerImportRowFactory.Create(
                rowNumber,
                headers,
                index => worksheet.Cell(rowNumber, index + 1).GetFormattedString());

            if (row is not null)
            {
                customers.Add(row);
            }
        }

        // Empty formatted rows and rows containing values only in deliberately ignored
        // source columns are not customer records and are intentionally not reported.
        return new CustomerImportParseResult(customers, 0);
    }
}
