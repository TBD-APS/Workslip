using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api.Endpoints;
using Workslip.Api.Services;
using Workslip.Application.Customers;
using Xunit;

namespace Workslip.Tests.Endpoints;

public sealed class CustomerImportParserTests
{
    [Fact]
    public void Excel_parser_maps_danish_customer_export_and_ignores_blank_formatted_rows()
    {
        using var stream = CreateExcelStream();
        var parser = new CustomerExcelParser();

        var result = parser.Parse(stream);

        var customer = Assert.Single(result.Customers);
        Assert.Equal("830", customer.CustomerNumber);
        Assert.Equal("Lars Worm", customer.Name);
        Assert.Equal("Nørremarksvej 6", customer.Address);
        Assert.Equal("7323", customer.ZipCode);
        Assert.Equal("Give", customer.City);
        Assert.Equal("Danmark", customer.Country);
        Assert.Equal("20900500", customer.Phone);
        Assert.Equal("Lars", customer.ContactPerson);
        Assert.Equal("lars@example.com", customer.Email);
        Assert.Equal(0, result.Skipped);
    }

    [Fact]
    public void Csv_parser_supports_semicolon_delimiter_and_danish_headers()
    {
        const string csv = "Nr.;Navn;Adresse 1;Postnr.;By;Land;Telfon/fax;Attention;E-mail\n28405769;Torben Kæseler;Damholtvej 19;7441;Bording;Danmark;;;torben@example.com\n";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var parser = new CustomerCsvParser(NullLogger<CustomerCsvParser>.Instance);

        var result = parser.Parse(stream);

        var customer = Assert.Single(result.Customers);
        Assert.Equal("28405769", customer.CustomerNumber);
        Assert.Equal("7441", customer.ZipCode);
        Assert.Equal("Bording", customer.City);
        Assert.Equal("torben@example.com", customer.Email);
    }

    [Fact]
    public void Csv_parser_stops_when_source_row_limit_is_exceeded()
    {
        var rows = Enumerable.Range(1, CustomerImportLimits.MaxRows + 1)
            .Select(index => $"{index};Customer {index}");
        var csv = "Nr.;Navn\n" + string.Join("\n", rows);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csv));
        var parser = new CustomerCsvParser(NullLogger<CustomerCsvParser>.Instance);

        var exception = Assert.Throws<CustomerImportFormatException>(() => parser.Parse(stream));

        Assert.Equal($"For mange rækker. Maksimum er {CustomerImportLimits.MaxRows}.", exception.Message);
    }

    [Fact]
    public void Excel_parser_ignores_formatting_only_rows_far_below_customer_data()
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
        var parser = new CustomerExcelParser();

        var result = parser.Parse(stream);

        var customer = Assert.Single(result.Customers);
        Assert.Equal("Customer", customer.Name);
    }

    [Fact]
    public void File_parser_prefers_extension_when_content_type_is_generic_or_wrong()
    {
        using var stream = CreateExcelStream();
        var file = new FormFile(stream, 0, stream.Length, "file", "customers.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        var parser = CreateFileParser();

        var result = parser.Parse(file);

        var customer = Assert.Single(result.Customers);
        Assert.Equal("Lars Worm", customer.Name);
    }

    [Fact]
    public void File_parser_rejects_unsupported_format()
    {
        using var stream = new MemoryStream("not a customer import"u8.ToArray());
        var file = new FormFile(stream, 0, stream.Length, "file", "customers.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var parser = CreateFileParser();

        var exception = Assert.Throws<CustomerImportFormatException>(() => parser.Parse(file));

        Assert.Equal("Kun .csv- og .xlsx-filer accepteres.", exception.Message);
    }

    [Fact]
    public void File_parser_rejects_files_above_upload_limit_before_parsing()
    {
        using var stream = new MemoryStream([1]);
        var file = new FormFile(stream, 0, CustomerImportFileParser.MaxUploadSize + 1, "file", "customers.csv")
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/csv"
        };
        var parser = CreateFileParser();

        var exception = Assert.Throws<CustomerImportFormatException>(() => parser.Parse(file));

        Assert.Contains("Filen er for stor", exception.Message);
    }

    private static CustomerImportFileParser CreateFileParser() => new(
        [
            new CustomerCsvParser(NullLogger<CustomerCsvParser>.Instance),
            new CustomerExcelParser()
        ],
        NullLogger<CustomerImportFileParser>.Instance);

    private static MemoryStream CreateExcelStream()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("in");
        var headers = new[] { "Gruppe", "Nr.", "Navn", "Adresse 1", "Postnr. ", "By", "Land", "Telfon/fax", "Attention", "Deres ref. ", "E-mail" };
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(1, column + 1).Value = headers[column];
        }

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
        sheet.Cell(3, 1).Value = "Diverse";
        sheet.Cell(100_000, 1).Style.Font.Bold = true;

        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}
