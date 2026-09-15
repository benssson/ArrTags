# AGENTS.md

## Project

This is a Jellyfin plugin targeting Jellyfin 12.

## General rules

- Follow the existing project structure and Jellyfin 12 plugin conventions.
- Prefer existing Jellyfin APIs over custom implementations.
- Do not introduce dependencies unless necessary.
- Do not modify unrelated files.
- Do not create commits unless explicitly asked.
- Do not claim something works without building/testing it.

## Development workflow

For significant changes:

1. Inspect the relevant existing code.
2. Explain the proposed approach before implementing it.
3. Make the smallest appropriate change.
4. Build the project.
5. Run relevant tests.
6. Review the resulting diff.

## Compatibility

Maintain compatibility with the project's declared Jellyfin and .NET versions.