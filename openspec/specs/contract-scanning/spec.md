# Contract Scanning Specification

## Purpose
Discovers and extracts API contracts, route templates, HTTP methods, parameter bindings, and DTO types from C# interfaces decorated with RestEase attributes, both at compile time and dynamically at runtime from external assemblies.

## Requirements

### Requirement: RestEase Interface Scanning
The system SHALL scan .NET interfaces decorated with RestEase attributes (`[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Path]`, `[Query]`, `[Body]`) and produce structured contract endpoint definitions.

#### Scenario: Scan synchronous and asynchronous endpoints
- **WHEN** an interface containing RestEase method definitions returning `Task<T>` or `Task` is provided
- **THEN** the system extracts the HTTP method, the URL route template, the return type, and parameter mappings for path, query, and request body.

### Requirement: Dynamic Assembly Contract Discovery
The system SHALL load contract interfaces dynamically at runtime from specified assembly files (`.dll`) or directories without requiring compile-time assembly references.

#### Scenario: Load contracts from external assembly
- **WHEN** an external contract assembly path or directory is provided to the dynamic contract loader
- **THEN** the system scans and registers all valid RestEase contract interfaces found in the loaded assemblies.
