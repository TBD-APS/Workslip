using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public class MockAccountingProvider : IAccountingProvider
{
    public string ProviderId => "mock";
    public string DisplayName => "Mock Accounting (Dev)";

    public async Task<bool> TestConnectionAsync(string tenantId) => await Task.FromResult(true);

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Workslip.Application.Integrations;

public class MockAccountingProvider : IAccountingProvider
{
    public string ProviderId => "mock";
    public string DisplayName => "Mock Accounting (Dev)";

    public async Task<bool> TestConnectionAsync(string tenantId) => await Task.FromResult(true);

    public async Task<IEnumerable<AccountingDocument>> GetDocumentsForUserAsync(string tenantId, string userId, string startDate, string endDate)
    {
        // Create a deterministic set of documents based on userId to simulate real data
        var random = new Random(userId.GetHashCode());
        var docs = new List<AccountingDocument>();
        
        int docCount = random.Next(0, 6);
        for (int i = 0; i < docCount; i++)
        {
            bool isInvoice = random.Next(0, 2) == 0;
            string type = isInvoice ? "Invoice" : "Receipt";
            string prefix = isInvoice ? "FAK" : "BIL";
            decimal amount = (decimal)(random.NextDouble() * 5000 + 100);
            
            docs.Add(new AccountingDocument(
                $"doc-{userId}-{i}",
                $"{prefix}-{random.Next(1000, 9999)}",
                type,
                Math.Round(amount, 2),
                $"{startDate.Substring(0, 7)}-{random.Next(1, 28):D2}",
                isInvoice ? (random.Next(0, 3) == 0 ? "Overdue" : "Paid") : "Pending",
                $"https://economics.mock/doc/{random.Next(1000, 9999)}"
            ));
        }

        return await Task.FromResult(docs.OrderByDescending(d => d.Date));
    }

    public async Task<bool> SyncHoursAsync(string tenantId, object hoursData) => await Task.FromResult(true);
}

    public async Task<bool> SyncHoursAsync(string tenantId, object hoursData) => await Task.FromResult(true);
}
