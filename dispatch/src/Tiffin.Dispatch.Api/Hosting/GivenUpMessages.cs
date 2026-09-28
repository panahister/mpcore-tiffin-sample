using MPCore.Messaging.Abstractions;
using Tiffin.Dispatch.Application.Contracts;
using Tiffin.Dispatch.Domain.Events;
using Wolverine;
using Wolverine.Runtime;

namespace Tiffin.Dispatch.Api.Hosting;

/// <summary>What the order is told when its request for a courier is given up and goes to the error queue.</summary>
/// <remarks>
/// A request that ends in the error queue answers nothing, and the order would wait for ever with food
/// that gets cold. So giving the request up is itself an answer: nobody can carry it. The answer carries
/// the request's tenant, because it is published from the error policy, outside any handler.
/// </remarks>
public static class GivenUpMessages
{
    /// <summary>The description Wolverine shows for this action.</summary>
    public const string Description = "tell the order that nobody can carry it";

    /// <summary>The answer for a request that was given up, or null when nobody waits for one.</summary>
    public static object? AnswerFor(object? message, DateTimeOffset now) => message switch
    {
        CourierRequested request => new CourierUnavailable(request.OrderId, now),
        _ => null
    };

    /// <summary>Runs with the error policy's decision to give the message up.</summary>
    public static async ValueTask TellTheOrderAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (AnswerFor(lifecycle.Envelope?.Message, DateTimeOffset.UtcNow) is not { } answer)
        {
            return;
        }

        var delivery = new DeliveryOptions();
        if (lifecycle.Envelope!.Headers.TryGetValue(MessageHeaders.TenantId, out var tenant) && !string.IsNullOrWhiteSpace(tenant))
        {
            delivery.Headers[MessageHeaders.TenantId] = tenant;
        }

        await new MessageBus(runtime).PublishAsync(answer, delivery).ConfigureAwait(false);
    }
}
