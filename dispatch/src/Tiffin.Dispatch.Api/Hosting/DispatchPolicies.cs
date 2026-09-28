using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Dispatch.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
public sealed class DispatchPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Couriers.</summary>
    public const string Courier = "tiffin.courier";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Courier, MPCoreAuthorizationPolicies.RequireRole("courier"));
    }
}
