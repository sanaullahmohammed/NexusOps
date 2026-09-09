# NexusOps — Issues & Enhancements

A self-audit of known defects, gaps, and proposed improvements. Recorded so they are explicit and
ranked rather than discovered later.

**Audited against:** `424edfc` (2026-09-04) · **Audit date:** 2026-09-09
**Verification at audit time:** `dotnet build` clean · unit tests 174/174 · integration tests 4/4 ·
live evaluation 24/24 (100%) against the full Aspire stack on `gpt-5.4-nano`.

Every finding cites `file:line` against that tree. Line numbers drift; the named symbols do not.

## How to read this

| Field | Meaning |
|---|---|
| **Severity** | `high` — blocks any real deployment · `medium` — correctness or reliability gap · `low` — quality, cost, or ergonomics |
| **Status** | `open` — unaddressed · `accepted` — known and deliberately not fixed, with a reason · `fixed-in-#NN` — closed by that PR |

This is a **findings register, not a feature spec.** These are independent items to be picked off or
consciously deferred, not one unit of work. When an item graduates to implementation it earns its
own `specs/0NN-<name>/` under the normal spec-kit workflow — which is what keeps that convention
meaningful for the work it actually governs.

**Scope note.** NexusOps is a proof-of-concept. Several items below are inherent to that scope and
are listed as `accepted` with the reasoning, not as defects. The distinction between *scoped* and
*incomplete* is maintained deliberately throughout.

---

## Summary

| ID | Severity | Status | Title |
|---|---|---|---|
| [SEC-1](#sec-1) | high | open | No authentication anywhere; approval gate records no decider |
| [SEC-2](#sec-2) | high | open | Session identifiers are unauthenticated, unrevocable bearer tokens |
| [SEC-3](#sec-3) | high | open | No rate limiting or prompt-size cap on a paid model endpoint |
| [SEC-4](#sec-4) | low | **fixed** | Tracked `appsettings.Development.json` had drifted from its documented placeholder shape |
| [REL-1](#rel-1) | medium | open | Retry coverage stops exactly at the network legs |
| [REL-2](#rel-2) | medium | open | Session store has a lost-update race |
| [REL-3](#rel-3) | medium | open | Approval sagas park in `AwaitingApproval` forever |
| [REL-4](#rel-4) | medium | open | Compensation failure is silent — no dead-letter, no alert |
| [REL-5](#rel-5) | medium | open | No idempotency keys on mutation legs |
| [REL-6](#rel-6) | medium | open | No health check on the model provider |
| [QUA-1](#qua-1) | medium | open | Nothing tests the system prompt's behavioural constraints |
| [QUA-2](#qua-2) | medium | open | Evaluation dataset is saturated |
| [QUA-3](#qua-3) | low | open | Nine hardcoded timeouts, none configurable |
| [QUA-4](#qua-4) | low | open | History trimming is turn-counted, not token-counted |
| [QUA-5](#qua-5) | low | open | `notification-service` and `frontend` have no tests |
| [QUA-6](#qua-6) | low | open | Tool results enter model context undelimited |
| [ACC-1](#acc-1) | — | accepted | Domain services are in-memory |
| [ACC-2](#acc-2) | — | accepted | `NexusOps.Server` and `frontend/` are scaffold |

---

## Security

### SEC-1
**No authentication anywhere; the approval gate records no decider.** · `high` · `open`

`grep -rn "Authorize|Authentication"` returns nothing across the solution.
`POST /api/approvals/{id}/approve` (`NexusOps.AgentHost/Endpoints/ApprovalEndpoints.cs`) accepts a
GUID and executes a financial mutation. `OrderActionSagaState` records `DecidedAt` but has no
approver identity field, and nothing checks that the approver is not the requester.

**Impact.** Approval-gated side effects are Constitution Principle III, and the *architectural* half
holds: approval is deliberately not an agent tool, so no model output can reach it. The
*operational* half does not. The system is maker-checker in shape and cannot enforce it — there is
no identity, no authorisation, and no audit answer to "who approved this refund?"

The reference GUID being unguessable is obfuscation, not access control, and should not be presented
as one.

**Proposed fix.** Authentication on the approval endpoints; an `ApprovedBy` identity captured into
`OrderActionSagaState` at decision time; a separation-of-duties check rejecting an approver who
matches the requester. Requires a requester identity too, which means authenticating `/api/chat`.

**Effort.** Medium — touches the endpoint, saga state, a migration, and the chat surface.

> This is the single most significant gap in the project. It undercuts the claim the architecture is
> built around, and it is listed first for that reason.

---

### SEC-2
**Session identifiers are unauthenticated, unrevocable bearer tokens.** · `high` · `open`

`POST /api/chat` accepts any well-formed GUID as `sessionId` and resumes that conversation
(`AgentService.ResolveSessionAsync`). Session identifiers are not bound to any user identity, so
possession is authorisation.

There is no history-read endpoint — `ChatEndpoints` maps only `MapPost("/")` — so this is not a bulk
disclosure vector. But continuing another party's session places their history in the model's
context, and the response is derived from it.

**It cannot be revoked.** `DeleteSessionAsync` is implemented in `RedisConversationStore`, declared
on `IConversationStore`, and unit-tested — and no endpoint exposes it. Combined with the 30-minute
*sliding* expiration, a leaked identifier that is actively used never expires. The only remedy is
waiting out thirty minutes of the holder not using it.

**Impact.** Guessing a v4 GUID is impractical. Leaking one — a log, a URL, a screenshot, a support
ticket — is not, and there is no response available when it happens.

**Proposed fix.** Bind sessions to an authenticated principal and reject mismatches (depends on
SEC-1). Independently and much cheaper: expose the existing `DeleteSessionAsync` as an endpoint so
revocation is possible at all, and consider an absolute lifetime alongside the sliding one.

**Effort.** Low for revocation; medium for identity binding.

---

### SEC-3
**No rate limiting or prompt-size cap on a paid model endpoint.** · `high` · `open`

`grep -rn "RateLimit"` returns nothing. `ChatEndpoints.cs` rejects null/whitespace prompts before
minting a session or invoking the model — deliberately, so a malformed request costs nothing — but
enforces **no maximum length**.

**Impact.** `/api/chat` is unauthenticated and every call is a billed model invocation. There is no
per-caller throttle, no global ceiling, and no cost circuit-breaker. A large prompt in a loop is a
cost-amplification attack with no defence. Cost aside, unbounded concurrency also has no backpressure
toward Azure AI Foundry quota.

**Proposed fix.** ASP.NET Core rate limiting (`AddRateLimiter`) per caller and globally; a maximum
prompt length validated alongside the existing empty check; a configurable daily token or spend
ceiling that fails closed.

**Effort.** Low.

---

### SEC-4
**Tracked `appsettings.Development.json` had drifted from its own documented shape.** · `low` · `fixed`

`NexusOps.AgentHost/appsettings.Development.json` is tracked and contained
`https://nexus-ops-resource.cognitiveservices.azure.com/` and a real deployment name.

**This was never a credential leak, and the distinction matters.** No key has ever been committed:
the only `ApiKey` value in the file's entire history is the literal `<your-api-key>` placeholder.
There was nothing to rotate and no history to rewrite. Credential handling is otherwise correct —
user secrets, an `AZURE_AI_FOUNDRY_API_KEY` fallback for CI, a `UserSecretsId`, and an explanatory
comment in the `.csproj`.

**The actual defect was documentation drift.** README §2 states that this file "ships with
placeholders showing the shape" and prints exactly `<your-endpoint>` and `<your-deployment>`. The
tracked file held real values instead. So the repository documented one thing and shipped another,
and the exposure — a resource hostname and deployment name — was an accident of that drift rather
than a decision anyone made.

**Fix applied.** The file now contains the placeholders README already documents. The real values
belong in user secrets alongside the key:

```bash
cd NexusOps.AgentHost
dotnet user-secrets set "AzureAI:Endpoint"       "<your-endpoint>"
dotnet user-secrets set "AzureAI:DeploymentName" "<your-deployment>"
```

> **Note for existing clones.** Only `ApiKey` has an environment-variable fallback
> (`AZURE_AI_FOUNDRY_API_KEY`); `Endpoint` and `DeploymentName` are read from configuration only. A
> working checkout that relied on the tracked values must set the two user secrets above, or
> AgentHost will start and then fail on the first model call.

**Effort.** Trivial — applied in the same change that filed this register.

---

## Reliability

### REL-1
**Retry coverage stops exactly at the network legs.** · `medium` · `open`

`NexusOps.WorkflowOrchestrator/Program.cs:27` configures
`cfg.UseMessageRetry(r => r.Intervals(50, 100, 200, 500))`, and it is aimed deliberately — the
comment names its target as "a transient failure (including a saga optimistic-concurrency conflict,
which surfaces as a `DbUpdateConcurrencyException` from the EF Core repository)". Those exceptions
originate in the saga repository and do propagate, so the policy is live and does the job it was
written for. `Program.cs:47` reasons about a retried `DbUpdateConcurrencyException` as a real event
when justifying the receive-endpoint outbox.

The gap is where that coverage **ends**. `InvestigationFanOutConsumer` and
`OrderActionExecutionConsumer` catch every exception from their `IRequestClient` calls and convert it
to a status (`TimedOut` / `Unavailable`, or an `InventoryLegResult`). They never throw. So the retry
policy structurally cannot reach the outbound request/response legs — the network calls a reader
would most expect it to cover.

Additionally there is **no circuit breaker anywhere** on the AMQP path. `AddStandardResilienceHandler`
in `ServiceDefaults` covers outbound HTTP only.

**Impact.** A single transient blip on any leg degrades a whole investigation to `Degraded`, or
fails an execution, with no second attempt — even though one retry would very often succeed. Under
sustained downstream failure every call burns its full 5-second timeout with no fast-fail.

**Design tension, stated plainly.** The catch-all is not a mistake: converting failures to statuses
is what makes partial degradation work at all, and it is load-bearing for the `NotFound` vs
`Unavailable` distinction. A retry cannot simply be introduced by removing the catch. The fix is a
bounded retry *inside* the leg helpers, before the exception is converted to a status.

**Proposed fix.** Retry with jitter inside `GetOrderFindingAsync` / `GetInventoryFindingAsync` /
`GetProductFindingAsync` and the execution-leg equivalents, bounded so the outer client timeout still
dominates (see QUA-3 — the timeout budget must be re-derived if retries are added). Consider a
circuit breaker per downstream service.

**Effort.** Medium — the timeout budget is the harder half.

---

### REL-2
**Session store has a lost-update race.** · `medium` · `open`

`RedisConversationStore.AppendTurnsAsync` reads the session JSON, deserialises, appends, trims, and
writes back. There is no atomicity, no optimistic concurrency, and no CAS.

**Impact.** Two concurrent turns in the same session can interleave so that one is lost. Benign for
a single operator typing sequentially — the current usage — and wrong under any real concurrency,
including a user with two tabs or a retried client request.

**Not observed.** This is read from the code, not reproduced. There is no load or concurrency test
in the suite that would surface it (see QUA-5's neighbourhood).

**Proposed fix.** Model history as a Redis list — `RPUSH` the new turns, `LTRIM` to the cap, `EXPIRE`
to refresh the sliding window. All atomic server-side, and it removes the read-modify-write entirely.
The trade is that `IDistributedCache` does not expose list operations, so this needs
`IConnectionMultiplexer` directly.

**Effort.** Low-medium.

---

### REL-3
**Approval sagas park in `AwaitingApproval` forever.** · `medium` · `open`

`OrderActionSaga` has no timeout, expiry, escalation, or reminder on the `AwaitingApproval` state. A
request that is never decided remains there for the life of the database.

**Impact.** An undecided refund is an unreconciled financial liability with no expiry and no
operational signal. Nothing reports how many are pending or how old the oldest is. At any real
volume this accumulates silently.

**Proposed fix.** A MassTransit scheduled message (`Schedule<OrderActionSagaState, ExpireApproval>`)
armed on entry to `AwaitingApproval` and unscheduled on decision, transitioning to a terminal
`Expired` state and publishing a `NotificationRequested` on fire. The expiry window should be
configurable. A pending-approval age metric would pair with it.

**Effort.** Medium.

> This is also the gap that would matter most if approval windows lengthened — see the "three-day
> approval" scenario, where it becomes the primary constraint.

---

### REL-4
**Compensation failure is silent — no dead-letter, no alert.** · `medium` · `open`

`OrderActionExecutionConsumer.CompensateOrderMutationAsync` catches and discards its exception. The
comment is honest about why: the reported outcome already reflects failure, and a compensation that
cannot even be attempted because the order service is also down is a rarer, harder failure this POC
does not retry indefinitely.

**Impact.** The judgement is defensible; the *silence* is the defect. When compensation fails, the
system is in a state it knows is inconsistent — an order mutated, its inventory leg failed, and the
reversal also failed — and emits no signal beyond the caller's response. There is no dead-letter
queue, no alert, no metric, and no runbook. This is the single path where money is provably
inconsistent and it is the least observable one.

**Proposed fix.** At minimum log at `Error` with the correlation id and publish a distinct
`CompensationFailed` event for alerting. Better: persist it to a reconciliation queue an operator can
work, and document the manual procedure.

**Effort.** Low for the signal; medium for the reconciliation surface.

---

### REL-5
**No idempotency keys on mutation legs.** · `medium` · `open`

`ExecuteOrderMutationConsumer` and `ExecuteInventoryRestockConsumer` carry no deduplication key on
the mutation itself. They are *effectively* idempotent today because each operation sets a status to
a fixed target value rather than accumulating — a redelivered refund re-sets `Refunded`, and the
eligibility check then rejects it as already refunded.

**Impact.** That safety is a property of the operations happening to be set-to-constant, not a
mechanism. Any accumulating operation — "add 5 to stock", a partial refund applied twice — would be
genuinely unsafe under at-least-once delivery. The inventory restock is closer to that shape than the
order mutation is.

**A second-order benefit worth noting.** Real idempotency keys would dissolve the compensate-on-
timeout dilemma in `OrderActionExecutionConsumer`: an uncertain leg could simply be retried until a
confirmed answer arrives, instead of being reported as unreconciled. REL-5 and REL-1 are the same
fix viewed from two directions.

**Proposed fix.** Carry the saga `CorrelationId` as an idempotency key into the mutation consumers
and record applied mutations, so a redelivery is recognised rather than merely harmless.

**Effort.** Medium.

---

### REL-6
**No health check on the model provider.** · `medium` · `open`

`NexusOps.AgentHost/Program.cs` calls a bare `MapDefaultEndpoints()`. AgentHost's readiness is
therefore the `self` check alone. Nothing anywhere health-checks Azure AI Foundry reachability.

**Impact.** AgentHost reports `Healthy` while the model provider is unreachable — the state in which
it can do nothing at all. An orchestrator would route traffic to it; the Aspire dashboard shows
green; every request returns 500.

**This is an inconsistency with the project's own stated principle.** `ServiceDefaults/Extensions.cs`
argues, correctly, that readiness should reflect what a host structurally requires — and applies it
twice: `WorkflowOrchestrator` opts into bus-inclusive readiness because it cannot function without
the bus, and `notification-service` gates its readiness on AMQP connectivity for the same reason.

The bus being *excluded* from AgentHost's readiness is deliberate and correct, and the same file says
why: "the bus is one of several capabilities, not the reason they exist — their Direct-path HTTP
endpoints work fine with the broker down, so a broker blip must not pull them out of rotation." The
principle is applied there. It is simply never applied to the one dependency AgentHost genuinely
cannot operate without.

**Proposed fix.** A health check probing the configured Foundry endpoint, tagged `ready`, cached
briefly so readiness probes do not amplify into provider calls. Startup validation already fails fast
on missing configuration; this covers the running case.

**Effort.** Low.

---

## Quality & observability

### QUA-1
**Nothing tests the system prompt's behavioural constraints.** · `medium` · `open`

`AzureAIOptions.AgentInstructions` states hard constraints: a degraded investigation result must be
surfaced rather than presented as complete, and the agent "MUST NOT say or imply that the
refund/cancellation has happened" — the comment calls it "a hard constraint, not a phrasing
preference." Tool descriptions in `ToolNames` repeat both.

Nothing verifies either. `NexusOps.Evaluation` grades which tool was invoked, never the response
text.

**Impact.** The constraints with the highest safety stakes are the only ones with no coverage. A
model that routes correctly and then narrates a pending refund as completed passes every test in the
repository. The architectural controls mean no money moves — but the operator is misinformed, and
nothing detects it.

**Proposed fix.** Assertions on response text for the mutation cases: the approval reference appears,
and completion language does not. A regex-level check is sufficient and cheap; an LLM judge is not
required. Equivalently for degraded investigations, which needs a fault-injected fixture.

**Effort.** Low for the mutation assertions.

---

### QUA-2
**Evaluation dataset is saturated.** · `medium` · `open`

24 cases, tool routing only. It scored **24/24 (100%)** live on `gpt-5.4-nano` on 2026-09-09.

**Impact — two readings, both true.** A small model scoring perfectly is good evidence the *tool
design* carries routing accuracy rather than raw model capability, which is the stronger claim. It
also means the benchmark has **no headroom**: a saturated suite cannot detect degradation until
something is badly wrong. At 24 cases one flip moves the score four points, so the noise floor sits
above most real regressions.

The dataset is also single-turn throughout, and measures nothing about answer quality, groundedness,
latency, or cost.

**Proposed fix.** Harder cases, not merely more — deliberately ambiguous phrasings on the three-way
boundary between `investigate_order_anomaly`, `get_order_details`, and `investigate_order_root_cause`,
which is the routing decision most likely to regress. Then a committed baseline with a CI regression
gate, multi-turn cases, and per-case latency and token-cost tracking.

**Effort.** Low-medium.

---

### QUA-3
**Nine hardcoded timeouts, none configurable.** · `low` · `open`

| Value | Location |
|---|---|
| 12s | `AgentHost/Tools/OrderTools.cs` — `RootCauseTimeout` |
| 10s | `AgentHost/Tools/OrderTools.cs` — `ActionRequestTimeout` |
| 25s | `AgentHost/Program.cs` — `ApproveOrderAction` client |
| 5s | `AgentHost/Program.cs` — `RejectOrderAction` client |
| 5s | `WorkflowOrchestrator/OrderInvestigation/InvestigationFanOutConsumer.cs` |
| 5s | `WorkflowOrchestrator/OrderAction/OrderActionValidationConsumer.cs` |
| 5s | `WorkflowOrchestrator/OrderAction/OrderActionExecutionConsumer.cs` |
| 3s | `Evaluation/LiveRunner.cs` — reachability probe |
| 60s | `Evaluation/LiveRunner.cs` — per-case timeout |

All `private static readonly`. None bindable.

**Impact.** These form a genuine nested budget — the 12s client sits above a 5s + 5s worst case, and
the 25s approve client above a 15s three-leg chain — and the reasoning is documented well in-code.
But tuning any of it requires a rebuild, and the relationships are enforced only by comments. A
change to one leg silently invalidates the outer figure.

**Proposed fix.** Bind to a validated options class with startup assertions on the *relationships*
(outer > sum of inner), so the budget is enforced rather than described. This becomes materially more
important if REL-1's retries are added, since retries change the worst case.

**Effort.** Low-medium.

---

### QUA-4
**History trimming is turn-counted, not token-counted.** · `low` · `open`

`ConversationSessionOptions.MaxTurns` defaults to 20; `AppendTurnsAsync` drops from the front once
exceeded.

**Impact.** Twenty long turns can still exceed the model's context window, and there is no
summarisation — dropped context is lost silently, mid-conversation, with no signal to the user or
the logs.

**Proposed fix.** Token-aware trimming against the deployment's context limit; optionally summarise
the dropped prefix into a synthetic turn rather than discarding it.

**Effort.** Medium.

---

### QUA-5
**`notification-service` and `frontend` have no tests.** · `low` · `open`

Neither `package.json` defines a `test` script — `notification-service` has `start`, `dev`,
`typecheck`; `frontend` has `dev`, `build`, `typecheck`, `lint`, `preview`. CI type-checks and lints
both and runs no tests, because there are none.

**Impact.** `notification-service` contains real logic that a type-checker cannot verify: MassTransit
envelope parsing, `nack`-without-requeue on a malformed message, and reconnection with backoff — the
last of which exists *because* a review found the consumer previously stayed dead for the life of the
process after any disconnect. That regression would not be caught today.

**Proposed fix.** `node:test` covers this with no new dependency: envelope parsing against a captured
MassTransit payload, malformed-message handling, and the reconnect schedule with a faked timer.
Frontend tests can reasonably wait until it stops being scaffold (ACC-2).

**Effort.** Low.

---

### QUA-6
**Tool results enter model context undelimited.** · `low` · `open`

Tool results are serialised into the conversation and returned to the model with no delimiting,
escaping, or trust marking.

**Impact.** Not reachable today — all domain data is seeded constants in `SeedDataConstants` and the
store classes. It becomes reachable the moment any field is customer-supplied: an order's `Reason`,
a product description. Injected text would then arrive with the same standing as legitimate data.

The blast radius is genuinely bounded by the architecture — there is no privileged tool to escalate
to, and mutations still require a human — so the realistic outcome is the agent *misinforming* an
operator rather than acting. That is a real harm, and smaller than it would otherwise be.

**Proposed fix.** Wrap tool output in explicit delimiters and instruct the model to treat the
contents as data. Revisit properly before any real data source is connected.

**Effort.** Low.

---

## Accepted — known, deliberate, not defects

### ACC-1
**Domain services are in-memory.** · `accepted`

Order, Inventory, and Product regenerate seed data per call, with mutations layered in a
process-lifetime `ConcurrentDictionary` overlay (`OrderMutationOverlay`, `InventoryMutationOverlay`).

**Reasoning.** Deliberate scope control, recorded in feature 006's `research.md` (Decision 7): the
orchestration is the subject of the project and the e-commerce domain is a swappable sample pack.
Giving three services real persistence would add CRUD with nothing to learn from.

**Consequences to state honestly.** Mutations do not survive a restart and do not exist across
replicas. The overlay also only ever grows — it has no eviction — so a long-lived host accumulates
override entries indefinitely. Both are properties of the in-memory design rather than separate
defects, and both disappear with real persistence.

---

### ACC-2
**`NexusOps.Server` and `frontend/` are scaffold.** · `accepted`

`NexusOps.Server` serves the built React application and does not call AgentHost. There is no chat
UI; all interaction is via HTTP client or `curl`.

**Reasoning.** ROADMAP's locked "no UI" decision. README and CLAUDE.md both mark it as scaffold, and
the architecture diagram labels the boundary explicitly.

**Note.** An approvals UI is the highest-value thing this scaffold could become, since it pairs
directly with SEC-1 — an approval surface that authenticates the human is the natural home for the
identity that gate currently lacks.

---

## Enhancements

Not defects. Ordered by leverage.

**E-1 · Auto-approval policy engine.** Evaluate a deterministic policy inside `OrderActionSaga` after
validation — amount below a threshold, order in an eligible status, velocity limits per customer and
window — and transition straight through to `Executing` with the decision recorded as `System`.
Critically, the agent's action space does not change: it still cannot approve. The new authority
lives in versioned, testable, auditable code rather than in a prompt. Depends on SEC-1 for the audit
trail to mean anything.

**E-2 · Approvals UI.** Build on ACC-2's scaffold. Pairs with SEC-1: authenticating the approver is
most naturally done at a real approval surface.

**E-3 · Provider fallback.** `AIAgent` is constructed in one place (`AgentServiceExtensions`), so a
second provider behind `Microsoft.Extensions.AI` is contained. The hard part is behavioural, not
structural: a different model routes differently, so failover silently changes behaviour unless each
provider has its own evaluation baseline. Pairs with QUA-2.

**E-4 · OTEL enrichment.** Span attributes for tool invocations and saga state transitions, and
metrics for pending-approval age (REL-3), tool-routing distribution, and per-request token cost
(SEC-3). Tracing exists; these are the domain-specific signals it lacks.

**E-5 · Multi-tenancy preparation.** `TenantId` on saga state with an EF global query filter, tenant
in the Redis key, and — most importantly — the tenant predicate enforced in the *tool handlers*,
never in the prompt. Tool results flow into model context, so a filter the model is merely asked to
respect is not a boundary.

---

## Notes on this document

Findings are recorded against a specific tree so they can be re-verified rather than trusted. Where
a judgement is contestable it is stated as such, with the reasoning that produced it, so a future
reader can disagree with the conclusion rather than guess at it.

Two entries were narrowed during review after the original claims proved too broad — REL-1 (the
retry policy is live and correctly aimed; only its *reach* is the gap) and REL-6 (the bus exclusion
is deliberate and documented; only the model-provider check is missing). Both are recorded in their
corrected, narrower form. SEC-4 was likewise reframed once history confirmed no credential was ever
committed and README §2 turned out to already document the placeholder shape the file had drifted
from — which made it a drift defect with a two-minute fix rather than a disclosure to accept.
