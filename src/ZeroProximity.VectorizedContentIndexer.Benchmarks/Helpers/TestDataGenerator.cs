using System.Security.Cryptography;
using System.Text;

namespace ZeroProximity.VectorizedContentIndexer.Benchmarks.Helpers;

/// <summary>
/// Generates realistic test data for benchmarks.
/// </summary>
/// <remarks>
/// <para>
/// Generates text content with realistic vocabulary and structure for accurate
/// performance measurements of embedding generation and search operations.
/// </para>
/// <para>
/// Uses deterministic seeding based on index values to ensure reproducible benchmarks.
/// </para>
/// </remarks>
public static class TestDataGenerator
{
    /// <summary>
    /// Technical vocabulary for realistic document content.
    /// </summary>
    private static readonly string[] TechnicalWords =
    [
        // Programming concepts
        "algorithm", "function", "variable", "class", "interface", "module",
        "dependency", "injection", "abstraction", "polymorphism", "inheritance",
        "encapsulation", "composition", "aggregation", "coupling", "cohesion",

        // Architecture
        "microservice", "monolith", "serverless", "container", "orchestration",
        "gateway", "proxy", "loadbalancer", "cache", "queue", "stream",
        "database", "repository", "service", "controller", "middleware",

        // Operations
        "deployment", "integration", "delivery", "monitoring", "logging",
        "tracing", "alerting", "scaling", "resilience", "availability",
        "performance", "latency", "throughput", "capacity", "reliability",

        // Data
        "vector", "embedding", "index", "search", "query", "filter",
        "aggregation", "pipeline", "transformation", "normalization",
        "similarity", "distance", "ranking", "scoring", "relevance",

        // Machine Learning
        "model", "training", "inference", "prediction", "classification",
        "clustering", "regression", "neural", "network", "layer", "weight",
        "gradient", "optimization", "loss", "accuracy", "precision", "recall"
    ];

    /// <summary>
    /// Common English words for realistic sentence structure.
    /// </summary>
    private static readonly string[] CommonWords =
    [
        "the", "and", "is", "in", "to", "of", "for", "with", "on", "at",
        "by", "from", "that", "this", "which", "are", "was", "were", "been",
        "have", "has", "had", "will", "would", "could", "should", "may",
        "must", "can", "need", "want", "use", "used", "using", "make",
        "made", "making", "provide", "provides", "providing", "enable",
        "enables", "enabling", "support", "supports", "supporting",
        "implement", "implements", "implementing", "create", "creates",
        "creating", "build", "builds", "building", "develop", "develops"
    ];

    /// <summary>
    /// Sample search queries for benchmark scenarios.
    /// </summary>
    public static readonly string[] SampleQueries =
    [
        "vector search algorithm",
        "machine learning model training",
        "microservice architecture patterns",
        "database performance optimization",
        "neural network embedding",
        "semantic similarity ranking",
        "container orchestration deployment",
        "cache invalidation strategy",
        "distributed system resilience",
        "real-time data processing"
    ];

    /// <summary>
    /// Generates text with the specified word count.
    /// </summary>
    /// <param name="wordCount">The number of words to generate.</param>
    /// <param name="seed">Optional seed for reproducible generation.</param>
    /// <returns>Generated text with the specified word count.</returns>
    public static string GenerateText(int wordCount, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var words = new string[wordCount];
        var allWords = TechnicalWords.Concat(CommonWords).ToArray();

        for (int i = 0; i < wordCount; i++)
        {
            // Mix technical words with common words for realistic distribution
            if (random.NextDouble() < 0.4)
            {
                words[i] = TechnicalWords[random.Next(TechnicalWords.Length)];
            }
            else
            {
                words[i] = allWords[random.Next(allWords.Length)];
            }

            // Capitalize first word and words after periods
            if (i == 0 || (i > 0 && words[i - 1].EndsWith('.')))
            {
                words[i] = char.ToUpper(words[i][0]) + words[i][1..];
            }

            // Add punctuation occasionally
            if (i > 0 && i % 12 == 0 && random.NextDouble() < 0.3)
            {
                words[i - 1] += ".";
            }
            else if (i > 0 && i % 8 == 0 && random.NextDouble() < 0.2)
            {
                words[i - 1] += ",";
            }
        }

        // Ensure text ends with a period
        if (!words[^1].EndsWith('.'))
        {
            words[^1] += ".";
        }

        return string.Join(" ", words);
    }

    /// <summary>
    /// Generates a realistic document with the specified index for deterministic content.
    /// </summary>
    /// <param name="index">The document index used as seed.</param>
    /// <param name="minWords">Minimum word count (default 50).</param>
    /// <param name="maxWords">Maximum word count (default 200).</param>
    /// <returns>Generated document content.</returns>
    public static string GenerateDocument(int index, int minWords = 50, int maxWords = 200)
    {
        var random = new Random(index);
        var wordCount = random.Next(minWords, maxWords);
        return GenerateText(wordCount, index);
    }

    /// <summary>
    /// Generates documents with specific word count targets for text length benchmarks.
    /// </summary>
    /// <param name="targetWords">Target word count.</param>
    /// <param name="count">Number of documents to generate.</param>
    /// <returns>List of text strings with approximately the target word count.</returns>
    public static List<string> GenerateTextsByWordCount(int targetWords, int count)
    {
        var texts = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            texts.Add(GenerateText(targetWords, i));
        }
        return texts;
    }

    /// <summary>
    /// Generates a random search query from the sample queries.
    /// </summary>
    /// <param name="seed">Optional seed for reproducible selection.</param>
    /// <returns>A sample search query.</returns>
    public static string GetRandomQuery(int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        return SampleQueries[random.Next(SampleQueries.Length)];
    }

    /// <summary>
    /// Generates a batch of unique search queries.
    /// </summary>
    /// <param name="count">Number of queries to generate.</param>
    /// <returns>List of search queries.</returns>
    public static List<string> GenerateQueries(int count)
    {
        var queries = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            if (i < SampleQueries.Length)
            {
                queries.Add(SampleQueries[i]);
            }
            else
            {
                // Generate additional queries by combining technical terms
                var random = new Random(i);
                var term1 = TechnicalWords[random.Next(TechnicalWords.Length)];
                var term2 = TechnicalWords[random.Next(TechnicalWords.Length)];
                var term3 = TechnicalWords[random.Next(TechnicalWords.Length)];
                queries.Add($"{term1} {term2} {term3}");
            }
        }
        return queries;
    }

    /// <summary>
    /// Generates a random normalized embedding vector for testing.
    /// </summary>
    /// <param name="dimensions">Vector dimensions.</param>
    /// <param name="seed">Optional seed for reproducible generation.</param>
    /// <returns>A normalized float array representing an embedding vector.</returns>
    public static float[] GenerateRandomVector(int dimensions, int? seed = null)
    {
        var random = seed.HasValue ? new Random(seed.Value) : new Random();
        var vector = new float[dimensions];

        for (int i = 0; i < dimensions; i++)
        {
            vector[i] = (float)(random.NextDouble() * 2 - 1); // Range [-1, 1]
        }

        // Normalize the vector
        Normalize(vector);
        return vector;
    }

    /// <summary>
    /// Generates a batch of random normalized vectors.
    /// </summary>
    /// <param name="count">Number of vectors to generate.</param>
    /// <param name="dimensions">Vector dimensions.</param>
    /// <returns>List of normalized embedding vectors.</returns>
    public static List<float[]> GenerateRandomVectors(int count, int dimensions)
    {
        var vectors = new List<float[]>(count);
        for (int i = 0; i < count; i++)
        {
            vectors.Add(GenerateRandomVector(dimensions, i));
        }
        return vectors;
    }

    /// <summary>
    /// Generates a SHA256 content hash for a given string.
    /// </summary>
    /// <param name="content">The content to hash.</param>
    /// <returns>32-byte SHA256 hash.</returns>
    public static byte[] GenerateContentHash(string content)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// L2-normalizes a vector in-place.
    /// </summary>
    /// <param name="vector">The vector to normalize.</param>
    private static void Normalize(Span<float> vector)
    {
        float sumSquares = 0;
        for (int i = 0; i < vector.Length; i++)
        {
            sumSquares += vector[i] * vector[i];
        }

        if (sumSquares > 0)
        {
            float magnitude = MathF.Sqrt(sumSquares);
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] /= magnitude;
            }
        }
    }
}
