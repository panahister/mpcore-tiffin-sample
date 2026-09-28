using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Ordering.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// MP Core ships one named policy, any authenticated caller, and a fallback that protects every endpoint
/// without metadata. It ships no product role. A policy answers "may this kind of caller use this
/// endpoint"; whether this customer placed <i>that</i> order is a business question and stays in the handler.
/// </remarks>
public sealed class OrderingPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Customers.</summary>
    public const string Customer = "tiffin.customer";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Customer, MPCoreAuthorizationPolicies.RequireRole("customer"));
    }
}
