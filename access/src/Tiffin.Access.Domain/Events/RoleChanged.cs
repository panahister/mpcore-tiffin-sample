using MPCore.Domain.Events;

namespace Tiffin.Access.Domain.Events;

/// <summary>Somebody has a role they did not have, or no longer has one they had. Written to the event stream when the identity provider has it.</summary>
/// <remarks>
/// The event names the person by the subject of their tokens and carries no name, no e-mail and no phone
/// number: whoever needs those asks, and is asked who they are.
/// </remarks>
public sealed record RoleChanged : IntegrationEvent
{
    public const string Name = "tiffin.access.role-changed";

    public RoleChanged(Guid grantId, string city, string personId, string role, bool granted, DateTimeOffset occurredOnUtc)
        : base(Name, 1, occurredOnUtc, Guid.CreateVersion7())
    {
        GrantId = grantId;
        City = city;
        PersonId = personId;
        Role = role;
        Granted = granted;
    }

    public Guid GrantId { get; init; }

    public string City { get; init; }

    public string PersonId { get; init; }

    public string Role { get; init; }

    /// <summary>True when the role was given, false when it was taken away.</summary>
    public bool Granted { get; init; }
}
