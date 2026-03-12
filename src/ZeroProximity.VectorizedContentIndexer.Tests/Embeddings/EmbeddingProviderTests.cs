namespace ZeroProximity.VectorizedContentIndexer.Tests.Embeddings;

/// <summary>
/// Unit tests for embedding providers.
/// </summary>
public class EmbeddingProviderTests
{
    #region HashEmbeddingProvider Tests

    [Fact]
    public async Task HashProvider_EmbedAsync_ReturnsVectorOfCorrectDimensions()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act
        var embedding = await provider.EmbedAsync("test text");

        // Assert
        embedding.Should().HaveCount(provider.Dimensions);
        embedding.Should().HaveCount(384);
    }

    [Fact]
    public async Task HashProvider_EmbedAsync_ReturnsNormalizedVector()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act
        var embedding = await provider.EmbedAsync("test text with multiple words");

        // Assert
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        magnitude.Should().BeApproximately(1.0f, 0.0001f);
    }

    [Fact]
    public async Task HashProvider_EmbedAsync_SameTextProducesSameVector()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();
        var text = "deterministic embedding test";

        // Act
        var embedding1 = await provider.EmbedAsync(text);
        var embedding2 = await provider.EmbedAsync(text);

        // Assert
        embedding1.Should().BeEquivalentTo(embedding2);
    }

    [Fact]
    public async Task HashProvider_EmbedAsync_DifferentTextProducesDifferentVectors()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act
        var embedding1 = await provider.EmbedAsync("first text");
        var embedding2 = await provider.EmbedAsync("completely different content");

        // Assert
        embedding1.Should().NotBeEquivalentTo(embedding2);
    }

    [Fact]
    public async Task HashProvider_EmbedAsync_ThrowsOnNullOrWhitespace()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act & Assert
        await provider.Invoking(p => p.EmbedAsync(null!))
            .Should().ThrowAsync<ArgumentException>();

        await provider.Invoking(p => p.EmbedAsync(""))
            .Should().ThrowAsync<ArgumentException>();

        await provider.Invoking(p => p.EmbedAsync("   "))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task HashProvider_EmbedBatchAsync_ReturnsCorrectNumberOfVectors()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();
        var texts = new List<string> { "text one", "text two", "text three" };

        // Act
        var embeddings = await provider.EmbedBatchAsync(texts);

        // Assert
        embeddings.Should().HaveCount(3);
        embeddings.Should().AllSatisfy(e => e.Should().HaveCount(384));
    }

    [Fact]
    public async Task HashProvider_EmbedBatchAsync_ThrowsOnEmptyCollection()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act & Assert
        await provider.Invoking(p => p.EmbedBatchAsync(new List<string>()))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void HashProvider_Properties_HaveCorrectValues()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Assert
        provider.Dimensions.Should().Be(384);
        provider.ModelName.Should().Be("Hash-FNV1a");
        provider.IsGpuAccelerated.Should().BeFalse();
    }

    [Fact]
    public async Task HashProvider_DisposeAsync_CanBeCalledMultipleTimes()
    {
        // Arrange
        var provider = new HashEmbeddingProvider();

        // Act & Assert - should not throw
        await provider.DisposeAsync();
        await provider.DisposeAsync();
    }

    #endregion

    #region EmbeddingProviderFactory Tests

    [Fact]
    public async Task Factory_CreateAsync_WithNullPath_ReturnsHashProvider()
    {
        // Act
        var provider = await EmbeddingProviderFactory.CreateAsync(null);

        // Assert
        provider.Should().BeOfType<HashEmbeddingProvider>();
    }

    [Fact]
    public async Task Factory_CreateAsync_WithEmptyPath_ReturnsHashProvider()
    {
        // Act
        var provider = await EmbeddingProviderFactory.CreateAsync("");

        // Assert
        provider.Should().BeOfType<HashEmbeddingProvider>();
    }

    [Fact]
    public async Task Factory_CreateAsync_WithPath_ReturnsProvider()
    {
        // Act - Factory will try to extract bundled model to the path
        // If bundled model exists, it returns ONNX provider; otherwise HashProvider
        var provider = await EmbeddingProviderFactory.CreateAsync(Path.GetTempPath());

        // Assert - Provider should be created (either ONNX or Hash)
        provider.Should().NotBeNull();
        provider.Dimensions.Should().Be(384);

        await provider.DisposeAsync();
    }

    [Fact]
    public void Factory_CreateHashProvider_ReturnsHashProvider()
    {
        // Act
        var provider = EmbeddingProviderFactory.CreateHashProvider();

        // Assert
        provider.Should().BeOfType<HashEmbeddingProvider>();
    }

    [Fact]
    public async Task Factory_TryCreateOnnxProviderAsync_WithTempPath_ExtractsBundledModel()
    {
        // Act - Will try to extract bundled model to the temp path
        var tempPath = Path.Combine(Path.GetTempPath(), "onnx-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            var provider = await EmbeddingProviderFactory.TryCreateOnnxProviderAsync(tempPath);

            // Assert - If bundled model exists, provider will be created
            // If no bundled model, provider will be null
            if (provider != null)
            {
                provider.Dimensions.Should().Be(384);
                provider.ModelName.Should().Be("all-MiniLM-L6-v2");
                await provider.DisposeAsync();
            }
        }
        finally
        {
            // Cleanup
            try { Directory.Delete(tempPath, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Factory_TryCreateOnnxProviderAsync_WithNullPath_ThrowsArgumentException()
    {
        // Act & Assert
        await FluentActions.Invoking(() => EmbeddingProviderFactory.TryCreateOnnxProviderAsync(null!))
            .Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Normalization Tests

    [Fact]
    public void HashProvider_Normalize_ProducesUnitVector()
    {
        // Arrange
        var vector = new float[] { 3.0f, 4.0f }; // magnitude = 5

        // Act
        HashEmbeddingProvider.Normalize(vector);

        // Assert
        var magnitude = MathF.Sqrt(vector.Sum(x => x * x));
        magnitude.Should().BeApproximately(1.0f, 0.0001f);
        vector[0].Should().BeApproximately(0.6f, 0.0001f);
        vector[1].Should().BeApproximately(0.8f, 0.0001f);
    }

    [Fact]
    public void HashProvider_Normalize_ZeroVector_StaysZero()
    {
        // Arrange
        var vector = new float[] { 0.0f, 0.0f, 0.0f };

        // Act
        HashEmbeddingProvider.Normalize(vector);

        // Assert
        vector.Should().AllSatisfy(v => v.Should().Be(0.0f));
    }

    [Fact]
    public void OnnxProvider_Normalize_ProducesUnitVector()
    {
        // Arrange
        var vector = new float[] { 1.0f, 2.0f, 2.0f }; // magnitude = 3

        // Act
        OnnxEmbeddingProvider.Normalize(vector);

        // Assert
        var magnitude = MathF.Sqrt(vector.Sum(x => x * x));
        magnitude.Should().BeApproximately(1.0f, 0.0001f);
    }

    #endregion
}
