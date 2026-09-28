using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Restaurants.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// <para>
/// MP Core ships one named policy, any authenticated caller, and a fallback that protects every endpoint
/// without metadata. It ships no product role. The roles here are Tiffin's, issued by the <c>tiffin</c>
/// realm and normalised by MP Core's Keycloak claim mapping.
/// </para>
/// <para>
/// A policy answers "may this kind of caller use this endpoint". Whether this manager manages
/// <i>that</i> restaurant is a business question and stays in the handler.
/// </para>
/// </remarks>
public sealed class RestaurantsPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Managers of restaurants.</summary>
    public const string Manager = "tiffin.restaurant-manager";

    /// <summary>A service of the platform, calling as itself.</summary>
    public const string Service = "tiffin.service";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Manager, MPCoreAuthorizationPolicies.RequireRole("restaurant-manager"));
        options.AddPolicy(Service, MPCoreAuthorizationPolicies.RequireRole("service"));
    }
}
