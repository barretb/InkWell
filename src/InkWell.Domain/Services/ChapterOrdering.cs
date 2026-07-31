using InkWell.Domain.Abstractions;

namespace InkWell.Domain.Services;

/// <summary>
/// The position a chapter should occupy after a reorder or repack.
/// </summary>
/// <param name="ChapterId">The chapter to move.</param>
/// <param name="OrderIndex">Its new zero-based position.</param>
public readonly record struct ChapterOrderAssignment(Guid ChapterId, int OrderIndex);

/// <summary>
/// Decides chapter positions (FR-002). Kept as a pure function so the ordering rule — indices stay
/// contiguous and zero-based, and a reorder must name exactly the manuscript's chapters — is
/// unit-testable without a database, and so the repository has only to apply the result in a
/// transaction.
/// </summary>
public static class ChapterOrdering
{
    /// <summary>
    /// Produces the index assignments that put a manuscript's chapters into
    /// <paramref name="desiredOrder"/>.
    /// </summary>
    /// <param name="existingChapterIds">Every chapter currently in the manuscript.</param>
    /// <param name="desiredOrder">The chapters in the order the writer wants them.</param>
    /// <returns>
    /// Contiguous assignments starting at zero, or a <see cref="DomainErrorCode.ValidationError"/>
    /// if <paramref name="desiredOrder"/> is not exactly the manuscript's chapter set. Rejecting a
    /// partial list is deliberate: silently reordering a subset would leave gaps or duplicates.
    /// </returns>
    public static DomainResult<IReadOnlyList<ChapterOrderAssignment>> Reorder(
        IEnumerable<Guid> existingChapterIds,
        IReadOnlyList<Guid> desiredOrder)
    {
        ArgumentNullException.ThrowIfNull(existingChapterIds);
        ArgumentNullException.ThrowIfNull(desiredOrder);

        var existing = new HashSet<Guid>(existingChapterIds);
        var desired = new HashSet<Guid>(desiredOrder);

        if (desired.Count != desiredOrder.Count)
        {
            return DomainResult<IReadOnlyList<ChapterOrderAssignment>>.Validation(
                "The requested order lists the same chapter more than once.");
        }

        if (!existing.SetEquals(desired))
        {
            return DomainResult<IReadOnlyList<ChapterOrderAssignment>>.Validation(
                "The requested order must list exactly the chapters in this manuscript.");
        }

        return DomainResult<IReadOnlyList<ChapterOrderAssignment>>.Success(Assign(desiredOrder));
    }

    /// <summary>
    /// Closes the gap left by a deleted chapter by re-numbering the survivors from zero.
    /// </summary>
    /// <param name="remainingChapterIdsInOrder">The surviving chapters in their current order.</param>
    /// <returns>Contiguous assignments starting at zero.</returns>
    public static IReadOnlyList<ChapterOrderAssignment> Repack(IEnumerable<Guid> remainingChapterIdsInOrder)
    {
        ArgumentNullException.ThrowIfNull(remainingChapterIdsInOrder);

        return Assign([.. remainingChapterIdsInOrder]);
    }

    /// <summary>
    /// The index a newly added chapter takes, so it appends after the last existing chapter.
    /// </summary>
    /// <param name="existingOrderIndexes">The order indices already in use.</param>
    /// <returns>One past the highest existing index, or zero when the manuscript is empty.</returns>
    public static int NextOrderIndex(IEnumerable<int> existingOrderIndexes)
    {
        ArgumentNullException.ThrowIfNull(existingOrderIndexes);

        var highest = -1;
        foreach (int index in existingOrderIndexes)
        {
            if (index > highest)
            {
                highest = index;
            }
        }

        return highest + 1;
    }

    private static ChapterOrderAssignment[] Assign(IReadOnlyList<Guid> orderedIds)
    {
        var assignments = new ChapterOrderAssignment[orderedIds.Count];
        for (var i = 0; i < orderedIds.Count; i++)
        {
            assignments[i] = new ChapterOrderAssignment(orderedIds[i], i);
        }

        return assignments;
    }
}
