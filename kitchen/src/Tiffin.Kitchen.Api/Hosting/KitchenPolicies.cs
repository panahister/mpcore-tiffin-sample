using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Kitchen.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// A policy answers "may this kind of caller use this endpoint". Whether this manager manages the
/// restaurant of <i>that</i> order is a business question and stays in the handler.
/// </remarks>
public sealed class KitchenPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Managers of restaurants.</summary>
    public const string Manager = "tiffin.restaurant-manager";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Manager, MPCoreAuthorizationPolicies.RequireRole("restaurant-manager"));
    }
}
