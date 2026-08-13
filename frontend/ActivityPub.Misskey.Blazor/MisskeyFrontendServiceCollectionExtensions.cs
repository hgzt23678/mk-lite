using ActivityPub.Misskey.Blazor.BrowserInterop;
using ActivityPub.Misskey.Blazor.Client;
using ActivityPub.Misskey.Blazor.Identity;
using ActivityPub.Misskey.Blazor.Localization;
using ActivityPub.Misskey.Blazor.Overlays;
using ActivityPub.Misskey.Blazor.Presentation;
using ActivityPub.Misskey.Blazor.Routing;
using ActivityPub.Misskey.Blazor.State;
using ActivityPub.Misskey.Blazor.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace ActivityPub.Misskey.Blazor;

public static class MisskeyFrontendServiceCollectionExtensions
{
    public static IServiceCollection AddMisskeyBlazorFrontend(
        this IServiceCollection services,
        MisskeyFrontendRuntimeConfiguration? runtimeConfiguration = null,
        MisskeyFrontendRouteAssemblies? routeAssemblies = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        runtimeConfiguration ??= MisskeyFrontendRuntimeConfiguration.Default;
        routeAssemblies ??= MisskeyFrontendRouteAssemblies.Empty;
        if (string.IsNullOrWhiteSpace(runtimeConfiguration.Version) ||
            runtimeConfiguration.Version.Length > 128 ||
            runtimeConfiguration.Version.Any(char.IsControl) ||
            runtimeConfiguration.SourceUrl is { IsAbsoluteUri: false } ||
            runtimeConfiguration.PublicBaseUri is { IsAbsoluteUri: false })
        {
            throw new ArgumentException("The Misskey frontend runtime configuration is invalid.", nameof(runtimeConfiguration));
        }

        services.AddHttpContextAccessor();
        services.AddCascadingAuthenticationState();
        services.AddSingleton(runtimeConfiguration);
        services.AddSingleton(routeAssemblies);
        services.AddSingleton<IMisskeyLocaleCatalog, MisskeyLocaleCatalog>();
        services.AddSingleton<MisskeyLocaleRequestResolver>();
        services.AddScoped<IMisskeyLocalizer, MisskeyLocalizer>();
        services.AddScoped<IClientStorage, BrowserStorage>();
        services.AddScoped<IMisskeyIndexedStorage, MisskeyIndexedStorage>();
        services.AddScoped<IMisskeyAccountState, MisskeyAccountState>();
        services.AddSingleton<IThemeCatalog, ThemeCatalog>();
        services.AddSingleton<IEmojiCatalog, EmojiCatalog>();
        services.AddScoped<IThemeInterop, ThemeInterop>();
        services.AddScoped<IThemeDeviceInterop, ThemeDeviceInterop>();
        services.AddScoped<ICustomCssInterop, CustomCssInterop>();
        services.AddScoped<ISettingsGeneralInterop, SettingsGeneralInterop>();
        services.AddScoped<IPizzaxDeviceState, PizzaxDeviceState>();
        services.AddScoped<IAuthenticatedActorContext, AuthenticatedActorContext>();
        services.AddScoped<IMiauthAuthorizationService, ActivityPub.Misskey.Blazor.Server.ServerMiauthAuthorizationService>();
        services.AddScoped<IPageMetadataState, PageMetadataState>();
        services.AddScoped<IButtonRippleInterop, ButtonRippleInterop>();
        services.AddScoped<IRippleEffectInterop, RippleEffectInterop>();
        services.AddScoped<IClipboardInterop, ClipboardInterop>();
        services.AddScoped<IGoogleSearchInterop, GoogleSearchInterop>();
        services.AddScoped<IPrismSyntaxHighlightInterop, PrismSyntaxHighlightInterop>();
        services.AddScoped<IKatexFormulaInterop, KatexFormulaInterop>();
        services.AddScoped<IErrorAppearInterop, ErrorAppearInterop>();
        services.AddScoped<IPaginationInterop, PaginationInterop>();
        services.AddScoped<IDateSeparatedListInterop, DateSeparatedListInterop>();
        services.AddScoped<IFormSuspenseInterop, FormSuspenseInterop>();
        services.AddScoped<INotePageInterop, NotePageInterop>();
        services.AddScoped<IViewportInterop, ViewportInterop>();
        services.AddScoped<INavbarInterop, NavbarInterop>();
        services.AddScoped<IUniversalShellInterop, UniversalShellInterop>();
        services.AddScoped<IVisitorShellInterop, VisitorShellInterop>();
        services.AddScoped<IWelcomeTimelineInterop, WelcomeTimelineInterop>();
        services.AddScoped<IElementSizeInterop, ElementSizeInterop>();
        services.AddScoped<INoteViewInterop, NoteViewInterop>();
        services.AddScoped<INoteDetailedInterop, NoteDetailedInterop>();
        services.AddScoped<ISpacerInterop, SpacerInterop>();
        services.AddScoped<IFormInputInterop, FormInputInterop>();
        services.AddScoped<IFormRangeInterop, FormRangeInterop>();
        services.AddScoped<IAuthenticationFormInterop, AuthenticationFormInterop>();
        services.AddScoped<ICaptchaInterop, CaptchaInterop>();
        services.AddScoped<IPasswordResetFormInterop, PasswordResetFormInterop>();
        services.AddScoped<IDialogWindowInterop, DialogWindowInterop>();
        services.AddScoped<ISuccessFeedbackInterop, SuccessFeedbackInterop>();
        services.AddScoped<IPageHeaderInterop, PageHeaderInterop>();
        services.AddScoped<IStickyContainerInterop, StickyContainerInterop>();
        services.AddScoped<IContainerInterop, ContainerInterop>();
        services.AddScoped<IFolderInterop, FolderInterop>();
        services.AddScoped<IMisskeyLocaleInterop, MisskeyLocaleInterop>();
        services.AddScoped<IAboutMisskeyPhysicsInterop, AboutMisskeyPhysicsInterop>();
        services.AddScoped<IMarqueeInterop, MarqueeInterop>();
        services.AddScoped<ITimeInterop, TimeInterop>();
        services.AddScoped<IDigitalClockInterop, DigitalClockInterop>();
        services.AddScoped<ICalendarWidgetInterop, CalendarWidgetInterop>();
        services.AddScoped<IWidgetsInterop, WidgetsInterop>();
        services.AddScoped<IAnalogClockInterop, AnalogClockInterop>();
        services.AddScoped<IBlurhashImageInterop, BlurhashImageInterop>();
        services.AddScoped<IMediaElementInterop, MediaElementInterop>();
        services.AddScoped<IMediaGalleryInterop, MediaGalleryInterop>();
        services.AddScoped<IImageViewerInterop, ImageViewerInterop>();
        services.AddScoped<IMediaCaptionInterop, MediaCaptionInterop>();
        services.AddScoped<IModalPageWindowInterop, ModalPageWindowInterop>();
        services.AddScoped<IMkWindowInterop, MkWindowInterop>();
        services.AddScoped<IToastInterop, ToastInterop>();
        services.AddScoped<ISparkleInterop, SparkleInterop>();
        services.AddScoped<IClientUpdateInterop, ClientUpdateInterop>();
        services.AddScoped<IMkModalInterop, MkModalInterop>();
        services.AddScoped<IModalInterop, ModalInterop>();
        services.AddScoped<IMenuInterop, MenuInterop>();
        services.AddScoped<IContextMenuInterop, ContextMenuInterop>();
        services.AddScoped<ITagCloudInterop, TagCloudInterop>();
        services.AddScoped<IUnixClockInterop, UnixClockInterop>();
        services.AddScoped<IPostFormDialogInterop, PostFormDialogInterop>();
        services.AddScoped<IVisibilityPickerInterop, VisibilityPickerInterop>();
        services.AddScoped<IVisibilityTooltipInterop, VisibilityTooltipInterop>();
        services.AddScoped<IReactionViewerInterop, ReactionViewerInterop>();
        services.AddScoped<IRenoteButtonInterop, RenoteButtonInterop>();
        services.AddScoped<INotificationInterop, NotificationInterop>();
        services.AddScoped<INotificationsInterop, NotificationsInterop>();
        services.AddScoped<INotificationToastInterop, NotificationToastInterop>();
        services.AddScoped<IStreamIndicatorInterop, StreamIndicatorInterop>();
        services.AddScoped<IUserPreviewInterop, UserPreviewInterop>();
        services.AddScoped<IEmojiPickerDialogInterop, EmojiPickerDialogInterop>();
        services.AddScoped<IEmojiPickerInterop, EmojiPickerInterop>();
        services.AddScoped<IPostFormInterop, PostFormInterop>();
        services.AddScoped<IAutocompleteInterop, AutocompleteInterop>();
        services.AddScoped<IAutocompletePresentationService, AutocompletePresentationService>();
        services.AddScoped<IHashtagTrendPresentationService, HashtagTrendPresentationService>();
        services.AddScoped<ISettingsPresentationService, SettingsPresentationService>();
        services.AddScoped<IAdminPresentationService, AdminPresentationService>();
        services.AddScoped<IPostFormAttachesInterop, PostFormAttachesInterop>();
        services.AddScoped<IMfmParserInterop, MfmParserInterop>();
        services.AddScoped<IMisskeyOverlayService, MisskeyOverlayService>();
        services.AddScoped<IMisskeyTransientFeedbackService, MisskeyTransientFeedbackService>();
        services.AddScoped<IInstancePresentationService, InstancePresentationService>();
        services.AddScoped<IAboutPresentationService, AboutPresentationService>();
        services.AddScoped<IAnnouncementPresentationService, AnnouncementPresentationService>();
        services.AddScoped<IAnnouncementPagePresentationService, AnnouncementPagePresentationService>();
        services.AddScoped<ICurrentAccountPresentationService, CurrentAccountPresentationService>();
        services.AddScoped<TimelinePresentationService>();
        services.AddScoped<IUserPagePresentationService, UserPagePresentationService>();
        services.AddScoped<IUserFollowRelationsPresentationService, UserFollowRelationsPresentationService>();
        services.AddScoped<ITimelinePresentationService>(provider =>
            provider.GetRequiredService<TimelinePresentationService>());
        services.AddScoped<INotePagePresentationService>(provider =>
            provider.GetRequiredService<TimelinePresentationService>());
        services.AddScoped<INoteDeletionPresentationService, NoteDeletionPresentationService>();
        services.AddScoped<IVisibleUsersPresentationService, VisibleUsersPresentationService>();
        services.AddScoped<IAvatarsPresentationService, AvatarsPresentationService>();
        services.AddScoped<IReactionDetailsPresentationService, ReactionDetailsPresentationService>();
        services.AddScoped<IRenoteDetailsPresentationService, RenoteDetailsPresentationService>();
        services.AddScoped<INotificationPresentationService, NotificationPresentationService>();
        services.AddScoped<IUserPreviewPresentationService, UserPreviewPresentationService>();
        services.AddScoped<IUserSearchPresentationService, UserSearchPresentationService>();
        services.AddScoped<IComposerMediaService, ComposerMediaService>();
        services.AddScoped<IMisskeyStreamConnectionStatus, MisskeyStreamConnectionStatus>();
        services.AddScoped<ITimelineSubscriptionService, TimelineSubscriptionService>();
        services.AddScoped<IRelationshipSubscriptionService, RelationshipSubscriptionService>();
        services.AddScoped<INotificationSubscriptionService, NotificationSubscriptionService>();
        return services;
    }
}
