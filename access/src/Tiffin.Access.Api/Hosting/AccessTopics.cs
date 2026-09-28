namespace Tiffin.Access.Api.Hosting;

/// <summary>The Kafka topics this host writes: what happened to somebody's roles, for whoever wants to know.</summary>
public static class AccessTopics
{
    public const string RoleChanged = "tiffin.access.role-changed.v1";
}
