using InkWell.Application.Abstractions.Dtos;

namespace InkWell.Application.Abstractions;

/// <summary>
/// Writes manuscripts and chapters to EPUB and PDF (contracts/export-service.md). This is the only
/// path by which user content leaves the device, and it runs only when the writer asks for it and
/// only to a destination they chose (FR-017).
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Exports every chapter of a manuscript, in order, into one file with all inline images
    /// embedded (SC-009).
    /// </summary>
    Task<ExportResult> ExportManuscriptAsync(
        Guid manuscriptId,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>Exports a single chapter, with its inline images embedded (FR-018).</summary>
    Task<ExportResult> ExportChapterAsync(
        Guid chapterId,
        ExportFormat format,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>Exports each chapter of a manuscript to its own file in the chosen folder.</summary>
    Task<IReadOnlyList<ExportResult>> ExportManuscriptAllChaptersAsync(
        Guid manuscriptId,
        ExportFormat format,
        string destinationFolder,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Lets the writer see and erase everything the app has stored (FR-018, SC-008).
/// </summary>
public interface IDataControlsRepository
{
    /// <summary>Counts everything stored, per manuscript, for the "view all my data" screen.</summary>
    Task<DataInventory> GetInventoryAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes one manuscript and everything it owns.</summary>
    /// <returns>False when no manuscript has that identifier.</returns>
    Task<bool> DeleteManuscriptDataAsync(Guid manuscriptId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Erases every table and removes the database encryption key from secure storage, leaving no
    /// recoverable user content (SC-008).
    /// </summary>
    Task DeleteAllDataAsync(CancellationToken cancellationToken = default);
}
