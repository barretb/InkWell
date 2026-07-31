using CommunityToolkit.Maui;
using InkWell.Application.Abstractions;
using InkWell.Application.UseCases;
using InkWell.Infrastructure;
using InkWell.Infrastructure.Markdown;
using InkWell.Infrastructure.Persistence;
using InkWell.Infrastructure.Security;
using InkWell.Presentation.Services;
using InkWell.Presentation.ViewModels;
using InkWell.Maui.Views;
using Microsoft.Extensions.Logging;

namespace InkWell.Maui;

/// <summary>
/// The composition root. Every dependency is registered here and nowhere else, so the direction of
/// dependencies — presentation on application, application on domain, infrastructure plugged in at
/// the edge — is visible in one file (constitution §I).
/// </summary>
public static class MauiProgram
{
    /// <summary>Builds the application.</summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Platform adapters — the only places that touch a device API.
        builder.Services.AddSingleton<ISecureStore, MauiSecureStore>();
        builder.Services.AddSingleton<IAppStoragePaths, MauiAppStoragePaths>();
        builder.Services.AddSingleton<IClock, SystemClock>();

        // Infrastructure. The connection factory is a singleton because it owns the one encrypted
        // connection and the key cached behind it.
        builder.Services.AddSingleton<IKeyStore, KeyStore>();
        builder.Services.AddSingleton<ISqliteConnectionFactory, SqlCipherConnectionFactory>();
        builder.Services.AddSingleton<IMarkdownService, MarkdownService>();
        builder.Services.AddSingleton<IInlineImageRepository, InlineImageRepository>();
        builder.Services.AddSingleton<IManuscriptRepository, ManuscriptRepository>();
        builder.Services.AddSingleton<IChapterRepository, ChapterRepository>();
        builder.Services.AddSingleton<IDailyGoalRepository, DailyGoalRepository>();
        builder.Services.AddSingleton<IWritingHistoryRepository, WritingHistoryRepository>();
        builder.Services.AddSingleton<IReferenceRepository, ReferenceRepository>();

        // Application use cases.
        builder.Services.AddSingleton<ManuscriptUseCases>();
        builder.Services.AddSingleton<ChapterUseCases>();
        builder.Services.AddSingleton<GoalUseCases>();
        builder.Services.AddSingleton<ReferenceUseCases>();
        builder.Services.AddSingleton(AutoSaveOptions.Default);
        builder.Services.AddTransient<AutoSaveCoordinator>();

        // Presentation services.
        builder.Services.AddSingleton<INavigationService, ShellNavigationService>();
        builder.Services.AddSingleton<IConfirmationService, AlertConfirmationService>();
        builder.Services.AddSingleton<IErrorPresenter, AlertErrorPresenter>();

        // Screens. Transient so that reopening a chapter starts from a clean editor state rather
        // than inheriting the previous chapter's pending autosave.
        builder.Services.AddTransient<LibraryViewModel>();
        builder.Services.AddTransient<LibraryPage>();
        builder.Services.AddTransient<ManuscriptViewModel>();
        builder.Services.AddTransient<ManuscriptPage>();
        builder.Services.AddTransient<EditorViewModel>();
        builder.Services.AddTransient<EditorPage>();
        builder.Services.AddTransient<GoalsViewModel>();
        builder.Services.AddTransient<GoalsPage>();
        builder.Services.AddTransient<CharactersViewModel>();
        builder.Services.AddTransient<CharactersPage>();
        builder.Services.AddTransient<PlotThreadsViewModel>();
        builder.Services.AddTransient<PlotThreadsPage>();

        return builder.Build();
    }
}
