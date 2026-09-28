using Tiffin.Ordering.Application.Contracts;
using Wolverine;
using Wolverine.Runtime;

namespace Tiffin.Ordering.Api.Hosting;

/// <summary>What an operator is told when an answer to the order process is given up.</summary>
/// <remarks>
/// <para>
/// An answer that ends in the error queue leaves its order where it was: paid and waiting for a restaurant
/// that has already answered, for example. Nothing in this host can finish such an order by itself,
/// because the answer that was given up is the only one that says what happened. So the host says it
/// loudly, once, with the order's identity, and the message stays in the error queue to be replayed when
/// the cause is removed. Replaying is harmless: every handler of the process asks the order first.
/// </para>
/// </remarks>
public static class GivenUpMessages
{
    /// <summary>The description Wolverine shows for this action.</summary>
    public const string Description = "say which order waits for an answer that was given up";

    /// <summary>The order an answer belongs to, or null for a message that is not an answer.</summary>
    public static Guid? OrderOf(object? message) => message switch
    {
        PaymentAuthorized m => m.OrderId,
        PaymentDeclined m => m.OrderId,
        PaymentRefunded m => m.OrderId,
        KitchenAccepted m => m.OrderId,
        KitchenRejected m => m.OrderId,
        CourierAssigned m => m.OrderId,
        CourierUnavailable m => m.OrderId,
        DeliveryCompleted m => m.OrderId,
        _ => null
    };

    /// <summary>Runs with the error policy's decision to give the message up.</summary>
    public static ValueTask SayWhichOrderWaitsAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(lifecycle);

        if (OrderOf(lifecycle.Envelope?.Message) is { } orderId)
        {
            runtime.LoggerFactory.CreateLogger(typeof(GivenUpMessages)).LogError(
                exception, "Order {OrderId} waits: {Answer} was given up and is in the error queue", orderId, lifecycle.Envelope!.Message!.GetType().Name);
        }

        return ValueTask.CompletedTask;
    }
}
