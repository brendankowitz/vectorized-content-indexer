# Feature: Hybrid Content Indexing

**Status**: Planning
**Created**: 2026-01-21

## Overview

A reusable .NET library providing hybrid content indexing that combines keyword search (Lucene.NET BM25), vector search (ONNX embeddings with custom AJVI index), and temporal relevance scoring. Designed to serve multiple use cases including RAG systems and knowledge management applications.

## Goals

- Single-assembly library for easy deployment
- Support keyword, semantic, and hybrid search modes
- Include embedded MiniLM-L6-v2 model (no external downloads)
- Generic document abstraction for multiple content types
- Production-ready performance and security

## Non-Goals

- Built-in repository implementations (provide interfaces only)
- UI/visualization components
- Real-time streaming indexing
- Distributed search across multiple nodes

## Use Cases

1. **RAG Systems** (e.g., localagent)
   - Vector search for semantic document retrieval
   - Context expansion for LLM prompts
   - Temporal decay for fresh content prioritization

2. **Agent Session Search** (e.g., agent-session-search-tools)
   - Hybrid search of AI conversation history
   - Message-level and session-level indexing
   - Tool call and metadata filtering

3. **General Document Search**
   - Any application needing keyword + semantic search
   - Knowledge base systems
   - Documentation search

## Investigations

| Investigation | Status | Created | Verdict |
|--------------|--------|---------|---------|
| [component-extraction](investigations/component-extraction.md) | In Progress | 2026-01-21 | Pending |

## Related ADRs

*None yet - investigations will lead to ADRs*

## References

- Source project: `E:\data\src\agent-session-search-tools`
- Target RAG project: `E:\data\src\localagent`
- [Lucene.NET Documentation](https://lucenenet.apache.org/)
- [MiniLM-L6-v2 Model Card](https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2)
