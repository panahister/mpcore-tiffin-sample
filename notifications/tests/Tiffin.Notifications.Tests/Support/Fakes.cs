using MPCore.Application.Time;
using MPCore.Domain.Rules;
using MPCore.Messaging.Abstractions;
using MPCore.Persistence.Abstractions;
using MPCore.Security;
using MPCore.Tenancy;

namespace Tiffin.Notifications.Tests.Support;

// Hand-written fakes for the ports of MP Core. Every handler takes its collaborators as parameters, so a
// rule is tested in microseconds with no container, no database, no broker and no mocking library.

public sealed class FakeClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = now;

    public static FakeClock At2026() => new(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("A handler must not call SaveChangesAsync; MP Core's transaction middleware saves.");
}

/// <summary>The city of the work at hand: the token's in a request, the message's in a handler that runs from a queue.</summary>
public sealed class FakeTenant(string? city) : ITenantContext
{
    public string? TenantId { get; } = city;

    public static FakeTenant Tehran() => new("tehran");

    public static FakeTenant Istanbul() => new("istanbul");

    public static FakeTenant None() => new(null);
}

public sealed class FakeActor(CurrentActor current) : ICurrentActorAccessor
{
    public CurrentActor Current { get; } = current;

    public static FakeActor User(string subject, params string[] roles)
    {
        var builder = new CurrentActorBuilder(ActorKind.User) { SubjectId = subject, UserName = subject, DisplayName = subject };
        foreach (var role in roles)
        {
            builder.AddRole(role);
        }

        return new FakeActor(builder.Build());
    }

    public static FakeActor Anonymous() => new(CurrentActor.Anonymous);
}

/// <summary>Remembers what was published, and for which tenant when the publisher named one.</summary>
public sealed class RecordingPublisher : IMessagePublisher
{
    public List<object> Messages { get; } = [];

    public List<(object Message, MessageDeliveryContext Delivery)> Deliveries { get; } = [];

    public ValueTask PublishAsync(object message, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask PublishAsync(object message, MessageDeliveryContext delivery, CancellationToken cancellationToken = default)
    {
        Messages.Add(message);
        Deliveries.Add((message, delivery));
        return ValueTask.CompletedTask;
    }

    public T Single<T>() => Assert.Single(Messages.OfType<T>());

    public IReadOnlyList<string> Names => [.. Messages.Select(static m => m.GetType().Name)];
}

public static class Rules
{
    /// <summary>The code of the business rule the action breaks; fails the test when no rule breaks.</summary>
    public static string Broken(Action action) => Assert.Throws<BusinessRuleValidationException>(action).Rule.Code;

    public static async Task<string> BrokenAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<BusinessRuleValidationException>(action)).Rule.Code;
}
