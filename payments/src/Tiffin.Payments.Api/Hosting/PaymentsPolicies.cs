using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Payments.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// This service has one kind of caller: another service of the platform, as itself. A person's token is
/// refused whatever its role, and the edge has no route to this host at all.
/// </remarks>
public sealed class PaymentsPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>A service of the platform, calling as itself.</summary>
    public const string Service = "tiffin.service";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Service, MPCoreAuthorizationPolicies.RequireRole("service"));
    }
}
