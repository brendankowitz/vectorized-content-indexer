# Custom Field Mapping

This guide covers creating custom Lucene field mappers to control exactly how your documents are indexed, enabling advanced features like field boosting, filtering, faceting, and range queries.

## Table of Contents

1. [Overview](#overview)
2. [The ILuceneDocumentMapper Interface](#the-ilucinedocumentmapper-interface)
3. [Field Types](#field-types)
4. [Common Patterns](#common-patterns)
5. [Complete Examples](#complete-examples)
6. [Advanced Techniques](#advanced-techniques)
7. [Best Practices](#best-practices)

## Overview

By default, `LuceneSearchEngine` uses `DefaultLuceneDocumentMapper` which provides basic indexing. Custom mappers give you fine-grained control over:

- **Field analysis**: Which fields are analyzed (tokenized) vs. stored as-is
- **Field boosting**: Weight certain fields higher in search results
- **Storage**: What gets stored for retrieval vs. just indexed
- **Filtering**: Create exact-match fields for filtering
- **Range queries**: Enable numeric and date range searches
- **Faceting**: Support categorical field aggregation

## The ILuceneDocumentMapper Interface

```csharp
public interface ILuceneDocumentMapper<TDocument>
    where TDocument : ISearchable
{
    // Map your document to Lucene's Document
    Document MapToLuceneDocument(TDocument document);

    // Map Lucene's Document back to your document
    TDocument MapFromLuceneDocument(Document luceneDocument);

    // Field names for internal use
    string IdField { get; }
    string ContentField { get; }
    string TimestampField { get; }
}
```

### Minimal Implementation

```csharp
public class SimpleMapper : ILuceneDocumentMapper<Article>
{
    public string IdField => "Id";
    public string ContentField => "Content";
    public string TimestampField => "Timestamp";

    public Document MapToLuceneDocument(Article article)
    {
        var doc = new Document();

        // Required fields
        doc.Add(new StringField(IdField, article.Id, Field.Store.YES));
        doc.Add(new TextField(ContentField, article.GetSearchableText(), Field.Store.YES));
        doc.Add(new Int64Field(TimestampField, article.GetTimestamp().Ticks, Field.Store.YES));

        return doc;
    }

    public Article MapFromLuceneDocument(Document luceneDoc)
    {
        // Reconstruct from stored fields
        var id = luceneDoc.Get(IdField);
        var content = luceneDoc.Get(ContentField);
        var ticks = long.Parse(luceneDoc.Get(TimestampField));

        return new Article
        {
            Id = id,
            Content = content,
            PublishedAt = new DateTime(ticks)
        };
    }
}
```

## Field Types

### 1. StringField

Exact-match field (not analyzed, not tokenized).

```csharp
// Example: Category, Author, Tags
doc.Add(new StringField("Category", "Technology", Field.Store.YES));
doc.Add(new StringField("Author", "John Doe", Field.Store.YES));

// Usage: Exact match queries
// Query: Category:Technology
```

**Use Cases:**
- IDs, UUIDs, codes
- Enum values
- Exact tag matching
- Filter fields

---

### 2. TextField

Analyzed field (tokenized, stemmed, lowercased).

```csharp
// Example: Title, body content
doc.Add(new TextField("Title", article.Title, Field.Store.YES)
{
    Boost = 2.0f  // Title matches score higher
});

doc.Add(new TextField("Body", article.Body, Field.Store.YES));

// Usage: Full-text search
// Query: Title:async OR Body:performance
```

**Use Cases:**
- Article content
- Descriptions
- User comments
- Any text requiring full-text search

**Boosting:**

```csharp
// Title 2x more important than body
doc.Add(new TextField("Title", title, Field.Store.YES) { Boost = 2.0f });
doc.Add(new TextField("Body", body, Field.Store.YES) { Boost = 1.0f });
```

---

### 3. Numeric Fields

Enable range queries and numeric sorting.

```csharp
// Int32Field
doc.Add(new Int32Field("ViewCount", article.ViewCount, Field.Store.YES));

// Int64Field
doc.Add(new Int64Field("Timestamp", DateTime.UtcNow.Ticks, Field.Store.YES));

// DoubleField
doc.Add(new DoubleField("Rating", 4.5, Field.Store.YES));

// Usage: Range queries
// Query: ViewCount:[1000 TO *]
// Query: Rating:[4.0 TO 5.0]
```

**Use Cases:**
- View counts, likes, shares
- Timestamps, dates
- Ratings, scores
- Prices, quantities

---

### 4. Stored vs. Indexed

```csharp
// Stored + Indexed (searchable + retrievable)
doc.Add(new TextField("Title", title, Field.Store.YES));

// Indexed only (searchable but not retrievable)
doc.Add(new TextField("FullText", content, Field.Store.NO));

// Stored only (not searchable, just retrieved)
doc.Add(new StoredField("Metadata", jsonMetadata));
```

**Field.Store Options:**

- `Field.Store.YES`: Field value stored for retrieval
- `Field.Store.NO`: Field value not stored (saves space)

**When to Store:**

- **YES**: Small fields you need in results (title, author)
- **NO**: Large fields you can reconstruct (full body text)
- **YES**: IDs and keys for linking back to original data

---

### 5. Multi-Valued Fields

```csharp
// Multiple values for same field
foreach (var tag in article.Tags)
{
    doc.Add(new StringField("Tag", tag, Field.Store.YES));
}

// Usage: Match any tag
// Query: Tag:csharp OR Tag:dotnet

// Or use a delimited string
doc.Add(new TextField("Tags", string.Join(" ", article.Tags), Field.Store.YES));
```

---

## Common Patterns

### Pattern 1: Article/Blog Post Mapper

```csharp
public class ArticleMapper : ILuceneDocumentMapper<Article>
{
    public string IdField => "Id";
    public string ContentField => "Content";
    public string TimestampField => "PublishedAt";

    public Document MapToLuceneDocument(Article article)
    {
        var doc = new Document();

        // ID (exact match, stored)
        doc.Add(new StringField(IdField, article.Id, Field.Store.YES));

        // Title (analyzed, stored, boosted)
        doc.Add(new TextField("Title", article.Title, Field.Store.YES)
        {
            Boost = 2.5f  // Titles very important
        });

        // Body (analyzed, stored)
        doc.Add(new TextField(ContentField, article.Body, Field.Store.YES));

        // Summary (analyzed, stored, boosted)
        doc.Add(new TextField("Summary", article.Summary, Field.Store.YES)
        {
            Boost = 1.5f  // Summaries somewhat important
        });

        // Author (exact match, stored)
        doc.Add(new StringField("Author", article.Author, Field.Store.YES));

        // Category (exact match for filtering)
        doc.Add(new StringField("Category", article.Category, Field.Store.YES));

        // Tags (multi-valued, exact match)
        foreach (var tag in article.Tags)
        {
            doc.Add(new StringField("Tag", tag, Field.Store.YES));
        }

        // Numeric fields
        doc.Add(new Int32Field("ViewCount", article.ViewCount, Field.Store.YES));
        doc.Add(new DoubleField("Rating", article.Rating, Field.Store.YES));

        // Timestamp
        doc.Add(new Int64Field(TimestampField, article.PublishedAt.Ticks, Field.Store.YES));

        // Boolean field (stored as string)
        doc.Add(new StringField("IsFeatured",
            article.IsFeatured.ToString(),
            Field.Store.YES));

        // Combined search field (all text, not stored)
        var allText = $"{article.Title} {article.Summary} {article.Body}";
        doc.Add(new TextField("All", allText, Field.Store.NO)
        {
            Boost = 1.0f
        });

        return doc;
    }

    public Article MapFromLuceneDocument(Document doc)
    {
        return new Article
        {
            Id = doc.Get(IdField),
            Title = doc.Get("Title"),
            Body = doc.Get(ContentField),
            Summary = doc.Get("Summary"),
            Author = doc.Get("Author"),
            Category = doc.Get("Category"),
            Tags = doc.GetValues("Tag").ToList(),
            ViewCount = int.Parse(doc.Get("ViewCount")),
            Rating = double.Parse(doc.Get("Rating")),
            PublishedAt = new DateTime(long.Parse(doc.Get(TimestampField))),
            IsFeatured = bool.Parse(doc.Get("IsFeatured"))
        };
    }
}

// Query examples with this mapper:
// - Title:async AND Category:Technology
// - Tag:csharp AND IsFeatured:true
// - ViewCount:[1000 TO *] AND Rating:[4.0 TO 5.0]
// - Author:"John Doe" AND PublishedAt:[20240101 TO 20241231]
```

---

### Pattern 2: E-commerce Product Mapper

```csharp
public class ProductMapper : ILuceneDocumentMapper<Product>
{
    public string IdField => "Sku";
    public string ContentField => "Description";
    public string TimestampField => "CreatedAt";

    public Document MapToLuceneDocument(Product product)
    {
        var doc = new Document();

        // SKU (product ID)
        doc.Add(new StringField(IdField, product.Sku, Field.Store.YES));

        // Name (analyzed, boosted heavily)
        doc.Add(new TextField("Name", product.Name, Field.Store.YES)
        {
            Boost = 3.0f
        });

        // Description
        doc.Add(new TextField(ContentField, product.Description, Field.Store.YES));

        // Brand (exact match for faceting)
        doc.Add(new StringField("Brand", product.Brand, Field.Store.YES));

        // Category hierarchy
        doc.Add(new StringField("Category", product.Category, Field.Store.YES));
        if (!string.IsNullOrEmpty(product.Subcategory))
        {
            doc.Add(new StringField("Subcategory", product.Subcategory, Field.Store.YES));
        }

        // Price (range queries)
        doc.Add(new DoubleField("Price", product.Price, Field.Store.YES));

        // Stock availability
        doc.Add(new Int32Field("StockQuantity", product.StockQuantity, Field.Store.YES));
        doc.Add(new StringField("InStock",
            (product.StockQuantity > 0).ToString(),
            Field.Store.YES));

        // Ratings
        doc.Add(new DoubleField("AverageRating", product.AverageRating, Field.Store.YES));
        doc.Add(new Int32Field("ReviewCount", product.ReviewCount, Field.Store.YES));

        // Timestamps
        doc.Add(new Int64Field(TimestampField, product.CreatedAt.Ticks, Field.Store.YES));

        // Features (multi-valued text)
        if (product.Features?.Any() == true)
        {
            var featuresText = string.Join(" ", product.Features);
            doc.Add(new TextField("Features", featuresText, Field.Store.YES));
        }

        // Attributes for filtering (color, size, etc.)
        foreach (var attr in product.Attributes)
        {
            doc.Add(new StringField($"Attr_{attr.Key}", attr.Value, Field.Store.YES));
        }

        return doc;
    }

    public Product MapFromLuceneDocument(Document doc)
    {
        // ... reconstruction logic ...
    }
}

// Query examples:
// - Name:laptop AND Brand:Dell AND Price:[500 TO 1000]
// - Category:Electronics AND InStock:true AND AverageRating:[4.0 TO *]
// - Attr_Color:Black AND Attr_Size:Large
```

---

### Pattern 3: Code Search Mapper

```csharp
public class CodeFileMapper : ILuceneDocumentMapper<CodeFile>
{
    public string IdField => "FilePath";
    public string ContentField => "Content";
    public string TimestampField => "ModifiedAt";

    public Document MapToLuceneDocument(CodeFile file)
    {
        var doc = new Document();

        // File path (ID)
        doc.Add(new StringField(IdField, file.FilePath, Field.Store.YES));

        // File name (exact and analyzed)
        doc.Add(new StringField("FileName_Exact", file.FileName, Field.Store.YES));
        doc.Add(new TextField("FileName", file.FileName, Field.Store.YES)
        {
            Boost = 2.0f
        });

        // Code content (analyzed)
        doc.Add(new TextField(ContentField, file.Content, Field.Store.YES));

        // Programming language
        doc.Add(new StringField("Language", file.Language, Field.Store.YES));

        // Extension
        doc.Add(new StringField("Extension", file.Extension, Field.Store.YES));

        // Repository info
        doc.Add(new StringField("Repository", file.Repository, Field.Store.YES));
        doc.Add(new StringField("Branch", file.Branch, Field.Store.YES));

        // Metrics
        doc.Add(new Int32Field("LineCount", file.LineCount, Field.Store.YES));
        doc.Add(new Int32Field("ByteSize", file.ByteSize, Field.Store.YES));

        // Timestamp
        doc.Add(new Int64Field(TimestampField, file.ModifiedAt.Ticks, Field.Store.YES));

        // Author
        doc.Add(new StringField("LastAuthor", file.LastAuthor, Field.Store.YES));

        // Identifiers (class names, function names - exact match)
        foreach (var className in file.ClassNames)
        {
            doc.Add(new StringField("ClassName", className, Field.Store.YES));
        }

        foreach (var funcName in file.FunctionNames)
        {
            doc.Add(new StringField("FunctionName", funcName, Field.Store.YES));
        }

        // Imports/dependencies
        if (file.Imports?.Any() == true)
        {
            var imports = string.Join(" ", file.Imports);
            doc.Add(new TextField("Imports", imports, Field.Store.YES));
        }

        return doc;
    }

    public CodeFile MapFromLuceneDocument(Document doc)
    {
        // ... reconstruction logic ...
    }
}

// Query examples:
// - Language:csharp AND FunctionName:ConfigureServices
// - FileName:*Controller.cs AND Repository:MyApp
// - Extension:.cs AND LineCount:[100 TO 500]
// - Imports:EntityFramework AND Language:csharp
```

---

## Advanced Techniques

### 1. Dynamic Field Boosting

```csharp
public Document MapToLuceneDocument(Article article)
{
    var doc = new Document();

    // Base boost
    var titleBoost = 2.0f;

    // Increase boost for featured articles
    if (article.IsFeatured)
        titleBoost *= 1.5f;

    // Increase boost for highly rated
    if (article.Rating >= 4.5)
        titleBoost *= 1.2f;

    // Increase boost for recent articles
    var ageDays = (DateTime.UtcNow - article.PublishedAt).TotalDays;
    if (ageDays < 7)
        titleBoost *= 1.3f;

    doc.Add(new TextField("Title", article.Title, Field.Store.YES)
    {
        Boost = titleBoost
    });

    return doc;
}
```

---

### 2. Computed Fields

```csharp
public Document MapToLuceneDocument(Product product)
{
    var doc = new Document();

    // ... standard fields ...

    // Computed popularity score
    var popularityScore = CalculatePopularity(product);
    doc.Add(new DoubleField("PopularityScore", popularityScore, Field.Store.YES));

    // Computed discount percentage
    if (product.OriginalPrice > 0)
    {
        var discount = (product.OriginalPrice - product.Price) / product.OriginalPrice * 100;
        doc.Add(new DoubleField("DiscountPercent", discount, Field.Store.YES));
    }

    // Computed age category
    var ageCategory = CalculateAgeCategory(product.CreatedAt);
    doc.Add(new StringField("AgeCategory", ageCategory, Field.Store.YES));

    return doc;
}

private double CalculatePopularity(Product product)
{
    return (product.ViewCount * 0.3) +
           (product.PurchaseCount * 2.0) +
           (product.AverageRating * product.ReviewCount * 0.5);
}

private string CalculateAgeCategory(DateTime createdAt)
{
    var age = DateTime.UtcNow - createdAt;
    if (age.TotalDays < 7) return "New";
    if (age.TotalDays < 30) return "Recent";
    if (age.TotalDays < 180) return "Current";
    return "Catalog";
}

// Query: AgeCategory:New AND PopularityScore:[100 TO *]
```

---

### 3. Hierarchical Facets

```csharp
public Document MapToLuceneDocument(Product product)
{
    var doc = new Document();

    // Category hierarchy: Electronics > Computers > Laptops
    var categoryPath = product.CategoryPath; // e.g., "Electronics/Computers/Laptops"
    var parts = categoryPath.Split('/');

    // Index each level for hierarchical faceting
    for (int i = 0; i < parts.Length; i++)
    {
        var pathSegment = string.Join("/", parts.Take(i + 1));
        doc.Add(new StringField($"CategoryLevel{i}", parts[i], Field.Store.YES));
        doc.Add(new StringField("CategoryPath", pathSegment, Field.Store.YES));
    }

    // Enables queries like:
    // - CategoryLevel0:Electronics
    // - CategoryLevel1:Computers
    // - CategoryPath:Electronics/Computers
    // - CategoryPath:Electronics/Computers/Laptops

    return doc;
}
```

---

### 4. Localization Support

```csharp
public Document MapToLuceneDocument(MultilingualArticle article)
{
    var doc = new Document();

    doc.Add(new StringField("Id", article.Id, Field.Store.YES));

    // Index in multiple languages
    foreach (var translation in article.Translations)
    {
        var langCode = translation.Language; // "en", "es", "fr", etc.

        doc.Add(new TextField($"Title_{langCode}",
            translation.Title,
            Field.Store.YES)
        {
            Boost = 2.0f
        });

        doc.Add(new TextField($"Content_{langCode}",
            translation.Content,
            Field.Store.YES));
    }

    // Default language (always index)
    doc.Add(new TextField("Title",
        article.Translations.First().Title,
        Field.Store.YES)
    {
        Boost = 2.0f
    });

    return doc;
}

// Query by language:
// - Title_en:async OR Content_en:performance
// - Title_es:asíncrono OR Content_es:rendimiento
```

---

## Best Practices

### 1. Field Naming Conventions

```csharp
// Use consistent naming
public const string FIELD_ID = "Id";
public const string FIELD_TITLE = "Title";
public const string FIELD_CONTENT = "Content";

// Use prefixes for field types
// - "num_" for numeric fields
// - "date_" for date fields
// - "exact_" for non-analyzed fields
// - "text_" for analyzed fields

doc.Add(new Int32Field("num_ViewCount", viewCount, Field.Store.YES));
doc.Add(new Int64Field("date_PublishedAt", timestamp, Field.Store.YES));
doc.Add(new StringField("exact_Category", category, Field.Store.YES));
doc.Add(new TextField("text_Description", description, Field.Store.YES));
```

---

### 2. Optimize Storage

```csharp
// Store small, frequently accessed fields
doc.Add(new StringField("Title", title, Field.Store.YES));

// Don't store large fields you can reconstruct
doc.Add(new TextField("FullBodyText", bodyText, Field.Store.NO));

// Store IDs to retrieve full objects from database
doc.Add(new StringField("Id", id, Field.Store.YES));
// Then: var fullObject = await database.GetByIdAsync(id);
```

---

### 3. Use Appropriate Analyzers

```csharp
// For code search, preserve case and special characters
var codeAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

// For natural language, use language-specific analyzers
var englishAnalyzer = new EnglishAnalyzer(LuceneVersion.LUCENE_48);
var spanishAnalyzer = new SpanishAnalyzer(LuceneVersion.LUCENE_48);

// Create field with custom analyzer
var fieldType = new FieldType(TextField.TYPE_STORED)
{
    IsIndexed = true,
    IsTokenized = true,
    StoreTermVectors = true
};

doc.Add(new Field("Code", codeContent, fieldType));
```

---

### 4. Test Field Configuration

```csharp
[Test]
public async Task TestFieldMapping()
{
    var mapper = new ArticleMapper();
    var article = CreateTestArticle();

    // Map to Lucene
    var luceneDoc = mapper.MapToLuceneDocument(article);

    // Verify fields exist
    Assert.IsNotNull(luceneDoc.Get("Title"));
    Assert.IsNotNull(luceneDoc.Get("Category"));

    // Map back
    var reconstructed = mapper.MapFromLuceneDocument(luceneDoc);

    // Verify round-trip
    Assert.AreEqual(article.Id, reconstructed.Id);
    Assert.AreEqual(article.Title, reconstructed.Title);
}

[Test]
public async Task TestFieldBoosting()
{
    // Index documents with different boost values
    var article1 = new Article { Title = "async programming" };
    var article2 = new Article { Body = "async programming" };

    await engine.IndexAsync(article1);
    await engine.IndexAsync(article2);

    // Search
    var results = await engine.SearchAsync("async programming");

    // Verify title match (boosted) scores higher
    Assert.Greater(results[0].Score, results[1].Score);
    Assert.AreEqual(article1.Id, results[0].Document.Id);
}
```

---

### 5. Document Field Configuration

```csharp
// Create comprehensive field summary
public class FieldConfiguration
{
    public static readonly Dictionary<string, FieldInfo> Fields = new()
    {
        ["Id"] = new FieldInfo
        {
            Type = FieldType.String,
            Stored = true,
            Indexed = true,
            Analyzed = false,
            Boost = 1.0f
        },
        ["Title"] = new FieldInfo
        {
            Type = FieldType.Text,
            Stored = true,
            Indexed = true,
            Analyzed = true,
            Boost = 2.5f
        },
        // ... etc
    };
}

public record FieldInfo
{
    public FieldType Type { get; init; }
    public bool Stored { get; init; }
    public bool Indexed { get; init; }
    public bool Analyzed { get; init; }
    public float Boost { get; init; }
}
```

---

## See Also

- [API Documentation](../api/README.md) - ILuceneDocumentMapper interface
- [Hierarchical Documents](hierarchical-documents.md) - Mapping parent-child relationships
- [Performance Tuning](performance-tuning.md) - Index optimization
- [Getting Started](../getting-started.md) - Basic usage
