namespace InkWell.Domain.Abstractions;

/// <summary>
/// A validated, trimmed display name: a manuscript or chapter title, a character name, or a plot
/// thread title. Every one of those fields carries the same rule in data-model.md — required,
/// trimmed, 1–200 characters — so the rule lives in one place and cannot drift between entities.
/// </summary>
public sealed record EntityTitle
{
    /// <summary>The longest title the store accepts, measured after trimming.</summary>
    public const int MaxLength = 200;

    private EntityTitle(string value) => Value = value;

    /// <summary>The trimmed title text.</summary>
    public string Value { get; }

    /// <summary>
    /// Validates and trims <paramref name="raw"/>.
    /// </summary>
    /// <param name="raw">The text the writer typed.</param>
    /// <returns>The title, or a <see cref="DomainErrorCode.ValidationError"/>.</returns>
    public static DomainResult<EntityTitle> Create(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DomainResult<EntityTitle>.Validation("A title is required.");
        }

        string trimmed = raw.Trim();

        return trimmed.Length > MaxLength
            ? DomainResult<EntityTitle>.Validation($"A title may be at most {MaxLength} characters.")
            : DomainResult<EntityTitle>.Success(new EntityTitle(trimmed));
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Unwraps the title to its text.</summary>
    public static implicit operator string(EntityTitle title)
        => title is null ? throw new ArgumentNullException(nameof(title)) : title.Value;
}
