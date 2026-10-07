using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MPCore.Application.Querying;
using Wolverine;
using Tiffin.Access.Api.Rest.Endpoints;
using Tiffin.Access.Application.Views;

namespace Tiffin.Access.Tests;

public sealed class EndpointContractTests
{
    [Theory]
    [InlineData("GetGrantableRoles", typeof(GrantableRoles), 200)]
    [InlineData("ListPeople", typeof(Page<PersonView>), 200)]
    [InlineData("GetPerson", typeof(PersonView), 200)]
    [InlineData("GiveRole", typeof(GrantView), 202)]
    [InlineData("TakeRole", typeof(GrantView), 202)]
    [InlineData("ListGrants", typeof(Page<GrantView>), 200)]
    [InlineData("GetGrant", typeof(GrantView), 200)]
    public async Task Real_endpoint_metadata_describes_its_existing_success_body_and_status(string operation, Type body, int status)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IMessageBus>(_ => throw new InvalidOperationException("Contract tests never invoke business operations."));
        await using var app = builder.Build();
        app.MapAccessEndpoints();
        var endpoint = Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints),
            e => e.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == operation);
        var produces = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(produces, metadata => metadata.StatusCode == status && metadata.Type == body &&
            (body == typeof(void) ? !metadata.ContentTypes.Any() : metadata.ContentTypes.Contains("application/json")));
    }
}
