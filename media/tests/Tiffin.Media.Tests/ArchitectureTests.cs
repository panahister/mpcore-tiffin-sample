using System.Reflection;
using NetArchTest.Rules;

namespace Tiffin.Media.Tests;

/// <summary>
/// The layers of the Media service, held by tests. The direction of dependencies is the Dependency Rule of
/// Robert C. Martin's "Clean Architecture" (2017), and the ports are Alistair Cockburn's "Hexagonal
/// Architecture" (2005): the domain and the application know ports, never a provider. The project
/// references already forbid most of it; these tests guard what the compiler cannot see.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Tiffin.Media.Domain.AssemblyReference).Assembly;
    private static readonly Assembly Application = typeof(Tiffin.Media.Application.AssemblyReference).Assembly;

    private static readonly string[] Providers =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Wolverine", "Confluent.Kafka", "Microsoft.AspNetCore", "Grpc"];

    [Fact]
    public void The_domain_and_the_application_name_no_provider()
    {
        foreach (var assembly in new[] { Domain, Application })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(Providers).GetResult();
            Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
        }
    }

    [Fact]
    public void The_domain_knows_nothing_of_the_layers_around_it()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("Tiffin.Media.Application", "Tiffin.Media.Infrastructure", "Tiffin.Media.Api").GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_application_knows_neither_the_adapters_nor_the_host()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("Tiffin.Media.Infrastructure", "Tiffin.Media.Api").GetResult();
        Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void The_service_shares_no_assembly_with_another_service()
    {
        // A service is deployed alone. What two services agree on is a message or a contract file, stated
        // by each of them in its own words, never a shared assembly (tests/Tiffin.Contracts.Tests).
        var others = new[] { "Access", "Media", "Restaurants", "Ordering", "Payments", "Kitchen", "Dispatch", "Tracking", "Notifications" }
            .Where(static other => other != "Media").Select(static other => "Tiffin." + other).ToArray();
        foreach (var assembly in new[] { Domain, Application, typeof(Program).Assembly, typeof(Tiffin.Media.Infrastructure.DependencyInjection).Assembly })
        {
            var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(others).GetResult();
            Assert.True(result.IsSuccessful, string.Join(", ", result.FailingTypeNames ?? []));
        }
    }
}
