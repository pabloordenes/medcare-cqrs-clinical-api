# MedCareOS CQRS API

> Note: This repository is currently under active development.

Enterprise clinical management and medical scheduling REST API built with .NET. This project serves as a technical demonstration of modern architectural patterns, focusing on strict separation of concerns, domain encapsulation, and cloud-ready infrastructure.

## Architectural Overview

The solution strictly adheres to **Clean Architecture** principles, divided into four primary layers: Domain, Application, Infrastructure, and API. The dependency rule points exclusively inward, ensuring the core business logic remains framework-agnostic.

### Key Patterns & Principles

*   **Domain-Driven Design (DDD):** 
    The core features a rich domain model. Entities (e.g., `Appointment`, `ScheduleBlock`, `Patient`, `Staff`) act as Aggregate Roots, encapsulating their invariants and omitting ORM navigation properties to enforce strict aggregate boundaries. Properties have private setters to prevent anemic models, forcing state changes through explicit domain behaviors (e.g., `Create()`, `StartConsultation()`, `FinishConsultation()`).
*   **State Machine Logic:** 
    Clinical appointment workflows are embedded within the domain entity. Transitions between `Scheduled`, `Confirmed`, `InProgress`, `Finished`, `Cancelled`, and `NoShow` are protected by domain rules, preventing illegal state shifts at the core level rather than relying on application-layer validation.
*   **CQRS (Command Query Responsibility Segregation):** 
    Implemented via `MediatR`. Write operations (Commands) are strictly separated from read operations (Queries). Read handlers bypass domain repositories and EF Core change tracking (`.AsNoTracking()`), projecting data directly into immutable DTOs for independent scaling and optimal query performance.
*   **AI-Assisted Clinical Triage:** 
    The domain model natively integrates natural language processing summaries (`NlpSummary`) and predictive no-show risk scoring, providing physicians with structured clinical context directly within the scheduling workflow.

## Cloud-Ready Infrastructure Strategy

The infrastructure layer is designed for modern cloud deployment while maintaining a secure, zero-cost local development workflow:

*   **Relational Persistence:** Data access is implemented using Entity Framework Core (`ApplicationDbContext`) targeting PostgreSQL hosted on **Supabase**, utilizing explicit relational projections and anti-join patterns for high-concurrency availability searches.
*   **Secret Management:** Development environments utilize .NET User Secrets and API-layer configuration to prevent credential leakage in source control, keeping the infrastructure layer ready for production secret stores.

## Technology Stack

*   **Framework:** .NET / C#
*   **Architecture:** Clean Architecture, CQRS, DDD
*   **Libraries:** MediatR, Entity Framework Core
*   **Database & Cloud Target:** PostgreSQL (Supabase)

## Getting Started

### Prerequisites
*   .NET SDK
*   PostgreSQL (Local instance or Supabase project)

### Local Setup

1. Clone the repository.
2. Navigate to the API project directory (`MedCareOS.Api`).
3. Configure your local database connection string using User Secrets to maintain repository hygiene:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=<SUPABASE_HOST>;Port=5432;Database=postgres;Username=<USER>;Password=<PASSWORD>;SSL Mode=Require;Trust Server Certificate=true;"
   ```
