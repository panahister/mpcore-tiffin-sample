using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MPCore.Application.Querying;
using Tiffin.Access.Application.Ports;

namespace Tiffin.Access.Infrastructure.Keycloak;

/// <summary>Where the identity provider's administration is, and how cities are kept there.</summary>
public sealed class KeycloakOptions
{
    /// <summary>The administration of the realm, for example <c>https://identity.example/admin/realms/tiffin/</c>.</summary>
    public required Uri Administration { get; init; }

    /// <summary>The group whose children are the cities.</summary>
    public string CitiesGroup { get; init; } = "cities";
}

/// <summary>
/// The adapter for Keycloak's administration: the only code of the platform that speaks its language, and
/// the only client of the platform that is allowed to.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is translated.</b> A city is a child of the group <c>cities</c>; a person belongs to the city
/// whose group they are in; a role of the platform is a realm role. Groups, paths, representations and
/// role mappings stop here.
/// </para>
/// <para>
/// <b>Who calls.</b> This service, as itself: the named client carries a token the identity provider
/// issued to the Access service (OAuth 2.0 client credentials), and its service account holds the roles of
/// <c>realm-management</c> that the calls below need and no other. No other service's account holds any,
/// so no other service can do what this adapter does, whatever its code says.
/// </para>
/// <para>
/// <b>What is safe to repeat.</b> Keycloak answers 204 to a role that is given twice and to one that is
/// taken twice, so a step that is tried again after a timeout changes nothing the second time.
/// </para>
/// </remarks>
public sealed class KeycloakDirectory(IHttpClientFactory clients, KeycloakOptions options, ILogger<KeycloakDirectory> logger) : IIdentityDirectory
{
    public const string ClientName = "keycloak-administration";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Person?> FindAsync(string personId, CancellationToken cancellationToken)
    {
        // An identifier of Keycloak is a UUID. Anything else cannot be a person, and is not sent on as a path.
        if (!Guid.TryParse(personId, out var id))
        {
            return null;
        }

        var user = await GetAsync<UserRepresentation>($"users/{id}", cancellationToken).ConfigureAwait(false);
        if (user is null)
        {
            return null;
        }

        var groups = await GetAsync<List<GroupRepresentation>>($"users/{id}/groups", cancellationToken).ConfigureAwait(false) ?? [];
        var roles = await GetAsync<List<RoleRepresentation>>($"users/{id}/role-mappings/realm", cancellationToken).ConfigureAwait(false) ?? [];
        return ToPerson(user, CityOf(groups), roles);
    }

    public async Task<Page<Person>> PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        var group = await GetAsync<GroupRepresentation>($"group-by-path/{options.CitiesGroup}/{Uri.EscapeDataString(city)}", cancellationToken)
            .ConfigureAwait(false);
        if (group?.Id is null)
        {
            return Page<Person>.Empty(page);
        }

        var members = await GetAsync<List<UserRepresentation>>(
            $"groups/{group.Id}/members?first={page.Skip}&max={page.Size}&briefRepresentation=true", cancellationToken).ConfigureAwait(false) ?? [];
        // Keycloak has no count of the members of a group. The whole list is asked for in its brief form
        // and counted: a city of the sample has a handful of people. A city of a million needs another way.
        var everybody = await GetAsync<List<UserRepresentation>>(
            $"groups/{group.Id}/members?briefRepresentation=true&max=100000", cancellationToken).ConfigureAwait(false) ?? [];

        var people = new List<Person>(members.Count);
        foreach (var member in members)
        {
            var roles = await GetAsync<List<RoleRepresentation>>($"users/{member.Id}/role-mappings/realm", cancellationToken).ConfigureAwait(false) ?? [];
            people.Add(ToPerson(member, city, roles));
        }

        return new Page<Person>(people, page.Number, page.Size, everybody.Count);
    }

    public Task GrantAsync(string personId, string role, CancellationToken cancellationToken) =>
        ChangeAsync(HttpMethod.Post, personId, role, cancellationToken);

    public Task RevokeAsync(string personId, string role, CancellationToken cancellationToken) =>
        ChangeAsync(HttpMethod.Delete, personId, role, cancellationToken);

    private async Task ChangeAsync(HttpMethod method, string personId, string role, CancellationToken cancellationToken)
    {
        var known = await GetAsync<RoleRepresentation>($"roles/{Uri.EscapeDataString(role)}", cancellationToken).ConfigureAwait(false)
            ?? throw new DirectoryUnavailableException($"The identity provider has no role '{role}'.");

        using var request = new HttpRequestMessage(method, $"users/{Guid.Parse(personId)}/role-mappings/realm")
        {
            Content = JsonContent.Create(new[] { known }, options: Json)
        };
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw Unavailable(request, response.StatusCode);
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw Unavailable(request, response.StatusCode);
        }

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new DirectoryUnavailableException("The identity provider answered what could not be read.", exception);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await clients.CreateClient(ClientName).SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TimeoutException
                                              or Polly.CircuitBreaker.BrokenCircuitException
                                              or Polly.Timeout.TimeoutRejectedException
                                              or TaskCanceledException { CancellationToken.IsCancellationRequested: false })
        {
            logger.LogWarning(exception, "The identity provider did not answer {Method} {Path}", request.Method, PathOf(request));
            throw new DirectoryUnavailableException("The identity provider did not answer.", exception);
        }
    }

    private DirectoryUnavailableException Unavailable(HttpRequestMessage request, HttpStatusCode status)
    {
        logger.LogWarning("The identity provider answered {Method} {Path} with {Status}", request.Method, PathOf(request), (int)status);
        return new DirectoryUnavailableException($"The identity provider answered {(int)status}.");
    }

    /// <summary>What a log may show of a request: what was asked, never the address with its host, and never a body.</summary>
    private static string PathOf(HttpRequestMessage request) => request.RequestUri?.OriginalString.Split('?')[0] ?? string.Empty;

    private string? CityOf(List<GroupRepresentation> groups)
    {
        var prefix = $"/{options.CitiesGroup}/";
        return groups.Select(static g => g.Path).FirstOrDefault(p => p is not null && p.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
    }

    private static Person ToPerson(UserRepresentation user, string? city, List<RoleRepresentation> roles) => new(
        user.Id!, user.Username ?? string.Empty,
        $"{user.FirstName} {user.LastName}".Trim() is { Length: > 0 } name ? name : user.Username ?? string.Empty,
        city, [.. roles.Select(static r => r.Name).OfType<string>()], user.Enabled ?? false);

    // What Keycloak calls things. Private: no type of the identity provider leaves this file.
    private sealed record UserRepresentation(string? Id, string? Username, string? FirstName, string? LastName, bool? Enabled);

    private sealed record GroupRepresentation(string? Id, string? Name, string? Path);

    private sealed record RoleRepresentation(string? Id, string? Name);
}
