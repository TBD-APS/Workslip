using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Xunit;
using Workslip.Application.Integrations;

namespace Workslip.Tests.Application.Integrations;

public sealed class EconomicsProviderTests
{
    private EconomicsProvider CreateProvider(HttpMessageHandler handler)
    {
        var factory = new FakeHttpClientFactory(handler);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Economic:AppSecretToken"] = "test-app-secret",
                ["Integrations:Economic:Agreements:tenant-1:GrantToken"] = "test-grant",
                ["Integrations:Economic:Agreements:t:GrantToken"] = "test-grant",
                ["Integrations:Economic:Defaults:CustomerGroupNumber"] = "1",
                ["Integrations:Economic:Defaults:PaymentTermsNumber"] = "1",
                ["Integrations:Economic:Defaults:VatZoneNumber"] = "1",
                ["Integrations:Economic:Products:Hours"] = "HOURS",
                ["Integrations:Economic:Products:Material"] = "MATERIAL",
                ["Integrations:Economic:Products:Outlay"] = "OUTLAY",
            })
            .Build();
        return new EconomicsProvider(factory, configuration);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_WhenApiRespondsSuccess()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = CreateProvider(handler);

        var result = await provider.TestConnectionAsync("tenant-1");

        Assert.True(result);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenApiRespondsError()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = CreateProvider(handler);

        var result = await provider.TestConnectionAsync("tenant-1");

        Assert.False(result);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenTenantHasNoCredentials()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = CreateProvider(handler);

        var result = await provider.TestConnectionAsync("unconfigured-tenant");

        Assert.False(result);
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_MapsJsonCorrectly_WhenApiReturnsInvoices()
    {
        var json = @"{
            ""collection"": [
                {
                    ""bookedInvoiceNumber"": 101,
                    ""orderNumber"": 501,
                    ""date"": ""2023-01-01"",
                    ""currency"": ""DKK"",
                    ""netAmount"": 1234.56,
                    ""grossAmount"": 1543.20,
                    ""vatAmount"": 308.64,
                    ""remainder"": 0
                },
                {
                    ""bookedInvoiceNumber"": 102,
                    ""orderNumber"": 502,
                    ""date"": ""2023-01-02"",
                    ""currency"": ""DKK"",
                    ""netAmount"": 678.90,
                    ""grossAmount"": 848.63,
                    ""vatAmount"": 169.73,
                    ""remainder"": 678.90
                }
            ]
        }";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);

        var docs = (await provider.GetDocumentsForUserAsync("t", "u", "s", "e")).ToList();

        Assert.Equal(2, docs.Count);
        var first = docs.First();
        Assert.Equal("101", first.DocumentId);
        Assert.Equal("FAK-0101", first.DocumentNumber);
        Assert.Equal(1234.56m, first.Amount);
        Assert.Equal("Invoice", first.Type);
        Assert.Equal("Paid", first.Status);
        Assert.Equal("Unpaid", docs.Last().Status);
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_PropagatesProviderFailure()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetDocumentsForUserAsync("t", "u", "s", "e"));
    }

    [Fact]
    public async Task GetDocumentStreamAsync_ReturnsStream_WhenApiRespondsSuccess()
    {
        var content = "Fake PDF Content";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        });
        var provider = CreateProvider(handler);

        using var stream = await provider.GetDocumentStreamAsync("t", "doc-1");

        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal(content, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task GetDocumentStreamAsync_ReturnsNull_WhenApiRespondsError()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        var stream = await provider.GetDocumentStreamAsync("t", "doc-1");

        Assert.Null(stream);
    }

    private sealed class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public MockHttpMessageHandler(HttpResponseMessage response) => _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_response);
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public FakeHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name = "") => new HttpClient(_handler, disposeHandler: false);
    }
}
