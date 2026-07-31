using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// Creates and upgrades the manuscript database schema.
/// </summary>
/// <remarks>
/// <para>
/// The schema is hand-written SQL rather than generated from the row types, because three things
/// the data model requires cannot be expressed with sqlite-net's attributes: <c>ON DELETE CASCADE</c>
/// foreign keys (FR-018, SC-008), composite uniqueness on
/// <c>DailyWritingRecord(ManuscriptId, Date)</c>, and the composite
/// <c>Chapter(ManuscriptId, OrderIndex)</c> index that keeps opening a 50-chapter manuscript cheap.
/// </para>
/// <para>
/// Version is tracked in SQLite's own <c>user_version</c> pragma, so a future migration is an
/// additional numbered step rather than a rewrite.
/// </para>
/// </remarks>
public static class DatabaseMigrator
{
    /// <summary>The schema version this build expects.</summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// Brings the database up to <see cref="CurrentVersion"/>. Safe to call on every open.
    /// </summary>
    /// <param name="connection">An open, keyed connection.</param>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public static async Task MigrateAsync(SQLiteAsyncConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();

        int version = await connection.ExecuteScalarAsync<int>("PRAGMA user_version").ConfigureAwait(false);

        if (version >= CurrentVersion)
        {
            return;
        }

        if (version < 1)
        {
            foreach (string statement in SchemaV1)
            {
                await connection.ExecuteAsync(statement).ConfigureAwait(false);
            }
        }

        await connection.ExecuteAsync($"PRAGMA user_version={CurrentVersion}").ConfigureAwait(false);
    }

    /// <summary>Every table this schema defines, in dependency order.</summary>
    public static IReadOnlyList<string> TableNames { get; } =
    [
        "Manuscript",
        "Chapter",
        "InlineImage",
        "Character",
        "PlotThread",
        "DailyGoal",
        "DailyWritingRecord",
    ];

    private static readonly string[] SchemaV1 =
    [
        """
        CREATE TABLE IF NOT EXISTS Manuscript (
            Id          TEXT    NOT NULL PRIMARY KEY,
            Title       TEXT    NOT NULL,
            CreatedAt   INTEGER NOT NULL,
            ModifiedAt  INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_Manuscript_ModifiedAt ON Manuscript(ModifiedAt DESC)",

        """
        CREATE TABLE IF NOT EXISTS Chapter (
            Id              TEXT    NOT NULL PRIMARY KEY,
            ManuscriptId    TEXT    NOT NULL REFERENCES Manuscript(Id) ON DELETE CASCADE,
            Title           TEXT    NOT NULL,
            ContentMarkdown TEXT    NOT NULL DEFAULT '',
            OrderIndex      INTEGER NOT NULL,
            WordCount       INTEGER NOT NULL DEFAULT 0,
            CreatedAt       INTEGER NOT NULL,
            ModifiedAt      INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_Chapter_Manuscript_Order ON Chapter(ManuscriptId, OrderIndex)",

        """
        CREATE TABLE IF NOT EXISTS InlineImage (
            Id          TEXT    NOT NULL PRIMARY KEY,
            ChapterId   TEXT    NOT NULL REFERENCES Chapter(Id) ON DELETE CASCADE,
            Bytes       BLOB    NOT NULL,
            MimeType    TEXT    NOT NULL,
            AltText     TEXT    NULL,
            ByteLength  INTEGER NOT NULL,
            CreatedAt   INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_InlineImage_Chapter ON InlineImage(ChapterId)",

        """
        CREATE TABLE IF NOT EXISTS Character (
            Id              TEXT    NOT NULL PRIMARY KEY,
            ManuscriptId    TEXT    NOT NULL REFERENCES Manuscript(Id) ON DELETE CASCADE,
            Name            TEXT    NOT NULL,
            Notes           TEXT    NOT NULL DEFAULT '',
            CreatedAt       INTEGER NOT NULL,
            ModifiedAt      INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_Character_Manuscript ON Character(ManuscriptId)",

        """
        CREATE TABLE IF NOT EXISTS PlotThread (
            Id              TEXT    NOT NULL PRIMARY KEY,
            ManuscriptId    TEXT    NOT NULL REFERENCES Manuscript(Id) ON DELETE CASCADE,
            Title           TEXT    NOT NULL,
            Notes           TEXT    NOT NULL DEFAULT '',
            CreatedAt       INTEGER NOT NULL,
            ModifiedAt      INTEGER NOT NULL
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_PlotThread_Manuscript ON PlotThread(ManuscriptId)",

        """
        CREATE TABLE IF NOT EXISTS DailyGoal (
            Id              TEXT    NOT NULL PRIMARY KEY,
            ManuscriptId    TEXT    NOT NULL UNIQUE REFERENCES Manuscript(Id) ON DELETE CASCADE,
            TargetWords     INTEGER NOT NULL,
            IsActive        INTEGER NOT NULL,
            CreatedAt       INTEGER NOT NULL,
            ModifiedAt      INTEGER NOT NULL
        )
        """,

        """
        CREATE TABLE IF NOT EXISTS DailyWritingRecord (
            Id              TEXT    NOT NULL PRIMARY KEY,
            ManuscriptId    TEXT    NOT NULL REFERENCES Manuscript(Id) ON DELETE CASCADE,
            Date            TEXT    NOT NULL,
            WordsWritten    INTEGER NOT NULL DEFAULT 0,
            GoalTarget      INTEGER NULL,
            GoalMet         INTEGER NOT NULL DEFAULT 0,
            UNIQUE(ManuscriptId, Date)
        )
        """,
        "CREATE INDEX IF NOT EXISTS IX_DailyWritingRecord_Manuscript_Date ON DailyWritingRecord(ManuscriptId, Date DESC)",
    ];
}
