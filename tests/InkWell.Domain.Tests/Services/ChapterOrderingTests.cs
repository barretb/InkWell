using InkWell.Domain.Abstractions;
using InkWell.Domain.Services;

namespace InkWell.Domain.Tests.Services;

/// <summary>
/// FR-002: chapters carry a contiguous, zero-based order that survives reorder and delete.
/// </summary>
public class ChapterOrderingTests
{
    private static readonly Guid A = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid B = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid C = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid D = new("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Reorder_assigns_contiguous_indices_from_zero()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([A, B, C], [C, A, B]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [new ChapterOrderAssignment(C, 0), new ChapterOrderAssignment(A, 1), new ChapterOrderAssignment(B, 2)],
            result.Value);
    }

    [Fact]
    public void Reorder_accepts_the_existing_order_unchanged()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([A, B], [A, B]);

        Assert.True(result.IsSuccess);
        Assert.Equal([0, 1], result.Value.Select(a => a.OrderIndex));
    }

    [Fact]
    public void Reorder_of_an_empty_manuscript_produces_no_assignments()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([], []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public void Reorder_rejects_an_unknown_chapter()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([A, B], [A, B, D]);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public void Reorder_rejects_a_missing_chapter()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([A, B, C], [A, B]);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public void Reorder_rejects_a_duplicated_chapter()
    {
        DomainResult<IReadOnlyList<ChapterOrderAssignment>> result =
            ChapterOrdering.Reorder([A, B], [A, A]);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public void Repack_closes_the_gap_left_by_a_deleted_chapter()
    {
        // B was deleted from [A, B, C, D]; the survivors keep their relative order.
        IReadOnlyList<ChapterOrderAssignment> assignments = ChapterOrdering.Repack([A, C, D]);

        Assert.Equal(
            [new ChapterOrderAssignment(A, 0), new ChapterOrderAssignment(C, 1), new ChapterOrderAssignment(D, 2)],
            assignments);
    }

    [Fact]
    public void Repack_of_an_empty_manuscript_produces_no_assignments()
        => Assert.Empty(ChapterOrdering.Repack([]));

    [Fact]
    public void NextOrderIndex_appends_after_the_highest_existing_index()
    {
        Assert.Equal(3, ChapterOrdering.NextOrderIndex([0, 1, 2]));
        Assert.Equal(0, ChapterOrdering.NextOrderIndex([]));
    }

    [Fact]
    public void NextOrderIndex_is_robust_to_a_non_contiguous_existing_order()
    {
        // Defensive: a store that somehow holds a gap must still append at the end.
        Assert.Equal(8, ChapterOrdering.NextOrderIndex([0, 3, 7]));
    }
}
