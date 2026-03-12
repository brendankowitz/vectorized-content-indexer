# Hierarchical Documents

This guide covers working with hierarchical documents (parent-child relationships) such as sessions containing messages, documents containing chapters, or threads containing posts.

## Table of Contents

1. [Overview](#overview)
2. [The IHierarchicalDocument Interface](#the-ihierarchicaldocument-interface)
3. [Indexing Strategies](#indexing-strategies)
4. [Context Expansion](#context-expansion)
5. [Complete Example](#complete-example)
6. [Best Practices](#best-practices)

## Overview

Many content types have hierarchical structure:

- **Agent Sessions** → Messages
- **Documents** → Chunks or Sections
- **Email Threads** → Individual Emails
- **Forum Threads** → Posts
- **Code Repositories** → Files → Functions

The library supports multiple strategies for indexing and searching hierarchical content.

## The IHierarchicalDocument Interface

```csharp
public interface IHierarchicalDocument<TChild> : IDocument
    where TChild : ISearchable
{
    // Required: Get all children
    IReadOnlyList<TChild> GetChildren();

    // Optional: Get specific child by ID
    TChild? GetChildById(string childId) => null;

    // Optional: Context expansion (N children before)
    IReadOnlyList<TChild> GetChildrenBefore(string childId, int count) =>
        Array.Empty<TChild>();

    // Optional: Context expansion (N children after)
    IReadOnlyList<TChild> GetChildrenAfter(string childId, int count) =>
        Array.Empty<TChild>();
}
```

### Design Notes

- `GetChildren()` is **lazily evaluated** - only called when needed
- Context expansion methods are **optional** (default to empty)
- Children must implement `ISearchable` (minimal interface)
- Parent must implement `IDocument` (for metadata)

## Indexing Strategies

There are four main strategies for indexing hierarchical documents:

### 1. Parent-Only Indexing

Index only the parent document with combined content from all children.

**Pros:**
- Simplest implementation
- Smaller index size
- Fast search

**Cons:**
- No child-level match information
- Cannot expand context around specific child
- Less precise highlighting

**Use Cases:**
- Small hierarchies (< 10 children)
- When parent-level results are sufficient
- Document summaries or overviews

**Example:**

```csharp
public class SessionDocument : IDocument
{
    private readonly Session _session;

    public string Id => _session.Id;

    public string GetSearchableText()
    {
        // Combine all messages into single searchable text
        return string.Join("\n\n",
            _session.Messages.Select(m => $"[{m.Role}] {m.Content}"));
    }

    public DateTime GetTimestamp() => _session.StartedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["MessageCount"] = _session.Messages.Count,
        ["AgentType"] = _session.AgentType,
        ["Duration"] = _session.Duration?.TotalMinutes ?? 0
    };
}

// Index sessions
await searchEngine.IndexAsync(new SessionDocument(session));

// Search returns sessions (don't know which message matched)
var results = await searchEngine.SearchAsync("authentication error");
```

---

### 2. Children-Only Indexing

Index only child documents, each with a reference to its parent.

**Pros:**
- Precise child-level matching
- Can identify which child matched
- Smaller per-document footprint

**Cons:**
- Need separate parent retrieval
- More complex result processing
- Larger total index (more documents)

**Use Cases:**
- Large hierarchies (100+ children)
- RAG systems (chunk-level retrieval)
- When child matches are primary interest

**Example:**

```csharp
public class MessageDocument : IDocument
{
    private readonly Message _message;
    private readonly string _sessionId;

    public string Id => _message.Id;
    public string GetSearchableText() => _message.Content;
    public DateTime GetTimestamp() => _message.Timestamp;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["SessionId"] = _sessionId,  // Link to parent
        ["Role"] = _message.Role.ToString(),
        ["HasToolCalls"] = _message.HasToolCalls
    };
}

// Index messages individually
foreach (var message in session.Messages)
{
    await searchEngine.IndexAsync(new MessageDocument(message, session.Id));
}

// Search returns messages
var results = await searchEngine.SearchAsync("authentication error");

// Group by parent session
var sessionGroups = results
    .GroupBy(r => r.Document.GetMetadata()["SessionId"])
    .Select(g => new
    {
        SessionId = g.Key,
        MatchedMessages = g.ToList(),
        BestScore = g.Max(r => r.Score)
    });

// Retrieve parent sessions separately
foreach (var group in sessionGroups)
{
    var session = await sessionRepository.GetAsync(group.SessionId);
    // Build combined result
}
```

---

### 3. Both (Parent + Children)

Index both parent and children as separate documents.

**Pros:**
- Best of both worlds
- Can search at either level
- Most precise results

**Cons:**
- Largest index size
- Possible duplicate results
- More complex implementation

**Use Cases:**
- Full-featured search systems
- When both session-level and message-level search needed
- Agent conversation history

**Example:**

```csharp
public class SessionDocument : IHierarchicalDocument<MessageDocument>
{
    private readonly Session _session;
    private readonly Lazy<IReadOnlyList<MessageDocument>> _children;

    public SessionDocument(Session session)
    {
        _session = session;
        _children = new Lazy<IReadOnlyList<MessageDocument>>(() =>
            _session.Messages
                .Select((m, idx) => new MessageDocument(m, _session.Id, idx))
                .ToList());
    }

    public string Id => _session.Id;

    public string GetSearchableText() =>
        string.Join("\n\n", _session.Messages.Select(m => m.Content));

    public DateTime GetTimestamp() => _session.StartedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["AgentType"] = _session.AgentType,
        ["MessageCount"] = _session.Messages.Count
    };

    public IReadOnlyList<MessageDocument> GetChildren() => _children.Value;

    public MessageDocument? GetChildById(string childId) =>
        _children.Value.FirstOrDefault(c => c.Id == childId);

    public IReadOnlyList<MessageDocument> GetChildrenBefore(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocument>();

        return _children.Value
            .Where(c => c.Position < child.Position)
            .OrderByDescending(c => c.Position)
            .Take(count)
            .Reverse()  // Back to chronological order
            .ToList();
    }

    public IReadOnlyList<MessageDocument> GetChildrenAfter(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocument>();

        return _children.Value
            .Where(c => c.Position > child.Position)
            .OrderBy(c => c.Position)
            .Take(count)
            .ToList();
    }
}

public class MessageDocument : IDocument
{
    private readonly Message _message;
    private readonly string _sessionId;

    public int Position { get; }

    public MessageDocument(Message message, string sessionId, int position)
    {
        _message = message;
        _sessionId = sessionId;
        Position = position;
    }

    public string Id => _message.Id;
    public string GetSearchableText() => _message.Content;
    public DateTime GetTimestamp() => _message.Timestamp;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["SessionId"] = _sessionId,
        ["Role"] = _message.Role.ToString(),
        ["Position"] = Position
    };
}

// Index both parent and children
var sessionDoc = new SessionDocument(session);

// Index session
await sessionSearchEngine.IndexAsync(sessionDoc);

// Index each message
foreach (var messageDoc in sessionDoc.GetChildren())
{
    await messageSearchEngine.IndexAsync(messageDoc);
}

// Or use a hierarchical indexer (future feature)
// await hierarchicalEngine.IndexHierarchicalAsync(sessionDoc);
```

---

### 4. Parent with Embedded Children

Index parent with children embedded as separate Lucene fields.

**Pros:**
- Single document
- Preserves structure
- Can boost individual children

**Cons:**
- Limited to small hierarchies (< 100 children)
- Complex field mapping
- Larger parent documents

**Use Cases:**
- Small, structured hierarchies
- Fixed-size children (e.g., document sections)
- When field boosting needed

**Example:**

```csharp
public class DocumentWithSections : IDocument
{
    private readonly Document _document;

    public string Id => _document.Id;
    public string GetSearchableText() => _document.Title + "\n" + _document.Summary;
    public DateTime GetTimestamp() => _document.PublishedAt;

    public IDictionary<string, object> GetMetadata()
    {
        var metadata = new Dictionary<string, object>
        {
            ["Title"] = _document.Title,
            ["Author"] = _document.Author
        };

        // Embed up to 10 sections as separate fields
        for (int i = 0; i < Math.Min(_document.Sections.Count, 10); i++)
        {
            metadata[$"Section_{i}_Title"] = _document.Sections[i].Title;
            metadata[$"Section_{i}_Content"] = _document.Sections[i].Content;
        }

        return metadata;
    }
}

// Custom Lucene mapper to index sections with boosting
public class DocumentSectionMapper : ILuceneDocumentMapper<DocumentWithSections>
{
    public Document MapToLuceneDocument(DocumentWithSections doc)
    {
        var luceneDoc = new Document();

        luceneDoc.Add(new StringField("Id", doc.Id, Field.Store.YES));
        luceneDoc.Add(new TextField("Title", doc.GetMetadata()["Title"].ToString()!,
            Field.Store.YES) { Boost = 2.0f });

        // Index sections with decreasing boost
        var metadata = doc.GetMetadata();
        for (int i = 0; i < 10; i++)
        {
            if (metadata.TryGetValue($"Section_{i}_Content", out var content))
            {
                var boost = 1.0f - (i * 0.05f);  // First section: 1.0, last: 0.55
                luceneDoc.Add(new TextField($"Section_{i}",
                    content.ToString()!,
                    Field.Store.YES) { Boost = boost });
            }
        }

        return luceneDoc;
    }
}
```

---

## Context Expansion

Context expansion retrieves surrounding children for better understanding of matches.

### Use Cases

**Agent Conversations:**
```
User: "How do I fix the authentication error?"
AI: [provides solution]
User: "That didn't work"  ← This message matches search
AI: "Let me try a different approach..."
```

Without context expansion, you only see "That didn't work".
With expansion (2 before, 2 after), you see the full conversation flow.

**RAG Systems:**
```
Chunk N-1: [Introduction to async/await]
Chunk N: [Performance optimization]  ← Matches query
Chunk N+1: [Best practices]
```

Retrieving adjacent chunks provides better context for the LLM.

### Implementation

```csharp
public class ConversationSearchService
{
    private readonly ISearchEngine<SessionDocument> _searchEngine;

    public async Task<ExpandedSearchResult> SearchWithContextAsync(
        string query,
        int messagesBefore = 2,
        int messagesAfter = 2)
    {
        // Search for sessions
        var results = await _searchEngine.SearchAsync(query, maxResults: 10);

        var expandedResults = new List<ExpandedSessionResult>();

        foreach (var result in results)
        {
            // Assume we can identify which message(s) matched
            // (This would require child-level search or message indexing)
            var matchedMessageIds = IdentifyMatchedMessages(result);

            foreach (var messageId in matchedMessageIds)
            {
                var before = result.Document.GetChildrenBefore(messageId, messagesBefore);
                var matched = result.Document.GetChildById(messageId);
                var after = result.Document.GetChildrenAfter(messageId, messagesAfter);

                expandedResults.Add(new ExpandedSessionResult
                {
                    Session = result.Document,
                    Score = result.Score,
                    ContextBefore = before,
                    MatchedMessage = matched,
                    ContextAfter = after
                });
            }
        }

        return new ExpandedSearchResult
        {
            Results = expandedResults
        };
    }

    private List<string> IdentifyMatchedMessages(SearchResult<SessionDocument> result)
    {
        // Strategy 1: Use highlight information
        if (result.Highlight != null)
        {
            return result.Document.GetChildren()
                .Where(m => result.Highlight.Contains(m.GetSearchableText().Substring(0, 50)))
                .Select(m => m.Id)
                .ToList();
        }

        // Strategy 2: Search children individually
        // Strategy 3: Store matched child IDs during indexing

        return new List<string>();
    }
}

public record ExpandedSessionResult
{
    public required SessionDocument Session { get; init; }
    public required double Score { get; init; }
    public required IReadOnlyList<MessageDocument> ContextBefore { get; init; }
    public required MessageDocument? MatchedMessage { get; init; }
    public required IReadOnlyList<MessageDocument> ContextAfter { get; init; }
}
```

### Display Example

```csharp
public void DisplayExpandedResult(ExpandedSessionResult result)
{
    Console.WriteLine($"Session: {result.Session.Id} (Score: {result.Score:F3})");
    Console.WriteLine();

    // Context before
    if (result.ContextBefore.Any())
    {
        Console.WriteLine("--- Context Before ---");
        foreach (var msg in result.ContextBefore)
        {
            Console.WriteLine($"[{msg.GetMetadata()["Role"]}] {msg.GetSearchableText()}");
        }
        Console.WriteLine();
    }

    // Matched message (highlighted)
    if (result.MatchedMessage != null)
    {
        Console.WriteLine(">>> MATCHED MESSAGE <<<");
        Console.WriteLine($"[{result.MatchedMessage.GetMetadata()["Role"]}] " +
                         $"{result.MatchedMessage.GetSearchableText()}");
        Console.WriteLine();
    }

    // Context after
    if (result.ContextAfter.Any())
    {
        Console.WriteLine("--- Context After ---");
        foreach (var msg in result.ContextAfter)
        {
            Console.WriteLine($"[{msg.GetMetadata()["Role"]}] {msg.GetSearchableText()}");
        }
    }

    Console.WriteLine(new string('=', 80));
}
```

---

## Complete Example

Here's a complete implementation for agent session search with hierarchical documents:

```csharp
// Models
public record Session
{
    public required string Id { get; init; }
    public required string AgentType { get; init; }
    public required DateTime StartedAt { get; init; }
    public required List<Message> Messages { get; init; }
}

public record Message
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTime Timestamp { get; init; }
}

// Document adapters
public class SessionDocument : IHierarchicalDocument<MessageDocument>
{
    private readonly Session _session;
    private readonly Lazy<List<MessageDocument>> _children;

    public SessionDocument(Session session)
    {
        _session = session;
        _children = new Lazy<List<MessageDocument>>(() =>
            _session.Messages
                .Select((m, idx) => new MessageDocument(m, _session.Id, idx))
                .ToList());
    }

    public string Id => _session.Id;
    public string GetSearchableText() =>
        string.Join("\n\n", _session.Messages.Select(m =>
            $"[{m.Role.ToUpperInvariant()}] {m.Content}"));

    public DateTime GetTimestamp() => _session.StartedAt;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["AgentType"] = _session.AgentType,
        ["MessageCount"] = _session.Messages.Count,
        ["StartedAt"] = _session.StartedAt
    };

    public IReadOnlyList<MessageDocument> GetChildren() => _children.Value;

    public MessageDocument? GetChildById(string childId) =>
        _children.Value.FirstOrDefault(c => c.Id == childId);

    public IReadOnlyList<MessageDocument> GetChildrenBefore(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocument>();

        return _children.Value
            .Where(c => c.Position < child.Position)
            .TakeLast(count)
            .ToList();
    }

    public IReadOnlyList<MessageDocument> GetChildrenAfter(string childId, int count)
    {
        var child = GetChildById(childId);
        if (child == null) return Array.Empty<MessageDocument>();

        return _children.Value
            .Where(c => c.Position > child.Position)
            .Take(count)
            .ToList();
    }

    public Session UnderlyingSession => _session;
}

public class MessageDocument : IDocument
{
    private readonly Message _message;
    private readonly string _sessionId;

    public int Position { get; }

    public MessageDocument(Message message, string sessionId, int position)
    {
        _message = message;
        _sessionId = sessionId;
        Position = position;
    }

    public string Id => _message.Id;
    public string GetSearchableText() => _message.Content;
    public DateTime GetTimestamp() => _message.Timestamp;

    public IDictionary<string, object> GetMetadata() => new Dictionary<string, object>
    {
        ["SessionId"] = _sessionId,
        ["Role"] = _message.Role,
        ["Position"] = Position
    };

    public Message UnderlyingMessage => _message;
}

// Service
public class SessionSearchService
{
    private readonly ISearchEngine<SessionDocument> _searchEngine;

    public SessionSearchService(ISearchEngine<SessionDocument> searchEngine)
    {
        _searchEngine = searchEngine;
    }

    public async Task IndexSessionAsync(Session session)
    {
        var sessionDoc = new SessionDocument(session);
        await _searchEngine.IndexAsync(sessionDoc);
    }

    public async Task<List<SessionSearchResult>> SearchSessionsAsync(
        string query,
        int maxResults = 10)
    {
        var results = await _searchEngine.SearchAsync(query, maxResults);

        return results.Select(r => new SessionSearchResult
        {
            Session = r.Document.UnderlyingSession,
            Score = r.Score,
            Highlight = r.Highlight
        }).ToList();
    }
}

public record SessionSearchResult
{
    public required Session Session { get; init; }
    public required double Score { get; init; }
    public string? Highlight { get; init; }
}

// Usage
var embeddings = await EmbeddingProviderFactory.TryCreateAsync();
var searchEngine = new HybridSearcher<SessionDocument>(
    new LuceneSearchEngine<SessionDocument>("./data/sessions/lucene"),
    new VectorSearchEngine<SessionDocument>("./data/sessions/vector", embeddings)
);

var service = new SessionSearchService(searchEngine);

// Index sessions
foreach (var session in sessions)
{
    await service.IndexSessionAsync(session);
}

// Search
var results = await service.SearchSessionsAsync("authentication error");

foreach (var result in results)
{
    Console.WriteLine($"Session: {result.Session.Id}");
    Console.WriteLine($"Score: {result.Score:F3}");
    Console.WriteLine($"Messages: {result.Session.Messages.Count}");
    Console.WriteLine($"Highlight: {result.Highlight}");
    Console.WriteLine("---");
}
```

---

## Best Practices

### 1. Choose the Right Strategy

- **Parent-Only**: Small hierarchies, summary search
- **Children-Only**: RAG systems, large hierarchies
- **Both**: Full-featured search, moderate hierarchies
- **Embedded**: Fixed structure, field boosting needed

### 2. Lazy Evaluation

```csharp
// Use Lazy<T> to defer child creation
private readonly Lazy<List<MessageDocument>> _children;

// Only computed if GetChildren() called
public IReadOnlyList<MessageDocument> GetChildren() => _children.Value;
```

### 3. Maintain Position Information

```csharp
// Store position for ordering and context expansion
public int Position { get; }

// Enables efficient before/after queries
.Where(c => c.Position < targetPosition)
```

### 4. Optimize Context Expansion

```csharp
// Use LINQ for efficient window queries
public IReadOnlyList<T> GetChildrenBefore(string id, int count)
{
    return _children.Value
        .Where(c => c.Position < targetPosition)
        .TakeLast(count)  // More efficient than OrderByDescending
        .ToList();
}
```

### 5. Cache Parent References

```csharp
// When using Children-Only strategy
private readonly Dictionary<string, Session> _sessionCache = new();

public async Task<Session> GetSessionForMessage(string messageId)
{
    var sessionId = GetSessionIdFromMessage(messageId);
    if (!_sessionCache.TryGetValue(sessionId, out var session))
    {
        session = await _repository.GetSessionAsync(sessionId);
        _sessionCache[sessionId] = session;
    }
    return session;
}
```

### 6. Consider Index Size

**Parent-Only:**
- Index size: N parents
- Storage: Most efficient

**Children-Only:**
- Index size: Sum of all children
- Storage: Moderate (many small documents)

**Both:**
- Index size: N parents + sum of children
- Storage: Largest but most flexible

---

## See Also

- [API Documentation](../api/README.md) - IHierarchicalDocument reference
- [Custom Field Mapping](custom-field-mapping.md) - Advanced Lucene field control
- [Performance Tuning](performance-tuning.md) - Optimize for large hierarchies
- [Agent Session Example](../../samples/AgentSessionExample/) - Complete working example
