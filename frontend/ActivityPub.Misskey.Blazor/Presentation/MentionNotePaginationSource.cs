namespace ActivityPub.Misskey.Blazor.Presentation;

public sealed record MentionNoteListItem(
    string NotificationId,
    NoteViewModel Note);

public sealed class MentionNotePaginationSource(
    INotificationPresentationService notifications,
    bool directOnly = false) : IMisskeyPaginationSource<MentionNoteListItem>
{
    private const int ScanBatchSize = 100;
    private static readonly HashSet<MisskeyNotificationType> MentionTypes =
        new HashSet<MisskeyNotificationType>
        {
            MisskeyNotificationType.Mention,
            MisskeyNotificationType.Reply,
            MisskeyNotificationType.Quote
        };

    public MisskeyPaginationOptions Options { get; } = new(10);

    public async ValueTask<IReadOnlyList<MentionNoteListItem>> FetchAsync(
        MisskeyPaginationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Offset is not null || request.SinceId is not null)
        {
            throw new NotificationPresentationException("MENTION_NOTE_PAGINATION_DIRECTION_UNSUPPORTED");
        }

        int requestedLimit = Math.Clamp(request.Limit, 1, 100);
        var result = new List<MentionNoteListItem>(requestedLimit);
        string? cursor = request.UntilId;
        while (result.Count < requestedLimit)
        {
            int batchLimit = directOnly ? ScanBatchSize : requestedLimit - result.Count;
            IReadOnlyList<NotificationViewModel> batch = await notifications.ReadAsync(
                new(
                    cursor,
                    batchLimit,
                    IncludeTypes: MentionTypes),
                cancellationToken).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            foreach (NotificationViewModel notification in batch)
            {
                if (!MentionTypes.Contains(notification.Type))
                {
                    continue;
                }

                NoteViewModel note = notification.FullNote ??
                    throw new NotificationPresentationException("NOTIFICATION_NOTE_PROJECTION_UNAVAILABLE");
                if (!directOnly || note.Visibility == ActivityPub.Domain.Visibility.MentionedOnly)
                {
                    result.Add(new(notification.Id, note));
                    if (result.Count == requestedLimit)
                    {
                        break;
                    }
                }
            }

            string nextCursor = batch[^1].Id;
            if (batch.Count < batchLimit || string.Equals(cursor, nextCursor, StringComparison.Ordinal))
            {
                break;
            }

            cursor = nextCursor;
        }

        return result;
    }

    public string GetId(MentionNoteListItem item) => item.NotificationId;
}
