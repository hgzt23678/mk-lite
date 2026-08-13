#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
frontend_root="$repository_root/frontend/ActivityPub.Misskey.Blazor"

classify_source() {
    local path="$1"

    case "$path" in
        App.razor|MisskeyFrontendServiceCollectionExtensions.cs|Localization/MisskeyFrontendLocalizationMiddleware.cs|Localization/MisskeyLocaleRequestResolver.cs|Localization/MisskeyLocalizer.cs|Security/FrontendCspNonce.cs)
            printf 'host-only'
            ;;
        Client/MisskeyClientModuleUtilities.cs|Identity/AuthenticatedActorContext.cs|Streaming/NotificationSubscriptionService.cs|Streaming/RelationshipSubscriptionService.cs|Streaming/ServerTimelineStream.cs|Presentation/AboutPresentationService.cs|Presentation/AdminPresentationService.cs|Presentation/AnnouncementPagePresentationService.cs|Presentation/AnnouncementPresentationService.cs|Presentation/AutocompletePresentationService.cs|Presentation/AvatarsPresentationService.cs|Presentation/ComposerMediaService.cs|Presentation/CurrentAccountPresentationService.cs|Presentation/HashtagTrendPresentationService.cs|Presentation/InstancePresentationService.cs|Presentation/NoteDeletionPresentationService.cs|Presentation/NotificationPresentationService.cs|Presentation/ReactionDetailsPresentationService.cs|Presentation/RenoteDetailsPresentationService.cs|Presentation/SettingsPresentationService.cs|Presentation/TimelinePresentationService.cs|Presentation/UserFollowRelationsPresentationService.cs|Presentation/UserPreviewPresentationService.cs|Presentation/UserSearchPresentationService.cs|Presentation/VisibleUsersPresentationService.cs)
            printf 'mixed-contract-and-server'
            ;;
        _Imports.razor|Client/MisskeyClientScriptUtilities.cs|Components/MkPoll.razor|Components/TimelineView.razor|Overlays/IMisskeyOverlayService.cs|Pages/V12/MiauthSession.razor|Presentation/MentionNotePaginationSource.cs|Presentation/TimelineModels.cs)
            printf 'browser-refactor-required'
            ;;
        Components/CalendarWidgetSnapshot.cs|Components/EmojiPickerChosenEvent.cs|Components/InstanceTickerViewModel.cs|Components/MediaCaptionModels.cs|Components/MisskeyDialogModels.cs|Components/MisskeyFormDialogModels.cs|Components/MisskeyFormRadiosContext.cs|Components/MisskeyWidgetModels.cs|Components/MkAnalogClockModels.cs|Components/MkFormSelectItem.cs|Components/MkModalModels.cs|Components/MkModalPageWindowModels.cs|Components/MkPageHeaderModels.cs|Components/MkPagePreviewModels.cs|Components/MkSuperMenuModels.cs|Components/MkTabModels.cs|Components/MkTokenGenerateWindowModels.cs|Components/MkWindowModels.cs|Components/NotificationSettingResult.cs|Components/StickyOffsetContext.cs|Presentation/ComposerModels.cs|Presentation/EmojiPickerModels.cs|Presentation/MisskeyFrontendRuntimeConfiguration.cs|Presentation/MisskeyPaginationModels.cs|Presentation/VisitorAnnouncementViewModel.cs)
            printf 'shared-ui-contract'
            ;;
        *)
            printf 'browser-safe-source'
            ;;
    esac
}

classify_asset() {
    local path="$1"

    case "$path" in
        wwwroot/manifest.webmanifest|wwwroot/service-worker.js|wwwroot/service-worker.published.js|wwwroot/js/register-service-worker.js)
            printf 'browser-asset-refactor-required'
            ;;
        *)
            printf 'browser-safe-asset'
            ;;
    esac
}

while IFS= read -r -d '' file; do
    relative="${file#"$frontend_root/"}"
    printf '%s\t%s\n' "$(classify_source "$relative")" "$relative"
done < <(find "$frontend_root" -type f \( -name '*.cs' -o -name '*.razor' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' -print0 | sort -z)

while IFS= read -r -d '' file; do
    relative="${file#"$frontend_root/"}"
    printf '%s\t%s\n' "$(classify_asset "$relative")" "$relative"
done < <(find "$frontend_root" -type f \
    \( -path "$frontend_root/wwwroot/*" -o -name '*.razor.css' \) \
    -not -path '*/bin/*' -not -path '*/obj/*' -print0 | sort -z)
