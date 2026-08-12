using ActivityPub.Application;
using ActivityPub.Domain;
using ActivityPub.Misskey.Blazor.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor.Presentation;

public interface IComposerMediaService
{
    Task<ComposerMediaViewModel> UploadAsync(
        string fileName,
        string? declaredMediaType,
        Stream content,
        CancellationToken cancellationToken);
}

public sealed class ComposerMediaService(
    IServiceProvider services,
    IAuthenticatedActorContext actorContext) : IComposerMediaService
{
    public async Task<ComposerMediaViewModel> UploadAsync(
        string fileName,
        string? declaredMediaType,
        Stream content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        IMediaService mediaService = services.GetService<IMediaService>() ??
            throw new ComposerMediaUnavailableException();
        AuthenticatedActor actor = await actorContext.RequireAsync(cancellationToken).ConfigureAwait(false);

        // An unattached upload must never be publicly retrievable. DeliveryRepository changes it
        // to the note visibility in the same transaction that creates the attachment relation.
        MediaUploadResult result = await mediaService.UploadAsync(
            new MediaUploadCommand(
                actor.ActorIri,
                fileName,
                declaredMediaType,
                Visibility.MentionedOnly,
                content),
            cancellationToken).ConfigureAwait(false);
        string servedUrl = $"/media/{result.Id:D}";
        return new(
            result.Id,
            fileName,
            result.MediaType,
            servedUrl,
            servedUrl,
            Sensitive: false,
            Description: null,
            result.Width,
            result.Height,
            result.Length);
    }
}

public sealed record ComposerMediaViewModel(
    Guid Id,
    string Name,
    string MediaType,
    string Url,
    string PreviewUrl,
    bool Sensitive,
    string? Description,
    int? Width,
    int? Height,
    long? Size = null);

public sealed class ComposerMediaUnavailableException : Exception
{
    public ComposerMediaUnavailableException()
        : base("The configured media pipeline is unavailable.")
    {
    }
}
