using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions.Authentication;
using Workslip.Application;
using Workslip.Application.Users;
using Xunit;

namespace Workslip.Tests.Users;

public sealed class UserEntraServiceTests
{
    private const string ExistingUserId = "11111111-1111-1111-1111-111111111111";
    private const string ServicePrincipalId = "22222222-2222-2222-2222-222222222222";
    private const string SuperadminRoleId = "33333333-3333-3333-3333-333333333333";
    private const string AssignmentId = "44444444-4444-4444-4444-444444444444";
    private const string ClientId = "55555555-5555-5555-5555-555555555555";

    [Fact]
    public async Task EnsureSuperadminAsync_ExistingGuestByExternalEmail_ReusesGuestWithoutInvitation()
    {
        var handler = new GraphHttpMessageHandler(CreateGraphResponse);
        using var httpClient = new HttpClient(handler);
        var graphClient = new GraphServiceClient(
            httpClient,
            new AnonymousAuthenticationProvider());
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Azure:AdOAuth:ClientId"] = ClientId,
                ["Azure:AdOAuth:Domain"] = "tenant.onmicrosoft.com",
                ["Azure:Domain:BaseUrl"] = "https://app.example.test"
            })
            .Build();
        var service = new UserEntraService(
            NullLogger<UserEntraService>.Instance,
            graphClient,
            configuration,
            new FakeCorrelationIdAccessor());

        var result = await service.EnsureSuperadminAsync(
            "mathiaslt1@hotmail.dk",
            "Mathias Lambæk",
            CancellationToken.None);

        Assert.False(result.Created);
        Assert.Equal(ExistingUserId, result.EntraUserId);
        Assert.Equal("mathiaslt1@hotmail.dk", result.EntraMail);
        Assert.DoesNotContain(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Url.Contains("/invitations", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request =>
            request.Method == HttpMethod.Post && request.Url.Contains("/appRoleAssignments", StringComparison.Ordinal));
    }

    private static HttpResponseMessage CreateGraphResponse(CapturedRequest request)
    {
        if (request.Method == HttpMethod.Get && request.Url.Contains("/users?", StringComparison.Ordinal))
        {
            Assert.Contains("mathiaslt1%40hotmail.dk", request.Url, StringComparison.OrdinalIgnoreCase);
            return JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "value": [
                    {
                      "id": "{{ExistingUserId}}",
                      "displayName": "Mathias Lambæk",
                      "userPrincipalName": "mathiaslt1_hotmail.dk#EXT#@tenant.onmicrosoft.com",
                      "mail": null,
                      "otherMails": ["mathiaslt1@hotmail.dk"]
                    }
                  ]
                }
                """);
        }

        if (request.Method == HttpMethod.Get && request.Url.Contains("/servicePrincipals?", StringComparison.Ordinal))
        {
            return JsonResponse(HttpStatusCode.OK, $$"""
                {
                  "value": [
                    {
                      "id": "{{ServicePrincipalId}}",
                      "appId": "{{ClientId}}",
                      "appRoles": [
                        {
                          "id": "{{SuperadminRoleId}}",
                          "value": "Superadmin",
                          "isEnabled": true
                        }
                      ]
                    }
                  ]
                }
                """);
        }

        if (request.Method == HttpMethod.Post && request.Url.Contains("/appRoleAssignments", StringComparison.Ordinal))
        {
            return JsonResponse(HttpStatusCode.Created, $$"""
                {
                  "id": "{{AssignmentId}}",
                  "principalId": "{{ExistingUserId}}",
                  "resourceId": "{{ServicePrincipalId}}",
                  "appRoleId": "{{SuperadminRoleId}}"
                }
                """);
        }

        return JsonResponse(
            HttpStatusCode.NotFound,
            """{"error":{"code":"NotFound","message":"Unexpected Graph request"}}""");
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class GraphHttpMessageHandler(
        Func<CapturedRequest, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.Method,
                request.RequestUri?.ToString() ?? string.Empty,
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));
            Requests.Add(captured);
            return responseFactory(captured);
        }
    }

    private sealed class FakeCorrelationIdAccessor : ICorrelationIdAccessor
    {
        public string CorrelationId => "wor-371-test";
    }

    private sealed record CapturedRequest(HttpMethod Method, string Url, string Body);
}
