using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public class MockAccountingProvider : IAccountingProvider
{
    public string ProviderId => "mock";
    public string DisplayName => "Mock Accounting (Dev)";

    public Task<bool> TestConnectionAsync(string tenantId) => Task.FromResult(true);

    public Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(
        string tenantId,
        string userId,
        string startDate,
        string endDate)
    {
        // Create a deterministic set of documents based on userId to simulate real data.
        var random = new Random(userId.GetHashCode());
        var docs = new List<AccountingDocument>();

        var docCount = random.Next(0, 6);
        for (var i = 0; i < docCount; i++)
        {
            var isInvoice = random.Next(0, 2) == 0;
            var type = isInvoice ? "Invoice" : "Receipt";
            var prefix = isInvoice ? "FAK" : "BIL";
            var amount = (decimal)(random.NextDouble() * 5000 + 100);

            docs.Add(new AccountingDocument(
                $"doc-{userId}-{i}",
                $"{prefix}-{random.Next(1000, 9999)}",
                type,
                Math.Round(amount, 2),
                $"{startDate.Substring(0, 7)}-{random.Next(1, 28):D2}",
                isInvoice ? (random.Next(0, 3) == 0 ? "Overdue" : "Paid") : "Pending",
                $"https://economics.mock/doc/{random.Next(1000, 9999)}"));
        }

        return Task.FromResult<IEnumerable<AccountingDocument>>(
            docs.OrderByDescending(document => document.Date).ToArray());
    }

    public Task<Stream> GetDocumentStreamAsync(string tenantId, string documentId)
    {
        Stream stream = new MemoryStream(
            Encoding.UTF8.GetBytes($"Mock accounting document {documentId}"),
            writable: false);

        return Task.FromResult(stream);
    }

    public Task<bool> SyncHoursAsync(string tenantId, object hoursData) => Task.FromResult(true);
}
