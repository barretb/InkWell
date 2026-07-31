namespace InkWell.Presentation;

/// <summary>
/// Navigation route names and the query parameters they take, in one place so a typo is a build
/// error at the call site rather than a silent no-op at run time.
/// </summary>
public static class Routes
{
    /// <summary>The library of manuscripts; the app's root.</summary>
    public const string Library = "//library";

    /// <summary>One manuscript and its chapters.</summary>
    public const string Manuscript = "manuscript";

    /// <summary>The chapter editor.</summary>
    public const string Editor = "editor";

    /// <summary>The daily word-count goal and writing history for a manuscript.</summary>
    public const string Goals = "goals";

    /// <summary>Character profiles for a manuscript.</summary>
    public const string Characters = "characters";

    /// <summary>Plot threads for a manuscript.</summary>
    public const string PlotThreads = "plotthreads";

    /// <summary>Query parameter carrying a manuscript identifier.</summary>
    public const string ManuscriptIdParameter = "manuscriptId";

    /// <summary>Query parameter carrying a chapter identifier.</summary>
    public const string ChapterIdParameter = "chapterId";
}
