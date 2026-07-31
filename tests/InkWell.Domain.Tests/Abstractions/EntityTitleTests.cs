using InkWell.Domain.Abstractions;

namespace InkWell.Domain.Tests.Abstractions;

/// <summary>
/// Title validation rules shared by manuscripts, chapters, characters, and plot threads:
/// required, trimmed, 1–200 characters (data-model.md).
/// </summary>
public class EntityTitleTests
{
    [Theory]
    [InlineData("The Long Winter", "The Long Winter")]
    [InlineData("  The Long Winter  ", "The Long Winter")]
    [InlineData("\tChapter One\r\n", "Chapter One")]
    [InlineData("A", "A")]
    public void Create_trims_surrounding_whitespace(string raw, string expected)
    {
        DomainResult<EntityTitle> result = EntityTitle.Create(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void Create_rejects_missing_titles(string? raw)
    {
        DomainResult<EntityTitle> result = EntityTitle.Create(raw);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public void Create_accepts_the_maximum_length()
    {
        string raw = new('a', EntityTitle.MaxLength);

        DomainResult<EntityTitle> result = EntityTitle.Create(raw);

        Assert.True(result.IsSuccess);
        Assert.Equal(EntityTitle.MaxLength, result.Value.Value.Length);
    }

    [Fact]
    public void Create_rejects_titles_longer_than_the_maximum()
    {
        string raw = new('a', EntityTitle.MaxLength + 1);

        DomainResult<EntityTitle> result = EntityTitle.Create(raw);

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.ValidationError, result.Error.Code);
    }

    [Fact]
    public void Length_is_measured_after_trimming()
    {
        string raw = "   " + new string('a', EntityTitle.MaxLength) + "   ";

        DomainResult<EntityTitle> result = EntityTitle.Create(raw);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Titles_with_the_same_text_are_equal()
    {
        EntityTitle first = EntityTitle.Create(" Draft ").Value;
        EntityTitle second = EntityTitle.Create("Draft").Value;

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Failed_result_does_not_expose_a_value()
    {
        DomainResult<EntityTitle> result = EntityTitle.Create("");

        Assert.Throws<InvalidOperationException>(() => _ = result.Value);
    }

    [Fact]
    public void Successful_result_does_not_expose_an_error()
    {
        DomainResult<EntityTitle> result = EntityTitle.Create("Draft");

        Assert.Throws<InvalidOperationException>(() => _ = result.Error);
    }

    [Fact]
    public void NotFound_carries_the_not_found_code()
    {
        DomainResult result = DomainResult.NotFound("No such chapter.");

        Assert.False(result.IsSuccess);
        Assert.Equal(DomainErrorCode.NotFound, result.Error.Code);
        Assert.Equal("No such chapter.", result.Error.Message);
    }
}
