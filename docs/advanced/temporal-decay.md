# Temporal Decay

This guide covers using temporal relevance decay to boost recent content in search results automatically.

## Table of Contents

1. [Overview](#overview)
2. [The DecayCalculator](#the-decaycalculator)
3. [Configuring Decay](#configuring-decay)
4. [Decay Categories](#decay-categories)
5. [Use Cases](#use-cases)
6. [Complete Examples](#complete-examples)
7. [Best Practices](#best-practices)

## Overview

Temporal decay adjusts search scores based on document age, ensuring recent content ranks higher than older content with similar textual relevance. This is particularly useful for:

- **News and articles** - Recent news more relevant
- **Knowledge bases** - Fresher documentation preferred
- **Agent conversations** - Recent sessions more useful
- **Social media** - Trending recent posts
- **E-commerce** - New products highlighted

### How It Works

The library uses **exponential decay** with a configurable half-life:

```
decay_factor = 0.5 ^ (age_days / half_life_days)

final_score = base_score × decay_factor
```

**Example:**

- Document age: 90 days
- Half-life: 90 days
- Decay factor: 0.5 ^ (90/90) = 0.5
- Final score: base_score × 0.5

After one half-life period, the score is reduced by 50%.

## The DecayCalculator

```csharp
using ZeroProximity.VectorizedContentIndexer.Utilities;

public static class DecayCalculator
{
    // Calculate decay factor for a timestamp
    public static double CalculateDecayFactor(
        DateTime timestamp,
        DateTime now,
        double halfLifeDays);

    // Classify decay factor into categories
    public static DecayCategory ClassifyDecay(double decayFactor);
}

public enum DecayCategory
{
    Fresh,      // decay > 0.9 (very recent)
    Good,       // 0.7 - 0.9
    Aging,      // 0.4 - 0.7
    Decaying,   // 0.1 - 0.4
    Expiring    // < 0.1 (very old)
}
```

### Basic Usage

```csharp
var now = DateTime.UtcNow;
var documentTimestamp = DateTime.Parse("2024-01-15");

var decay = DecayCalculator.CalculateDecayFactor(
    timestamp: documentTimestamp,
    now: now,
    halfLifeDays: 90.0
);

Console.WriteLine($"Decay factor: {decay:F3}");
// Output: "Decay factor: 0.500"

var category = DecayCalculator.ClassifyDecay(decay);
Console.WriteLine($"Category: {category}");
// Output: "Category: Aging"
```

## Configuring Decay

### Via SearchEngineOptions

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.ApplyDecay = true;              // Enable temporal decay
    options.DecayHalfLifeDays = 90.0;       // Half-life period in days
});
```

### Programmatically

```csharp
var options = new SearchEngineOptions
{
    ApplyDecay = true,
    DecayHalfLifeDays = 90.0
};

// Pass to search engine constructor
var searchEngine = new HybridSearcher<Article>(
    luceneEngine,
    vectorEngine,
    options: options
);
```

### Choosing a Half-Life

The half-life determines how quickly content ages. Common values:

| Use Case | Half-Life | Reasoning |
|----------|-----------|-----------|
| Breaking news | 1-3 days | News becomes stale very quickly |
| Blog articles | 30 days | Monthly relevance cycle |
| Documentation | 90-180 days | Updates quarterly/semi-annually |
| Reference content | 365 days | Annually updated |
| Historical archives | Disabled | Age not relevant |

**Formula:**

To find half-life for desired decay:

```
half_life_days = desired_age / log2(1 / desired_decay)

Example: Want 50% decay after 60 days?
half_life_days = 60 / log2(1 / 0.5) = 60 / 1 = 60 days
```

## Decay Categories

### Category Thresholds

```csharp
public static DecayCategory ClassifyDecay(double decayFactor)
{
    if (decayFactor > 0.9) return DecayCategory.Fresh;
    if (decayFactor > 0.7) return DecayCategory.Good;
    if (decayFactor > 0.4) return DecayCategory.Aging;
    if (decayFactor > 0.1) return DecayCategory.Decaying;
    return DecayCategory.Expiring;
}
```

### Age Examples (90-day half-life)

| Age | Decay Factor | Category | Notes |
|-----|--------------|----------|-------|
| 1 day | 0.992 | Fresh | Essentially no decay |
| 7 days | 0.948 | Fresh | ~5% decay |
| 30 days | 0.794 | Good | ~20% decay |
| 60 days | 0.630 | Aging | ~37% decay |
| 90 days | 0.500 | Aging | 50% decay (1 half-life) |
| 180 days | 0.250 | Decaying | 75% decay (2 half-lives) |
| 270 days | 0.125 | Decaying | 87.5% decay (3 half-lives) |
| 365 days | 0.081 | Expiring | ~92% decay |

### Visual Decay Curve

```
Decay Factor
1.0 ┤ █
    │  █
0.9 ┤   █  ← Fresh
    │    █
0.7 ┤     █  ← Good
    │      █
0.5 ┤       █  ← Half-life
    │         █  ← Aging
0.3 ┤          █
    │            █  ← Decaying
0.1 ┤              █
    │                █  ← Expiring
0.0 ┤                  ████████
    └────────────────────────────
     0   30   60   90  120  150  180  (days)
```

## Use Cases

### Use Case 1: News Search

**Goal:** Recent news significantly more relevant than old news.

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.ApplyDecay = true;
    options.DecayHalfLifeDays = 3.0;  // Very short half-life
});

// Results:
// - Today's news: decay = 1.0
// - 3-day-old news: decay = 0.5 (50% penalty)
// - 6-day-old news: decay = 0.25 (75% penalty)
// - 1-month-old news: decay ~0.001 (99.9% penalty)
```

### Use Case 2: Knowledge Base

**Goal:** Prefer recent docs but don't completely ignore older content.

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.ApplyDecay = true;
    options.DecayHalfLifeDays = 180.0;  // 6-month half-life
});

// Results:
// - This week's docs: decay = 0.99
// - 3-month-old docs: decay = 0.71 (29% penalty)
// - 6-month-old docs: decay = 0.50 (50% penalty)
// - 1-year-old docs: decay = 0.25 (75% penalty)
```

### Use Case 3: Agent Sessions

**Goal:** Recent sessions slightly preferred, but older sessions still valuable.

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.ApplyDecay = true;
    options.DecayHalfLifeDays = 90.0;  // 3-month half-life
});

// Results:
// - This month's sessions: decay = 0.79
// - 3-month-old sessions: decay = 0.50
// - 6-month-old sessions: decay = 0.25
// - 1-year-old sessions: decay = 0.06
```

### Use Case 4: E-commerce Products

**Goal:** New products boosted but established products not penalized.

```csharp
services.Configure<SearchEngineOptions>(options =>
{
    options.ApplyDecay = true;
    options.DecayHalfLifeDays = 365.0;  // 1-year half-life
});

// Results:
// - New products (this week): decay = 0.99
// - 6-month-old products: decay = 0.71
// - 1-year-old products: decay = 0.50
// - 2-year-old products: decay = 0.25
```

## Complete Examples

### Example 1: Custom Decay Implementation

```csharp
public class ArticleSearchService
{
    private readonly ISearchEngine<Article> _searchEngine;
    private readonly SearchEngineOptions _options;

    public async Task<List<ArticleResult>> SearchWithDecayAsync(
        string query,
        int maxResults = 10)
    {
        // Get base search results
        var results = await _searchEngine.SearchAsync(query, maxResults * 2);

        var now = DateTime.UtcNow;
        var scoredResults = new List<ArticleResult>();

        foreach (var result in results)
        {
            // Calculate decay
            var decay = DecayCalculator.CalculateDecayFactor(
                result.Document.GetTimestamp(),
                now,
                _options.DecayHalfLifeDays
            );

            // Apply decay to score
            var finalScore = result.Score * decay;

            // Classify freshness
            var category = DecayCalculator.ClassifyDecay(decay);

            scoredResults.Add(new ArticleResult
            {
                Article = result.Document,
                BaseScore = result.Score,
                DecayFactor = decay,
                FinalScore = finalScore,
                FreshnessCategory = category,
                Age = now - result.Document.GetTimestamp()
            });
        }

        // Re-sort by final score
        return scoredResults
            .OrderByDescending(r => r.FinalScore)
            .Take(maxResults)
            .ToList();
    }
}

public record ArticleResult
{
    public required Article Article { get; init; }
    public required double BaseScore { get; init; }
    public required double DecayFactor { get; init; }
    public required double FinalScore { get; init; }
    public required DecayCategory FreshnessCategory { get; init; }
    public required TimeSpan Age { get; init; }
}

// Display results
foreach (var result in results)
{
    Console.WriteLine($"Title: {result.Article.Title}");
    Console.WriteLine($"Age: {result.Age.TotalDays:F0} days");
    Console.WriteLine($"Base Score: {result.BaseScore:F3}");
    Console.WriteLine($"Decay Factor: {result.DecayFactor:F3} ({result.FreshnessCategory})");
    Console.WriteLine($"Final Score: {result.FinalScore:F3}");
    Console.WriteLine("---");
}
```

### Example 2: Adaptive Half-Life

Adjust half-life based on content type:

```csharp
public class AdaptiveDecayService
{
    public double GetHalfLife(Article article)
    {
        return article.Category switch
        {
            "News" => 3.0,           // News ages quickly
            "Tutorial" => 90.0,      // Tutorials age moderately
            "Reference" => 365.0,    // Reference docs age slowly
            "Historical" => double.MaxValue,  // Never decay
            _ => 90.0                // Default
        };
    }

    public async Task<List<SearchResult<Article>>> SearchWithAdaptiveDecayAsync(
        string query)
    {
        var results = await _searchEngine.SearchAsync(query, maxResults: 100);
        var now = DateTime.UtcNow;

        var scoredResults = results.Select(result =>
        {
            var halfLife = GetHalfLife(result.Document);
            var decay = DecayCalculator.CalculateDecayFactor(
                result.Document.GetTimestamp(),
                now,
                halfLife
            );

            return new
            {
                Result = result,
                Decay = decay,
                FinalScore = result.Score * decay
            };
        })
        .OrderByDescending(r => r.FinalScore)
        .Take(10)
        .Select(r => r.Result)
        .ToList();

        return scoredResults;
    }
}
```

### Example 3: Reinforcement Learning

Boost documents that have been recently accessed:

```csharp
public class ReinforcedDecayService
{
    private readonly Dictionary<string, DateTime> _lastAccessTimes = new();

    public void RecordAccess(string documentId)
    {
        _lastAccessTimes[documentId] = DateTime.UtcNow;
    }

    public async Task<List<SearchResult<Article>>> SearchWithReinforcementAsync(
        string query)
    {
        var results = await _searchEngine.SearchAsync(query, maxResults: 100);
        var now = DateTime.UtcNow;

        var scoredResults = results.Select(result =>
        {
            // Base temporal decay
            var baseDecay = DecayCalculator.CalculateDecayFactor(
                result.Document.GetTimestamp(),
                now,
                halfLifeDays: 90.0
            );

            // Reinforcement from recent access
            var reinforcement = 1.0;
            if (_lastAccessTimes.TryGetValue(result.Document.Id, out var lastAccess))
            {
                var daysSinceAccess = (now - lastAccess).TotalDays;
                // Boost recently accessed docs
                reinforcement = 1.0 + Math.Exp(-daysSinceAccess / 30.0);
            }

            var finalScore = result.Score * baseDecay * reinforcement;

            return new
            {
                Result = result,
                FinalScore = finalScore,
                Reinforcement = reinforcement
            };
        })
        .OrderByDescending(r => r.FinalScore)
        .Take(10)
        .Select(r => r.Result)
        .ToList();

        return scoredResults;
    }
}

// Usage:
var service = new ReinforcedDecayService();

// User searches and views a document
var results = await service.SearchWithReinforcementAsync("query");
var selected = results.First();

// Record that user accessed this document
service.RecordAccess(selected.Document.Id);

// Next search will boost this document if it matches
```

### Example 4: Threshold-Based Filtering

Filter out documents below a decay threshold:

```csharp
public class FreshContentService
{
    public async Task<List<SearchResult<Article>>> SearchFreshContentAsync(
        string query,
        double minDecayFactor = 0.5)
    {
        var results = await _searchEngine.SearchAsync(query, maxResults: 100);
        var now = DateTime.UtcNow;

        var freshResults = results
            .Select(result =>
            {
                var decay = DecayCalculator.CalculateDecayFactor(
                    result.Document.GetTimestamp(),
                    now,
                    halfLifeDays: 90.0
                );

                return new { Result = result, Decay = decay };
            })
            .Where(r => r.Decay >= minDecayFactor)  // Filter old content
            .OrderByDescending(r => r.Result.Score * r.Decay)
            .Take(10)
            .Select(r => r.Result)
            .ToList();

        return freshResults;
    }
}

// Only show content with decay > 0.5 (< 90 days old)
var recentResults = await service.SearchFreshContentAsync("query", minDecayFactor: 0.5);
```

## Best Practices

### 1. Choose Appropriate Half-Life

```csharp
// Too short: Older content becomes completely irrelevant
options.DecayHalfLifeDays = 1.0;  // Only yesterday's content ranks well

// Too long: Decay has minimal effect
options.DecayHalfLifeDays = 3650.0;  // 10 years - effectively no decay

// Just right: Balance between freshness and relevance
options.DecayHalfLifeDays = 90.0;  // 3 months for documentation
```

### 2. Test Decay Impact

```csharp
[Test]
public async Task TestDecayImpact()
{
    var oldDoc = new Article
    {
        Id = "1",
        Title = "Test Article",
        PublishedAt = DateTime.UtcNow.AddDays(-180)  // 6 months old
    };

    var newDoc = new Article
    {
        Id = "2",
        Title = "Test Article",
        PublishedAt = DateTime.UtcNow.AddDays(-1)  // 1 day old
    };

    await engine.IndexAsync(oldDoc);
    await engine.IndexAsync(newDoc);

    var results = await engine.SearchAsync("Test Article");

    // With decay enabled, newer doc should rank higher
    Assert.AreEqual(newDoc.Id, results[0].Document.Id);
}
```

### 3. Display Freshness Indicators

```csharp
public void DisplayWithFreshness(ArticleResult result)
{
    var freshnessIcon = result.FreshnessCategory switch
    {
        DecayCategory.Fresh => "🟢",
        DecayCategory.Good => "🟡",
        DecayCategory.Aging => "🟠",
        DecayCategory.Decaying => "🔴",
        DecayCategory.Expiring => "⚫",
        _ => ""
    };

    Console.WriteLine($"{freshnessIcon} {result.Article.Title}");
    Console.WriteLine($"   Published: {result.Article.PublishedAt:d}");
    Console.WriteLine($"   Freshness: {result.FreshnessCategory}");
}
```

### 4. Combine with Other Signals

```csharp
public double CalculateFinalScore(
    double baseScore,
    double decayFactor,
    int viewCount,
    double userRating)
{
    // Weighted combination of multiple signals
    return (baseScore * 0.5) +        // Search relevance: 50%
           (decayFactor * 0.2) +      // Freshness: 20%
           (viewCount / 10000.0 * 0.2) +  // Popularity: 20%
           (userRating / 5.0 * 0.1);  // Quality: 10%
}
```

### 5. Consider Content Update Times

```csharp
public interface ISearchable
{
    string Id { get; }
    string GetSearchableText();
    DateTime GetTimestamp();  // Use last modified date, not created date
}

// For articles that get updated
public class Article : ISearchable
{
    public DateTime CreatedAt { get; set; }
    public DateTime LastModifiedAt { get; set; }

    // Use last modified for decay calculation
    public DateTime GetTimestamp() => LastModifiedAt;
}
```

### 6. Provide Decay Override Options

```csharp
public async Task<List<SearchResult<Article>>> SearchAsync(
    string query,
    bool applyDecay = true,
    double? customHalfLife = null)
{
    var results = await _searchEngine.SearchAsync(query);

    if (!applyDecay)
        return results;

    var halfLife = customHalfLife ?? _options.DecayHalfLifeDays;
    var now = DateTime.UtcNow;

    return results
        .Select(r => new
        {
            Result = r,
            Decay = DecayCalculator.CalculateDecayFactor(
                r.Document.GetTimestamp(), now, halfLife),
        })
        .OrderByDescending(r => r.Result.Score * r.Decay)
        .Select(r => r.Result)
        .ToList();
}

// Usage:
var withDecay = await service.SearchAsync("query", applyDecay: true);
var withoutDecay = await service.SearchAsync("query", applyDecay: false);
var customDecay = await service.SearchAsync("query", customHalfLife: 30.0);
```

## See Also

- [API Documentation](../api/README.md) - DecayCalculator reference
- [Configuration](../getting-started.md#configuration) - Configuring decay options
- [Performance Tuning](performance-tuning.md) - Optimizing decay calculations
