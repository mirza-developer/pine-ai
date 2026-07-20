# PineAI Copilot Instructions

## Build

```bash
dotnet build PineAI.slnx
```

No test or lint commands exist in this repository.

## Architecture

PineAI is a .NET 10 multi-project solution for an AI-powered Persian-language customer support platform built for **Ananas Collection Boutique**. The core product is a Bale messenger chatbot backed by an ASP.NET Core API.

### Projects

| Project | Type | Role |
|---|---|---|
| `PineAI.Core` | Class library | Domain contracts (interfaces), entities, and feature DTOs |
| `PineAI.Persistence` | Class library | EF Core `PineAIDbContext`, repositories, caching, messaging services |
| `PineAI.Identity` | Class library | ASP.NET Core Identity with separate `PineAIIdentityContext` |
| `PineAI.Api` | ASP.NET Core Web API | REST endpoints, order notifications, SMS, bot webhooks |
| `PineAI.BaleBot` | .NET Worker Service | Long-polling Bale messenger bot (can also run as a Windows Service) |
| `PineAI.Shared` | Class library | Cross-cutting utilities (Persian calendar, XLSX builder) |

### Dependency Flow

```
PineAI.Api / PineAI.BaleBot
    → PineAI.Identity
    → PineAI.Persistence
        → PineAI.Core
PineAI.BaleBot → PineAI.Shared
```

### Data Layer

- `PineAIDbContext` (in `PineAI.Persistence`) holds all domain entities.
- `PineAIIdentityContext` (in `PineAI.Identity`) is a separate context for ASP.NET Identity.
- Both support **SQL Server** (production) and **SQLite** (local/lightweight) via the `DatabaseProvider` config key.
- SQL Server uses EF migrations (`db.Database.Migrate()`); SQLite uses `EnsureCreated()`.
- Caching uses **FusionCache** with a Redis backplane when `ConnectionStrings:Redis` is configured, falling back to in-memory only.

### Bot Processing Pipeline (`PineAI.BaleBot`)

1. `BaleBotWorker` long-polls Bale API and dispatches updates with a **dual-semaphore** model:
   - Global semaphore (`MaxConcurrentUpdates = 10`) caps total concurrency.
   - Per-user semaphore serializes messages from the same chat to preserve session order.
2. `BotUpdateHandler` orchestrates each update: penalty check → AI call → structured block parsing → order lookup / escalation → persistence.
3. `IChatAgentService` abstracts two AI backends, selected at startup via `AiProvider` config:
   - `"github"` → `ChatAgentService` (Microsoft Agents SDK + GitHub Models / OpenAI endpoint)
   - `"arvan"` → `ArvanChatAgentService` (direct HTTP to ArvanCloud OpenAI-compatible API)
4. Both services load system-prompt instructions by concatenating all `*.md` files from the `Chat/` folder at startup.
5. `ChatSessionStore` holds per-user session state in memory (keyed by Bale chat ID).

### AI Structured Response Blocks

The AI embeds command blocks in its text replies that `ResponseBlockTools` strips before the text is shown to the user:

| Block | Purpose |
|---|---|
| `<<ORDER_CODE ... >>` | Triggers a database order-status lookup |
| `<<FEEDBACK ... >>` | Routes the conversation to one of ~24 predefined human support chat IDs |
| `<<PENALTY ... >>` | Applies a 10-minute lockout to the user |
| `<<VERIFICATION ... >>` | Carries a confirmation sentence; always stripped, never shown |

`FEEDBACK` blocks carry a JSON payload with a `Type` field. `BotUpdateHandler.RequiredFeedbackFields` defines which JSON fields must be non-empty for each feedback type before the escalation is dispatched.

## Key Conventions

### Service Registration

Each layer exposes a single static extension method on `IServiceCollection`:
- `AddCoreServices()` — `PineAI.Core`
- `AddPersistenceServices(configuration)` — `PineAI.Persistence`
- `AddIdentityServices(configuration)` — `PineAI.Identity`

`PineAI.BaleBot` registers its own services directly in `Program.cs` (no wrapper method).

### Global Project Settings (`Directory.Build.props`)

All projects inherit:
- `<Nullable>enable</Nullable>`
- `<ImplicitUsings>enable</ImplicitUsings>`
- `<LangVersion>latest</LangVersion>`
- Global usings: `System`, `System.Linq`, `System.Collections.Generic`, `System.Threading.Tasks`, `System.ComponentModel.DataAnnotations`

### Interface / Implementation Separation

Contracts (interfaces) live in `PineAI.Core.Contracts`. Implementations live in `PineAI.Persistence` (repositories) or `PineAI.Identity.Services`. `PineAI.Core` has no implementation code.

### Configuration Keys

| Key | Values | Effect |
|---|---|---|
| `DatabaseProvider` | `"SqlServer"` (default), `"Sqlite"` | Switches EF provider |
| `AiProvider` | `"github"` (default), `"arvan"` | Selects AI backend in `PineAI.BaleBot` |
| `AiAgent:*` | ApiKey, Model, Endpoint | GitHub Models / OpenAI config |
| `ArvanAiAgent:*` | ApiKey, Model, Endpoint | ArvanCloud config |
| `BaleMessenger:Token` | Bot token | Bale API base URL construction |
| `BlockedUsernames` | string array | Silently drops messages, no AI call made |
| `Seq:ServerUrl` | URL | Centralized Serilog log sink |

### Logging

Use Serilog throughout. All hosted services enrich logs with `.WithProperty("AppName", ...)`. Log levels default to `Information`; `Microsoft.*` and `System.*` are overridden to `Warning`.

### Persian/Arabic Digit Normalization

Any user-supplied input that may contain Persian (U+06F0–U+06F9) or Arabic-Indic (U+0660–U+0669) digits must be normalized before processing. Use `ResponseBlockTools.NormalizeDigits(value)` from `PineAI.BaleBot.Tools`.

### API Authentication

The API uses a custom `ApiKeyAuthenticationHandler` scheme (`X-Api-Key` header). The scheme name is `ApiKeyAuthenticationHandler.SchemeName`. Swagger shows the `ApiKey` security definition on all endpoints that require it via `ApiKeyOperationFilter`.
