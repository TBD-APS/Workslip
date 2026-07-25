using ClosedXML.Excel;
using Microsoft.Extensions.Logging.Abstractions;
using Workslip.Api.Endpoints;

namespace Workslip.Tests.Endpoints;

public sealed class CustomerImportParserTests
{
    [Fact]
    public void Excel_parser_maps_danish_customer_export_and_ignores_blank_formatted_rows()
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

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var result = CustomerExcelParser.Parse(stream);

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

        var result = CustomerCsvParser.Parse(stream, NullLogger.Instance);

        var customer = Assert.Single(result.Customers);
        Assert.Equal("28405769", customer.CustomerNumber);
        Assert.Equal("7441", customer.ZipCode);
        Assert.Equal("Bording", customer.City);
        Assert.Equal("torben@example.com", customer.Email);
    }
}
