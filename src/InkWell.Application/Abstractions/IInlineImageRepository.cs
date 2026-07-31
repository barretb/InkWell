using InkWell.Application.Abstractions.Dtos;
using InkWell.Domain.Entities;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Storage for embedded image bytes (FR-003a). Bytes live in their own table and are loaded only on
/// demand, so the frequently rewritten chapter row stays small and autosave stays fast
/// (research.md §2).
/// </summary>
public interface IInlineImageRepository
{
    /// <summary>
    /// Copies image bytes into the encrypted store and returns the reference the editor renders.
    /// After this call the manuscript no longer depends on the source file existing.
    /// </summary>
    Task<InlineImageReference> AddAsync(InlineImageInsert insert, DateTimeOffset createdAt, CancellationToken cancellationToken = default);

    /// <summary>Loads one image with its bytes, or null when it does not exist.</summary>
    Task<InlineImage?> GetAsync(Guid imageId, CancellationToken cancellationToken = default);

    /// <summary>Lists a chapter's images resolved to data URIs, for opening the chapter.</summary>
    Task<IReadOnlyList<InlineImageReference>> ListReferencesAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>Lists a chapter's images with bytes, for export.</summary>
    Task<IReadOnlyList<InlineImage>> ListWithBytesAsync(Guid chapterId, CancellationToken cancellationToken = default);

    /// <summary>Records alternative text a writer supplied after inserting an image (FR-019).</summary>
    /// <returns>False when no image has that identifier.</returns>
    Task<bool> SetAltTextAsync(Guid imageId, string? altText, CancellationToken cancellationToken = default);

    /// <summary>Lists images in a manuscript that still lack alternative text (accessibility gap).</summary>
    Task<IReadOnlyList<Guid>> ListMissingAltTextAsync(Guid manuscriptId, CancellationToken cancellationToken = default);
}
