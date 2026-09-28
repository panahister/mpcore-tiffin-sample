using MPCore.Domain.Model;

namespace Tiffin.Notifications.Domain;

/// <summary>Something that happened to somebody's order, kept for them until they have read it.</summary>
/// <remarks>
/// <para>
/// <b>A notification is kept as what happened, not as a sentence.</b> It holds the key of a message and
/// the values that go into it. The sentence is made when it is read, in the language of whoever reads: a
/// customer who orders in Chinese today and reads in English tomorrow is told in English. A sentence that
/// was stored could be read in one language only, and corrected in none.
/// </para>
/// <para>
/// <b>One notification per event, keyed by the event.</b> The same event read twice from the stream finds
/// its notification and adds nothing: the <i>Idempotent Receiver</i> of Gregor Hohpe and Bobby Woolf
/// (<i>Enterprise Integration Patterns</i>).
/// </para>
/// </remarks>
public sealed class Notification : AggregateRoot<Guid>
{
    private readonly Dictionary<string, string> arguments = [];

    private Notification()
    {
        City = string.Empty;
        RecipientId = string.Empty;
        MessageKey = string.Empty;
    }

    private Notification(
        Guid eventId, string city, string recipientId, string messageKey, IReadOnlyDictionary<string, string> messageArguments, Guid orderId,
        DateTimeOffset occurredOnUtc)
        : base(eventId)
    {
        City = city;
        RecipientId = recipientId;
        MessageKey = messageKey;
        OrderId = orderId;
        OccurredOnUtc = occurredOnUtc;
        foreach (var (name, value) in messageArguments)
        {
            arguments[name] = value;
        }
    }

    /// <summary>The tenant.</summary>
    public string City { get; private set; }

    /// <summary>The subject of the tokens of whoever it is for.</summary>
    public string RecipientId { get; private set; }

    public string MessageKey { get; private set; }

    public IReadOnlyDictionary<string, string> Arguments => arguments;

    public Guid OrderId { get; private set; }

    public DateTimeOffset OccurredOnUtc { get; private set; }

    public DateTimeOffset? ReadOnUtc { get; private set; }

    public static Notification About(
        Guid eventId, string city, string recipientId, string messageKey, IReadOnlyDictionary<string, string> arguments, Guid orderId,
        DateTimeOffset occurredOnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageKey);
        ArgumentNullException.ThrowIfNull(arguments);
        return new Notification(eventId, city, recipientId, messageKey, arguments, orderId, occurredOnUtc);
    }

    /// <summary>Read once: reading it again does not make it newer.</summary>
    public void MarkRead(DateTimeOffset now) => ReadOnUtc ??= now;

    public bool IsFor(string subjectId) => string.Equals(RecipientId, subjectId, StringComparison.Ordinal);
}
