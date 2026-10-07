using MPCore.Application.Querying;
using MPCore.Application.Results;
using Tiffin.Access.Application;
using Tiffin.Access.Application.Commands;
using Tiffin.Access.Application.Ports;
using Tiffin.Access.Application.Queries;
using Tiffin.Access.Application.Views;
using Tiffin.Access.Domain;
using Tiffin.Access.Domain.Events;
using Tiffin.Access.Tests.Support;

namespace Tiffin.Access.Tests;

/// <summary>The identity provider: a few people, their cities and roles; and whether it answers.</summary>
public sealed class FakeDirectory : IIdentityDirectory, IIdentityProjection
{
    public const string Sara = "11111111-1111-1111-1111-111111111111";
    public const string Ali = "22222222-2222-2222-2222-222222222222";
    public const string Elif = "33333333-3333-3333-3333-333333333333";

    private readonly Dictionary<string, (string Name, string City, List<string> Roles)> people = new()
    {
        [Sara] = ("Sara Ahmadi", "tehran", ["customer", "default-roles-tiffin", "offline_access"]),
        [Ali] = ("Ali Moradi", "tehran", ["city-admin"]),
        [Elif] = ("Elif Yilmaz", "istanbul", ["customer"]),
    };

    public bool IsDown { get; set; }

    public List<string> Changes { get; } = [];

    public IReadOnlyList<string> RolesOf(string personId) => people[personId].Roles;

    public Task<Person?> FindAsync(string personId, CancellationToken cancellationToken)
    {
        Answer();
        return Task.FromResult(people.TryGetValue(personId, out var p) ? new Person(personId, personId, p.Name, p.City, [.. p.Roles], true) : null);
    }

    public Task<Page<Person>> PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken)
    {
        Answer();
        var found = people.Where(p => p.Value.City == city).Select(static p => new Person(p.Key, p.Key, p.Value.Name, p.Value.City, [.. p.Value.Roles], true)).ToList();
        return Task.FromResult(new Page<Person>(found, page.Number, page.Size, found.Count));
    }

    public Task<IReadOnlyList<Person>> SnapshotAsync(CancellationToken cancellationToken)
    {
        Answer();
        IReadOnlyList<Person> found = [.. people.Select(static p =>
            new Person(p.Key, p.Key, p.Value.Name, p.Value.City, [.. p.Value.Roles], true))];
        return Task.FromResult(found);
    }

    public Task GrantAsync(string personId, string role, CancellationToken cancellationToken)
    {
        Answer();
        Changes.Add($"+{role}");
        if (!people[personId].Roles.Contains(role))
        {
            people[personId].Roles.Add(role);
        }

        return Task.CompletedTask;
    }

    public Task RevokeAsync(string personId, string role, CancellationToken cancellationToken)
    {
        Answer();
        Changes.Add($"-{role}");
        people[personId].Roles.Remove(role);
        return Task.CompletedTask;
    }

    public Task UpsertAsync(Person person, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        people[person.PersonId] = (person.Name, person.City ?? string.Empty, [.. person.Roles]);
        return Task.CompletedTask;
    }

    public Task DisableAsync(string personId, string eventId, DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        people.Remove(personId);
        return Task.CompletedTask;
    }

    Task<Person?> IIdentityProjection.FindAsync(string personId, CancellationToken cancellationToken) =>
        Task.FromResult(people.TryGetValue(personId, out var p)
            ? new Person(personId, personId, p.Name, p.City, [.. p.Roles], true)
            : null);

    Task<Page<Person>> IIdentityProjection.PeopleOfAsync(string city, PageRequest page, CancellationToken cancellationToken)
    {
        var found = people.Where(p => p.Value.City == city)
            .Select(static p => new Person(p.Key, p.Key, p.Value.Name, p.Value.City, [.. p.Value.Roles], true)).ToList();
        return Task.FromResult(new Page<Person>(found, page.Number, page.Size, found.Count));
    }

    private void Answer()
    {
        if (IsDown)
        {
            throw new DirectoryUnavailableException("The identity provider did not answer.");
        }
    }
}

public sealed class FakeGrants : IGrantRepository
{
    private readonly List<Grant> items = [];

    public IReadOnlyList<Grant> All => items;

    public Task<Grant?> GetAsync(Guid id, string city, CancellationToken cancellationToken) => Task.FromResult(items.Find(g => g.Id == id && g.City == city));

    public void Add(Grant grant) => items.Add(grant);
}

/// <summary>Who may give which role to whom, and what becomes of a decision when the identity provider is told.</summary>
public sealed class AccessTests
{
    private readonly FakeDirectory directory = new();
    private readonly FakeGrants grants = new();
    private readonly RecordingPublisher publisher = new();
    private readonly FakeAudit audit = new();
    private readonly FakeClock clock = FakeClock.At2026();
    private readonly FakeActor ali = FakeActor.User(FakeDirectory.Ali, "city-admin");
    private readonly FakeActor nora = FakeActor.User("44444444-4444-4444-4444-444444444444", "platform-admin");

    private Task<Result<GrantView>> Change(string person, string role, bool give = true, FakeActor? by = null, FakeTenant? tenant = null, string? city = null) =>
        ChangeRoleHandler.Handle(
            new ChangeRole(person, role, give, city), by ?? ali, tenant ?? FakeTenant.Tehran(), directory, grants, publisher, audit, new FakeUnitOfWork(),
            clock, default);

    private Task Apply(Grant grant, FakeTenant? tenant = null) => ApplyGrantHandler.Handle(
        new ApplyGrant(grant.Id), grants, tenant ?? FakeTenant.Tehran(), directory, directory, new FakeUnitOfWork(), clock, default);

    [Fact]
    public async Task A_decision_is_recorded_with_the_step_that_applies_it_and_the_identity_provider_is_not_told_yet()
    {
        var view = (await Change(FakeDirectory.Sara, "courier")).Value;

        var grant = Assert.Single(grants.All);
        Assert.Equal(("Requested", "tehran", "courier", "Grant"), (view.State, view.City, view.Role, view.Kind));
        Assert.Equal(grant.Id, publisher.Single<ApplyGrant>().GrantId);
        Assert.Equal("tehran", Assert.Single(publisher.Deliveries).Delivery.TenantId);
        Assert.Empty(directory.Changes);
        Assert.Equal("role-granted", Assert.Single(audit.Records).Action);
    }

    [Fact]
    public async Task The_step_tells_the_identity_provider_marks_the_decision_applied_and_says_so_on_the_stream()
    {
        await Change(FakeDirectory.Sara, "courier");
        var grant = Assert.Single(grants.All);

        await Apply(grant);
        await Apply(grant);

        Assert.Equal(GrantState.Applied, grant.State);
        Assert.Equal(["+courier"], directory.Changes);
        var changed = Assert.IsType<RoleChanged>(Assert.Single(grant.IntegrationEvents));
        Assert.Equal((FakeDirectory.Sara, "courier", true, "tehran"), (changed.PersonId, changed.Role, changed.Granted, changed.City));
    }

    [Fact]
    public async Task An_identity_provider_that_does_not_answer_is_a_failure_to_try_again_and_the_decision_waits()
    {
        await Change(FakeDirectory.Sara, "courier");
        var grant = Assert.Single(grants.All);
        directory.IsDown = true;

        var exception = await Assert.ThrowsAsync<ResultFailureException>(() => Apply(grant));

        Assert.True(exception.Failure.Retry.IsRetryable);
        Assert.Equal(GrantState.Requested, grant.State);

        await GrantGivenUpHandler.Handle(new GrantGivenUp(grant.Id, "The identity provider could not be told."), grants, FakeTenant.Tehran(), new FakeUnitOfWork(), default);
        Assert.Equal((GrantState.Failed, "The identity provider could not be told."), (grant.State, grant.Failure));

        directory.IsDown = false;
        await Apply(grant);
        Assert.Equal((GrantState.Applied, null), (grant.State, grant.Failure));
    }

    [Theory]
    [InlineData("city-admin", "ROLE_NOT_YOURS_TO_GIVE")]
    [InlineData("platform-admin", "ROLE_UNKNOWN")]
    [InlineData("service", "ROLE_UNKNOWN")]
    [InlineData("offline_access", "ROLE_UNKNOWN")]
    public async Task An_admin_of_a_city_gives_the_roles_of_a_city_and_no_other(string role, string rule)
    {
        Assert.Equal(rule, await Rules.BrokenAsync(() => Change(FakeDirectory.Sara, role)));
        Assert.Empty(grants.All);
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public async Task Nobody_changes_their_own_roles()
    {
        Assert.Equal("OWN_ROLES", await Rules.BrokenAsync(() => Change(FakeDirectory.Ali, "courier")));
    }

    [Fact]
    public async Task Somebody_of_another_city_does_not_exist_for_an_admin_of_a_city_whatever_city_is_asked_for()
    {
        var result = await Change(FakeDirectory.Elif, "courier", city: "istanbul");

        Assert.Equal("PERSON_NOT_FOUND", result.FailureDescriptor!.Identity.Code);
        Assert.Empty(grants.All);
    }

    [Fact]
    public async Task An_admin_of_the_platform_names_the_city_and_the_decision_belongs_to_that_city()
    {
        var platform = new FakeTenant(Roles.PlatformCity);

        var unnamed = await Change(FakeDirectory.Elif, "city-admin", by: nora, tenant: platform);
        var wrongCity = await Change(FakeDirectory.Elif, "city-admin", by: nora, tenant: platform, city: "tehran");
        var named = await Change(FakeDirectory.Elif, "city-admin", by: nora, tenant: platform, city: "Istanbul");

        Assert.Equal("ACCESS_INVALID", unnamed.FailureDescriptor!.Identity.Code);
        Assert.Equal("PERSON_NOT_FOUND", wrongCity.FailureDescriptor!.Identity.Code);
        Assert.Equal("istanbul", named.Value.City);
        Assert.Equal("istanbul", Assert.Single(publisher.Deliveries).Delivery.TenantId);
    }

    [Fact]
    public async Task Somebody_who_is_no_admin_decides_nothing_and_sees_nobody()
    {
        var sara = FakeActor.User(FakeDirectory.Sara, "customer");

        var decision = await Change(FakeDirectory.Elif, "courier", by: sara);
        var people = await AccessQueriesHandler.Handle(new ListPeople(null), sara, FakeTenant.Tehran(), directory, default);

        Assert.Equal("ADMIN_REQUIRED", decision.FailureDescriptor!.Identity.Code);
        Assert.Equal("ADMIN_REQUIRED", people.FailureDescriptor!.Identity.Code);
    }

    [Fact]
    public async Task People_are_shown_with_the_roles_of_the_platform_and_nothing_else_the_provider_keeps()
    {
        var people = (await AccessQueriesHandler.Handle(new ListPeople("istanbul"), ali, FakeTenant.Tehran(), directory, default)).Value;

        Assert.Equal(["tehran"], people.Items.Select(static p => p.City).Distinct());
        Assert.Equal(["customer"], people.Items.Single(static p => p.PersonId == FakeDirectory.Sara).Roles);
    }

    [Fact]
    public async Task A_Keycloak_outage_does_not_take_down_the_local_projection_but_blocks_identity_mutation()
    {
        directory.IsDown = true;

        var people = await AccessQueriesHandler.Handle(new ListPeople(null), ali, FakeTenant.Tehran(), directory, default);
        var decision = await Change(FakeDirectory.Sara, "courier");

        Assert.True(people.IsSuccess);
        Assert.NotEmpty(people.Value.Items);
        Assert.Equal("DIRECTORY_UNAVAILABLE", decision.FailureDescriptor!.Identity.Code);
        Assert.Empty(grants.All);
    }

    [Fact]
    public void Who_may_give_what()
    {
        Assert.Equal(["customer", "restaurant-manager", "courier"], Roles.GrantableBy(["city-admin"]));
        Assert.Equal(["customer", "restaurant-manager", "courier", "city-admin"], Roles.GrantableBy(["platform-admin"]));
        Assert.Empty(Roles.GrantableBy(["customer", "courier", "restaurant-manager"]));
        Assert.Equal(
            ["city-admin"],
            ((GrantableRoles)AccessQueriesHandler.Handle(new GetGrantableRoles(), nora).Value).Roles.Except(Roles.GrantableBy(["city-admin"])));
    }
}

public sealed class SettingsTests
{
    [Fact]
    public void Who_the_service_is_prints_no_secret()
    {
        var identity = new Tiffin.Access.Infrastructure.ServiceIdentity
        {
            Authority = new Uri("http://localhost:38180/realms/tiffin"), ClientId = "tiffin-access-service", ClientSecret = "a-secret-nobody-may-see"
        };

        Assert.DoesNotContain("a-secret-nobody-may-see", identity.ToString(), StringComparison.Ordinal);
    }
}
