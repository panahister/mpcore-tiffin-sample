using MPCore.Domain.Rules;

namespace Tiffin.Access.Domain.Rules;

/// <summary>
/// A business rule of the Access service, reported under the <c>tiffin.access</c> error domain with a
/// stable code and a message key. The named-rule pattern is Kamil Grzybek's (<i>Modular Monolith with
/// DDD</i>); checking before changing is Vladimir Khorikov's <i>always-valid domain model</i>.
/// </summary>
public abstract class AccessRule(string code, string messageKey, IReadOnlyDictionary<string, string>? arguments = null)
    : BusinessRule(Domain, code, messageKey, arguments)
{
    public const string Domain = "tiffin.access";
}

/// <summary>Rule A1: a role is one of the platform's.</summary>
public sealed class ARoleIsKnown(string role) : AccessRule(
    "ROLE_UNKNOWN", "access.role_unknown", new Dictionary<string, string> { ["role"] = role ?? string.Empty })
{
    public override bool IsBroken() => !Roles.Known.Contains(role);
}

/// <summary>Rule A2: a city's admin hands out the roles of a city; only the platform's admin makes a city's admin.</summary>
/// <remarks>The principle of least privilege (Jerome Saltzer and Michael Schroeder, 1975): nobody can give what they were not given the right to give.</remarks>
public sealed class ARoleIsHandedOutByWhoMay(string role, IReadOnlyCollection<string> rolesOfTheOneWhoDecides) : AccessRule(
    "ROLE_NOT_YOURS_TO_GIVE", "access.role_not_yours_to_give", new Dictionary<string, string> { ["role"] = role ?? string.Empty })
{
    public override bool IsBroken() => !Roles.GrantableBy(rolesOfTheOneWhoDecides).Contains(role);
}

/// <summary>Rule A3: nobody changes their own roles.</summary>
/// <remarks>Separation of duties: the one who benefits from a decision is not the one who makes it.</remarks>
public sealed class NobodyChangesTheirOwnRoles(string personId, string decidedBy) : AccessRule("OWN_ROLES", "access.own_roles")
{
    public override bool IsBroken() => string.Equals(personId, decidedBy, StringComparison.Ordinal);
}
