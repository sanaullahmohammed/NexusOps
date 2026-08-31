# Implementation Plan: Review Remediation

**Branch**: `003-review-remediation` | **Date**: 2026-08-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/003-review-remediation/spec.md`

## Summary

Correct 19 reviewed findings plus one found during verification. The work divides into five batches, each landing as one commit that carries both the code change and any specification amendment it implies, so no commit leaves specs 001 or 002 contradicting the implementation.

Two findings are not defects in the code. Finding 19 did not reproduce (the .NET SDK is present at 10.0.400) and yields no requirement. Findings 6 and 18 are real but were filed against the wrong evidence — no credential has actually leaked, and the disputed workflow count lives in CLAUDE.md rather than README.md. Both are corrected on their true evidence.

Batch D is sequenced first. There is no test project in the repository, so the CI test step has always passed without executing anything; every defect being fixed here reached `master` through a green build. Verification must exist before the behavioural batches land.

## Technical Context

**Language/Version**: C# / .NET 10 (SDK 10.0.400 verified present)

**Primary Dependencies**:
- `Microsoft.Agents.AI` 1.9.0 — `AIFunctionFactory.Create` binds a trailing `CancellationToken` without exposing it in the tool schema (FR-014)
- `Aspire.StackExchange.Redis.DistributedCaching` 13.4.3 — `IDistributedCache`; note `DistributedCacheEntryOptions.SlidingExpiration` throws on a non-positive value (FR-008)
- `Microsoft.Extensions.Options` — `ValidateOnStart` for startup configuration validation (FR-008)
- `TimeProvider` (in-box, .NET 8+) — injectable time source for date-derived anomaly fields (FR-005)
- xUnit — new test project (FR-016)

**Storage**: Unchanged. Redis via `IDistributedCache`, key `nexusops:session:{guid}`. The retrieval contract widens from a turn list to a result carrying miss-versus-failure (FR-009).

**Testing**: `dotnet test` against `NexusOps.deployable.slnf`. Unit-level only — a fake `IDistributedCache` and a fake `TimeProvider`, no Redis or Azure AI dependency, so the suite runs on fork pull requests without secrets.

**Target Platform**: Linux container (Aspire-orchestrated); CI on ubuntu-latest

**Constraints**: No change to the `OrderStatus` lifecycle vocabulary or to the `status` string returned by order details. The chat endpoint's success contract stays additive; only the error contract gains a field (FR-011).

**Scale/Scope**: Order service, Contracts, Agent Host, four service `Extensions.cs` files, frontend config, CI configuration, one new test project, and amendments to specs 001 and 002.

## Constitution Check

- [x] **I. Cognition/Durability boundary** — No saga logic introduced. `AnomalyReason` is domain data read over the Direct path; the session store remains a cache in the cognition layer.
- [x] **II. Curated tool boundaries** — No new tools. FR-006 and FR-014 change what existing tools return and how they are cancelled; the six curated tool names and their schemas are unchanged. Adding a trailing `CancellationToken` does not alter the schema the model sees.
- [x] **III. Approval-gated side effects** — All touched endpoints remain read-only. No mutation introduced.
- [x] **IV. Message-driven service integration** — Unchanged; no saga-to-service communication exists yet.
- [x] **V. Domain pluggability** — `AnomalyReason` is confined to the E-Commerce sample pack (`NexusOps.OrderService` and its DTO in `NexusOps.Contracts`). No orchestration-core component gains domain knowledge. `HistoryResult` is domain-agnostic.
- [x] **VI. Observability first** — FR-013 restores health endpoints in all environments, which strengthens this principle rather than weakening it. FR-012 makes existing lifecycle logs correlatable. No new service is introduced.

**Re-validation after design**: no gate moved. FR-013 is the only item touching Principle VI and it is corrective.

## Project Structure

### Documentation (this feature)

```text
specs/003-review-remediation/
├── spec.md              ← findings, requirements, decisions
├── plan.md              ← this file
└── tasks.md             ← batch-ordered task list
```

Amendments to existing specifications, made in the commit that implements them:

```text
specs/001-ecommerce-domain-services/
├── spec.md                          ← Status: Draft → Implemented (batch E)
├── data-model.md                    ← AnomalyReason; relative seed dates (batch A)
└── contracts/
    ├── order-service-api.md         ← anomaly semantics + severity rule (batch A)
    ├── inventory-service-api.md     ← health contract (batch C)
    ├── product-service-api.md       ← health contract (batch C)
    └── tool-definitions.md          ← 400 handling on the anomaly tool (batch A)

specs/002-session-management/
├── spec.md                          ← FR-007, FR-008, FR-010, FR-012 (batch B)
└── contracts/chat-api.md            ← 400 response; 500 carries sessionId (batch B)
```

### Source Code Changes

```text
NexusOps.Contracts/
└── Dtos/OrderAnomaly.cs               ← full 8-field contract shape (A)

NexusOps.OrderService/
├── Models/Order.cs                    ← AnomalyReason enum + property (A)
├── Data/OrderStore.cs                 ← ORD-0011; relative dates (A)
├── Endpoints/OrderEndpoints.cs        ← select on reason; severity rule; TimeProvider (A)
├── Program.cs                         ← register TimeProvider (A)
└── Extensions.cs                      ← unconditional /health + JSON writer (C)

NexusOps.InventoryService/Extensions.cs   ← as above (C)
NexusOps.ProductService/Extensions.cs     ← as above (C)
NexusOps.Server/Extensions.cs             ← as above (C)

NexusOps.AgentHost/
├── Configuration/ConversationSessionOptions.cs  ← validation attributes (B)
├── Endpoints/ChatEndpoints.cs                   ← prompt guard; 400; sessionId on error (B)
├── Services/IConversationStore.cs               ← HistoryResult (B)
├── Services/RedisConversationStore.cs           ← miss vs failure; shared log token (B)
├── Services/AgentService.cs                     ← skip miss on new session; branch on result (B)
├── Program.cs                                   ← ValidateOnStart (B)
├── Tools/{Order,Inventory,Product}Tools.cs      ← CancellationToken; 400 handling (A, C)
└── NexusOps.AgentHost.csproj                    ← UserSecretsId (E)

NexusOps.Tests/                        ← new xUnit project (D)
NexusOps.sln                           ← register test project (D)
NexusOps.deployable.slnf               ← add test project and AppHost (D)
frontend/vite.config.ts                ← proxy target fallback (C)
CLAUDE.md, README.md                   ← documentation sync (E)
```

## Implementation Phases

Each phase is one commit, followed by `dotnet build` and `dotnet test`, then a checkpoint for review before the next begins.

### Batch D — Verification First

Covers FR-016, FR-017. Create `NexusOps.Tests` (xUnit) at the repository root with a project `.gitignore` per the CLAUDE.md convention; register it in `NexusOps.sln` and `NexusOps.deployable.slnf`. Add `NexusOps.AppHost` to the solution filter so CI and CodeQL compile it — the AppHost has no project reference to `frontend.esproj`, so this does not couple npm into the dotnet job. Update the CLAUDE.md paragraph describing the filter.

**Verification**: `dotnet test` executes a non-zero test count; `dotnet build NexusOps.deployable.slnf` compiles the AppHost.

### Batch A — Order Contract and Anomaly Semantics

Covers FR-001 to FR-006. Add the `AnomalyReason` enum and the nullable `Order.AnomalyReason` property; seed `ORD-0011`; convert seed delivery dates to offsets from the current date; register and inject `TimeProvider`; widen `OrderAnomaly` to the eight fields the contract already publishes; derive `anomalyType` and `severity` from the reason; add 400 handling to `OrderTools`. Amend 001's data model and two contract files in the same commit.

**Verification**: the three anomaly filters return disjoint, non-empty sets; an order's `anomalyType` is invariant across filters; a faked `TimeProvider` produces deterministic `daysOverdue`.

### Batch B — Chat and Session Correctness

Covers FR-007 to FR-012. Guard the prompt and return 400 before minting a session; move both session options behind a `ValidateOnStart` validator and add a `configuration` error category so a bad value can no longer be reported as a connection fault; widen `GetHistoryAsync` to return `HistoryResult` and branch on miss versus failure; skip the history read entirely when no session ID was supplied; return the session ID on the error path so the turn persisted under 002 FR-005 is reachable; unify both loggers on the hashed session token. Amend 002's spec and chat contract in the same commit.

**Verification**: a store stubbed to throw preserves the caller's session ID; an expired session still mints; a blank prompt returns 400 with no store write; non-positive options prevent startup.

### Batch C — Health, Cancellation, Proxy

Covers FR-013 to FR-015. Register `/health` in all environments across the four services with a JSON response writer matching the contracts, keeping `/alive` Development-only and preserving the security rationale in a rewritten comment; thread `CancellationToken` through all six tool handlers with an `OperationCanceledException` rethrow ahead of the generic catch; give the Vite proxy a localhost fallback. Amend the three service contracts in the same commit.

**Verification**: `/health` returns 200 with a JSON body under a Production environment name; a cancelled token surfaces as cancellation rather than as a service outage.

### Batch E — Secrets Hygiene and Documentation

Covers FR-018, FR-019. Add a `UserSecretsId` to the Agent Host and make `dotnet user-secrets` the documented primary credential path, with the environment variable as the CI and container alternative. Correct the workflow count in CLAUDE.md, repoint its Active Feature Plan to this feature, sync the README order status vocabulary and the stale `investigate_delayed_order` tool name, put the evaluation-runner paragraph in the future tense, and flip 001's status to Implemented.

**Verification**: no statement in README.md or CLAUDE.md contradicts the implementation; `git grep` finds no remaining reference to the removed vocabulary.

## Complexity Tracking

| Item | Principle at risk | Justification |
|---|---|---|
| `IConversationStore` gains a result type rather than returning a bare list | None — internal abstraction | The two-state return is the root cause of finding 12. A sentinel value or an out-parameter would encode the same information less legibly, and an exception-based signal would defeat 002 FR-010's requirement that degradation not surface to the caller. |
| Health endpoints exposed outside Development | VI, and the stock Aspire security guidance | The AppHost is the sole consumer and probes unconditionally, and the service contracts document the endpoint without an environment qualifier. Access restriction belongs at the ingress; the rationale is preserved in the code comment rather than deleted. |
| Adding `ORD-0011` rather than reusing an existing order | None | Reusing `ORD-0010` would remove the only `Pending` order from the seed set, costing demo coverage of a normal lifecycle state. |

## Open Questions / Deferred

- **Full line-ending normalisation.** 28 tracked files are committed with CRLF while the rest of the tree uses LF. Only `frontend/package-lock.json` is pinned here; normalising the tree would rewrite all 28 in one commit and redirect `git blame` on each. Recorded in spec.md under Known Issues.
- **Integration tests.** The suite added in batch D is unit-level so it runs without secrets on fork pull requests. Tests exercising real Redis or a live model deployment remain unbuilt; CLAUDE.md already notes that such a job must be gated to pushes on `master`.
- **`AnomalyReason` on the order-details response.** This feature exposes the reason on the anomaly payload only. Whether `GET /orders/{id}` should also carry it is left open, since no consumer requires it yet.
