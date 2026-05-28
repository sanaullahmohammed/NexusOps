# Implementation Readiness Checklist: E-Commerce Domain Services with Direct-Path Tool Integration

**Purpose**: Validate requirements quality, contract completeness, and spec readiness before and during implementation. Serves as both a pre-implementation gate (author) and a PR review aid (reviewer).
**Created**: 2026-05-28
**Feature**: [spec.md](../spec.md)

**Focus areas**: Integration contract quality · Observability & operational readiness · Seed data spec quality · Cross-cutting consistency (post-remediation)

---

## Requirement Completeness

- [ ] CHK001 Does FR-004 specify all three Order service read operations fully — including the "list orders by status" gap identified in `/speckit-analyze` finding C1 — or does at least one operation lack an endpoint in the contract? [Completeness, Spec §FR-004, Gap]
- [ ] CHK002 Does FR-005 enumerate both Inventory read operations (low-stock alert list AND point-lookup by SKU) with sufficient detail that each maps to a distinct endpoint without ambiguity? [Completeness, Spec §FR-005]
- [ ] CHK003 After the C2 remediation (making `category` optional), does FR-006's "list all products" operation now have end-to-end coverage: spec requirement → tool definition → service endpoint? [Completeness, Spec §FR-006]
- [ ] CHK004 Are all 6 tool names listed explicitly in `contracts/tool-definitions.md`, and is there a corresponding requirement (FR-002/FR-008) that names each one — or are any tools implied but not named in the spec? [Completeness, Spec §FR-002]
- [ ] CHK005 Does the spec define what the agent must do when no curated tool matches a user query — is the "cannot fulfil" response behaviour a requirement (FR or edge case) or only informal guidance? [Completeness, Spec §Edge Cases]
- [ ] CHK006 Are structured log requirements defined for domain services beyond "emit OTEL traces" — does FR-010 or the spec specify which events must be logged at minimum (e.g., request received, store queried, error returned)? [Completeness, Spec §FR-010, Gap]

---

## Requirement Clarity

- [ ] CHK007 Is the `ToolResult<T>` failure contract in FR-013 specific enough to implement consistently — does the spec define what constitutes a "human-readable reason string" (format, max length, must name the service)? [Clarity, Spec §FR-013]
- [ ] CHK008 Is FR-012's "Aspire service discovery named client" pattern described precisely enough that an implementor knows the base URL scheme (`http://service-name`) without consulting external Aspire docs? [Clarity, Spec §FR-012, Plan §Phase D]
- [ ] CHK009 Is the routing distinction between `investigate_order_anomaly` (returns a list of anomalous orders) and `get_order_details` (returns one order by ID) sufficiently specified that LLM tool selection is deterministic for the same query? [Clarity, contracts/tool-definitions.md]
- [ ] CHK010 Does the spec define what "structured, human-readable results" means for agent responses — are formatting expectations (markdown tables, bullets, field ordering) specified as requirements or left to LLM discretion? [Clarity, Spec §US1]
- [ ] CHK011 Is the `delayed` status definition sufficiently precise — does the spec state that it is a first-class `OrderStatus` enum value set at seed time, and that no runtime date-comparison logic is required? [Clarity, Spec §Clarifications, FR-007]
- [ ] CHK012 Does FR-009 define what "appear in the Aspire dashboard" means as an acceptance criterion — is it the health check turning green, the resource appearing in the Resources tab, or both? [Clarity, Spec §FR-009, SC-002]

---

## Integration Contract Quality

- [ ] CHK013 Does each of the 6 tool definitions in `contracts/tool-definitions.md` specify both the success return type AND the exact failure return string — are any tools missing either? [Completeness, contracts/tool-definitions.md]
- [ ] CHK014 Are the 6 tool names in `contracts/tool-definitions.md` verified to be identical (character-for-character) to the names that will appear in `ToolNames.cs` constants — is there a risk of capitalisation or underscore drift? [Consistency, contracts/tool-definitions.md]
- [ ] CHK015 Does the Order service API contract define HTTP response codes for all non-200 cases — specifically, is a 404 response defined for `GET /orders/{orderId}` when the ID does not exist in seed data? [Completeness, contracts/order-service-api.md]
- [ ] CHK016 Does the Inventory service API contract clarify whether `outOfStockOnly` omitted and `outOfStockOnly=false` produce identical results — or is this behaviour undefined? [Clarity, contracts/inventory-service-api.md]
- [ ] CHK017 After the C2 remediation, does the Product service API contract (`GET /products`) explicitly state that omitting `category` returns all 15 products — not an empty list or an error? [Clarity, contracts/product-service-api.md]
- [ ] CHK018 Are the Aspire resource names in the contracts (`order-service`, `inventory-service`, `product-service`) the canonical names — are they consistent with AppHost resource registration names specified in tasks T022/T030/T038? [Consistency, contracts/, tasks.md]
- [ ] CHK019 Do the tool parameter names in `contracts/tool-definitions.md` match the C# method parameter names specified in tasks T024, T032, and T040 — is there any drift (e.g., `orderId` vs `order_id`, `outOfStockOnly` vs `outOfStock`)? [Consistency, contracts/tool-definitions.md, tasks.md]
- [ ] CHK020 Is the `ToolResult<T>` failure message wording consistent across all 6 tool definitions — do they follow the same pattern (e.g., `"{ServiceName} is temporarily unavailable."`) without variation? [Consistency, contracts/tool-definitions.md]

---

## Observability & Operational Readiness

- [ ] CHK021 Does SC-005 specify which unit of work must produce a visible trace — is it one trace per tool invocation, per HTTP call to a domain service, or per full chat turn? [Clarity, Spec §SC-005]
- [ ] CHK022 Is the `/health` endpoint response body format specified beyond "HTTP 200 when healthy" — does FR-011 define the response schema, or is the body format left open? [Clarity, Spec §FR-011]
- [ ] CHK023 Are health check probe requirements defined consistently — does FR-009 specify the path (`/health`), expected status code (200), and whether AppHost uses `WithHttpHealthCheck` or a separate probe definition? [Completeness, Spec §FR-009]
- [ ] CHK024 Does FR-010 specify which OTEL signal types are required for domain services — traces only, or also metrics and structured logs? [Completeness, Spec §FR-010]
- [ ] CHK025 Are the five services (AgentHost, Server, OrderService, InventoryService, ProductService) all listed as required healthy in SC-002 — or does SC-002 only cover the three domain services? [Completeness, Spec §SC-002]

---

## Seed Data Specification Quality

- [ ] CHK026 Does FR-007 define the minimum number of inventory records (not just products) — does the spec guarantee a 1:1 correspondence between the 15 products and their inventory records, or is the count undefined? [Completeness, Spec §FR-007]
- [ ] CHK027 Is the cross-service SKU consistency requirement testable as written — does the spec name the specific SKU (e.g., SKU-ELEC-001) that must appear in both OrderStore (as a line item on a delayed order) and InventoryStore (with zero stock)? [Measurability, Spec §FR-007, Clarification Q4]
- [ ] CHK028 Does the seed data spec define supporting identifiers (customer IDs, warehouse IDs) to the level needed to implement the DTOs — or are those left as implementor discretion? [Completeness, Spec §FR-007, data-model.md]
- [ ] CHK029 Is the 3-category product set (Electronics, Apparel, Home & Garden) the closed canonical list in the spec — or could an implementor reasonably add a fourth category? [Clarity, Spec §Key Entities, FR-007]
- [ ] CHK030 Does FR-007 specify the minimum order status distribution (e.g., at least 2 delayed, 1 cancelled, 2 shipped) — or only the total count of 10, leaving the status mix to the implementor? [Clarity, Spec §FR-007]
- [ ] CHK031 Is the requirement that "at least 1 order references a product with zero inventory" precise enough to be verifiable — does the spec name the order ID (ORD-0003) and SKU (SKU-ELEC-001), or only state the constraint abstractly? [Measurability, Spec §FR-007, data-model.md]

---

## Acceptance Criteria Quality

- [ ] CHK032 Is SC-001's 10-second E2E target defined to include or exclude Azure AI Foundry network latency — could the criterion pass or fail based solely on LLM provider response time, outside the implementation's control? [Clarity, Spec §SC-001]
- [ ] CHK033 Is SC-003 ("80% routing accuracy across 5 varied queries") measurable — does the spec or quickstart.md enumerate the specific 5 test queries, or is "varied" left to the tester's discretion? [Measurability, Spec §SC-003, Gap]
- [ ] CHK034 Can SC-006 ("at least one result returned for each query type") be verified without knowing expected values — does the spec define which result IDs or SKUs should appear for each query type? [Measurability, Spec §SC-006]
- [ ] CHK035 Is SC-004 ("no hardcoded URLs") objectively verifiable — does the spec define what "hardcoded URL" means (string literal, config value, env var) precisely enough to pass or fail a code review? [Measurability, Spec §SC-004]

---

## Scenario & Edge Case Coverage

- [ ] CHK036 Is the agent's partial-results behaviour defined when one tool in a multi-tool turn returns `ToolResult.Fail(...)` — does the spec specify whether the agent should return the successful results alongside the failure notice, or report total failure? [Coverage, Spec §Edge Cases, FR-013]
- [ ] CHK037 Is the "ambiguous query" edge case (e.g., "check order 123") resolved with a deterministic rule in the spec — or does it rely on LLM judgement with no specified fallback? [Clarity, Spec §Edge Cases]
- [ ] CHK038 Does the spec define behaviour when all three domain services are simultaneously unavailable — is total degradation (all tool handlers return Fail) addressed anywhere? [Coverage, Edge Case, Gap]
- [ ] CHK039 Are requirements defined for invalid parameters passed to tool handlers (non-existent SKU, malformed order ID) — is the expected `ToolResult` failure message specified for 404-class errors vs network-class errors? [Coverage, Spec §FR-013, Edge Cases]

---

## Terminology & Consistency (Post-Remediation Validation)

- [ ] CHK040 After the F3 remediation, is "payment-failed" used as the canonical anomaly type across all four locations: FR-004, the OrderAnomaly entity definition, `contracts/tool-definitions.md`, and `data-model.md` — are any "overdue" references remaining? [Consistency, Spec §FR-004]
- [ ] CHK041 After the F1 remediation guidance in tasks T025/T033/T041, is the set of deprecated tool names to remove (`get_product_catalog`, `get_inventory_status`) named explicitly in tasks.md — not just implied by "replace"? [Consistency, tasks.md T025/T033/T041]
- [ ] CHK042 Is "Direct path" used as the sole canonical term for single-service HTTP reads throughout spec, plan, contracts, and tasks — are there any uses of "synchronous path", "read path", or "fast path" that should be normalised? [Consistency, Spec §Architecture, plan.md]

---

## Dependencies & Traceability

- [ ] CHK043 Is every FR traceable to at least one task in tasks.md — specifically, does the "list orders by status" operation (FR-004 gap, analysis finding C1) have a corresponding task, or does it remain uncovered? [Traceability, Spec §FR-004, Gap]
- [ ] CHK044 Are the agent instruction changes for T025/T033/T041 specified precisely enough that two implementors would produce identical routing sections in `AzureAIOptions.cs` — or does "update instructions" leave too much discretion? [Clarity, tasks.md T025/T033/T041]
- [ ] CHK045 Is the `NexusOps.Contracts` project reference explicitly listed in T006's AppHost scope — and has the analysis finding C3 (AppHost should NOT reference Contracts) been reflected in the tasks? [Traceability, tasks.md T006, analysis finding C3]
