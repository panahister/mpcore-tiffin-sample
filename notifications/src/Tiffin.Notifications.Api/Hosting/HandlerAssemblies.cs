using System.Reflection;

namespace Tiffin.Notifications.Api.Hosting;

/// <summary>
/// The assemblies whose message handlers this host owns. Discovery is explicit: an assembly that is
/// not listed here is never scanned, and this host registers no catch-all handler or route policy.
/// Wolverine additionally discovers handlers in this host assembly itself, which this project owns;
/// Program.cs names it as <c>ApplicationAssembly</c> for that reason.
/// A handler class is discovered only when its name ends in <c>Handler</c> or <c>Consumer</c> (Wolverine's
/// convention). A message whose handler class is named otherwise has no route and is dropped.
/// </summary>
public static class HandlerAssemblies
{
    public static IReadOnlyList<Assembly> All { get; } =
    [
        Tiffin.Notifications.Application.AssemblyReference.Assembly,
    ];
}
