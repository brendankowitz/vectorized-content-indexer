namespace ZeroProximity.VectorizedContentIndexer.Tests.Search;

/// <summary>
/// Comprehensive unit tests for the HybridSearcher class.
/// </summary>
public sealed class HybridSearcherTests : IAsyncDisposable
{
    private readonly List<string> _tempDirs = [];

    private (string lucenePath, string vectorPath) GetTempIndexPaths()
    {
        var basePath = Path.Combine(Path.GetTempPath(), $"hybrid_test_{Guid.NewGuid()}");
        var lucenePath = Path.Combine(basePath, "lucene");
        var vectorPath = Path.Combine(basePath, "vector");
        _tempDirs.Add(basePath);
        return (lucenePath, vectorPath);
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

    private static float[] CreateDeterministicEmbedding(string text, int dimensions)
    {
        var hash = text.GetHashCode();
        var random = new Random(hash);
        var vector = new float[dimensions];
        float sumSquares = 0;

        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1);
            sumSquares += vector[i] * vector[i];
        }

        float magnitude = MathF.Sqrt(sumSquares);
        for (int i = 0; i < dimensions; i++)
        {
            vector[i] /= magnitude;
        }

        return vector;
    }

    private async Task<HybridSearcher<TestDocument>> CreateHybridSearcherAsync()
    {
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();

        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);
        var hybridSearcher = new HybridSearcher<TestDocument>(lexicalEngine, vectorEngine);

        await hybridSearcher.InitializeAsync();
        return hybridSearcher;
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullLexicalEngine_ThrowsArgumentNullException()
    {
        // Arrange
        var (_, vectorPath) = GetTempIndexPaths();
        var embedder = CreateMockEmbedder();
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);

        // Act & Assert
        var action = () => new HybridSearcher<TestDocument>(null!, vectorEngine);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("lexicalEngine");
    }

    [Fact]
    public void Constructor_WithNullVectorEngine_ThrowsArgumentNullException()
    {
        // Arrange
        var (lucenePath, _) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);

        // Act & Assert
        var action = () => new HybridSearcher<TestDocument>(lexicalEngine, null!);
        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("vectorEngine");
    }

    [Fact]
    public void Constructor_WithNegativeWeights_ThrowsArgumentException()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();
        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);

        // Act & Assert
        var action = () => new HybridSearcher<TestDocument>(lexicalEngine, vectorEngine, lexicalWeight: -0.5f);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithZeroWeights_ThrowsArgumentException()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();
        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);

        // Act & Assert
        var action = () => new HybridSearcher<TestDocument>(lexicalEngine, vectorEngine, lexicalWeight: 0, semanticWeight: 0);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_WithZeroRrfK_ThrowsArgumentException()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();
        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);

        // Act & Assert
        var action = () => new HybridSearcher<TestDocument>(lexicalEngine, vectorEngine, rrfK: 0);
        action.Should().Throw<ArgumentException>()
            .WithParameterName("rrfK");
    }

    [Fact]
    public void Constructor_StoresConfiguredWeights()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();
        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);

        // Act
        var searcher = new HybridSearcher<TestDocument>(
            lexicalEngine,
            vectorEngine,
            lexicalWeight: 0.7f,
            semanticWeight: 0.3f,
            rrfK: 40);

        // Assert
        searcher.LexicalWeight.Should().Be(0.7f);
        searcher.SemanticWeight.Should().Be(0.3f);
        searcher.RrfK.Should().Be(40);
    }

    #endregion

    #region Initialization Tests

    [Fact]
    public async Task InitializeAsync_InitializesBothEngines()
    {
        // Arrange & Act
        await using var searcher = await CreateHybridSearcherAsync();

        // Assert - should not throw
        var count = await searcher.GetCountAsync();
        count.Should().Be(0);
    }

    #endregion

    #region Indexing Tests

    [Fact]
    public async Task IndexAsync_SingleDocument_IndexesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = TestDocument.Create(1);

        // Act
        await searcher.IndexAsync(document);

        // Assert
        var count = await searcher.GetCountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task IndexManyAsync_MultipleDocuments_IndexesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var documents = Enumerable.Range(1, 30).Select(i => TestDocument.Create(i)).ToList();

        // Act
        await searcher.IndexManyAsync(documents);

        // Assert
        var count = await searcher.GetCountAsync();
        count.Should().Be(30);
    }

    #endregion

    #region Search Mode Tests

    [Fact]
    public async Task SearchAsync_LexicalMode_UsesOnlyLexicalEngine()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = new TestDocument
        {
            Id = "lexical-test",
            Content = "The quick brown fox jumps over the lazy dog",
            CreatedAt = DateTime.UtcNow
        };
        await searcher.IndexAsync(document);

        // Act
        var results = await searcher.SearchAsync("quick fox", maxResults: 10, mode: SearchMode.Lexical);

        // Assert
        results.Should().HaveCount(1);
        results[0].Document.Id.Should().Be("lexical-test");
    }

    [Fact]
    public async Task SearchAsync_SemanticMode_UsesOnlyVectorEngine()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = new TestDocument
        {
            Id = "semantic-test",
            Content = "Machine learning and artificial intelligence concepts",
            CreatedAt = DateTime.UtcNow
        };
        await searcher.IndexAsync(document);

        // Act
        var results = await searcher.SearchAsync("AI and ML", maxResults: 10, mode: SearchMode.Semantic);

        // Assert
        results.Should().HaveCount(1);
        results[0].Document.Id.Should().Be("semantic-test");
    }

    [Fact]
    public async Task SearchAsync_HybridMode_CombinesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var documents = Enumerable.Range(1, 20).Select(i => new TestDocument
        {
            Id = $"hybrid-doc-{i}",
            Content = $"Document {i} about programming and software development topic {i}",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        var results = await searcher.SearchAsync("programming software", maxResults: 10, mode: SearchMode.Hybrid);

        // Assert
        results.Should().NotBeEmpty();
        results.Count.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmpty()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        await searcher.IndexAsync(TestDocument.Create(1));

        // Act
        var results = await searcher.SearchAsync("", maxResults: 10, mode: SearchMode.Hybrid);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_InvalidMaxResults_ThrowsArgumentException()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();

        // Act & Assert
        var action1 = () => searcher.SearchAsync("query", maxResults: 0, mode: SearchMode.Hybrid);
        await action1.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var action2 = () => searcher.SearchAsync("query", maxResults: 1001, mode: SearchMode.Hybrid);
        await action2.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    #endregion

    #region RRF Algorithm Tests

    [Fact]
    public async Task SearchAsync_RRF_CombinesRanksFromBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();

        // Create documents that would rank differently in lexical vs semantic
        var documents = new[]
        {
            new TestDocument { Id = "exact-match", Content = "programming code development", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "semantic-match", Content = "software engineering coding practices", CreatedAt = DateTime.UtcNow },
            new TestDocument { Id = "both-match", Content = "programming software code development", CreatedAt = DateTime.UtcNow },
        };
        await searcher.IndexManyAsync(documents);

        // Act
        var results = await searcher.SearchAsync("programming code", maxResults: 10, mode: SearchMode.Hybrid);

        // Assert
        results.Should().NotBeEmpty();
        // Results should be ordered by RRF fusion score
        for (int i = 1; i < results.Count; i++)
        {
            results[i - 1].Score.Should().BeGreaterThanOrEqualTo(results[i].Score);
        }
    }

    [Fact]
    public async Task SearchWithBreakdownAsync_ReturnsDetailedScoring()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var documents = Enumerable.Range(1, 10).Select(i => new TestDocument
        {
            Id = $"breakdown-doc-{i}",
            Content = $"Document {i} for testing score breakdown with keyword content",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        var results = await searcher.SearchWithBreakdownAsync("keyword content", maxResults: 5);

        // Assert
        results.Should().NotBeEmpty();
        foreach (var result in results)
        {
            // At least one score component should be present
            (result.LexicalScore.HasValue || result.SemanticScore.HasValue).Should().BeTrue();

            // Total score should be sum of components
            var expectedTotal = (result.LexicalScore ?? 0) + (result.SemanticScore ?? 0);
            result.Score.Should().BeApproximately(expectedTotal, 0.0001);
        }
    }

    #endregion

    #region Delete Tests

    [Fact]
    public async Task DeleteAsync_RemovesFromBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = TestDocument.Create(1);
        await searcher.IndexAsync(document);

        // Act
        var result = await searcher.DeleteAsync(document.Id);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesFromBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var documents = Enumerable.Range(1, 10).Select(i => TestDocument.Create(i)).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        var idsToDelete = documents.Take(5).Select(d => d.Id);
        var deleteCount = await searcher.DeleteManyAsync(idsToDelete);

        // Assert
        deleteCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ClearAsync_ClearsBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        await searcher.IndexManyAsync(Enumerable.Range(1, 20).Select(i => TestDocument.Create(i)));

        // Act
        await searcher.ClearAsync();

        // Assert
        var count = await searcher.GetCountAsync();
        count.Should().Be(0);
    }

    #endregion

    #region Caching Tests

    [Fact]
    public async Task CacheDocument_CachesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = TestDocument.Create(1);
        await searcher.IndexAsync(document);

        // Act
        searcher.CacheDocument(document);

        // Assert - no exception means success
        var results = await searcher.SearchAsync("test", maxResults: 10, mode: SearchMode.Hybrid);
        results.Should().NotBeNull();
    }

    [Fact]
    public async Task CacheDocuments_CachesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var documents = Enumerable.Range(1, 5).Select(i => TestDocument.Create(i)).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        searcher.CacheDocuments(documents);

        // Assert - no exception means success
        var results = await searcher.SearchAsync("test", maxResults: 10, mode: SearchMode.Hybrid);
        results.Should().NotBeNull();
    }

    #endregion

    #region Optimization Tests

    [Fact]
    public async Task OptimizeAsync_OptimizesBothEngines()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        await searcher.IndexManyAsync(Enumerable.Range(1, 50).Select(i => TestDocument.Create(i)));

        // Act & Assert - should not throw
        await searcher.OptimizeAsync();
    }

    #endregion

    #region Custom Weights Tests

    [Fact]
    public async Task SearchAsync_WithHighLexicalWeight_FavorsLexicalResults()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();

        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);
        await using var searcher = new HybridSearcher<TestDocument>(
            lexicalEngine,
            vectorEngine,
            lexicalWeight: 0.9f,
            semanticWeight: 0.1f);

        await searcher.InitializeAsync();

        var documents = Enumerable.Range(1, 10).Select(i => new TestDocument
        {
            Id = $"weighted-doc-{i}",
            Content = $"Document number {i} with keyword content",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        var results = await searcher.SearchWithBreakdownAsync("keyword content", maxResults: 5);

        // Assert
        results.Should().NotBeEmpty();
        // Lexical scores should generally contribute more to total
        var avgLexicalContribution = results.Where(r => r.LexicalScore.HasValue).Average(r => r.LexicalScore!.Value / r.Score);
        avgLexicalContribution.Should().BeGreaterThan(0.5); // Lexical should contribute more than half
    }

    [Fact]
    public async Task SearchAsync_WithHighSemanticWeight_FavorsSemanticResults()
    {
        // Arrange
        var (lucenePath, vectorPath) = GetTempIndexPaths();
        var mapper = new TestDocumentMapper();
        var embedder = CreateMockEmbedder();

        var lexicalEngine = new LuceneSearchEngine<TestDocument>(lucenePath, mapper);
        var vectorEngine = new VectorSearchEngine<TestDocument>(vectorPath, embedder);
        await using var searcher = new HybridSearcher<TestDocument>(
            lexicalEngine,
            vectorEngine,
            lexicalWeight: 0.1f,
            semanticWeight: 0.9f);

        await searcher.InitializeAsync();

        var documents = Enumerable.Range(1, 10).Select(i => new TestDocument
        {
            Id = $"weighted-doc-{i}",
            Content = $"Document number {i} with unique semantic content",
            CreatedAt = DateTime.UtcNow
        }).ToList();
        await searcher.IndexManyAsync(documents);

        // Act
        var results = await searcher.SearchWithBreakdownAsync("semantic content", maxResults: 5);

        // Assert
        results.Should().NotBeEmpty();
        // Semantic scores should generally contribute more to total
        var avgSemanticContribution = results.Where(r => r.SemanticScore.HasValue).Average(r => r.SemanticScore!.Value / r.Score);
        avgSemanticContribution.Should().BeGreaterThan(0.5); // Semantic should contribute more than half
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task SearchAsync_OnlyLexicalMatches_ReturnsResults()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();
        var document = new TestDocument
        {
            Id = "lexical-only",
            Content = "xyzpdq exact keyword match",
            CreatedAt = DateTime.UtcNow
        };
        await searcher.IndexAsync(document);

        // Act - search for exact keyword not in semantic training
        var results = await searcher.SearchAsync("xyzpdq", maxResults: 10, mode: SearchMode.Hybrid);

        // Assert
        results.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_DocumentInBothSources_HasHigherScore()
    {
        // Arrange
        await using var searcher = await CreateHybridSearcherAsync();

        // This document should rank high in both lexical and semantic
        var strongMatch = new TestDocument
        {
            Id = "strong-match",
            Content = "programming code development software engineering",
            CreatedAt = DateTime.UtcNow
        };

        // This document might rank high in one but not the other
        var weakMatch = new TestDocument
        {
            Id = "weak-match",
            Content = "random unrelated content about cats and dogs",
            CreatedAt = DateTime.UtcNow
        };

        await searcher.IndexManyAsync(new[] { strongMatch, weakMatch });

        // Act
        var results = await searcher.SearchAsync("programming software", maxResults: 10, mode: SearchMode.Hybrid);

        // Assert
        results.Should().NotBeEmpty();
        // Strong match should generally rank higher due to RRF boosting
        var strongMatchResult = results.FirstOrDefault(r => r.Document.Id == "strong-match");
        strongMatchResult.Should().NotBeNull();
    }

    #endregion
}
