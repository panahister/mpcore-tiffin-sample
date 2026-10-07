using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Tiffin.Access.Infrastructure.Keycloak;

namespace Tiffin.Access.Tests;

public sealed class KeycloakDirectoryTests
{
    [Fact]
    public async Task A_self_registered_user_gets_the_effective_customer_role_without_Keycloak_builtin_roles()
    {
        const string subject = "9aaac169-00a9-4d39-87f1-ca47400feb2a";
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(requests))
        {
            BaseAddress = new Uri("http://identity.test/admin/realms/tiffin/")
        };
        var directory = new KeycloakDirectory(
            new StubFactory(client),
            new KeycloakOptions { Administration = client.BaseAddress },
            NullLogger<KeycloakDirectory>.Instance);

        var person = await directory.FindAsync(subject, default);

        Assert.NotNull(person);
        Assert.Equal(subject, person.PersonId);
        Assert.Equal("seattle", person.City);
        Assert.Equal(["customer"], person.Roles);
        Assert.Contains($"users/{subject}/role-mappings/realm/composite", requests);
        Assert.DoesNotContain($"users/{subject}/role-mappings/realm", requests);
    }

    [Fact]
    public async Task The_authoritative_snapshot_contains_only_city_assigned_business_identities()
    {
        const string subject = "9aaac169-00a9-4d39-87f1-ca47400feb2a";
        var requests = new List<string>();
        using var client = new HttpClient(new StubHandler(requests))
        {
            BaseAddress = new Uri("http://identity.test/admin/realms/tiffin/")
        };
        var directory = new KeycloakDirectory(
            new StubFactory(client),
            new KeycloakOptions { Administration = client.BaseAddress },
            NullLogger<KeycloakDirectory>.Instance);

        var people = await directory.SnapshotAsync(default);

        var person = Assert.Single(people);
        Assert.Equal(subject, person.PersonId);
        Assert.Equal("charlotte", person.UserName);
        Assert.Equal("seattle", person.City);
        Assert.Contains("users?first=0&max=100&briefRepresentation=true", requests);
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(List<string> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.PathAndQuery
                .Replace("/admin/realms/tiffin/", string.Empty, StringComparison.Ordinal) ?? string.Empty;
            requests.Add(path);

            var body = path switch
            {
                var value when value.StartsWith("users?first=", StringComparison.Ordinal) =>
                    "[{\"id\":\"9aaac169-00a9-4d39-87f1-ca47400feb2a\",\"username\":\"charlotte\",\"enabled\":true}]",
                var value when value.EndsWith("/groups", StringComparison.Ordinal) =>
                    "[{\"id\":\"seattle-id\",\"name\":\"seattle\",\"path\":\"/cities/seattle\"}]",
                var value when value.EndsWith("/role-mappings/realm/composite", StringComparison.Ordinal) =>
                    "[{\"id\":\"default-id\",\"name\":\"default-roles-tiffin\"},"
                    + "{\"id\":\"customer-id\",\"name\":\"customer\"},"
                    + "{\"id\":\"offline-id\",\"name\":\"offline_access\"}]",
                _ => "{\"id\":\"9aaac169-00a9-4d39-87f1-ca47400feb2a\",\"username\":\"charlotte\","
                    + "\"firstName\":\"Charlotte\",\"lastName\":\"Brooks\",\"enabled\":true}"
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
