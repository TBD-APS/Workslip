using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api.Endpoints;
using Workslip.Api.Services;
using Xunit;

namespace Workslip.Tests.Endpoints;

public sealed class CustomerImportParserTests
{
    [Fact]
    public void Parser_maps_danish_customer_export_from_excel()
    {
        using var stream = CreateExcelStream();
        var parser = CreateParser();

        var result = parser.Parse(CreateFile(stream, "customers.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var customer = Assert.Single(result.Customers);
        Assert.Equal("830", customer.CustomerNumber);
        Assert.Equal("Lars Worm", customer.Name);
        Assert.Equal("7323", customer.ZipCode);
        Assert.Equal("lars@example.com", customer.Email);
    }

    [Fact]
    public void Parser_supports_semicolon_csv_and_danish_headers()
    {
        const string csv = "Nr.;Navn;Adresse 1;Postnr.;By;Land;Telfon/fax;Attention;E-mail\n28405769;Torben Kæseler;Damholtvej 19;7441;Bording;Danmark;;;torben@example.com\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var parser = CreateParser();

        var result = parser.Parse(CreateFile(stream, "customers.csv", "text/csv"));

        var customer = Assert.Single(result.Customers);
        Assert.Equal("28405769", customer.CustomerNumber);
        Assert.Equal("Bording", customer.City);
        Assert.Equal("torben@example.com", customer.Email);
    }

    [Fact]
    public void Parser_prefers_file_extension_over_mime_type()
    {
        using var stream = CreateExcelStream();
        var parser = CreateParser();

        var result = parser.Parse(CreateFile(stream, "customers.xlsx", "text/csv"));

        Assert.Equal("Lars Worm", Assert.Single(result.Customers).Name);
    }

    [Fact]
    public void Parser_rejects_unsupported_format()
    {
        using var stream = new MemoryStream("not an import"u8.ToArray());
        var parser = CreateParser();

        var exception = Assert.Throws<CustomerImportFormatException>(() =>
            parser.Parse(CreateFile(stream, "customers.pdf", "application/pdf")));

        Assert.Equal("Kun .csv- og .xlsx-filer accepteres.", exception.Message);
    }

    [Fact]
    public void Parser_stops_csv_at_source_row_limit()
    {
        var rows = Enumerable.Range(1, CustomerImportFileParser.MaxRows + 1)
            .Select(index => $"{index};Customer {index}");
        var csv = "Nr.;Navn\n" + string.Join("\n", rows);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var parser = CreateParser();

        var exception = Assert.Throws<CustomerImportFormatException>(() =>
            parser.Parse(CreateFile(stream, "customers.csv", "text/csv")));

        Assert.Equal($"For mange rækker. Maksimum er {CustomerImportFileParser.MaxRows}.", exception.Message);
    }

    [Fact]
    public void Parser_ignores_excel_formatting_only_rows_far_below_data()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("in");
        sheet.Cell(1, 1).Value = "Nr.";
        sheet.Cell(1, 2).Value = "Navn";
        sheet.Cell(2, 1).Value = "1";
        sheet.Cell(2, 2).Value = "Customer";
        sheet.Cell(100_000, 1).Style.Font.Bold = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        var parser = CreateParser();

        var result = parser.Parse(CreateFile(stream, "customers.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        Assert.Equal("Customer", Assert.Single(result.Customers).Name);
    }

    private static CustomerImportFileParser CreateParser() => new(NullLogger<CustomerImportFileParser>.Instance);

    private static FormFile CreateFile(Stream stream, string fileName, string contentType) => new(stream, 0, stream.Length, "file", fileName)
    {
        Headers = new HeaderDictionary(),
        ContentType = contentType
    };

    private static MemoryStream CreateExcelStream()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("in");
        var headers = new[] { "Gruppe", "Nr.", "Navn", "Adresse 1", "Postnr. ", "By", "Land", "Telfon/fax", "Attention", "Deres ref. ", "E-mail" };
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];

        sheet.Cell(2, 1).Value = "Diverse";
        sheet.Cell(2, 2).Value = 830;
        sheet.Cell(2, 3).Value = "Lars Worm";
        sheet.Cell(2, 4).Value = "Nørremarksvej 6";
        sheet.Cell(2, 5).Value = 7323;
        sheet.Cell(2, 6).Value = "Give";
        sheet.Cell(2, 7).Value = "Danmark";
        sheet.Cell(2, 8).Value = 20900500;
        sheet.Cell(2, 9).Value = "Lars";
        sheet.Cell(2, 10).Value = "IGNORED";
        sheet.Cell(2, 11).Value = "lars@example.com";

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
