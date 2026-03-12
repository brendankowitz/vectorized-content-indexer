using System.Security.Cryptography;
using ZeroProximity.VectorizedContentIndexer.Search.Vector;

namespace ZeroProximity.VectorizedContentIndexer.Tests.Search.Vector;

/// <summary>
/// Comprehensive unit tests for the AjviIndex class.
/// </summary>
public sealed class AjviIndexTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string GetTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ajvi_test_{Guid.NewGuid()}.ajvi");
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private static byte[] CreateContentHash(string content)
    {
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
    }

    private static float[] CreateNormalizedVector(int dimensions, int seed)
    {
        var random = new Random(seed);
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

    #region Index Creation Tests

    [Fact]
    public void Create_WithFloat32Precision_CreatesValidIndex()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 384;

        // Act
        using var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float32);

        // Assert
        index.Dimensions.Should().Be(dimensions);
        index.Precision.Should().Be(VectorPrecision.Float32);
        index.EntryCount.Should().Be(0);
        index.FilePath.Should().Be(filePath);
        File.Exists(filePath).Should().BeTrue();
    }

    [Fact]
    public void Create_WithFloat16Precision_CreatesValidIndex()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 768;

        // Act
        using var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float16);

        // Assert
        index.Dimensions.Should().Be(dimensions);
        index.Precision.Should().Be(VectorPrecision.Float16);
        index.EntryCount.Should().Be(0);
    }

    [Fact]
    public void Create_WithDefaultPrecision_UsesFloat16()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Act
        using var index = AjviIndex.Create(filePath, 128);

        // Assert
        index.Precision.Should().Be(VectorPrecision.Float16);
    }

    [Fact]
    public void Create_WithInvalidDimensions_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Act & Assert
        var action = () => AjviIndex.Create(filePath, 0);
        action.Should().Throw<ArgumentException>()
            .WithParameterName("dimensions");
    }

    [Fact]
    public void Create_WithNegativeDimensions_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Act & Assert
        var action = () => AjviIndex.Create(filePath, -1);
        action.Should().Throw<ArgumentException>()
            .WithParameterName("dimensions");
    }

    [Fact]
    public void Create_WithDimensionsExceedingMax_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Act & Assert
        var action = () => AjviIndex.Create(filePath, ushort.MaxValue + 1);
        action.Should().Throw<ArgumentException>()
            .WithParameterName("dimensions");
    }

    [Fact]
    public void Create_WhenFileExists_ThrowsInvalidOperationException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        File.WriteAllText(filePath, "existing content");

        // Act & Assert
        var action = () => AjviIndex.Create(filePath, 128);
        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"Index file already exists: {filePath}");
    }

    [Fact]
    public void Create_CreatesDirectoryIfNotExists()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"ajvi_test_dir_{Guid.NewGuid()}");
        var filePath = Path.Combine(tempDir, "test.ajvi");
        _tempFiles.Add(filePath);

        try
        {
            // Act
            using var index = AjviIndex.Create(filePath, 128);

            // Assert
            Directory.Exists(tempDir).Should().BeTrue();
            File.Exists(filePath).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region Opening Existing Index Tests

    [Fact]
    public void Open_ExistingIndex_ReturnsValidIndex()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 256;
        using (var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float32))
        {
            // Add some entries
            var hash = CreateContentHash("test");
            var vector = CreateNormalizedVector(dimensions, 42);
            index.AddEntry(hash, Guid.NewGuid(), 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), vector);
        }

        // Act
        using var reopened = AjviIndex.Open(filePath);

        // Assert
        reopened.Dimensions.Should().Be(dimensions);
        reopened.Precision.Should().Be(VectorPrecision.Float32);
        reopened.EntryCount.Should().Be(1);
    }

    [Fact]
    public void Open_NonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var filePath = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid()}.ajvi");

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_ReadOnlyMode_AllowsReading()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 128;
        var docId = Guid.NewGuid();

        using (var index = AjviIndex.Create(filePath, dimensions))
        {
            var hash = CreateContentHash("test");
            var vector = CreateNormalizedVector(dimensions, 42);
            index.AddEntry(hash, docId, 5, 1000L, vector);
        }

        // Act
        using var readOnly = AjviIndex.Open(filePath, readOnly: true);

        // Assert
        readOnly.EntryCount.Should().Be(1);
        readOnly.GetDocumentId(0).Should().Be(docId);
    }

    [Fact]
    public void Open_ReadOnlyMode_PreventsMutations()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using (var index = AjviIndex.Create(filePath, 128))
        {
            // Empty index
        }

        using var readOnly = AjviIndex.Open(filePath, readOnly: true);

        // Act & Assert
        var action = () => readOnly.AddEntry(
            CreateContentHash("new"),
            Guid.NewGuid(),
            0,
            1000L,
            CreateNormalizedVector(128, 1));

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("Cannot add entries to a read-only index");
    }

    #endregion

    #region Adding Entries Tests

    [Fact]
    public void AddEntry_ValidInput_IncreasesEntryCount()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Act
        var hash = CreateContentHash("document1");
        var vector = CreateNormalizedVector(dimensions, 1);
        index.AddEntry(hash, Guid.NewGuid(), 1, 1000L, vector);

        // Assert
        index.EntryCount.Should().Be(1);
    }

    [Fact]
    public void AddEntry_MultipleEntries_TracksAll()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Act
        for (int i = 0; i < 100; i++)
        {
            var hash = CreateContentHash($"document{i}");
            var vector = CreateNormalizedVector(dimensions, i);
            index.AddEntry(hash, Guid.NewGuid(), (byte)(i % 256), (long)i * 1000, vector);
        }

        // Assert
        index.EntryCount.Should().Be(100);
    }

    [Fact]
    public void AddEntry_InvalidHashSize_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act & Assert
        var action = () => index.AddEntry(
            new byte[16], // Invalid: should be 32 bytes
            Guid.NewGuid(),
            0,
            1000L,
            CreateNormalizedVector(64, 1));

        action.Should().Throw<ArgumentException>()
            .WithParameterName("contentHash");
    }

    [Fact]
    public void AddEntry_InvalidVectorDimensions_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act & Assert
        var action = () => index.AddEntry(
            CreateContentHash("test"),
            Guid.NewGuid(),
            0,
            1000L,
            CreateNormalizedVector(128, 1)); // Wrong dimensions

        action.Should().Throw<ArgumentException>()
            .WithParameterName("vector");
    }

    [Fact]
    public void AddEntry_AllTypeCodeValues_Accepted()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 32;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Act - Add entries with all possible type codes (0-255)
        for (int typeCode = 0; typeCode <= 255; typeCode++)
        {
            var hash = CreateContentHash($"doc{typeCode}");
            var vector = CreateNormalizedVector(dimensions, typeCode);
            index.AddEntry(hash, Guid.NewGuid(), (byte)typeCode, 1000L, vector);
        }

        // Assert
        index.EntryCount.Should().Be(256);
        for (long i = 0; i < 256; i++)
        {
            index.GetTypeCode(i).Should().Be((byte)i);
        }
    }

    #endregion

    #region Duplicate Detection Tests

    [Fact]
    public void ContainsHash_ExistingHash_ReturnsTrue()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        var hash = CreateContentHash("unique content");
        index.AddEntry(hash, Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, 1));

        // Act
        var result = index.ContainsHash(hash);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ContainsHash_NonExistingHash_ReturnsFalse()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        var existingHash = CreateContentHash("existing");
        index.AddEntry(existingHash, Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, 1));

        var nonExistingHash = CreateContentHash("not in index");

        // Act
        var result = index.ContainsHash(nonExistingHash);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsHash_EmptyIndex_ReturnsFalse()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act
        var result = index.ContainsHash(CreateContentHash("anything"));

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ContainsHash_InvalidHashSize_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act & Assert
        var action = () => index.ContainsHash(new byte[16]);
        action.Should().Throw<ArgumentException>()
            .WithParameterName("contentHash");
    }

    #endregion

    #region Vector Search Tests

    [Fact]
    public void Search_IdenticalVector_ReturnsHighScore()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        var vector = CreateNormalizedVector(dimensions, 42);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, vector);

        // Act
        var results = index.Search(vector, topK: 1);

        // Assert
        results.Should().HaveCount(1);
        results[0].Index.Should().Be(0);
        results[0].Score.Should().BeApproximately(1.0f, 0.001f); // Dot product of normalized vector with itself is 1
    }

    [Fact]
    public void Search_MultipleEntries_ReturnsTopKByScore()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Add 10 entries with different vectors
        for (int i = 0; i < 10; i++)
        {
            var vector = CreateNormalizedVector(dimensions, i);
            index.AddEntry(CreateContentHash($"doc{i}"), Guid.NewGuid(), 0, (long)i * 1000, vector);
        }

        var queryVector = CreateNormalizedVector(dimensions, 5); // Should be most similar to entry 5

        // Act
        var results = index.Search(queryVector, topK: 3);

        // Assert
        results.Should().HaveCount(3);
        results[0].Index.Should().Be(5); // Entry 5 should have highest score (identical vector)
        results[0].Score.Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void Search_EmptyIndex_ReturnsEmptyList()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act
        var results = index.Search(CreateNormalizedVector(64, 1), topK: 10);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_TopKExceedsEntryCount_ReturnsAllEntries()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Add only 3 entries
        for (int i = 0; i < 3; i++)
        {
            index.AddEntry(CreateContentHash($"doc{i}"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, i));
        }

        // Act
        var results = index.Search(CreateNormalizedVector(dimensions, 0), topK: 100);

        // Assert
        results.Should().HaveCount(3);
    }

    [Fact]
    public void Search_ResultsOrderedByScoreDescending()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        for (int i = 0; i < 20; i++)
        {
            index.AddEntry(CreateContentHash($"doc{i}"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, i));
        }

        // Act
        var results = index.Search(CreateNormalizedVector(dimensions, 10), topK: 10);

        // Assert
        for (int i = 1; i < results.Count; i++)
        {
            results[i - 1].Score.Should().BeGreaterThanOrEqualTo(results[i].Score);
        }
    }

    [Fact]
    public void Search_InvalidVectorDimensions_ThrowsArgumentException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(64, 1));

        // Act & Assert
        var action = () => index.Search(CreateNormalizedVector(128, 1)); // Wrong dimensions
        action.Should().Throw<ArgumentException>()
            .WithParameterName("queryVector");
    }

    #endregion

    #region Memory-Mapped File Access Tests

    [Fact]
    public void Index_PersistsDataBetweenSessions()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 128;
        var docId = Guid.NewGuid();
        var hash = CreateContentHash("persistent data");
        var vector = CreateNormalizedVector(dimensions, 42);
        const byte typeCode = 7;
        const long timestamp = 1234567890L;

        // First session: create and write
        using (var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float32))
        {
            index.AddEntry(hash, docId, typeCode, timestamp, vector);
        }

        // Second session: open and verify
        using var reopened = AjviIndex.Open(filePath);

        // Assert
        reopened.EntryCount.Should().Be(1);
        reopened.GetDocumentId(0).Should().Be(docId);
        reopened.GetContentHash(0).Should().BeEquivalentTo(hash);
        reopened.GetTypeCode(0).Should().Be(typeCode);
        reopened.GetTimestamp(0).Should().Be(timestamp);
        reopened.GetVector(0).ToArray().Should().BeEquivalentTo(vector, opts => opts.Using<float>(ctx =>
            ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.0001f)).WhenTypeIs<float>());
    }

    [Fact]
    public void Index_HandlesLargeDataset()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 384; // Common embedding dimension
        const int entryCount = 1000;
        using var index = AjviIndex.Create(filePath, dimensions);

        // Act
        for (int i = 0; i < entryCount; i++)
        {
            index.AddEntry(
                CreateContentHash($"document_{i}"),
                Guid.NewGuid(),
                (byte)(i % 10),
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + i,
                CreateNormalizedVector(dimensions, i));
        }

        // Assert
        index.EntryCount.Should().Be(entryCount);

        // Verify we can read all entries
        for (int i = 0; i < entryCount; i++)
        {
            var vector = index.GetVector(i);
            vector.Length.Should().Be(dimensions);
        }
    }

    #endregion

    #region Float16 Precision Tests

    [Fact]
    public void Float16Precision_PreservesVectorApproximately()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float16);

        var originalVector = CreateNormalizedVector(dimensions, 42);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, originalVector);

        // Act
        var retrievedVector = index.GetVector(0).ToArray();

        // Assert - Float16 has lower precision, so we allow more tolerance
        for (int i = 0; i < dimensions; i++)
        {
            retrievedVector[i].Should().BeApproximately(originalVector[i], 0.01f);
        }
    }

    [Fact]
    public void Float32Precision_PreservesVectorExactly()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float32);

        var originalVector = CreateNormalizedVector(dimensions, 42);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, originalVector);

        // Act
        var retrievedVector = index.GetVector(0).ToArray();

        // Assert - Float32 should preserve values exactly
        retrievedVector.Should().BeEquivalentTo(originalVector);
    }

    [Fact]
    public void Float16_SearchAccuracyIsAcceptable()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 128;
        using var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float16);

        // Add entries
        for (int i = 0; i < 50; i++)
        {
            index.AddEntry(CreateContentHash($"doc{i}"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, i));
        }

        var queryVector = CreateNormalizedVector(dimensions, 25);

        // Act
        var results = index.Search(queryVector, topK: 5);

        // Assert - Entry 25 should still be the top result
        results[0].Index.Should().Be(25);
        results[0].Score.Should().BeGreaterThan(0.99f); // High similarity expected
    }

    #endregion

    #region File Corruption Detection Tests

    [Fact]
    public void Open_InvalidMagicNumber_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Create a file with invalid magic number
        var invalidData = new byte[32];
        invalidData[0] = 0xFF; // Invalid magic
        File.WriteAllBytes(filePath, invalidData);

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid magic number*");
    }

    [Fact]
    public void Open_UnsupportedVersion_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        // Create header with wrong version
        var header = new byte[32];
        // Magic number 0x494A5641 "AJVI" in little-endian byte order
        header[0] = 0x41; // low byte
        header[1] = 0x56;
        header[2] = 0x4A;
        header[3] = 0x49; // high byte
        header[4] = 99; // Invalid version
        header[5] = 0; // Float32 precision
        header[6] = 64; // dimensions low byte
        header[7] = 0;  // dimensions high byte
        // Entry count = 0 (bytes 8-15)
        File.WriteAllBytes(filePath, header);

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*Unsupported version*");
    }

    [Fact]
    public void Open_FileTooSmallForHeader_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        File.WriteAllBytes(filePath, new byte[10]); // Too small

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*too small*");
    }

    [Fact]
    public void Open_FileTruncated_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;

        // Create valid index with entries
        using (var index = AjviIndex.Create(filePath, dimensions))
        {
            for (int i = 0; i < 5; i++)
            {
                index.AddEntry(CreateContentHash($"doc{i}"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, i));
            }
        }

        // Truncate the file to simulate corruption
        var fileInfo = new FileInfo(filePath);
        using (var fs = new FileStream(filePath, FileMode.Open))
        {
            fs.SetLength(fileInfo.Length / 2);
        }

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*corrupted*");
    }

    [Fact]
    public void Open_ZeroDimensions_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        var header = new byte[32];
        // Magic number 0x494A5641 "AJVI" in little-endian byte order
        header[0] = 0x41; // low byte
        header[1] = 0x56;
        header[2] = 0x4A;
        header[3] = 0x49; // high byte
        header[4] = 1; // Version
        header[5] = 0; // Float32
        header[6] = 0; // dimensions = 0
        header[7] = 0;
        File.WriteAllBytes(filePath, header);

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*Dimensions cannot be zero*");
    }

    [Fact]
    public void Open_InvalidPrecision_ThrowsInvalidDataException()
    {
        // Arrange
        var filePath = GetTempFilePath();

        var header = new byte[32];
        // Magic number 0x494A5641 "AJVI" in little-endian byte order
        header[0] = 0x41; // low byte
        header[1] = 0x56;
        header[2] = 0x4A;
        header[3] = 0x49; // high byte
        header[4] = 1; // Version
        header[5] = 99; // Invalid precision
        header[6] = 64; // dimensions
        header[7] = 0;
        File.WriteAllBytes(filePath, header);

        // Act & Assert
        var action = () => AjviIndex.Open(filePath);
        action.Should().Throw<InvalidDataException>()
            .WithMessage("*Invalid precision*");
    }

    #endregion

    #region Version Compatibility Tests

    [Fact]
    public void Version1Index_CanBeOpenedAndRead()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        var docId = Guid.NewGuid();
        var hash = CreateContentHash("v1 content");
        var vector = CreateNormalizedVector(dimensions, 1);

        // Create version 1 index
        using (var index = AjviIndex.Create(filePath, dimensions))
        {
            index.AddEntry(hash, docId, 42, 1234567890L, vector);
        }

        // Act
        using var reopened = AjviIndex.Open(filePath);

        // Assert
        reopened.Dimensions.Should().Be(dimensions);
        reopened.EntryCount.Should().Be(1);
        reopened.GetDocumentId(0).Should().Be(docId);
        reopened.GetTypeCode(0).Should().Be(42);
        reopened.GetTimestamp(0).Should().Be(1234567890L);
    }

    #endregion

    #region Disposal and Resource Management Tests

    [Fact]
    public void Dispose_ReleasesFileHandle()
    {
        // Arrange
        var filePath = GetTempFilePath();
        var index = AjviIndex.Create(filePath, 64);

        // Act
        index.Dispose();

        // Assert - File should be deletable (no locks)
        var action = () => File.Delete(filePath);
        action.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var filePath = GetTempFilePath();
        var index = AjviIndex.Create(filePath, 64);

        // Act & Assert
        var action = () =>
        {
            index.Dispose();
            index.Dispose();
            index.Dispose();
        };
        action.Should().NotThrow();
    }

    [Fact]
    public void AfterDispose_MethodsThrowObjectDisposedException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        var index = AjviIndex.Create(filePath, 64);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(64, 1));
        index.Dispose();

        // Act & Assert
        FluentActions.Invoking(() => { _ = index.GetVector(0).ToArray(); })
            .Should().Throw<ObjectDisposedException>();

        FluentActions.Invoking(() => index.GetDocumentId(0))
            .Should().Throw<ObjectDisposedException>();

        FluentActions.Invoking(() => index.Search(CreateNormalizedVector(64, 1)))
            .Should().Throw<ObjectDisposedException>();

        FluentActions.Invoking(() => index.AddEntry(CreateContentHash("new"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(64, 1)))
            .Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Index Out of Range Tests

    [Fact]
    public void GetVector_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(64, 1));

        // Act & Assert
        FluentActions.Invoking(() => { _ = index.GetVector(-1).ToArray(); })
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetDocumentId_IndexTooLarge_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(64, 1));

        // Act & Assert
        var action = () => index.GetDocumentId(100);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetTypeCode_EmptyIndex_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using var index = AjviIndex.Create(filePath, 64);

        // Act & Assert
        var action = () => index.GetTypeCode(0);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }

    #endregion

    #region Metadata Retrieval Tests

    [Fact]
    public void GetContentHash_ReturnsCorrectHash()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        var expectedHash = CreateContentHash("specific content");
        index.AddEntry(expectedHash, Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, 1));

        // Act
        var actualHash = index.GetContentHash(0);

        // Assert
        actualHash.Should().BeEquivalentTo(expectedHash);
    }

    [Fact]
    public void GetDocumentId_ReturnsCorrectGuid()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        var expectedId = Guid.NewGuid();
        index.AddEntry(CreateContentHash("test"), expectedId, 0, 1000L, CreateNormalizedVector(dimensions, 1));

        // Act
        var actualId = index.GetDocumentId(0);

        // Assert
        actualId.Should().Be(expectedId);
    }

    [Fact]
    public void GetTypeCode_ReturnsCorrectValue()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        const byte expectedType = 123;
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), expectedType, 1000L, CreateNormalizedVector(dimensions, 1));

        // Act
        var actualType = index.GetTypeCode(0);

        // Assert
        actualType.Should().Be(expectedType);
    }

    [Fact]
    public void GetTimestamp_ReturnsCorrectValue()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;
        using var index = AjviIndex.Create(filePath, dimensions);

        const long expectedTimestamp = 1705849200000L; // 2024-01-21 13:00:00 UTC in milliseconds
        index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, expectedTimestamp, CreateNormalizedVector(dimensions, 1));

        // Act
        var actualTimestamp = index.GetTimestamp(0);

        // Assert
        actualTimestamp.Should().Be(expectedTimestamp);
    }

    #endregion

    #region Binary Format Verification Tests

    [Fact]
    public void Index_HasCorrectHeaderSize()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using (var index = AjviIndex.Create(filePath, 64))
        {
            // Empty index - just header
        }

        // Act
        var fileInfo = new FileInfo(filePath);

        // Assert
        fileInfo.Length.Should().Be(32); // Header is exactly 32 bytes
    }

    [Fact]
    public void Index_Float32EntryHasCorrectSize()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;

        using (var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float32))
        {
            index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, 1));
        }

        // Act
        var fileInfo = new FileInfo(filePath);

        // Assert
        // Header (32) + Entry (32 hash + 16 guid + 1 type + 8 timestamp + 64*4 vector) = 32 + 313 = 345
        int expectedEntrySize = 32 + 16 + 1 + 8 + (dimensions * 4);
        fileInfo.Length.Should().Be(32 + expectedEntrySize);
    }

    [Fact]
    public void Index_Float16EntryHasCorrectSize()
    {
        // Arrange
        var filePath = GetTempFilePath();
        const int dimensions = 64;

        using (var index = AjviIndex.Create(filePath, dimensions, VectorPrecision.Float16))
        {
            index.AddEntry(CreateContentHash("test"), Guid.NewGuid(), 0, 1000L, CreateNormalizedVector(dimensions, 1));
        }

        // Act
        var fileInfo = new FileInfo(filePath);

        // Assert
        // Header (32) + Entry (32 hash + 16 guid + 1 type + 8 timestamp + 64*2 vector) = 32 + 185 = 217
        int expectedEntrySize = 32 + 16 + 1 + 8 + (dimensions * 2);
        fileInfo.Length.Should().Be(32 + expectedEntrySize);
    }

    [Fact]
    public void Index_MagicNumberIsCorrect()
    {
        // Arrange
        var filePath = GetTempFilePath();
        using (var index = AjviIndex.Create(filePath, 64))
        {
            // Empty index
        }

        // Act
        var bytes = File.ReadAllBytes(filePath);

        // Assert - 0x494A5641 "AJVI" in little-endian byte order
        bytes[0].Should().Be(0x41); // low byte
        bytes[1].Should().Be(0x56);
        bytes[2].Should().Be(0x4A);
        bytes[3].Should().Be(0x49); // high byte
    }

    #endregion
}
