namespace Tiffin.Payments.Api.Hosting;

/// <summary>The RabbitMQ queues this host listens on and answers to.</summary>
/// <remarks>
/// One queue per contract, named after the service that reads it and the contract: a <i>Point-to-Point
/// Channel</i> (Gregor Hohpe and Bobby Woolf, <i>Enterprise Integration Patterns</i>). The names are the
/// contract with the Ordering service, which declares the same names in its own code.
/// </remarks>
public static class PaymentsQueues
{
    public const string PaymentRequested = "tiffin.payments.payment-requested.v1";
    public const string RefundRequested = "tiffin.payments.refund-requested.v1";

    public const string PaymentAuthorized = "tiffin.ordering.payment-authorized.v1";
    public const string PaymentDeclined = "tiffin.ordering.payment-declined.v1";
    public const string PaymentRefunded = "tiffin.ordering.payment-refunded.v1";
}
