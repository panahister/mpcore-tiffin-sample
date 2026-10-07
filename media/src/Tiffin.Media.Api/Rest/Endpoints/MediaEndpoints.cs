using MPCore.Application.Results;
using MPCore.Transport.Http;
using Tiffin.Media.Application.Commands;
using Tiffin.Media.Application.Queries;
using Tiffin.Media.Application.Views;
using Wolverine;

namespace Tiffin.Media.Api.Rest.Endpoints;

/// <summary>The REST surface of the Media service.</summary>
/// <remarks>
/// <para>
/// Every endpoint binds the request, invokes one command or query through Wolverine, and renders the
/// <see cref="Result"/> with MP Core's <c>ToHttpResult</c>. A failure becomes RFC 9457 Problem Details
/// with the failure's stable code; no endpoint builds an error by hand.
/// </para>
/// <para>
/// No endpoint takes a file, and none gives one: the bytes travel between the app and the store, over an
/// address this service signed. Anybody who is signed in may announce a file; what it may be is decided
/// by its purpose.
/// </para>
/// </remarks>
public static class MediaEndpoints
{
    public static RouteGroupBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var media = endpoints.MapGroup("/v1/media").WithTags("Media");

        media.MapPost("/uploads", static async (ReserveUpload request, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<UploadTicket>>(request, ct).ConfigureAwait(false))
                .ToHttpResult(ticket => Results.Created($"/v1/media/{ticket.MediaId}", ticket)))
            .Produces<UploadTicket>(201)
            .WithName("ReserveUpload");

        media.MapPost("/{mediaId:guid}/confirm", static async (Guid mediaId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MediaView>>(new ConfirmUpload(mediaId), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<MediaView>(200)
            .WithName("ConfirmUpload");

        media.MapGet("/{mediaId:guid}", static async (Guid mediaId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MediaView>>(new GetMedia(mediaId), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<MediaView>(200)
            .WithName("GetMedia");

        media.MapDelete("/{mediaId:guid}", static async (Guid mediaId, IMessageBus bus, CancellationToken ct) =>
                (await bus.InvokeAsync<Result<MediaView>>(new DeleteMedia(mediaId), ct).ConfigureAwait(false)).ToHttpResult(Results.Ok))
            .Produces<MediaView>(200)
            .WithName("DeleteMedia");

        return media;
    }
}
