using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ZeroProximity.VectorizedContentIndexer.Search.Vector;

/// <summary>
/// AJVI (Agent Journal Vector Index) - Memory-mapped binary vector index for efficient semantic search.
/// </summary>
/// <remarks>
/// <para>
/// This index provides high-performance vector similarity search using memory-mapped files
/// for efficient access to large collections of document embeddings.
/// </para>
/// <para>
/// Binary format specification:
/// <code>
/// Header (32 bytes):
///   - Magic: "AJVI" (0x494A5641)
///   - Version: 1 byte
///   - Precision: 1 byte (Float32=0, Float16=1)
///   - Dimensions: 2 bytes (ushort)
///   - Entry count: 8 bytes (long)
///   - Reserved: 18 bytes
///
/// Entry (variable size):
///   - Content hash: 32 bytes (SHA256)
///   - Document ID: 16 bytes (GUID)
///   - Type: 1 byte (generic type field, 0-255)
///   - Timestamp: 8 bytes (Unix milliseconds)
///   - Vector: dimensions * (2 or 4) bytes depending on precision
/// </code>
/// </para>
/// <para>
/// Thread safety: This class is not thread-safe. Concurrent writes require external synchronization.
/// Multiple readers can safely access a read-only index concurrently.
/// </para>
/// </remarks>
public sealed class AjviIndex : IDisposable
{
    private const uint MagicNumber = 0x494A5641; // "AJVI" in hex
    private const byte CurrentVersion = 1;
    private const int HeaderSize = 32;
    private const int ContentHashSize = 32; // SHA256
    private const int DocumentIdSize = 16; // GUID
    private const int TypeCodeSize = 1;
    private const int TimestampSize = 8;

    private readonly string _filePath;
    private readonly VectorPrecision _precision;
    private readonly int _dimensions;
    private readonly int _entrySize;
    private readonly bool _readOnly;

    private FileStream? _fileStream;
    private MemoryMappedFile? _memoryMappedFile;
    private MemoryMappedViewAccessor? _accessor;
    private long _entryCount;
    private bool _disposed;

    /// <summary>
    /// Gets the number of entries in the index.
    /// </summary>
    public long EntryCount => _entryCount;

    /// <summary>
    /// Gets the vector dimensions.
    /// </summary>
    public int Dimensions => _dimensions;

    /// <summary>
    /// Gets the vector precision mode.
    /// </summary>
    public VectorPrecision Precision => _precision;

    /// <summary>
    /// Gets the file path of the index.
    /// </summary>
    public string FilePath => _filePath;

    private AjviIndex(string filePath, int dimensions, VectorPrecision precision, long entryCount, bool readOnly)
    {
        _filePath = filePath;
        _dimensions = dimensions;
        _precision = precision;
        _entryCount = entryCount;
        _readOnly = readOnly;

        int vectorSize = precision == VectorPrecision.Float16 ? dimensions * 2 : dimensions * 4;
        _entrySize = ContentHashSize + DocumentIdSize + TypeCodeSize + TimestampSize + vectorSize;
    }

    /// <summary>
    /// Creates a new AJVI index file.
    /// </summary>
    /// <param name="filePath">Path to the index file.</param>
    /// <param name="dimensions">Vector dimensions (must be between 1 and 65535).</param>
    /// <param name="precision">Vector precision mode. Defaults to Float16 for optimal storage/accuracy tradeoff.</param>
    /// <returns>A new index instance ready for writing.</returns>
    /// <exception cref="ArgumentException">Thrown when dimensions is out of valid range.</exception>
    /// <exception cref="InvalidOperationException">Thrown when file already exists.</exception>
    public static AjviIndex Create(string filePath, int dimensions, VectorPrecision precision = VectorPrecision.Float16)
    {
        if (dimensions <= 0 || dimensions > ushort.MaxValue)
        {
            throw new ArgumentException($"Dimensions must be between 1 and {ushort.MaxValue}", nameof(dimensions));
        }

        if (File.Exists(filePath))
        {
            throw new InvalidOperationException($"Index file already exists: {filePath}");
        }

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Create empty file with header
        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            WriteHeader(fs, dimensions, precision, 0);
        }

        var index = new AjviIndex(filePath, dimensions, precision, 0, readOnly: false);
        index.OpenFile();
        return index;
    }

    /// <summary>
    /// Opens an existing AJVI index file.
    /// </summary>
    /// <param name="filePath">Path to the index file.</param>
    /// <param name="readOnly">Whether to open in read-only mode. Read-only mode allows concurrent readers.</param>
    /// <returns>An opened index instance.</returns>
    /// <exception cref="FileNotFoundException">Thrown when file doesn't exist.</exception>
    /// <exception cref="InvalidDataException">Thrown when file format is invalid or corrupted.</exception>
    public static AjviIndex Open(string filePath, bool readOnly = false)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Index file not found: {filePath}");
        }

        int dimensions;
        VectorPrecision precision;
        long entryCount;

        // Read header in a separate scope to release the file handle before OpenFile
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            (dimensions, precision, entryCount) = ReadHeader(fs);

            // After reading header, validate file size
            int vectorSize = precision == VectorPrecision.Float16 ? dimensions * 2 : dimensions * 4;
            int entrySize = ContentHashSize + DocumentIdSize + TypeCodeSize + TimestampSize + vectorSize;

            checked
            {
                long expectedMinSize = HeaderSize + entryCount * entrySize;
                if (fs.Length < expectedMinSize)
                {
                    throw new InvalidDataException(
                        $"AJVI file is corrupted: file size too small for declared entry count. " +
                        $"Expected at least {expectedMinSize} bytes, got {fs.Length}.");
                }
            }
        } // FileStream is disposed here before OpenFile

        var index = new AjviIndex(filePath, dimensions, precision, entryCount, readOnly);
        index.OpenFile();
        return index;
    }

    private void OpenFile()
    {
        var fileAccess = _readOnly ? FileAccess.Read : FileAccess.ReadWrite;
        // Allow concurrent reads and writes - each process manages its own view
        var fileShare = FileShare.ReadWrite;
        var mmfAccess = _readOnly ? MemoryMappedFileAccess.Read : MemoryMappedFileAccess.ReadWrite;

        _fileStream = new FileStream(_filePath, FileMode.Open, fileAccess, fileShare);
        _memoryMappedFile = MemoryMappedFile.CreateFromFile(
            _fileStream,
            null,
            0,
            mmfAccess,
            HandleInheritability.None,
            leaveOpen: true  // Keep FileStream open for ResizeFile
        );
        _accessor = _memoryMappedFile.CreateViewAccessor(0, 0, mmfAccess);
    }

    private static void WriteHeader(FileStream fs, int dimensions, VectorPrecision precision, long entryCount)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        header.Clear();

        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], MagicNumber);
        header[4] = CurrentVersion;
        header[5] = (byte)precision;
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..8], (ushort)dimensions);
        BinaryPrimitives.WriteInt64LittleEndian(header[8..16], entryCount);
        // bytes 16-31 are reserved (already cleared)

        fs.Write(header);
        fs.Flush();
    }

    private static (int dimensions, VectorPrecision precision, long entryCount) ReadHeader(FileStream fs)
    {
        if (fs.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too small to contain a valid AJVI header");
        }

        Span<byte> header = stackalloc byte[HeaderSize];
        int bytesRead = fs.Read(header);
        if (bytesRead != HeaderSize)
        {
            throw new InvalidDataException($"Failed to read complete header. Expected {HeaderSize} bytes, got {bytesRead}");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header[0..4]);
        if (magic != MagicNumber)
        {
            throw new InvalidDataException($"Invalid magic number. Expected 0x{MagicNumber:X8}, got 0x{magic:X8}");
        }

        var version = header[4];
        if (version != CurrentVersion)
        {
            throw new InvalidDataException($"Unsupported version {version}. Expected {CurrentVersion}");
        }

        var precisionByte = header[5];
        if (precisionByte != (byte)VectorPrecision.Float32 && precisionByte != (byte)VectorPrecision.Float16)
        {
            throw new InvalidDataException($"Invalid precision value: {precisionByte}");
        }
        var precision = (VectorPrecision)precisionByte;

        var dimensions = BinaryPrimitives.ReadUInt16LittleEndian(header[6..8]);
        if (dimensions == 0)
        {
            throw new InvalidDataException("Dimensions cannot be zero");
        }

        var entryCount = BinaryPrimitives.ReadInt64LittleEndian(header[8..16]);
        if (entryCount < 0)
        {
            throw new InvalidDataException($"Invalid entry count: {entryCount}");
        }

        return (dimensions, precision, entryCount);
    }

    /// <summary>
    /// Adds a new entry to the index.
    /// </summary>
    /// <param name="contentHash">SHA256 hash of the content (32 bytes). Used for deduplication.</param>
    /// <param name="documentId">Unique document identifier (GUID).</param>
    /// <param name="typeCode">Generic type identifier (0-255). Application-defined semantics.</param>
    /// <param name="timestamp">Unix timestamp in milliseconds.</param>
    /// <param name="vector">Normalized embedding vector (must match index dimensions).</param>
    /// <exception cref="ObjectDisposedException">Thrown when index is disposed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when index is read-only.</exception>
    /// <exception cref="ArgumentException">Thrown when arguments are invalid.</exception>
    public void AddEntry(byte[] contentHash, Guid documentId, byte typeCode, long timestamp, ReadOnlySpan<float> vector)
    {
        ThrowIfDisposed();

        if (_readOnly)
        {
            throw new InvalidOperationException("Cannot add entries to a read-only index");
        }

        if (contentHash.Length != ContentHashSize)
        {
            throw new ArgumentException($"Content hash must be exactly {ContentHashSize} bytes", nameof(contentHash));
        }

        if (vector.Length != _dimensions)
        {
            throw new ArgumentException($"Vector must have exactly {_dimensions} dimensions", nameof(vector));
        }

        // Ensure file has enough capacity
        long requiredSize = HeaderSize + (_entryCount + 1) * _entrySize;
        if (_fileStream!.Length < requiredSize)
        {
            ResizeFile(requiredSize);
        }

        // Write entry at the end
        long entryOffset = HeaderSize + _entryCount * _entrySize;
        WriteEntry(entryOffset, contentHash, documentId, typeCode, timestamp, vector);

        // Update entry count in header
        _entryCount++;
        UpdateEntryCountInHeader();
    }

    private void WriteEntry(long offset, byte[] contentHash, Guid documentId, byte typeCode, long timestamp, ReadOnlySpan<float> vector)
    {
        long position = offset;

        // Write content hash
        _accessor!.WriteArray(position, contentHash, 0, ContentHashSize);
        position += ContentHashSize;

        // Write document ID
        Span<byte> guidBytes = stackalloc byte[DocumentIdSize];
        documentId.TryWriteBytes(guidBytes);
        for (int i = 0; i < DocumentIdSize; i++)
        {
            _accessor.Write(position + i, guidBytes[i]);
        }
        position += DocumentIdSize;

        // Write type code
        _accessor.Write(position, typeCode);
        position += TypeCodeSize;

        // Write timestamp
        _accessor.Write(position, timestamp);
        position += TimestampSize;

        // Write vector
        WriteVector(position, vector);
    }

    private void WriteVector(long position, ReadOnlySpan<float> vector)
    {
        if (_precision == VectorPrecision.Float32)
        {
            for (int i = 0; i < vector.Length; i++)
            {
                _accessor!.Write(position + i * 4, vector[i]);
            }
        }
        else // Float16
        {
            for (int i = 0; i < vector.Length; i++)
            {
                var half = (Half)vector[i];
                var halfBits = BitConverter.HalfToUInt16Bits(half);
                _accessor!.Write(position + i * 2, halfBits);
            }
        }
    }

    private void ResizeFile(long newSize)
    {
        // Close current memory mapped file
        _accessor?.Dispose();
        _accessor = null;
        _memoryMappedFile?.Dispose();
        _memoryMappedFile = null;

        // Resize file
        _fileStream!.SetLength(newSize);

        // Recreate memory mapped file
        _memoryMappedFile = MemoryMappedFile.CreateFromFile(
            _fileStream,
            null,
            0,
            MemoryMappedFileAccess.ReadWrite,
            HandleInheritability.None,
            leaveOpen: true  // Keep FileStream open
        );
        _accessor = _memoryMappedFile.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
    }

    private void UpdateEntryCountInHeader()
    {
        _accessor!.Write(8, _entryCount); // Entry count is at offset 8
    }

    /// <summary>
    /// Gets the vector for the specified entry index.
    /// </summary>
    /// <param name="index">Entry index (0-based).</param>
    /// <returns>Vector as float array.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public ReadOnlySpan<float> GetVector(long index)
    {
        ThrowIfDisposed();
        ValidateIndex(index);

        long vectorOffset = GetEntryOffset(index) + ContentHashSize + DocumentIdSize + TypeCodeSize + TimestampSize;

        float[] vector = new float[_dimensions];
        ReadVector(vectorOffset, vector);
        return vector;
    }

    private void ReadVector(long position, float[] destination)
    {
        ObjectDisposedException.ThrowIf(_accessor == null, this);

        if (_precision == VectorPrecision.Float32)
        {
            // Fast path: read entire array at once
            _accessor.ReadArray(position, destination, 0, destination.Length);
        }
        else // Float16
        {
            // Read half-precision and convert
            for (int i = 0; i < destination.Length; i++)
            {
                ushort halfBits = _accessor.ReadUInt16(position + i * 2);
                destination[i] = (float)BitConverter.UInt16BitsToHalf(halfBits);
            }
        }
    }

    /// <summary>
    /// Gets the document ID for the specified entry index.
    /// </summary>
    /// <param name="index">Entry index (0-based).</param>
    /// <returns>Document GUID.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public Guid GetDocumentId(long index)
    {
        ThrowIfDisposed();
        ValidateIndex(index);

        long documentIdOffset = GetEntryOffset(index) + ContentHashSize;
        Span<byte> guidBytes = stackalloc byte[DocumentIdSize];

        for (int i = 0; i < DocumentIdSize; i++)
        {
            guidBytes[i] = _accessor!.ReadByte(documentIdOffset + i);
        }

        return new Guid(guidBytes);
    }

    /// <summary>
    /// Gets the content hash for the specified entry index.
    /// </summary>
    /// <param name="index">Entry index (0-based).</param>
    /// <returns>SHA256 content hash (32 bytes).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public byte[] GetContentHash(long index)
    {
        ThrowIfDisposed();
        ValidateIndex(index);

        long hashOffset = GetEntryOffset(index);
        byte[] hash = new byte[ContentHashSize];
        _accessor!.ReadArray(hashOffset, hash, 0, ContentHashSize);
        return hash;
    }

    /// <summary>
    /// Gets the type code for the specified entry index.
    /// </summary>
    /// <param name="index">Entry index (0-based).</param>
    /// <returns>Type code identifier (0-255).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public byte GetTypeCode(long index)
    {
        ThrowIfDisposed();
        ValidateIndex(index);

        long typeCodeOffset = GetEntryOffset(index) + ContentHashSize + DocumentIdSize;
        return _accessor!.ReadByte(typeCodeOffset);
    }

    /// <summary>
    /// Gets the timestamp for the specified entry index.
    /// </summary>
    /// <param name="index">Entry index (0-based).</param>
    /// <returns>Unix timestamp in milliseconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range.</exception>
    public long GetTimestamp(long index)
    {
        ThrowIfDisposed();
        ValidateIndex(index);

        long timestampOffset = GetEntryOffset(index) + ContentHashSize + DocumentIdSize + TypeCodeSize;
        return _accessor!.ReadInt64(timestampOffset);
    }

    /// <summary>
    /// Checks if the index contains an entry with the specified content hash.
    /// </summary>
    /// <param name="contentHash">SHA256 content hash to search for (32 bytes).</param>
    /// <returns>True if hash exists in the index; otherwise, false.</returns>
    /// <exception cref="ArgumentException">Thrown when hash length is invalid.</exception>
    /// <remarks>
    /// This performs a linear scan through all entries. For large indexes,
    /// consider maintaining a separate hash index for O(1) lookups.
    /// </remarks>
    public bool ContainsHash(ReadOnlySpan<byte> contentHash)
    {
        ThrowIfDisposed();

        if (contentHash.Length != ContentHashSize)
        {
            throw new ArgumentException($"Content hash must be exactly {ContentHashSize} bytes", nameof(contentHash));
        }

        // Linear search through all entries
        byte[] entryHash = new byte[ContentHashSize];
        for (long i = 0; i < _entryCount; i++)
        {
            long hashOffset = GetEntryOffset(i);
            _accessor!.ReadArray(hashOffset, entryHash, 0, ContentHashSize);

            if (contentHash.SequenceEqual(entryHash))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Performs SIMD-accelerated similarity search using cosine similarity (dot product of normalized vectors).
    /// </summary>
    /// <param name="queryVector">Normalized query vector (must match index dimensions).</param>
    /// <param name="topK">Number of top results to return. Defaults to 20.</param>
    /// <returns>Top-K results as (index, similarity score) pairs, ordered by score descending.</returns>
    /// <exception cref="ArgumentException">Thrown when query vector dimensions don't match index dimensions.</exception>
    /// <remarks>
    /// <para>
    /// The query vector should be normalized (unit length) for accurate cosine similarity.
    /// The dot product of two normalized vectors equals their cosine similarity.
    /// </para>
    /// <para>
    /// Performance: Uses System.Numerics.Tensors for SIMD-accelerated dot product computation.
    /// </para>
    /// </remarks>
    public IReadOnlyList<(long Index, float Score)> Search(ReadOnlySpan<float> queryVector, int topK = 20)
    {
        ThrowIfDisposed();

        if (queryVector.Length != _dimensions)
        {
            throw new ArgumentException($"Query vector must have exactly {_dimensions} dimensions", nameof(queryVector));
        }

        if (_entryCount == 0)
        {
            return Array.Empty<(long, float)>();
        }

        topK = Math.Min(topK, (int)_entryCount);
        if (topK <= 0)
            return Array.Empty<(long, float)>();

        float[] entryVector = new float[_dimensions];

        // Use min-heap to track top-K (smallest score at top)
        var heap = new PriorityQueue<long, float>(topK);

        for (long i = 0; i < _entryCount; i++)
        {
            long vectorOffset = GetEntryOffset(i) + ContentHashSize + DocumentIdSize + TypeCodeSize + TimestampSize;
            ReadVector(vectorOffset, entryVector);
            float similarity = TensorPrimitives.Dot(queryVector, entryVector);

            if (heap.Count < topK)
            {
                heap.Enqueue(i, similarity);  // Store actual score
            }
            else
            {
                // PriorityQueue.Peek returns the item with LOWEST priority
                heap.TryPeek(out _, out float minScore);
                if (similarity > minScore)
                {
                    heap.EnqueueDequeue(i, similarity);
                }
            }
        }

        // Extract results - heap gives lowest first, we need highest first
        var results = new List<(long Index, float Score)>(heap.Count);
        while (heap.Count > 0)
        {
            heap.TryDequeue(out long idx, out float score);
            results.Add((idx, score));
        }

        results.Reverse(); // Highest scores first
        return results;
    }

    private long GetEntryOffset(long index)
    {
        checked
        {
            return HeaderSize + index * _entrySize;
        }
    }

    private void ValidateIndex(long index)
    {
        if (index < 0 || index >= _entryCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index),
                $"Index {index} is out of range. Valid range is 0 to {_entryCount - 1}");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Disposes the index and releases all resources including file handles and memory-mapped views.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _accessor?.Dispose();
        _accessor = null;

        _memoryMappedFile?.Dispose();
        _memoryMappedFile = null;

        _fileStream?.Dispose();
        _fileStream = null;

        _disposed = true;
    }
}
