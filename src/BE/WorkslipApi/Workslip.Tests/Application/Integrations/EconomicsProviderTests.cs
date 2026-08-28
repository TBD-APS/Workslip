using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Workslip.Application.Integrations;
using Workslip.Domain.Models;

namespace Workslip.Tests.Application.Integrations;

public sealed class EconomicsProviderTests
{
    private EconomicsProvider CreateProvider(HttpMessageHandler handler)
    {
        var factory = new FakeHttpClientFactory(handler);
        return new EconomicsProvider(factory, new FakeAccountingTokenStore());
    }

    private sealed class FakeAccountingTokenStore : IAccountingTokenStore
    {
        public Task<AccountingTokens> GetAsync(Guid organizationId, CancellationToken cancellationToken) =>
            Task.FromResult(new AccountingTokens(null, null));
        public Task SetAsync(Guid organizationId, string? agreementGrantToken, string? appSecretToken, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsTrue_WhenApiRespondsSuccess()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK));
        var provider = CreateProvider(handler);

        // Act
        var result = await provider.TestConnectionAsync("tenant-1");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TestConnectionAsync_ReturnsFalse_WhenApiRespondsError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = CreateProvider(handler);

        // Act
        var result = await provider.TestConnectionAsync("tenant-1");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_MapsJsonCorrectly_WhenApiReturnsInvoices()
    {
        // Arrange — e-conomic demo returns { collection: [...] } from /invoices/booked
        var json = @"{ ""collection"": [
            { ""bookedInvoiceNumber"": 1, ""orderNumber"": 1, ""date"": ""2022-06-02"", ""currency"": ""DKK"", ""netAmount"": 70.00, ""grossAmount"": 87.50, ""vatAmount"": 17.50, ""remainder"": 0.00 },
            { ""bookedInvoiceNumber"": 2, ""orderNumber"": 2, ""date"": ""2022-06-03"", ""currency"": ""DKK"", ""netAmount"": 1234.56, ""grossAmount"": 1500.00, ""vatAmount"": 265.44, ""remainder"": 100.00 }
        ] }";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);

        // Act
        var docs = (await provider.GetDocumentsForUserAsync("t", "u", "s", "e")).ToList();

        // Assert — 1:1 mapping, beløb skal passe perfekt (netAmount)
        Assert.Equal(2, docs.Count);
        var first = docs.First();
        Assert.Equal("1", first.DocumentId);
        Assert.Equal("FAK-0001", first.DocumentNumber);
        Assert.Equal(70.00m, first.Amount);
        Assert.Equal("Invoice", first.Type);
        Assert.Equal("Paid", first.Status); // remainder 0 => Paid
        Assert.Equal("2022-06-02", first.Date);
        Assert.Equal("https://restapi.e-conomic.com/invoices/booked/1", first.ExternalLink);

        var second = docs.Skip(1).First();
        Assert.Equal("2", second.DocumentId);
        Assert.Equal("FAK-0002", second.DocumentNumber);
        Assert.Equal(1234.56m, second.Amount);
        Assert.Equal("Unpaid", second.Status); // remainder !=0
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_ReturnsEmpty_WhenCollectionIsEmpty()
    {
        var json = @"{ ""collection"": [] }";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);
        var docs = await provider.GetDocumentsForUserAsync("t", "u", "s", "e");
        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_ReturnsEmpty_WhenJsonIsInvalid()
    {
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not-json", Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);
        var docs = await provider.GetDocumentsForUserAsync("t", "u", "s", "e");
        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentsForUserAsync_ReturnsEmpty_WhenApiReturnsError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        // Act
        var docs = await provider.GetDocumentsForUserAsync("t", "u", "s", "e");

        // Assert
        Assert.Empty(docs);
    }

    [Fact]
    public async Task GetDocumentStreamAsync_ReturnsStream_WhenApiRespondsSuccess()
    {
        // Arrange
        var content = "Fake PDF Content";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content)
        });
        var provider = CreateProvider(handler);

        // Act
        using var stream = await provider.GetDocumentStreamAsync("t", "doc-1");

        // Assert
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        Assert.Equal(content, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task GetDocumentStreamAsync_ReturnsNull_WhenApiRespondsError()
    {
        // Arrange
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.NotFound));
        var provider = CreateProvider(handler);

        // Act
        var stream = await provider.GetDocumentStreamAsync("t", "doc-1");

        // Assert
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
        public HttpClient CreateClient(string name = "") => new HttpClient(_handler);
    }
}
