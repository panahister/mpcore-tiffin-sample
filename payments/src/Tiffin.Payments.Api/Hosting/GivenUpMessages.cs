using MPCore.Messaging.Abstractions;
using Tiffin.Payments.Application;
using Tiffin.Payments.Application.Contracts;
using Tiffin.Payments.Domain.Events;
using Wolverine;
using Wolverine.Runtime;

namespace Tiffin.Payments.Api.Hosting;

/// <summary>What the order is told when a request of it is given up and goes to the error queue.</summary>
/// <remarks>
/// <para>
/// A process manager (Gregor Hohpe and Bobby Woolf, <i>Enterprise Integration Patterns</i>) sends a request
/// and waits for the answer. A request that ends in the error queue answers nothing, and the order waits
/// for ever: the customer was told "accepted" and nobody is told anything else. So giving a request up is
/// itself an answer, sent here, where the host's error policy makes that decision.
/// </para>
/// <para>
/// The answer belongs to the city of the request, so it carries the request's tenant: it is published from
/// the error policy, outside any handler, where nothing else would name it.
/// </para>
/// <para>
/// The request stays in the error queue for an operator. Replaying it later charges the card of an order
/// that was cancelled meanwhile, and the order, which asks itself first, gives the money back.
/// </para>
/// </remarks>
public static class GivenUpMessages
{
    /// <summary>The description Wolverine shows for this action.</summary>
    public const string Description = "tell the order that its request was given up";

    /// <summary>The answer for a request that was given up, or null when nobody waits for one.</summary>
    public static object? AnswerFor(object? message, DateTimeOffset now) => message switch
    {
        PaymentRequested request => new PaymentDeclined(request.OrderId, DeclineCodes.ProviderUnavailable, now),
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
        if (HandlerTenantOf(lifecycle.Envelope!) is { } tenant)
        {
            delivery.Headers[MessageHeaders.TenantId] = tenant;
        }

        await new MessageBus(runtime).PublishAsync(answer, delivery).ConfigureAwait(false);
    }

    private static string? HandlerTenantOf(Envelope envelope) =>
        envelope.Headers.TryGetValue(MessageHeaders.TenantId, out var tenant) && !string.IsNullOrWhiteSpace(tenant) ? tenant : null;
}
