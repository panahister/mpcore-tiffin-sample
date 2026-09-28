using MPCore.Domain.Model;
using Tiffin.Access.Domain.Events;
using Tiffin.Access.Domain.Rules;

namespace Tiffin.Access.Domain;

public enum GrantKind
{
    Grant = 0,
    Revoke = 1
}

public enum GrantState
{
    /// <summary>Decided and recorded here; the identity provider has not been told yet.</summary>
    Requested = 0,

    /// <summary>The identity provider has it.</summary>
    Applied = 1,

    /// <summary>The identity provider could not be told, and telling it was given up.</summary>
    Failed = 2
}

/// <summary>One decision about one person's role: who decided, for whom, in which city, and what became of it.</summary>
/// <remarks>
/// <para>
/// <b>The decision is this service's; the role is the identity provider's.</b> They live in two systems
/// that share no transaction. So the decision is recorded first, with a message that says "apply it", in
/// one transaction of this service's own (the transactional outbox). The message is handled afterwards,
/// tells the identity provider, and marks the decision applied. A provider that is down delays a role; it
/// never loses a decision, and never applies one that was not recorded.
/// </para>
/// <para>
/// Telling the provider is safe to repeat: to give a role somebody has, or to take one they have not, is
/// to change nothing. Pat Helland calls what such a step needs <i>idempotence</i> (<i>Life beyond
/// Distributed Transactions</i>, 2007).
/// </para>
/// </remarks>
public sealed class Grant : AggregateRoot<Guid>
{
    private Grant()
    {
        City = string.Empty;
        PersonId = string.Empty;
        PersonName = string.Empty;
        Role = string.Empty;
        DecidedBy = string.Empty;
    }

    private Grant(Guid id, string city, string personId, string personName, string role, GrantKind kind, string decidedBy, DateTimeOffset now)
        : base(id)
    {
        City = city;
        PersonId = personId;
        PersonName = personName;
        Role = role;
        Kind = kind;
        DecidedBy = decidedBy;
        DecidedOnUtc = now;
        State = GrantState.Requested;
    }

    /// <summary>The tenant: the city of the person the decision is about.</summary>
    public string City { get; private set; }

    /// <summary>The subject of the person's tokens.</summary>
    public string PersonId { get; private set; }

    public string PersonName { get; private set; }

    public string Role { get; private set; }

    public GrantKind Kind { get; private set; }

    /// <summary>The subject of whoever decided.</summary>
    public string DecidedBy { get; private set; }

    public DateTimeOffset DecidedOnUtc { get; private set; }

    public GrantState State { get; private set; }

    public DateTimeOffset? AppliedOnUtc { get; private set; }

    public string? Failure { get; private set; }

    public static Grant Decide(
        string city, string personId, string personName, string role, GrantKind kind, string decidedBy,
        IReadOnlyCollection<string> rolesOfTheOneWhoDecides, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(personId);
        ArgumentException.ThrowIfNullOrWhiteSpace(decidedBy);
        CheckRule(new ARoleIsKnown(role));
        CheckRule(new ARoleIsHandedOutByWhoMay(role, rolesOfTheOneWhoDecides));
        CheckRule(new NobodyChangesTheirOwnRoles(personId, decidedBy));

        return new Grant(Guid.CreateVersion7(now), city, personId, personName, role, kind, decidedBy, now);
    }

    public void MarkApplied(DateTimeOffset now)
    {
        if (State == GrantState.Applied)
        {
            return;
        }

        State = GrantState.Applied;
        AppliedOnUtc = now;
        Failure = null;
        Raise(new RoleChanged(Id, City, PersonId, Role, Kind == GrantKind.Grant, now));
    }

    public void MarkFailed(string failure)
    {
        if (State == GrantState.Requested)
        {
            State = GrantState.Failed;
            Failure = failure;
        }
    }
}
