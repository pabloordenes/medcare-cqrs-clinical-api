# MedCareOS CQRS API

[![.NET Continuous Integration](https://github.com/pabloordenes/medcare-cqrs-clinical-api/actions/workflows/ci.yml/badge.svg)](https://github.com/pabloordenes/MedCareOS/actions)

> Note: This repository is currently under active development.

Enterprise clinical management and medical scheduling REST API built with .NET 10. This project serves as a technical demonstration of modern architectural patterns, focusing on strict separation of concerns, domain encapsulation, cloud-ready infrastructure, and automated quality assurance.

## Architectural Overview

The solution strictly adheres to **Clean Architecture** principles, divided into four primary layers: Domain, Application, Infrastructure, and API. The dependency rule points exclusively inward, ensuring the core business logic remains framework-agnostic.

### Key Patterns & Principles

- **Domain-Driven Design (DDD):**
  The core features a rich domain model. Entities (e.g., `Appointment`, `ScheduleBlock`, `Patient`, `Staff`) act as Aggregate Roots, encapsulating their invariants and omitting ORM navigation properties to enforce strict aggregate boundaries. Properties have private setters to prevent anemic models, forcing state changes through explicit domain behaviors.
- **State Machine Logic:**
  Clinical appointment workflows are embedded within the domain entity. Transitions between states (Scheduled, Confirmed, InProgress, Finished, Cancelled, NoShow) are protected by domain rules, preventing illegal state shifts at the core level.
- **CQRS (Command Query Responsibility Segregation):**
  Implemented via `MediatR`. Write operations (Commands) are strictly separated from read operations (Queries). Read handlers bypass domain repositories and EF Core change tracking (`.AsNoTracking()`), projecting data directly into immutable DTOs for independent scaling.
- **AI-Assisted Clinical Triage:**
  The domain model natively integrates natural language processing summaries (`NlpSummary`) and predictive no-show risk scoring, providing physicians with structured clinical context directly within the scheduling workflow.

## Engineering & Quality Assurance

- **Shift-Left Testing Strategy:**
  Business rules and application handlers are covered by unit tests using **xUnit**, **Moq**, and **FluentAssertions**. The test projects follow a strict mirroring pattern of the production architecture.
- **Continuous Integration (CI):**
  Enforced via GitHub Actions. Every push and pull request to the main branches triggers an automated pipeline that restores, builds, and executes the test suite to prevent regressions.

## Cloud-Ready Infrastructure Strategy

The infrastructure layer is designed for modern cloud deployment (PaaS) while maintaining a secure local development workflow:

- **Relational Persistence:** Data access is implemented using Entity Framework Core targeting PostgreSQL hosted on **Supabase**, utilizing explicit relational projections and anti-join patterns.
- **Hybrid Cryptography Management:** Security services (like RSA key loading for AI interactions) implement fallback mechanisms, reading from production environment variables (Render PaaS) while gracefully degrading to local `.pem` files during local development.
- **Secret Management:** Development environments utilize .NET User Secrets to prevent credential leakage in source control, keeping the infrastructure layer ready for production secret stores.

## Technology Stack

- **Framework:** .NET 10 / C#
- **Architecture:** Clean Architecture, CQRS, DDD
- **Testing:** xUnit, Moq, FluentAssertions
- **Libraries:** MediatR, Entity Framework Core, MassTransit
- **Database & Cloud Target:** PostgreSQL (Supabase), Render (PaaS)

## Getting Started

### Prerequisites
- .NET 10 SDK
- PostgreSQL (Local instance or Supabase project)

### Local Setup

1. Clone the repository.
2. Navigate to the API project directory (`MedCareOS.Api`).
3. Configure your local database connection string using User Secrets to maintain repository hygiene:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<SUPABASE_HOST>;Port=5432;Database=postgres;Username=<USER>;Password=<PASSWORD>;SSL Mode=Require;Trust Server Certificate=true;"
