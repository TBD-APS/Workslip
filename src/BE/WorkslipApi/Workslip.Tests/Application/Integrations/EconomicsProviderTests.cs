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
        return new EconomicsProvider(factory);
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
        // Arrange
        var invId = Guid.NewGuid();
        var json = @"[
            { ""Id"": """ + invId + @""", ""Number"": ""INV-001"", ""Amount"": 1234.56, ""Date"": ""2023-01-01"" },
            { ""Id"": """ + Guid.NewGuid() + @""", ""Number"": ""INV-002"", ""Amount"": 678.90, ""Date"": ""2023-01-02"" }
        ]";
        var handler = new MockHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var provider = CreateProvider(handler);

        // Act
        var docs = (await provider.GetDocumentsForUserAsync("t", "u", "s", "e")).ToList();

        // Assert
        Assert.Equal(2, docs.Count);
        var first = docs.First();
        Assert.Equal(invId.ToString(), first.DocumentId);
        Assert.Equal("INV-001", first.DocumentNumber);
        Assert.Equal(1234.56m, first.Amount);
        Assert.Equal("Invoice", first.Type);
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
