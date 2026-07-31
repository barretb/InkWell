using InkWell.Domain.Entities;
using SQLite;

namespace InkWell.Infrastructure.Persistence;

/// <summary>
/// Row shapes for sqlite-net's object mapper.
/// </summary>
/// <remarks>
/// <para>
/// These exist so that persistence concerns never leak into <c>InkWell.Domain</c>: the domain
/// entities carry no ORM attributes and no storage-shaped types, and this layer owns the two
/// conversions the store needs — <see cref="DateTimeOffset"/> as UTC ticks, and
/// <see cref="DateOnly"/> as an ISO <c>yyyy-MM-dd</c> string, which sorts chronologically as text
/// and stays readable when inspecting the database.
/// </para>
/// <para>
/// The <c>[Table]</c> attributes name the hand-written tables from <see cref="DatabaseMigrator"/>;
/// sqlite-net never creates them.
/// </para>
/// </remarks>
internal static class RowConversions
{
    internal const string DateFormat = "yyyy-MM-dd";

    internal static long ToTicks(DateTimeOffset value) => value.UtcTicks;

    internal static DateTimeOffset FromTicks(long ticks) => new(ticks, TimeSpan.Zero);

    internal static string ToText(DateOnly value) => value.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);

    internal static DateOnly FromText(string value) =>
        DateOnly.ParseExact(value, DateFormat, System.Globalization.CultureInfo.InvariantCulture);
}

[Table("Manuscript")]
internal sealed class ManuscriptRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public long CreatedAt { get; set; }

    public long ModifiedAt { get; set; }

    internal Manuscript ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        Title = Title,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
        ModifiedAt = RowConversions.FromTicks(ModifiedAt),
    };

    internal static ManuscriptRow FromEntity(Manuscript entity) => new()
    {
        Id = entity.Id.ToString(),
        Title = entity.Title,
        CreatedAt = RowConversions.ToTicks(entity.CreatedAt),
        ModifiedAt = RowConversions.ToTicks(entity.ModifiedAt),
    };
}

[Table("Chapter")]
internal sealed class ChapterRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ManuscriptId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string ContentMarkdown { get; set; } = string.Empty;

    public int OrderIndex { get; set; }

    public int WordCount { get; set; }

    public long CreatedAt { get; set; }

    public long ModifiedAt { get; set; }

    internal Chapter ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ManuscriptId = Guid.Parse(ManuscriptId),
        Title = Title,
        ContentMarkdown = ContentMarkdown,
        OrderIndex = OrderIndex,
        WordCount = WordCount,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
        ModifiedAt = RowConversions.FromTicks(ModifiedAt),
    };

    internal static ChapterRow FromEntity(Chapter entity) => new()
    {
        Id = entity.Id.ToString(),
        ManuscriptId = entity.ManuscriptId.ToString(),
        Title = entity.Title,
        ContentMarkdown = entity.ContentMarkdown,
        OrderIndex = entity.OrderIndex,
        WordCount = entity.WordCount,
        CreatedAt = RowConversions.ToTicks(entity.CreatedAt),
        ModifiedAt = RowConversions.ToTicks(entity.ModifiedAt),
    };
}

[Table("InlineImage")]
internal sealed class InlineImageRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ChapterId { get; set; } = string.Empty;

    public byte[] Bytes { get; set; } = [];

    public string MimeType { get; set; } = string.Empty;

    public string? AltText { get; set; }

    public int ByteLength { get; set; }

    public long CreatedAt { get; set; }

    internal InlineImage ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ChapterId = Guid.Parse(ChapterId),
        Bytes = Bytes,
        MimeType = MimeType,
        AltText = AltText,
        ByteLength = ByteLength,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
    };
}

[Table("Character")]
internal sealed class CharacterRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ManuscriptId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public long CreatedAt { get; set; }

    public long ModifiedAt { get; set; }

    internal Character ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ManuscriptId = Guid.Parse(ManuscriptId),
        Name = Name,
        Notes = Notes,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
        ModifiedAt = RowConversions.FromTicks(ModifiedAt),
    };
}

[Table("PlotThread")]
internal sealed class PlotThreadRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ManuscriptId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public long CreatedAt { get; set; }

    public long ModifiedAt { get; set; }

    internal PlotThread ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ManuscriptId = Guid.Parse(ManuscriptId),
        Title = Title,
        Notes = Notes,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
        ModifiedAt = RowConversions.FromTicks(ModifiedAt),
    };
}

[Table("DailyGoal")]
internal sealed class DailyGoalRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ManuscriptId { get; set; } = string.Empty;

    public int TargetWords { get; set; }

    public int IsActive { get; set; }

    public long CreatedAt { get; set; }

    public long ModifiedAt { get; set; }

    internal DailyGoal ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ManuscriptId = Guid.Parse(ManuscriptId),
        TargetWords = TargetWords,
        IsActive = IsActive != 0,
        CreatedAt = RowConversions.FromTicks(CreatedAt),
        ModifiedAt = RowConversions.FromTicks(ModifiedAt),
    };
}

[Table("DailyWritingRecord")]
internal sealed class DailyWritingRecordRow
{
    [PrimaryKey]
    public string Id { get; set; } = string.Empty;

    public string ManuscriptId { get; set; } = string.Empty;

    public string Date { get; set; } = string.Empty;

    public int WordsWritten { get; set; }

    public int? GoalTarget { get; set; }

    public int GoalMet { get; set; }

    internal DailyWritingRecord ToEntity() => new()
    {
        Id = Guid.Parse(Id),
        ManuscriptId = Guid.Parse(ManuscriptId),
        Date = RowConversions.FromText(Date),
        WordsWritten = WordsWritten,
        GoalTarget = GoalTarget,
        GoalMet = GoalMet != 0,
    };
}
