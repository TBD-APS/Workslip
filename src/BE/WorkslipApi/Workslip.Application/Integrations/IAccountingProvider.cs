using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public record AccountingDocument(
    string DocumentId,
    string DocumentNumber,
    string Type, // "Invoice" or "Receipt"
    decimal Amount,
    string Date,
    string Status,
    string ExternalLink
);

public interface IAccountingProvider : IIntegrationProvider
{
    Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string tenantId, string userId, string startDate, string endDate);
    Task<Stream> GetDocumentStreamAsync(string tenantId, string documentId);
    Task<bool> SyncHoursAsync(string tenantId, object hoursData);
}
