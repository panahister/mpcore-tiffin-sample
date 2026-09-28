using MPCore.Messaging.Abstractions;
using Tiffin.Access.Application.Commands;
using Wolverine;
using Wolverine.Runtime;

namespace Tiffin.Access.Api.Hosting;

/// <summary>What becomes of a decision when the step that tells the identity provider is given up.</summary>
/// <remarks>
/// The admin was told "accepted" and looks at the decision to see what became of it. A step that ends in
/// the error queue would leave it "requested" for ever, so giving the step up marks the decision failed.
/// The step stays in the error queue; replaying it applies the decision after all.
/// </remarks>
public static class GivenUpMessages
{
    /// <summary>The description Wolverine shows for this action.</summary>
    public const string Description = "mark the decision failed";

    /// <summary>Runs with the error policy's decision to give the message up.</summary>
    public static async ValueTask MarkTheDecisionFailedAsync(IWolverineRuntime runtime, IEnvelopeLifecycle lifecycle, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(lifecycle);
        ArgumentNullException.ThrowIfNull(exception);

        if (lifecycle.Envelope?.Message is not ApplyGrant step)
        {
            return;
        }

        var delivery = new DeliveryOptions();
        if (lifecycle.Envelope.Headers.TryGetValue(MessageHeaders.TenantId, out var tenant) && !string.IsNullOrWhiteSpace(tenant))
        {
            delivery.Headers[MessageHeaders.TenantId] = tenant;
        }

        await new MessageBus(runtime).PublishAsync(new GrantGivenUp(step.GrantId, "The identity provider could not be told."), delivery).ConfigureAwait(false);
    }
}
