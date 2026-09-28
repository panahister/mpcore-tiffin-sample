using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Tracking.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// A policy answers "may this kind of caller use this endpoint". Whether this courier carries <i>that</i>
/// order, and whether this customer waits for it, are business questions and stay in the aggregate and the
/// handler.
/// </remarks>
public sealed class TrackingPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Couriers.</summary>
    public const string Courier = "tiffin.courier";

    /// <summary>Whoever may ask where an order is: a customer, or a courier.</summary>
    public const string Followers = "tiffin.followers";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Courier, MPCoreAuthorizationPolicies.RequireRole("courier"));
        options.AddPolicy(Followers, MPCoreAuthorizationPolicies.RequireRole("customer", "courier"));
    }
}
