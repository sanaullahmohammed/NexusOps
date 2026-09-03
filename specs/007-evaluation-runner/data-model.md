# Data Model: Evaluation Runner

## EvaluationCase (dataset entry)

One entry in `NexusOps.Evaluation/Data/eval-cases.json`. The dataset file is a JSON array of these.

| Field | Type | Required | Notes |
|---|---|---|---|
| `id` | string | yes | Unique within the dataset (FR-006). Convention: `case-NNN`, zero-padded, stable once assigned (referenced in failure reports). |
| `prompt` | string | yes | The realistic natural-language user prompt sent verbatim to `POST /api/chat`. |
| `expectedTool` | string | yes | Must equal one of `NexusOps.Contracts.ToolNames`'s tool-name constants (FR-007). |
| `expectedPath` | string | yes | `"Direct"` or `"Saga"` (FR-008); must match `ToolCatalog`'s recorded path for `expectedTool`. |
| `notes` | string | no | Free-text rationale for why this case exists / what routing shape it probes. Not validated beyond being a string when present. |

Deserialized case-insensitively (`PropertyNameCaseInsensitive = true`) so hand-edited JSON isn't punished for casing. No defaults are substituted for a missing required field — a missing field is a validation failure (FR-009), not a silently-tolerated gap.

## EvaluationDataset

The full deserialized array of `EvaluationCase`, plus the file path it was loaded from (carried through only for error messages — "malformed JSON in Data/eval-cases.json" is more actionable than a bare parser exception).

## ValidationIssue

Produced by `DatasetValidator`, one per defect found. Validation collects every issue in a single pass rather than stopping at the first (FR-010).

| Field | Type | Notes |
|---|---|---|
| `CaseId` | string? | The offending case's `id`, or `null` for a dataset-wide issue (e.g., malformed JSON, a duplicate ID — reported against both duplicates). |
| `Message` | string | Human-readable description of the specific defect. |

Validation categories, each producing zero or more `ValidationIssue`s:
- Dataset file missing / not valid JSON → one dataset-wide issue, short-circuits the rest (nothing else can be checked against an unparsed file).
- Case count outside \[20, 30\] → one dataset-wide issue (FR-001).
- Missing/empty required field on a case → one issue per empty field.
- Duplicate `id` → one issue naming each case sharing the id.
- `expectedTool` not in `ToolCatalog`'s known set → one issue.
- `expectedPath` not `"Direct"`/`"Saga"`, or inconsistent with `expectedTool`'s actual path → one issue.
- Any of the 9 curated tools with zero cases referencing it, or either path with zero cases → one issue each (FR-003/FR-004).

## ToolCatalog (reference data, not part of the dataset)

Built once at startup:
- **Known tool names**: reflected from `NexusOps.Contracts.ToolNames`'s public `const string` fields, excluding any ending in `Description`.
- **Tool → path map**: a fixed table (research.md Decision 4) —

| Tool | Path |
|---|---|
| `investigate_order_anomaly` | Direct |
| `get_order_details` | Direct |
| `get_inventory_alerts` | Direct |
| `get_inventory_level` | Direct |
| `get_product_details` | Direct |
| `list_products_by_category` | Direct |
| `investigate_order_root_cause` | Saga |
| `request_order_refund` | Saga |
| `request_order_cancellation` | Saga |

## EvaluationResult (live-mode, per case)

| Field | Type | Notes |
|---|---|---|
| `CaseId` | string | From the source `EvaluationCase`. |
| `ExpectedTool` | string | From the source `EvaluationCase`. |
| `ToolsInvoked` | `IReadOnlyList<string>` | From the `AgentHost` response's new `toolsInvoked` field; empty when the agent invoked no tool. |
| `Passed` | bool | `ExpectedTool` is contained in `ToolsInvoked` (FR-013). |
| `Error` | string? | Set instead of `ToolsInvoked` when the HTTP call itself failed or timed out (FR-018); such a case is always `Passed = false`. |

## EvaluationSummary (live-mode, run-level)

| Field | Type | Notes |
|---|---|---|
| `Total` | int | Cases attempted. |
| `Passed` | int | |
| `Failed` | int | `Total - Passed`. |
| `PassRate` | double | `Passed / Total`, reported as a percentage (FR-015). |

A run that never reaches this stage (AgentHost unreachable) produces no `EvaluationSummary` at all — only a skip banner and setup guidance (FR-017); this is a distinct, third outcome, not a summary with `Total = 0`.

## AgentHost contract change: `ChatResponse.ToolsInvoked`

See [contracts/agent-chat-response.md](contracts/agent-chat-response.md).
