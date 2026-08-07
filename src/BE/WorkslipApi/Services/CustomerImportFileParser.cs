using Workslip.Api.Endpoints;

namespace Workslip.Api.Services;

public sealed class CustomerImportFileParser(
    IEnumerable<ICustomerImportFormatParser> parsers,
    ILogger<CustomerImportFileParser> logger)
{
    public const long MaxUploadSize = 10 * 1024 * 1024;

    private readonly IReadOnlyList<ICustomerImportFormatParser> _parsers = parsers.ToArray();

    public CustomerImportParseResult Parse(IFormFile file)
    {
        if (file is null or { Length: 0 })
        {
            throw new CustomerImportFormatException("Der blev ikke uploadet en fil.");
        }

        if (file.Length > MaxUploadSize)
        {
            throw new CustomerImportFormatException(
                $"Filen er for stor. Maksimal størrelse er {MaxUploadSize / 1024 / 1024} MB.");
        }

        var parser = ResolveParser(file.FileName, file.ContentType)
            ?? throw new CustomerImportFormatException("Kun .csv- og .xlsx-filer accepteres.");

        try
        {
            using var stream = file.OpenReadStream();
            return parser.Parse(stream);
        }
        catch (CustomerImportFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "Failed to parse customer import using parser {ParserType}.",
                parser.GetType().Name);
            throw new CustomerImportFormatException("Filen kunne ikke læses som en gyldig kundeimport.");
        }
    }

    private ICustomerImportFormatParser? ResolveParser(string? fileName, string? contentType)
    {
        // Prefer the file extension over the MIME type. Browsers and spreadsheet
        // applications frequently send generic MIME types, while the extension is
        // what the user explicitly selected and preserves the previous behavior.
        return _parsers.FirstOrDefault(parser => parser.SupportsFileName(fileName))
            ?? _parsers.FirstOrDefault(parser => parser.SupportsContentType(contentType));
    }
}
