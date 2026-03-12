// ============================================================================
// Agent Session Example - Hybrid Search for Conversation History
// ============================================================================
//
// This sample demonstrates how to use ZeroProximity.VectorizedContentIndexer
// for searching through agent conversation history. This pattern is useful for:
// - Finding relevant past conversations to provide as context
// - Searching for specific commands or techniques mentioned previously
// - Building conversational memory for AI agents
//
// Key concepts demonstrated:
// - Hybrid search (lexical + semantic combined)
// - IDocument implementation with metadata
// - Custom Lucene field mapper
// - Hierarchical documents (Session -> Messages)
// - Context expansion (N messages before/after match)
//
// Run with: dotnet run --project samples/AgentSessionExample
// ============================================================================

using System.Text.Json;
using AgentSessionExample.Models;
using ZeroProximity.VectorizedContentIndexer.Embeddings;
using ZeroProximity.VectorizedContentIndexer.Search;
using ZeroProximity.VectorizedContentIndexer.Search.Lucene;
using ZeroProximity.VectorizedContentIndexer.Search.Vector;

Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine("Agent Session Example - Hybrid Search for Conversations");
Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine();

// ============================================================================
// Step 1: Initialize Search Components
// ============================================================================

Console.WriteLine("[Step 1] Initializing search components...");

// Create embedding provider for semantic search
var embeddings = await EmbeddingProviderFactory.CreateAsync();
Console.WriteLine($"         Embedding provider: {embeddings.ModelName}");

// Use temporary directories for this demo
var basePath = Path.Combine(Path.GetTempPath(), "agent-session-example", Guid.NewGuid().ToString("N")[..8]);
var lucenePath = Path.Combine(basePath, "lucene");
var vectorPath = Path.Combine(basePath, "vector");
Console.WriteLine($"         Index path: {basePath}");

// Create document mapper for SessionDocument
var mapper = new SessionDocumentMapper();

// Create Lucene search engine for keyword (BM25) search
var luceneEngine = new LuceneSearchEngine<SessionDocument>(lucenePath, mapper);
await luceneEngine.InitializeAsync();
Console.WriteLine("         Lucene engine initialized.");

// Create vector search engine for semantic search
var vectorEngine = new VectorSearchEngine<SessionDocument>(vectorPath, embeddings);
await vectorEngine.InitializeAsync();
Console.WriteLine("         Vector engine initialized.");

// Create hybrid searcher combining both
await using var hybridSearcher = new HybridSearcher<SessionDocument>(
    luceneEngine,
    vectorEngine,
    lexicalWeight: 0.5f,    // Equal weight to keywords
    semanticWeight: 0.5f,   // and semantics
    rrfK: 60);              // RRF constant (default)

await hybridSearcher.InitializeAsync();
Console.WriteLine("         Hybrid searcher initialized.");
Console.WriteLine();

// ============================================================================
// Step 2: Load Sample Sessions
// ============================================================================

Console.WriteLine("[Step 2] Loading sample sessions...");

var sessions = await LoadSampleSessionsAsync();
Console.WriteLine($"         Loaded {sessions.Count} sessions with {sessions.Sum(s => s.Messages.Count)} total messages.");

// Convert to SessionDocument adapters
var sessionDocuments = sessions.Select(s => new SessionDocument(s)).ToList();
Console.WriteLine();

// ============================================================================
// Step 3: Index Sessions
// ============================================================================

Console.WriteLine("[Step 3] Indexing sessions...");
var sw = System.Diagnostics.Stopwatch.StartNew();

// Index all session documents
await hybridSearcher.IndexManyAsync(sessionDocuments);

// Cache documents for retrieval (hybrid searcher caches in both engines)
hybridSearcher.CacheDocuments(sessionDocuments);

sw.Stop();
Console.WriteLine($"         Indexed {sessionDocuments.Count} sessions in {sw.ElapsedMilliseconds}ms");
Console.WriteLine();

// ============================================================================
// Step 4: Demonstrate Different Search Modes
// ============================================================================

Console.WriteLine("[Step 4] Demonstrating search modes...");
Console.WriteLine();

// Example queries
var queries = new[]
{
    "authentication error login failed",
    "How do I parse JSON in C#?",
    "async await task",
    "file not found exception"
};

foreach (var query in queries)
{
    Console.WriteLine($"Query: \"{query}\"");
    Console.WriteLine("-".PadRight(60, '-'));

    // Lexical (keyword) search
    var lexicalResults = await hybridSearcher.SearchAsync(query, maxResults: 3, mode: SearchMode.Lexical);
    Console.WriteLine($"\n  [Lexical Search] Found {lexicalResults.Count} results:");
    foreach (var result in lexicalResults.Take(2))
    {
        var session = result.Document.Session;
        Console.WriteLine($"    - Session '{session.Id}' (score: {result.Score:F4})");
        Console.WriteLine($"      Agent: {session.AgentType}, Messages: {session.MessageCount}");
        if (result.Highlight != null)
        {
            var truncatedHighlight = result.Highlight.Length > 100
                ? result.Highlight[..100] + "..."
                : result.Highlight;
            Console.WriteLine($"      Match: {truncatedHighlight}");
        }
    }

    // Semantic search
    var semanticResults = await hybridSearcher.SearchAsync(query, maxResults: 3, mode: SearchMode.Semantic);
    Console.WriteLine($"\n  [Semantic Search] Found {semanticResults.Count} results:");
    foreach (var result in semanticResults.Take(2))
    {
        var session = result.Document.Session;
        Console.WriteLine($"    - Session '{session.Id}' (score: {result.Score:F4})");
        Console.WriteLine($"      Agent: {session.AgentType}, Messages: {session.MessageCount}");
    }

    // Hybrid search (RRF fusion)
    var hybridResults = await hybridSearcher.SearchAsync(query, maxResults: 3, mode: SearchMode.Hybrid);
    Console.WriteLine($"\n  [Hybrid Search] Found {hybridResults.Count} results:");
    foreach (var result in hybridResults.Take(2))
    {
        var session = result.Document.Session;
        Console.WriteLine($"    - Session '{session.Id}' (score: {result.Score:F4})");
        Console.WriteLine($"      Agent: {session.AgentType}, Messages: {session.MessageCount}");
    }

    // Hybrid search with scoring breakdown
    var detailedResults = await hybridSearcher.SearchWithBreakdownAsync(query, maxResults: 2);
    Console.WriteLine($"\n  [Detailed Breakdown] Top result:");
    if (detailedResults.Count > 0)
    {
        var top = detailedResults[0];
        Console.WriteLine($"    - Session: {top.Document.Session.Id}");
        Console.WriteLine($"    - Combined Score: {top.Score:F4}");
        Console.WriteLine($"    - Lexical Score:  {(top.LexicalScore.HasValue ? top.LexicalScore.Value.ToString("F4") : "n/a")}");
        Console.WriteLine($"    - Semantic Score: {(top.SemanticScore.HasValue ? top.SemanticScore.Value.ToString("F4") : "n/a")}");
    }

    Console.WriteLine();
    Console.WriteLine();
}

// ============================================================================
// Step 5: Context Expansion Example
// ============================================================================

Console.WriteLine("[Step 5] Context expansion example...");
Console.WriteLine();

// Find a session with messages and show context expansion
var sessionWithMessages = sessionDocuments.FirstOrDefault(s => s.Session.Messages.Count >= 4);
if (sessionWithMessages != null)
{
    var messages = sessionWithMessages.Session.Messages;
    var middleMessage = messages[messages.Count / 2];

    Console.WriteLine($"Session: {sessionWithMessages.Id}");
    Console.WriteLine($"Reference message: {middleMessage.Id}");
    Console.WriteLine();

    var (before, match, after) = sessionWithMessages.GetContextWindow(middleMessage.Id, before: 2, after: 2);

    Console.WriteLine("  Messages BEFORE:");
    foreach (var msg in before)
    {
        var content = msg.Content.Length > 80 ? msg.Content[..80] + "..." : msg.Content;
        Console.WriteLine($"    [{msg.Role}]: {content}");
    }

    Console.WriteLine($"\n  MATCHED message:");
    if (match != null)
    {
        var content = match.Content.Length > 80 ? match.Content[..80] + "..." : match.Content;
        Console.WriteLine($"    [{match.Role}]: {content}");
    }

    Console.WriteLine($"\n  Messages AFTER:");
    foreach (var msg in after)
    {
        var content = msg.Content.Length > 80 ? msg.Content[..80] + "..." : msg.Content;
        Console.WriteLine($"    [{msg.Role}]: {content}");
    }
}
Console.WriteLine();

// ============================================================================
// Step 6: Show Statistics
// ============================================================================

Console.WriteLine("[Step 6] Index statistics...");
var docCount = await hybridSearcher.GetCountAsync();
Console.WriteLine($"         Document count: {docCount}");

var vectorStats = await vectorEngine.GetStatsAsync();
Console.WriteLine($"         Vector entries: {vectorStats.EntryCount}");
Console.WriteLine($"         Vector size: {vectorStats.SizeMB:F2} MB");

var luceneStats = await luceneEngine.GetStatsAsync();
Console.WriteLine($"         Lucene docs: {luceneStats.DocumentCount}");
Console.WriteLine($"         Lucene size: {luceneStats.SizeMB:F2} MB");
Console.WriteLine();

// ============================================================================
// Cleanup
// ============================================================================

Console.WriteLine("[Cleanup] Disposing engines and removing temp files...");
await luceneEngine.DisposeAsync();
await vectorEngine.DisposeAsync();

try
{
    Directory.Delete(basePath, recursive: true);
    Console.WriteLine("         Done.");
}
catch
{
    Console.WriteLine("         Could not delete temp directory (files may be in use).");
}

Console.WriteLine();
Console.WriteLine("=".PadRight(70, '='));
Console.WriteLine("Agent Session Example Complete!");
Console.WriteLine("=".PadRight(70, '='));


// ============================================================================
// Helper: Load Sample Sessions
// ============================================================================
async Task<List<Session>> LoadSampleSessionsAsync()
{
    var dataPath = Path.Combine(AppContext.BaseDirectory, "Data", "sample-sessions.json");

    if (File.Exists(dataPath))
    {
        var json = await File.ReadAllTextAsync(dataPath);
        var loaded = JsonSerializer.Deserialize<List<Session>>(json, JsonOptions.Default);
        return loaded ?? CreateInlineSampleSessions();
    }

    return CreateInlineSampleSessions();
}

// ============================================================================
// Helper: Create inline sample sessions when file not available
// ============================================================================
static List<Session> CreateInlineSampleSessions()
{
    var now = DateTime.UtcNow;

    return new List<Session>
    {
        // Session 1: Authentication debugging
        new Session
        {
            Id = "session-001",
            AgentType = "code-assistant",
            ProjectPath = "/projects/web-api",
            StartedAt = now.AddHours(-5),
            EndedAt = now.AddHours(-4),
            IsActive = false,
            Summary = "Debugging authentication errors in the login flow",
            Messages = new List<Message>
            {
                Message.CreateUserMessage("session-001", 0,
                    "I'm getting an authentication error when users try to log in. The error says 'Invalid credentials' even with correct passwords.",
                    now.AddHours(-5)),
                Message.CreateAssistantMessage("session-001", 1,
                    "Let me help you debug this authentication issue. First, let's check if the password hashing is consistent. Are you using the same hashing algorithm for registration and login? Can you show me the relevant code?",
                    now.AddHours(-5).AddMinutes(1)),
                Message.CreateUserMessage("session-001", 2,
                    "Here's my login method:\n```csharp\npublic async Task<bool> ValidateUser(string email, string password)\n{\n    var user = await _db.Users.FindByEmailAsync(email);\n    return BCrypt.Verify(password, user.PasswordHash);\n}\n```",
                    now.AddHours(-5).AddMinutes(3)),
                Message.CreateAssistantMessage("session-001", 3,
                    "The code looks correct for BCrypt verification. The issue might be in how passwords are hashed during registration. Check that you're using the same BCrypt work factor. Also verify the password isn't being trimmed or modified before hashing.",
                    now.AddHours(-4).AddMinutes(55),
                    new List<string> { "file_read:/src/Auth/UserService.cs" })
            }
        },

        // Session 2: JSON parsing help
        new Session
        {
            Id = "session-002",
            AgentType = "code-assistant",
            ProjectPath = "/projects/data-processor",
            StartedAt = now.AddHours(-3),
            EndedAt = now.AddHours(-2),
            IsActive = false,
            Summary = "Help with JSON deserialization and handling nested objects",
            Messages = new List<Message>
            {
                Message.CreateUserMessage("session-002", 0,
                    "How do I parse JSON with nested objects in C#? I need to deserialize a complex API response.",
                    now.AddHours(-3)),
                Message.CreateAssistantMessage("session-002", 1,
                    "For JSON parsing in C#, you have two main options: System.Text.Json (built-in) or Newtonsoft.Json. Here's how to deserialize nested objects:\n\n```csharp\n// Using System.Text.Json\nvar response = JsonSerializer.Deserialize<ApiResponse>(json);\n\n// Define your models\npublic record ApiResponse(User User, List<Order> Orders);\npublic record User(string Name, Address Address);\npublic record Address(string City, string Country);\n```",
                    now.AddHours(-3).AddMinutes(1)),
                Message.CreateUserMessage("session-002", 2,
                    "What if the JSON property names don't match my C# property names?",
                    now.AddHours(-3).AddMinutes(5)),
                Message.CreateAssistantMessage("session-002", 3,
                    "You can use attributes to map different names:\n\n```csharp\npublic record User(\n    [property: JsonPropertyName(\"user_name\")] string Name,\n    [property: JsonPropertyName(\"mail_address\")] string Email\n);\n```\n\nOr configure globally:\n```csharp\nvar options = new JsonSerializerOptions\n{\n    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower\n};\n```",
                    now.AddHours(-2).AddMinutes(50))
            }
        },

        // Session 3: Async/await patterns
        new Session
        {
            Id = "session-003",
            AgentType = "code-assistant",
            ProjectPath = "/projects/api-gateway",
            StartedAt = now.AddHours(-1),
            IsActive = true,
            Summary = "Best practices for async/await and Task handling",
            Messages = new List<Message>
            {
                Message.CreateUserMessage("session-003", 0,
                    "I'm having issues with async/await. My tasks seem to be blocking sometimes. What are the best practices?",
                    now.AddHours(-1)),
                Message.CreateAssistantMessage("session-003", 1,
                    "Common async/await issues and best practices:\n\n1. **Never block on async code**: Don't use `.Result` or `.Wait()` - this can cause deadlocks\n2. **Use ConfigureAwait(false)** in library code\n3. **Avoid async void** except for event handlers\n4. **Use Task.WhenAll** for parallel operations\n\nWhat specific issue are you seeing?",
                    now.AddHours(-1).AddMinutes(1)),
                Message.CreateUserMessage("session-003", 2,
                    "I have multiple independent API calls that I want to run in parallel. Currently I'm awaiting each one sequentially.",
                    now.AddHours(-1).AddMinutes(5)),
                Message.CreateAssistantMessage("session-003", 3,
                    "Here's how to run them in parallel with Task.WhenAll:\n\n```csharp\n// Instead of this (sequential):\nvar user = await GetUserAsync();\nvar orders = await GetOrdersAsync();\nvar settings = await GetSettingsAsync();\n\n// Do this (parallel):\nvar userTask = GetUserAsync();\nvar ordersTask = GetOrdersAsync();\nvar settingsTask = GetSettingsAsync();\n\nawait Task.WhenAll(userTask, ordersTask, settingsTask);\n\nvar user = userTask.Result; // Safe after WhenAll\nvar orders = ordersTask.Result;\nvar settings = settingsTask.Result;\n```\n\nThis runs all three calls concurrently, reducing total time from sum of all durations to just the longest one.",
                    now.AddMinutes(-55))
            }
        },

        // Session 4: File handling
        new Session
        {
            Id = "session-004",
            AgentType = "code-assistant",
            ProjectPath = "/projects/file-processor",
            StartedAt = now.AddDays(-1),
            EndedAt = now.AddDays(-1).AddHours(1),
            IsActive = false,
            Summary = "File I/O operations and handling FileNotFoundException",
            Messages = new List<Message>
            {
                Message.CreateUserMessage("session-004", 0,
                    "I'm getting FileNotFoundException when trying to read a config file. How should I handle this?",
                    now.AddDays(-1)),
                Message.CreateAssistantMessage("session-004", 1,
                    "There are several approaches to handle missing files:\n\n1. **Check existence first**:\n```csharp\nif (File.Exists(path))\n{\n    var content = File.ReadAllText(path);\n}\nelse\n{\n    // Create default or throw meaningful error\n}\n```\n\n2. **Use try-catch for atomic operation**:\n```csharp\ntry\n{\n    var content = File.ReadAllText(path);\n}\ncatch (FileNotFoundException)\n{\n    // Handle missing file\n}\n```\n\nThe second approach is better when the file might be deleted between the check and read.",
                    now.AddDays(-1).AddMinutes(2)),
                Message.CreateUserMessage("session-004", 2,
                    "What about creating the file with default content if it doesn't exist?",
                    now.AddDays(-1).AddMinutes(10)),
                Message.CreateAssistantMessage("session-004", 3,
                    "Here's a pattern for creating files with defaults:\n\n```csharp\npublic static async Task<Config> LoadOrCreateConfigAsync(string path)\n{\n    if (!File.Exists(path))\n    {\n        var defaultConfig = new Config { /* defaults */ };\n        var json = JsonSerializer.Serialize(defaultConfig);\n        await File.WriteAllTextAsync(path, json);\n        return defaultConfig;\n    }\n    \n    var content = await File.ReadAllTextAsync(path);\n    return JsonSerializer.Deserialize<Config>(content)!;\n}\n```",
                    now.AddDays(-1).AddMinutes(55))
            }
        },

        // Session 5: Database queries
        new Session
        {
            Id = "session-005",
            AgentType = "research-agent",
            ProjectPath = "/projects/analytics",
            StartedAt = now.AddDays(-2),
            EndedAt = now.AddDays(-2).AddHours(2),
            IsActive = false,
            Summary = "Optimizing Entity Framework queries and N+1 problem",
            Messages = new List<Message>
            {
                Message.CreateUserMessage("session-005", 0,
                    "My EF Core queries are slow. I think I have an N+1 query problem. How do I fix it?",
                    now.AddDays(-2)),
                Message.CreateAssistantMessage("session-005", 1,
                    "The N+1 problem occurs when you fetch a list and then lazy-load related data for each item. Here's how to fix it:\n\n**Use eager loading with Include:**\n```csharp\n// Instead of lazy loading:\nvar orders = await _db.Orders.ToListAsync();\nforeach (var order in orders)\n{\n    var customer = order.Customer; // N additional queries!\n}\n\n// Use Include:\nvar orders = await _db.Orders\n    .Include(o => o.Customer)\n    .Include(o => o.OrderItems)\n    .ToListAsync(); // Single query with joins\n```",
                    now.AddDays(-2).AddMinutes(2)),
                Message.CreateUserMessage("session-005", 2,
                    "What if I have deep nesting, like Order -> OrderItems -> Product -> Category?",
                    now.AddDays(-2).AddMinutes(15)),
                Message.CreateAssistantMessage("session-005", 3,
                    "For deeply nested includes, use ThenInclude:\n\n```csharp\nvar orders = await _db.Orders\n    .Include(o => o.Customer)\n    .Include(o => o.OrderItems)\n        .ThenInclude(oi => oi.Product)\n            .ThenInclude(p => p.Category)\n    .ToListAsync();\n```\n\nBe careful with deep includes as they can create large cartesian products. Consider using split queries:\n```csharp\nvar orders = await _db.Orders\n    .Include(o => o.OrderItems)\n    .AsSplitQuery() // Separate queries instead of joins\n    .ToListAsync();\n```",
                    now.AddDays(-2).AddHours(1).AddMinutes(50))
            }
        }
    };
}

// ============================================================================
// Helper class for cached JsonSerializerOptions (CA1869 compliance)
// ============================================================================
file static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
