# Contributing to ZeroProximity.VectorizedContentIndexer

Thank you for your interest in contributing! This document provides guidelines and instructions for contributing to the project.

## Table of Contents

1. [Code of Conduct](#code-of-conduct)
2. [Getting Started](#getting-started)
3. [Development Setup](#development-setup)
4. [Building the Project](#building-the-project)
5. [Running Tests](#running-tests)
6. [Code Style](#code-style)
7. [Submitting Changes](#submitting-changes)
8. [Reporting Issues](#reporting-issues)
9. [Feature Requests](#feature-requests)

## Code of Conduct

This project follows a standard code of conduct. Please be respectful and professional in all interactions.

## Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- Git
- A code editor (Visual Studio 2022, VS Code, or Rider)
- Optional: GPU with DirectML support for testing GPU acceleration

### Fork and Clone

1. Fork the repository on GitHub
2. Clone your fork locally:

```bash
git clone https://github.com/YOUR_USERNAME/vectorized-content-indexer.git
cd vectorized-content-indexer
```

3. Add the upstream repository:

```bash
git remote add upstream https://github.com/ORIGINAL_OWNER/vectorized-content-indexer.git
```

4. Create a feature branch:

```bash
git checkout -b feature/your-feature-name
```

## Development Setup

### Repository Structure

```
vectorized-content-indexer/
├── src/
│   ├── ZeroProximity.VectorizedContentIndexer/     # Main library
│   ├── ZeroProximity.VectorizedContentIndexer.Tests/  # Unit tests
│   └── ZeroProximity.VectorizedContentIndexer.Benchmarks/  # Performance tests
├── samples/
│   ├── RagExample/                   # RAG sample application
│   └── AgentSessionExample/          # Agent session sample
├── docs/                             # Documentation
├── README.md
├── CONTRIBUTING.md
└── CHANGELOG.md
```

### Restore Dependencies

```bash
dotnet restore
```

## Building the Project

### Build All Projects

```bash
dotnet build
```

### Build in Release Mode

```bash
dotnet build -c Release
```

### Build Specific Project

```bash
dotnet build src/ZeroProximity.VectorizedContentIndexer
```

## Running Tests

### Run All Tests

```bash
dotnet test
```

### Run Tests with Coverage

```bash
dotnet test /p:CollectCoverage=true /p:CoverageReporter=html
```

Coverage report will be in: `src/ZeroProximity.VectorizedContentIndexer.Tests/coverage/`

### Run Specific Test

```bash
dotnet test --filter "FullyQualifiedName~VectorSearchEngineTests.SearchAsync_ReturnsResults"
```

### Run Tests by Category

```bash
dotnet test --filter "Category=Integration"
```

### Writing Tests

Follow the AAA (Arrange-Act-Assert) pattern:

```csharp
[Test]
public async Task IndexAsync_ValidDocument_AddsToIndex()
{
    // Arrange
    var document = new TestDocument
    {
        Id = "test-1",
        Content = "Test content",
        Timestamp = DateTime.UtcNow
    };

    var engine = CreateTestEngine();

    // Act
    await engine.IndexAsync(document);

    // Assert
    var count = await engine.GetCountAsync();
    Assert.AreEqual(1, count);
}
```

### Test Naming Convention

- `MethodName_StateUnderTest_ExpectedBehavior`
- Examples:
  - `SearchAsync_EmptyQuery_ThrowsArgumentException`
  - `IndexAsync_DuplicateId_UpdatesExisting`
  - `EmbedAsync_ValidText_ReturnsNormalizedVector`

## Code Style

### General Guidelines

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Keep methods focused and small (< 50 lines)
- Add XML documentation comments to all public APIs
- Use `async`/`await` for all I/O operations
- Prefer immutable types (records, readonly structs)

### Naming Conventions

```csharp
// Classes: PascalCase
public class VectorSearchEngine { }

// Interfaces: IPascalCase
public interface ISearchEngine { }

// Methods: PascalCase
public async Task IndexAsync() { }

// Properties: PascalCase
public string Id { get; set; }

// Private fields: _camelCase
private readonly IEmbeddingProvider _embeddings;

// Local variables: camelCase
var searchResults = await SearchAsync();

// Constants: PascalCase
public const int DefaultBatchSize = 100;
```

### XML Documentation

All public APIs must have XML documentation:

```csharp
/// <summary>
/// Indexes a document for search.
/// </summary>
/// <param name="document">The document to index.</param>
/// <param name="cancellationToken">A token to cancel the operation.</param>
/// <returns>A task representing the asynchronous operation.</returns>
/// <exception cref="ArgumentNullException">
/// Thrown when <paramref name="document"/> is null.
/// </exception>
public async Task IndexAsync(
    TDocument document,
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

### Code Formatting

Use the included `.editorconfig` for consistent formatting. Key rules:

- Indentation: 4 spaces (no tabs)
- Line length: Prefer < 120 characters
- Braces: Always use braces, even for single-line blocks
- Trailing whitespace: Remove
- File encoding: UTF-8

### Run Code Formatter

```bash
dotnet format
```

## Submitting Changes

### Before Submitting

1. **Build succeeds**:
   ```bash
   dotnet build -c Release
   ```

2. **All tests pass**:
   ```bash
   dotnet test
   ```

3. **Code is formatted**:
   ```bash
   dotnet format --verify-no-changes
   ```

4. **Documentation updated**:
   - Update XML comments for API changes
   - Update relevant markdown docs
   - Add/update code examples

5. **Changelog updated**:
   - Add entry to `CHANGELOG.md` under `[Unreleased]`

### Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

**Types:**
- `feat`: New feature
- `fix`: Bug fix
- `docs`: Documentation only
- `style`: Code style changes (formatting, etc.)
- `refactor`: Code refactoring
- `perf`: Performance improvements
- `test`: Adding or updating tests
- `chore`: Maintenance tasks

**Examples:**

```
feat(search): add support for multi-valued fields

Implement support for indexing and searching multi-valued fields
in Lucene documents. This enables better faceting and filtering.

Closes #123
```

```
fix(vector): prevent memory leak in AJVI index

Ensure memory-mapped files are properly disposed when clearing
the index. Add using statements for IDisposable resources.

Fixes #456
```

```
docs(api): update ISearchEngine documentation

Add examples for batch indexing and clarify async behavior.
```

### Pull Request Process

1. **Update your branch** with the latest upstream changes:
   ```bash
   git fetch upstream
   git rebase upstream/main
   ```

2. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```

3. **Create Pull Request** on GitHub:
   - Use a clear, descriptive title
   - Fill out the PR template completely
   - Link related issues
   - Add screenshots for UI changes
   - Request reviews from maintainers

4. **Address review feedback**:
   - Make requested changes
   - Push updates to the same branch
   - Re-request review when ready

5. **Squash commits** if requested:
   ```bash
   git rebase -i HEAD~3  # Last 3 commits
   ```

6. **Merge**: Maintainers will merge once approved

### Pull Request Template

```markdown
## Description
Brief description of what this PR does.

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Breaking change
- [ ] Documentation update

## Related Issues
Fixes #123
Relates to #456

## Testing
Describe how you tested these changes.

## Checklist
- [ ] Code builds without errors
- [ ] All tests pass
- [ ] Added/updated tests for changes
- [ ] Updated documentation
- [ ] Updated CHANGELOG.md
- [ ] Follows code style guidelines
```

## Reporting Issues

### Before Creating an Issue

1. **Search existing issues** to avoid duplicates
2. **Try the latest version** to see if it's already fixed
3. **Gather information**:
   - Library version
   - .NET version
   - Operating system
   - Steps to reproduce
   - Expected vs. actual behavior

### Issue Template

```markdown
## Description
Clear description of the issue.

## Environment
- Library Version: 1.0.0
- .NET Version: 9.0
- OS: Windows 11 / Ubuntu 22.04 / macOS 14

## Steps to Reproduce
1. Create a search engine with...
2. Index documents with...
3. Search for...
4. See error

## Expected Behavior
What you expected to happen.

## Actual Behavior
What actually happened.

## Code Sample
```csharp
var engine = new VectorSearchEngine<Article>(...);
await engine.IndexAsync(article);
// Error occurs here
```

## Stack Trace
```
System.NullReferenceException: ...
   at ZeroProximity.VectorizedContentIndexer...
```

## Additional Context
Any other relevant information.
```

## Feature Requests

### Proposing New Features

1. **Create a discussion** first (not an issue)
2. **Describe the use case** and problem it solves
3. **Propose a solution** with code examples
4. **Consider alternatives** and trade-offs
5. **Discuss with maintainers** before implementing

### Feature Request Template

```markdown
## Problem
Describe the problem or limitation you're facing.

## Proposed Solution
How you think it should work.

## Code Example
```csharp
// How you'd like to use the feature
var results = await engine.SearchWithFeatureAsync(...);
```

## Alternatives Considered
Other approaches you thought about.

## Impact
Who would benefit from this feature?
```

## Development Guidelines

### Adding a New Feature

1. **Create an issue/discussion** first
2. **Design the API** (interfaces, public methods)
3. **Write tests** (TDD approach recommended)
4. **Implement the feature**
5. **Update documentation**
6. **Add usage examples**
7. **Update CHANGELOG.md**

### Fixing a Bug

1. **Create a test that reproduces the bug**
2. **Verify the test fails**
3. **Fix the bug**
4. **Verify the test passes**
5. **Add regression tests** if needed
6. **Update CHANGELOG.md**

### Performance Improvements

1. **Create a benchmark** using BenchmarkDotNet
2. **Establish baseline** performance
3. **Make improvements**
4. **Measure impact** with benchmarks
5. **Document performance characteristics**
6. **Update performance tuning docs**

### Breaking Changes

- **Avoid if possible**
- **Discuss with maintainers** first
- **Provide migration guide**
- **Update major version** (semver)
- **Clearly document** in CHANGELOG

## Resources

- [Architecture Documentation](docs/architecture.md)
- [API Reference](docs/api/README.md)
- [Performance Tuning](docs/advanced/performance-tuning.md)
- [Sample Applications](samples/)

## Questions?

- Open a [GitHub Discussion](https://github.com/OWNER/vectorized-content-indexer/discussions)
- Ask in the [Issues](https://github.com/OWNER/vectorized-content-indexer/issues) section

## License

By contributing, you agree that your contributions will be licensed under the same MIT License that covers the project.
