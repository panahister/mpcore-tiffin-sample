using Tiffin.Access.Application.Integration;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Infrastructure.Persistence;
using Tiffin.Access.Tests.Support;
using MPCore.Domain.Events;

namespace Tiffin.Access.Tests;

public sealed class RecordingIdentityProjection : IIdentityProjection
{
    public List<(Person Person, string EventId, DateTimeOffset OccurredAt)> Upserts { get; } = [];
    public List<(string PersonId, string EventId, DateTimeOffset OccurredAt)> Disables { get; } = [];

    public Task UpsertAsync(Person person, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        Upserts.Add((person, eventId, occurredAt));
        return Task.CompletedTask;
    }

    public Task DisableAsync(string personId, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        Disables.Add((personId, eventId, occurredAt));
        return Task.CompletedTask;
    }

    public Task<Person?> FindAsync(string personId, CancellationToken cancellationToken) => Task.FromResult<Person?>(null);

    public Task<MPCore.Application.Querying.Page<Person>> PeopleOfAsync(
        string city, MPCore.Application.Querying.PageRequest page, CancellationToken cancellationToken) =>
        Task.FromResult(MPCore.Application.Querying.Page<Person>.Empty(page));
}

public sealed class IdentityLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static IdentityUserEventV1 Event(
        string type,
        string outcome = "success",
        string source = "https://identity.tiffin.local/realms/tiffin") => new(
        "1.0", "keycloak-user-event-001", source,
        "identity.user-event.v1", Now, "application/json",
        "urn:portable:keycloak:identity:user-event:v1", $"683b46f1-5458-4657-852c-bec8558fc20b:{FakeDirectory.Sara}",
        new IdentityUserEventData(
            "1.0", "683b46f1-5458-4657-852c-bec8558fc20b", FakeDirectory.Sara,
            "tiffin-app", null, type, outcome, "account", Now, Now));

    [Fact]
    public void CloudEvent_identity_is_stable_for_the_transactional_inbox()
    {
        var first = (IIntegrationEvent)Event("REGISTER");
        var replay = (IIntegrationEvent)Event("REGISTER");
        var other = (IIntegrationEvent)(Event("REGISTER") with { Id = "keycloak-user-event-002" });

        Assert.Equal(first.EventId, replay.EventId);
        Assert.NotEqual(first.EventId, other.EventId);
        Assert.Equal("identity.user-event.v1", first.EventName);
        Assert.Equal(1, first.EventVersion);
        Assert.Equal(Now, first.OccurredOnUtc);
    }

    [Fact]
    public async Task A_successful_registration_hydrates_the_business_projection_from_Keycloak()
    {
        var directory = new FakeDirectory();
        var projection = new RecordingIdentityProjection();

        await IdentityUserEventConsumer.Handle(
            Event("REGISTER"), directory, projection, new FakeUnitOfWork(), default);

        var upsert = Assert.Single(projection.Upserts);
        Assert.Equal(FakeDirectory.Sara, upsert.Person.PersonId);
        Assert.Equal("keycloak-user-event-001", upsert.EventId);
        Assert.Empty(projection.Disables);
    }

    [Fact]
    public async Task Failed_foreign_and_non_lifecycle_events_do_not_create_a_business_user()
    {
        var directory = new FakeDirectory();
        var projection = new RecordingIdentityProjection();

        await IdentityUserEventConsumer.Handle(Event("REGISTER", outcome: "failure"), directory, projection, new FakeUnitOfWork(), default);
        await IdentityUserEventConsumer.Handle(
            Event("REGISTER", source: "https://identity.tiffin.local/realms/commerce"),
            directory, projection, new FakeUnitOfWork(), default);
        await IdentityUserEventConsumer.Handle(Event("LOGIN"), directory, projection, new FakeUnitOfWork(), default);

        Assert.Empty(projection.Upserts);
        Assert.Empty(projection.Disables);
    }

    [Fact]
    public async Task Account_deletion_disables_the_projection_without_copying_credentials()
    {
        var projection = new RecordingIdentityProjection();

        await IdentityUserEventConsumer.Handle(
            Event("DELETE_ACCOUNT"), new FakeDirectory(), projection, new FakeUnitOfWork(), default);

        Assert.Equal(FakeDirectory.Sara, Assert.Single(projection.Disables).PersonId);
        Assert.Empty(projection.Upserts);
    }

    [Fact]
    public async Task Reconciliation_hydrates_realm_imports_that_have_no_registration_event()
    {
        var directory = new FakeDirectory();
        var projection = new RecordingIdentityProjection();

        var count = await IdentityProjectionReconciliation.ReconcileAsync(directory, projection, Now, default);

        Assert.Equal(3, count);
        Assert.Equal(3, projection.Upserts.Count);
        Assert.All(projection.Upserts, upsert => Assert.Equal($"keycloak-reconciliation:{Now:O}", upsert.EventId));
    }

    [Fact]
    public void Duplicate_and_older_events_cannot_overwrite_a_newer_projection()
    {
        var original = new Person(FakeDirectory.Sara, "olivia", "Olivia Carter", "seattle", ["customer"], true);
        var row = IdentityPerson.From(original, "event-b", Now);

        row.Refresh(original with { Name = "Older Name" }, "event-z", Now.AddSeconds(-1));
        row.Refresh(original with { Name = "Equal But Lower Id" }, "event-a", Now);
        Assert.Equal("Olivia Carter", row.ToPerson().Name);

        row.Refresh(original with { Name = "Olivia Morgan" }, "event-c", Now);
        Assert.Equal("Olivia Morgan", row.ToPerson().Name);
    }
}
