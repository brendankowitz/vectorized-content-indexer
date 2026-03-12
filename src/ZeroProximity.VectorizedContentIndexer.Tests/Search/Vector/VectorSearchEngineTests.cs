namespace ZeroProximity.VectorizedContentIndexer.Tests.Search.Vector;

/// <summary>
/// Comprehensive unit tests for the VectorSearchEngine class.
/// </summary>
public sealed class VectorSearchEngineTests : IAsyncDisposable
{
    private readonly List<string> _tempDirs = [];

    private string GetTempIndexPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vector_test_{Guid.NewGuid()}");
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

    /// <summary>
    /// Creates a mock embedding provider for testing.
    /// </summary>
    private static IEmbeddingProvider CreateMockEmbedder(int dimensions = 64)
    {
        var embedder = Substitute.For<IEmbeddingProvider>();
        embedder.Dimensions.Returns(dimensions);
        embedder.ModelName.Returns("mock-model");
        embedder.IsGpuAccelerated.Returns(false);

        // Generate deterministic embeddings based on content hash
        embedder.EmbedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var text = callInfo.ArgAt<string>(0);
                return Task.FromResult(CreateDeterministicEmbedding(text, dimensions));
            });

        embedder.EmbedBatchAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var texts = callInfo.ArgAt<IReadOnlyList<string>>(0);
                var results = texts.Select(t => CreateDeterministicEmbedding(t, dimensions)).ToArray();
                return Task.FromResult(results);
            });

        return embedder;
    }

    /// <summary>
    /// Creates a deterministic normalized embedding vector from text.
    /// </summary>
    private static float[] CreateDeterministicEmbedding(string text, int dimensions)
    {
        // Use hash of text to seed random for deterministic results
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var vector = new float[dimensions];
        float sumSquares = 0;

        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
            sumSquares += vector[i] * vector[i];
        }

        // Normalize
        float magnitude = MathF.Sqrt(sumSquares);
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] /= magnitude;
        }

        return vector;
    }

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_CreatesIndexDirectory()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);

        // Act
        await engine.InitializeAsync();

        // Assert
        Directory.Exists(indexPath).Should().BeTrue();
        File.Exists(Path.Combine(indexPath, "index.ajvi")).Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);

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
        var embedder = CreateMockEmbedder();

        // Act & Assert
        var action = () => new VectorSearchEngine<TestDocument>(null!, embedder);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("indexPath");
    }

    [Fact]
    public void Constructor_WithNullEmbedder_ThrowsArgumentNullException()
    {
        // Act & Assert
        var action = () => new VectorSearchEngine<TestDocument>("path", null!);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("embedder");
    }

    #endregion

    #region Indexing Tests

    [Fact]
    public async Task IndexAsync_SingleDocument_IncreasesCount()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document = TestDocument.Create(1);

        // Act
        await engine.IndexAsync(document);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task IndexAsync_EmptyContent_SkipsDocument()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "empty-doc",
            Content = "", // Empty content
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await engine.IndexAsync(document);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(0); // Document was skipped
    }

    [Fact]
    public async Task IndexAsync_DuplicateContent_SkipsDeduplicated()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document1 = new TestDocument
        {
            Id = "doc-1",
            Content = "Same content for both documents",
            CreatedAt = DateTime.UtcNow
        };

        var document2 = new TestDocument
        {
            Id = "doc-2",
            Content = "Same content for both documents", // Same content
            CreatedAt = DateTime.UtcNow
        };

        // Act
        await engine.IndexAsync(document1);
        await engine.IndexAsync(document2);

        // Assert
        var count = await engine.GetCountAsync();
        count.Should().Be(1); // Only one entry due to content hash deduplication
    }

    [Fact]
    public async Task IndexManyAsync_MultipleDocuments_IndexesAll()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
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
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);

        var document = TestDocument.Create(1);

        // Act & Assert
        var action = () => engine.IndexAsync(document);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*initialized*");
    }

    #endregion

    #region Search Tests

    [Fact]
    public async Task SearchAsync_MatchingSemantic_ReturnsResults()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "unique-doc",
            Content = "The quick brown fox jumps over the lazy dog",
            CreatedAt = DateTime.UtcNow
        };
        await engine.IndexAsync(document);

        // Act
        var results = await engine.SearchAsync("quick brown fox", maxResults: 10, mode: SearchMode.Semantic);

        // Assert
        results.Should().HaveCount(1);
        results[0].Document.Id.Should().Be("unique-doc");
        results[0].Score.Should().BeInRange(-1, 1); // cosine similarity is [-1, 1]
    }

    [Fact]
    public async Task SearchAsync_ReturnsScoresInDescendingOrder()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        // Index multiple documents
        var documents = Enumerable.Range(1, 20).Select(i => new TestDocument
        {
            Id = $"doc-{i}",
            Content = $"Document number {i} with unique content variation {i * i}",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await engine.IndexManyAsync(documents);

        // Act
        var results = await engine.SearchAsync("document content", maxResults: 10, mode: SearchMode.Semantic);

        // Assert
        results.Count.Should().BeGreaterThan(0);
        for (int i = 1; i < results.Count; i++)
        {
            results[i - 1].Score.Should().BeGreaterThanOrEqualTo(results[i].Score);
        }
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        await engine.IndexAsync(TestDocument.Create(1));

        // Act
        var results = await engine.SearchAsync("", maxResults: 10, mode: SearchMode.Semantic);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_RespectsMaxResults()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var documents = Enumerable.Range(1, 30).Select(i => TestDocument.Create(i)).ToList();
        await engine.IndexManyAsync(documents);

        // Act
        var results = await engine.SearchAsync("test document", maxResults: 5, mode: SearchMode.Semantic);

        // Assert
        results.Count.Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task SearchAsync_LexicalMode_ThrowsNotSupported()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        // Act & Assert
        var action = () => engine.SearchAsync("query", maxResults: 10, mode: SearchMode.Lexical);
        await action.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SearchAsync_ProvidesHighlight()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document = new TestDocument
        {
            Id = "highlight-test",
            Content = "This document contains the keyword unicorn in the middle of text",
            CreatedAt = DateTime.UtcNow
        };
        await engine.IndexAsync(document);

        // Act
        var results = await engine.SearchAsync("unicorn", maxResults: 10, mode: SearchMode.Semantic);

        // Assert
        results.Should().HaveCount(1);
        results[0].Highlight.Should().NotBeNullOrEmpty();
        results[0].Highlight.Should().Contain("unicorn");
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_ExistingDocument_RemovesFromCache()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        var document = TestDocument.Create(1);
        await engine.IndexAsync(document);

        // Act
        var result = await engine.DeleteAsync(document.Id);

        // Assert
        result.Should().BeTrue();
        // Note: AJVI doesn't support deletion, so the vector entry remains
        // but the document won't be returned in search results since it's not in cache
    }

    [Fact]
    public async Task ClearAsync_RemovesAllData()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
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

    #region Statistics Tests

    [Fact]
    public async Task GetStatsAsync_ReturnsValidStatistics()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder(dimensions: 128);
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        await engine.IndexManyAsync(Enumerable.Range(1, 25).Select(i => TestDocument.Create(i)));

        // Act
        var stats = await engine.GetStatsAsync();

        // Assert
        stats.EntryCount.Should().Be(25);
        stats.Dimensions.Should().Be(128);
        stats.Precision.Should().Be(VectorPrecision.Float16);
        stats.SizeBytes.Should().BeGreaterThan(0);
    }

    #endregion

    #region Persistence Tests

    [Fact]
    public async Task Index_PersistsAcrossSessions()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        var document = new TestDocument
        {
            Id = "persistent-doc",
            Content = "This content should persist across sessions",
            CreatedAt = DateTime.UtcNow
        };

        // First session: index document
        {
            await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
            await engine.InitializeAsync();
            await engine.IndexAsync(document);
        }

        // Second session: verify entry count
        {
            await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
            await engine.InitializeAsync();

            // Cache the document for search result retrieval
            engine.CacheDocument(document);

            // Act
            var count = await engine.GetCountAsync();
            var results = await engine.SearchAsync("persist", maxResults: 10, mode: SearchMode.Semantic);

            // Assert
            count.Should().Be(1);
            results.Should().HaveCount(1);
            results[0].Document.Id.Should().Be("persistent-doc");
        }
    }

    #endregion

    #region Precision Tests

    [Fact]
    public async Task VectorPrecision_Float32_CreatesValidIndex()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder(dimensions: 64);
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder, VectorPrecision.Float32);
        await engine.InitializeAsync();

        // Act
        await engine.IndexAsync(TestDocument.Create(1));

        // Assert
        var stats = await engine.GetStatsAsync();
        stats.Precision.Should().Be(VectorPrecision.Float32);
        stats.EntryCount.Should().Be(1);
    }

    [Fact]
    public async Task VectorPrecision_Float16_Default()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);
        await engine.InitializeAsync();

        await engine.IndexAsync(TestDocument.Create(1));

        // Act
        var stats = await engine.GetStatsAsync();

        // Assert
        stats.Precision.Should().Be(VectorPrecision.Float16);
    }

    #endregion

    #region Document Caching Tests

    [Fact]
    public async Task CacheDocuments_StoresForRetrieval()
    {
        // Arrange
        var indexPath = GetTempIndexPath();
        var embedder = CreateMockEmbedder();
        await using var engine = new VectorSearchEngine<TestDocument>(indexPath, embedder);

        var documents = Enumerable.Range(1, 10).Select(i => TestDocument.Create(i)).ToList();

        // Act
        engine.CacheDocuments(documents);

        // Assert - documents are cached (verified indirectly through search results)
        // This is mainly a smoke test to ensure no exceptions
    }

    #endregion
}
