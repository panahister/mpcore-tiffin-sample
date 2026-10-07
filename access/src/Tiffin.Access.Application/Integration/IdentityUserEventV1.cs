using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MPCore.Domain.Events;

namespace Tiffin.Access.Application.Integration;

/// <summary>
/// The privacy-minimal Keycloak CloudEvent. It carries the immutable subject and event kind only;
/// the anti-corruption layer reads the current profile from Keycloak when a projection must change.
/// </summary>
public sealed record IdentityUserEventV1(
    [property: JsonPropertyName("specversion")] string SpecVersion,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("time")] DateTimeOffset Time,
    [property: JsonPropertyName("datacontenttype")] string DataContentType,
    [property: JsonPropertyName("dataschema")] string DataSchema,
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("data")] IdentityUserEventData Data) : IIntegrationEvent
{
    // Keycloak CloudEvent ids are opaque strings, while MP Core's transactional inbox is keyed by a
    // Guid. A deterministic digest keeps every redelivery on the same inbox key without weakening or
    // rewriting the owner contract's original id, which remains available as Id.
    [JsonIgnore]
    public Guid EventId => StableEventId(Id);

    [JsonIgnore]
    public DateTimeOffset OccurredOnUtc => Data.OccurredAt;

    [JsonIgnore]
    public string EventName => Type;

    [JsonIgnore]
    public int EventVersion => 1;

    [JsonIgnore]
    public string? CorrelationId => null;

    [JsonIgnore]
    public string? CausationId => null;

    internal static Guid StableEventId(string cloudEventId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cloudEventId);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(cloudEventId)).AsSpan(0, 16));
    }
}

public sealed record IdentityUserEventData(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("realmId")] string RealmId,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("clientId")] string? ClientId,
    [property: JsonPropertyName("sessionId")] string? SessionId,
    [property: JsonPropertyName("keycloakEventType")] string KeycloakEventType,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("capturedAt")] DateTimeOffset CapturedAt);
