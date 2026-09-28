using Tiffin.Payments.Domain;

namespace Tiffin.Payments.Application.Ports;

/// <summary>The payments of one city.</summary>
public interface IPaymentRepository
{
    Task<Payment?> GetAsync(Guid id, string city, CancellationToken cancellationToken);

    Task<Payment?> OfOrderAsync(Guid orderId, string city, CancellationToken cancellationToken);

    void Add(Payment payment);
}

/// <summary>The payment provider.</summary>
public interface IPaymentGateway
{
    /// <summary>Charges the card. The key makes a repeat of the same request harmless at the provider.</summary>
    Task<GatewayResult> AuthorizeAsync(string idempotencyKey, decimal amount, string currency, string paymentToken, CancellationToken cancellationToken);

    Task<GatewayResult> RefundAsync(string idempotencyKey, string providerReference, decimal amount, CancellationToken cancellationToken);
}

public enum GatewayOutcome
{
    Approved,
    Declined,
    Unavailable
}

/// <summary>What the provider said. <see cref="Reference"/> is its reference when it approved, and its code when it declined.</summary>
public sealed record GatewayResult(GatewayOutcome Outcome, string? Reference)
{
    public static GatewayResult Unavailable { get; } = new(GatewayOutcome.Unavailable, null);

    public static GatewayResult Approved(string reference) => new(GatewayOutcome.Approved, reference);

    public static GatewayResult Declined(string code) => new(GatewayOutcome.Declined, code);
}
