namespace ZeroProximity.VectorizedContentIndexer.Tests.Search.Lucene;

/// <summary>
/// Comprehensive unit tests for the LuceneSearchEngine class.
/// </summary>
public sealed class LuceneSearchEngineTests : IAsyncDisposable
{
    private readonly List<string> _tempDirs = [];

    private string GetTempIndexPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucene_test_{Guid.NewGuid()}");
        _tempDirs.Add(path);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        // Small delay to ensure files are released
        await Task.Delay(100);

        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CreatesIndexDirectory()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);

        // Act
        await engine.InitializeAsync();

        // Assert
        Directory.Exists(indexPath).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);

        // Act
        await engine.InitializeAsync();
        await engine.InitializeAsync();
        await engine.InitializeAsync();

        // Assert - should not throw
        var count = await engine.GetCountAsync();
        count.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithNullIndexPath_ThrowsArgumentNullException()
    {
        // Arrange
        var mapper = new TestDocumentMapper();

        // Act & Assert
        var action = () => new LuceneSearchEngine<TestDocument>(null!, mapper);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("indexPath");
    }

    [Fact]
    public void Constructor_WithNullMapper_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new LuceneSearchEngine<TestDocument>("path", null!);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("mapper");
    }

    #endregion

    #region Indexing Tests

    [Fact]
    public async Task IndexAsync_SingleDocument_IncreasesCount()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document = TestDocument.Create(1);

        // Act
        await engine.IndexAsync(document);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task IndexAsync_DuplicateDocument_UpdatesExisting()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document1 = new TestDocument
        {
            Id = "same-id",
            Content = "Original content",
            CreatedAt = DateTime.UtcNow
        };

        var document2 = new TestDocument
        {
            Id = "same-id",
            Content = "Updated content",
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await engine.IndexAsync(document1);
        await engine.IndexAsync(document2);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(1);

        var results = await engine.SearchAsync("Updated", maxResults: 10, mode: SearchMode.Lexical);
        results.Should().HaveCount(1);
        results[0].Document.Content.Should().Be("Updated content");
    }

    [Fact]
    public async Task IndexManyAsync_MultipleDocuments_IndexesAll()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var documents = Enumerable.Range(1, 50).Select(i => TestDocument.Create(i)).ToList();

        // Act
        await engine.IndexManyAsync(documents);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(50);
    }

    [Fact]
    public async Task IndexAsync_WithoutInitialize_ThrowsInvalidOperationException()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);

        var document = TestDocument.Create(1);

        // Act & Assert
        var action = () => engine.IndexAsync(document);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*initialized*");
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchAsync_MatchingQuery_ReturnsResults()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "unique-doc",
            Content = "The quick brown fox jumps over the lazy dog",
            CreatedAt = DateTime.UtcNow
        };
        await engine.IndexAsync(document);

        // Act
        var results = await engine.SearchAsync("quick brown fox", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(1);
        results[0].Document.Id.Should().Be("unique-doc");
        results[0].Score.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_NonMatchingQuery_ReturnsEmpty()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexAsync(new TestDocument
        {
            Id = "doc-1",
            Content = "The quick brown fox",
            CreatedAt = DateTime.UtcNow
        });

        // Act
        var results = await engine.SearchAsync("elephant", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexAsync(TestDocument.Create(1));

        // Act
        var results = await engine.SearchAsync("", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WhitespaceQuery_ReturnsEmpty()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexAsync(TestDocument.Create(1));

        // Act
        var results = await engine.SearchAsync("   ", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        // Index documents with common content
        var documents = Enumerable.Range(1, 20).Select(i => new TestDocument
        {
            Id = $"doc-{i}",
            Content = $"Document {i} about common topic",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await engine.IndexManyAsync(documents);

        // Act
        var results = await engine.SearchAsync("common topic", maxResults: 5, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(5);
    }

    [Fact]
    public async Task SearchAsync_ResultsOrderedByScore()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        // Index documents with varying relevance
        await engine.IndexManyAsync(new[]
        {
            new TestDocument { Id = "1", Content = "cat", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "2", Content = "cat cat cat", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "3", Content = "cat cat", CreatedAt = DateTime.UtcNow },
        });

        // Act
        var results = await engine.SearchAsync("cat", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(3);
        for (int i = 1; i < results.Count; i++)
        {
            results[i - 1].Score.Should().BeGreaterThanOrEqualTo(results[i].Score);
        }
    }

    [Fact]
    public async Task SearchAsync_WithSpecialCharacters_HandlesGracefully()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexAsync(TestDocument.Create(1));

        // Act - should not throw even with special Lucene characters
        var results = await engine.SearchAsync("test AND OR NOT + - ! ( ) { } [ ] ^ \" ~ * ? : \\", maxResults: 10, mode: SearchMode.Lexical);

        // Assert - may or may not return results, but should not throw
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task SearchAsync_SemanticMode_ThrowsNotSupported()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        // Act & Assert
        var action = () => engine.SearchAsync("query", maxResults: 10, mode: SearchMode.Semantic);
        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SearchAsync_ProvidesHighlight()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "highlight-test",
            Content = "This document contains the keyword unicorn in the middle of some text",
            CreatedAt = DateTime.UtcNow
        };
        await engine.IndexAsync(document);

        // Act
        var results = await engine.SearchAsync("unicorn", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(1);
        results[0].Highlight.Should().NotBeNullOrEmpty();
        results[0].Highlight.Should().Contain("unicorn");
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingDocument_RemovesFromIndex()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document = TestDocument.Create(1);
        await engine.IndexAsync(document);
        var countBefore = await engine.GetCountAsync();

        // Act
        var result = await engine.DeleteAsync(document.Id);

        // Assert
        result.Should().BeTrue();
        var countAfter = await engine.GetCountAsync();
        countAfter.Should().Be(countBefore - 1);
    }

    [Fact]
    public async Task DeleteManyAsync_MultipleDocuments_RemovesAll()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var documents = Enumerable.Range(1, 10).Select(i => TestDocument.Create(i)).ToList();
        await engine.IndexManyAsync(documents);

        // Act
        var idsToDelete = documents.Take(5).Select(d => d.Id);
        var deleteCount = await engine.DeleteManyAsync(idsToDelete);

        // Assert
        deleteCount.Should().Be(5);
        var count = await engine.GetCountAsync();
        count.Should().Be(5);
    }

    [Fact]
    public async Task ClearAsync_RemovesAllDocuments()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexManyAsync(Enumerable.Range(1, 20).Select(i => TestDocument.Create(i)));
        var countBefore = await engine.GetCountAsync();
        countBefore.Should().Be(20);

        // Act
        await engine.ClearAsync();

        // Assert
        var countAfter = await engine.GetCountAsync();
        countAfter.Should().Be(0);
    }

    #endregion

    #region Document Caching Tests

    [Fact]
    public async Task CacheDocument_AllowsSearchResultRetrieval()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "cached-doc",
            Content = "This document is cached",
            CreatedAt = DateTime.UtcNow
        };

        // Index and cache separately
        await engine.IndexAsync(document);
        engine.CacheDocument(document);

        // Act
        var results = await engine.SearchAsync("cached", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(1);
        results[0].Document.Should().Be(document); // Same reference from cache
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task GetStatsAsync_ReturnsValidStatistics()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexManyAsync(Enumerable.Range(1, 25).Select(i => TestDocument.Create(i)));

        // Act
        var stats = await engine.GetStatsAsync();

        // Assert
        stats.DocumentCount.Should().Be(25);
        stats.SizeBytes.Should().BeGreaterThan(0);
        stats.SizeMB.Should().BeGreaterThan(0);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task Index_PersistsAcrossSessions()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        var document = new TestDocument
        {
            Id = "persistent-doc",
            Content = "This content should persist",
            CreatedAt = DateTime.UtcNow
        };

        // First session: index document
        {
            await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
            await engine.InitializeAsync();
            await engine.IndexAsync(document);
        }

        // Second session: search for document
        {
            await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
            await engine.InitializeAsync();

            // Act
            var results = await engine.SearchAsync("persist", maxResults: 10, mode: SearchMode.Lexical);

            // Assert
            results.Should().HaveCount(1);
            results[0].Document.Id.Should().Be("persistent-doc");
        }
    }

    #endregion

    #region Optimization Tests

    [Fact]
    public async Task OptimizeAsync_CompletesSuccessfully()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var mapper = new TestDocumentMapper();
        await using var engine = new LuceneSearchEngine<TestDocument>(indexPath, mapper);
        await engine.InitializeAsync();

        await engine.IndexManyAsync(Enumerable.Range(1, 100).Select(i => TestDocument.Create(i)));

        // Act & Assert - should not throw
        await engine.OptimizeAsync();

        var count = await engine.GetCountAsync();
        count.Should().Be(100);
    }

    #endregion
}
