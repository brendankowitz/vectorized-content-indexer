namespace ZeroProximity.VectorizedContentIndexer.Tests.Utilities;

/// <summary>
/// Unit tests for <see cref="ContentUtils"/>.
/// </summary>
public class ContentUtilsTests
{
    #region ComputeHash Tests

    [Fact]
    public void ComputeHash_ReturnsConsistentHash()
    {
        // Arrange
        var content = "test content for hashing";

        // Act
        var hash1 = ContentUtils.ComputeHash(content);
        var hash2 = ContentUtils.ComputeHash(content);

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA256 produces 64 hex characters
    }

    [Fact]
    public void ComputeHash_DifferentContent_DifferentHash()
    {
        // Arrange & Act
        var hash1 = ContentUtils.ComputeHash("content one");
        var hash2 = ContentUtils.ComputeHash("content two");

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeHash_ThrowsOnNullOrWhitespace()
    {
        // Act & Assert
        FluentActions.Invoking(() => ContentUtils.ComputeHash(null!))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => ContentUtils.ComputeHash(""))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => ContentUtils.ComputeHash("   "))
            .Should().Throw<ArgumentException>();
    }

    #endregion

    #region ExtractTitle Tests

    [Fact]
    public void ExtractTitle_WithMarkdownHeader_ReturnsHeaderText()
    {
        // Arrange
        var content = "# My Document Title\n\nSome content here.";
        var filePath = "document.md";

        // Act
        var title = ContentUtils.ExtractTitle(content, filePath);

        // Assert
        title.Should().Be("My Document Title");
    }

    [Fact]
    public void ExtractTitle_WithoutHeader_ReturnsFilename()
    {
        // Arrange
        var content = "Some content without a header.";
        var filePath = "my-document.md";

        // Act
        var title = ContentUtils.ExtractTitle(content, filePath);

        // Assert
        title.Should().Be("my-document");
    }

    [Fact]
    public void ExtractTitle_WithH2Header_ReturnsFilename()
    {
        // Arrange - H2 is not an H1
        var content = "## Secondary Header\n\nContent.";
        var filePath = "document.md";

        // Act
        var title = ContentUtils.ExtractTitle(content, filePath);

        // Assert - Should fallback to filename
        title.Should().Be("document");
    }

    [Fact]
    public void ExtractTitle_WithLeadingWhitespace_TrimsTitle()
    {
        // Arrange
        var content = "   \n# Trimmed Title   \n\nContent.";
        var filePath = "doc.md";

        // Act
        var title = ContentUtils.ExtractTitle(content, filePath);

        // Assert
        title.Should().Be("Trimmed Title");
    }

    [Fact]
    public void ExtractTitle_ThrowsOnNullArguments()
    {
        // Act & Assert
        FluentActions.Invoking(() => ContentUtils.ExtractTitle(null!, "file.md"))
            .Should().Throw<ArgumentException>();

        FluentActions.Invoking(() => ContentUtils.ExtractTitle("content", null!))
            .Should().Throw<ArgumentException>();
    }

    #endregion

    #region EscapeLikePattern Tests

    [Fact]
    public void EscapeLikePattern_EscapesPercentAndUnderscore()
    {
        // Arrange
        var pattern = "test%pattern_here";

        // Act
        var escaped = ContentUtils.EscapeLikePattern(pattern);

        // Assert
        escaped.Should().Be(@"test\%pattern\_here");
    }

    [Fact]
    public void EscapeLikePattern_NullInput_ReturnsEmpty()
    {
        // Act
        var result = ContentUtils.EscapeLikePattern(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void EscapeLikePattern_EmptyInput_ReturnsEmpty()
    {
        // Act
        var result = ContentUtils.EscapeLikePattern("");

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region TruncateText Tests

    [Fact]
    public void TruncateText_WithinLimit_ReturnsOriginal()
    {
        // Arrange
        var text = "Short text";

        // Act
        var result = ContentUtils.TruncateText(text, maxLength: 100);

        // Assert
        result.Should().Be(text);
    }

    [Fact]
    public void TruncateText_ExceedsLimit_TruncatesWithSuffix()
    {
        // Arrange
        var text = "This is a longer text that needs to be truncated";

        // Act
        var result = ContentUtils.TruncateText(text, maxLength: 20);

        // Assert
        result.Should().EndWith("...");
        result.Length.Should().BeLessOrEqualTo(20);
    }

    [Fact]
    public void NormalizeWhitespace_CollapsesMultipleSpaces()
    {
        // Arrange
        var text = "Hello    world   with   spaces";

        // Act
        var result = ContentUtils.NormalizeWhitespace(text);

        // Assert
        result.Should().Be("Hello world with spaces");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeWhitespace_EmptyOrWhitespace_ReturnsEmpty(string? text)
    {
        // Act
        var result = ContentUtils.NormalizeWhitespace(text!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SanitizeFts5Query_EscapesSpecialCharacters()
    {
        // Arrange
        var query = "test AND query*";

        // Act
        var result = ContentUtils.SanitizeFts5Query(query);

        // Assert
        result.Should().NotContain("AND");
        result.Should().NotContain("*");
    }

    [Fact]
    public void SanitizeLikePattern_EscapesWildcards()
    {
        // Arrange
        var pattern = "test%pattern_with[brackets]";

        // Act
        var result = ContentUtils.SanitizeLikePattern(pattern);

        // Assert
        result.Should().Contain("[%]");
        result.Should().Contain("[_]");
        result.Should().Contain("[[]");
    }

    #endregion
}
