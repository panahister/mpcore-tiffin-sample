using MPCore.Domain.Model;

namespace Tiffin.Ordering.Domain;

/// <summary>Where the food goes, and whom the courier asks for. Equal by value.</summary>
public sealed class DeliveryAddress : ValueObject
{
    private DeliveryAddress()
    {
        Recipient = string.Empty;
        Phone = string.Empty;
        District = string.Empty;
        Line = string.Empty;
    }

    public DeliveryAddress(string recipient, string phone, string district, string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(phone);
        ArgumentException.ThrowIfNullOrWhiteSpace(district);
        ArgumentException.ThrowIfNullOrWhiteSpace(line);
        Recipient = recipient.Trim();
        Phone = phone.Trim();
        District = district.Trim();
        Line = line.Trim();
    }

    public string Recipient { get; private set; }

    public string Phone { get; private set; }

    public string District { get; private set; }

    public string Line { get; private set; }

    /// <summary>What a log may show of an address: the district, never the person or the door.</summary>
    public override string ToString() => $"{nameof(DeliveryAddress)} {{ District = {District} }}";

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Recipient;
        yield return Phone;
        yield return District;
        yield return Line;
    }
}
