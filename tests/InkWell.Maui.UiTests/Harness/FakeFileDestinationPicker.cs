using InkWell.Presentation.Services;

namespace InkWell.Maui.UiTests.Harness;

/// <summary>
/// Stands in for the platform's save dialog, so the export journey can be driven without one.
/// </summary>
/// <remarks>
/// It records every destination it was asked for and returns whatever the test chose, including
/// null for "the writer cancelled". That is the point: FR-017 turns on export writing only where
/// the writer pointed it, and this is what lets a test hold the app to that.
/// </remarks>
public sealed class FakeFileDestinationPicker : IFileDestinationPicker
{
    /// <summary>The path the next save dialog will return, or null to simulate cancelling.</summary>
    public string? NextSaveLocation { get; set; }

    /// <summary>The folder the next folder dialog will return, or null to simulate cancelling.</summary>
    public string? NextFolder { get; set; }

    /// <summary>Every file name the app suggested, in order.</summary>
    public List<string> SuggestedNames { get; } = [];

    /// <summary>How many times a folder was asked for.</summary>
    public int FolderPromptCount { get; private set; }

    /// <inheritdoc />
    public Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        SuggestedNames.Add(suggestedFileName);
        return Task.FromResult(NextSaveLocation);
    }

    /// <inheritdoc />
    public Task<string?> PickFolderAsync()
    {
        FolderPromptCount++;
        return Task.FromResult(NextFolder);
    }
}
