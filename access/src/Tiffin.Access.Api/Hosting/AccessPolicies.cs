using Microsoft.AspNetCore.Authorization;
using MPCore.Security.AspNetCore;

namespace Tiffin.Access.Api.Hosting;

/// <summary>
/// The named authorization policies of this host, contributed to MP Core's default-deny authorization.
/// </summary>
/// <remarks>
/// A policy answers "may this kind of caller use this endpoint". Which city an admin may work in, and
/// which roles are theirs to give, are business questions and stay in the handler and in the aggregate.
/// </remarks>
public sealed class AccessPolicies : IMPCoreAuthorizationPolicyContributor
{
    /// <summary>Admins, of a city or of the platform.</summary>
    public const string Admin = "tiffin.admin";

    /// <inheritdoc />
    public void Contribute(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Admin, MPCoreAuthorizationPolicies.RequireRole("city-admin", "platform-admin"));
    }
}
