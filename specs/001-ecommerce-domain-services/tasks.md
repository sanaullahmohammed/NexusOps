# Tasks: E-Commerce Domain Services with Direct-Path Tool Integration

**Input**: Design documents from `specs/001-ecommerce-domain-services/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅

**Tests**: Not requested in spec — no test tasks generated.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story this task belongs to (US1–US4)

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create the four new projects and wire them into the solution. No user story work can proceed until these exist.

- [ ] T001 Create `NexusOps.Contracts` class library (`dotnet new classlib -n NexusOps.Contracts`) and add to `NexusOps.sln`
- [ ] T002 Create `NexusOps.OrderService` web project (`dotnet new web -n NexusOps.OrderService`) and add to `NexusOps.sln`
- [ ] T003 [P] Create `NexusOps.InventoryService` web project (`dotnet new web -n NexusOps.InventoryService`) and add to `NexusOps.sln`
- [ ] T004 [P] Create `NexusOps.ProductService` web project (`dotnet new web -n NexusOps.ProductService`) and add to `NexusOps.sln`
- [ ] T005 Add `NexusOps.Contracts` `<ProjectReference>` to `NexusOps.OrderService/NexusOps.OrderService.csproj`, `NexusOps.InventoryService/NexusOps.InventoryService.csproj`, `NexusOps.ProductService/NexusOps.ProductService.csproj`, and `NexusOps.AgentHost/NexusOps.AgentHost.csproj`
- [ ] T006 Add `<ProjectReference>` entries for all three domain services and `NexusOps.Contracts` to `NexusOps.AppHost/NexusOps.AppHost.csproj`
- [ ] T007 Add `Aspire.Hosting.AppHost` SDK and `Microsoft.Extensions.ServiceDiscovery` references to each domain service `.csproj`; copy `NexusOps.AgentHost/appsettings.json` pattern to each service

**Checkpoint**: `dotnet build NexusOps.sln` succeeds with four new empty projects compiling cleanly.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared Contracts types and AgentHost wiring changes that all user story phases depend on. No user story can be completed until this phase is done.

**⚠️ CRITICAL**: Phases 3–6 cannot begin until this phase is complete.

- [ ] T008 Implement `ToolResult<T>` (Success, Data, Error; `Ok`/`Fail` factory methods) in `NexusOps.Contracts/Dtos/ToolResult.cs`
- [ ] T009 Implement `SeedDataConstants` static class (all shared SKUs, order IDs, product IDs from data-model.md) in `NexusOps.Contracts/SeedDataConstants.cs`
- [ ] T010 Implement `ToolNames` static class (tool name + description string constants for all 6 tools from `contracts/tool-definitions.md`) in `NexusOps.Contracts/ToolNames.cs`
- [ ] T011 [P] Implement `OrderSummary` and `OrderLineItem` DTO records in `NexusOps.Contracts/Dtos/OrderSummary.cs`
- [ ] T012 [P] Implement `OrderAnomaly` DTO record in `NexusOps.Contracts/Dtos/OrderAnomaly.cs`
- [ ] T013 [P] Implement `InventoryAlert` and `InventoryLevel` DTO records in `NexusOps.Contracts/Dtos/InventoryAlert.cs` and `NexusOps.Contracts/Dtos/InventoryLevel.cs`
- [ ] T014 [P] Implement `ProductDetail` and `ProductSummary` DTO records in `NexusOps.Contracts/Dtos/ProductDetail.cs`
- [ ] T015 Update `NexusOps.AgentHost/Extensions/AgentServiceExtensions.cs` to resolve `IList<AITool>` from DI (via `IServiceProvider`) and pass it to `chatClient.AsAIAgent(...)` as the `tools` parameter
- [ ] T016 Create `NexusOps.AgentHost/Tools/ToolHandlerExtensions.cs` with empty `AddToolHandlers(this IServiceCollection services, IConfiguration config)` extension method scaffold; call it from `NexusOps.AgentHost/Program.cs` before `AddAgentServices`

**Checkpoint**: `dotnet build NexusOps.sln` succeeds. AgentHost wires with empty tool list — no runtime change yet.

---

## Phase 3: User Story 1 — Query Delayed Orders (Priority: P1) 🎯 MVP

**Goal**: Agent answers "Show me all delayed orders" end-to-end via the Direct path through the Order service.

**Independent Test**: `POST /api/chat {"prompt": "Show me all delayed orders"}` returns ORD-0001 and ORD-0002 with delay details.

- [ ] T017 Implement `OrderStatus` enum and `LineItem` record in `NexusOps.OrderService/Models/Order.cs` (statuses: pending, processing, shipped, delivered, delayed, cancelled)
- [ ] T018 Implement `Order` domain model in `NexusOps.OrderService/Models/Order.cs` (all fields from data-model.md including first-class `delayed` status)
- [ ] T019 Implement `OrderStore` with static in-memory list and seed data (10 orders per FR-007 using `SeedDataConstants` SKUs — at least 2 `delayed`, 1 referencing `SKU-ELEC-001`) in `NexusOps.OrderService/Data/OrderStore.cs`
- [ ] T020 Implement `OrderEndpoints.MapOrderEndpoints()` extension in `NexusOps.OrderService/Endpoints/OrderEndpoints.cs`: `GET /orders/anomalies?status=` and `GET /orders/{orderId}` per `contracts/order-service-api.md`; map internal `Order` to `OrderSummary`/`OrderAnomaly` Contracts DTOs
- [ ] T021 Wire `NexusOps.OrderService/Program.cs`: call `builder.AddServiceDefaults()`, `builder.Services.AddProblemDetails()`, `app.MapDefaultEndpoints()`, `app.MapOrderEndpoints()`
- [ ] T022 Register `order-service` in `NexusOps.AppHost/AppHost.cs`: `builder.AddProject<Projects.NexusOps_OrderService>("order-service").WithHttpHealthCheck("/health")`; add `.WithReference(orderService)` to the `agent-host` registration
- [ ] T023 Register named HttpClient `"order-service"` with `.AddServiceDiscovery()` in `NexusOps.AgentHost/Program.cs`
- [ ] T024 Implement `OrderTools` class in `NexusOps.AgentHost/Tools/OrderTools.cs`: constructor-inject `IHttpClientFactory` and `ILogger<OrderTools>`; implement `InvestigateOrderAnomalyAsync(string? status)` and `GetOrderDetailsAsync(string orderId)` methods using `ToolResult<T>` — catch all exceptions and return `ToolResult.Fail(reason)`
- [ ] T025 Register `OrderTools` in DI, create `AIFunction` instances via `AIFunctionFactory.Create(...)` using `ToolNames` constants, add to `IList<AITool>` singleton in `NexusOps.AgentHost/Tools/ToolHandlerExtensions.cs`; update agent instructions in `NexusOps.AgentHost/Configuration/AzureAIOptions.cs` — replace the existing `get_order_details` stub reference with `investigate_order_anomaly` and `get_order_details` as the canonical order tools; also remove the stub entries for `get_product_catalog` and `get_inventory_status` from the routing section (these are deprecated names replaced by tools registered in T033 and T041)

**Checkpoint**: `dotnet run --project NexusOps.AppHost` → order-service healthy in dashboard → `POST /api/chat "Show me all delayed orders"` returns ORD-0001 and ORD-0002.

---

## Phase 4: User Story 2 — Query Product Inventory Status (Priority: P2)

**Goal**: Agent answers "Which products are low on stock?" and "What is the stock level for SKU-X?" via the Inventory service.

**Independent Test**: `POST /api/chat {"prompt": "Which products are running low on stock?"}` returns at least SKU-ELEC-001 (Wireless Headphones Pro, 0 stock) and SKU-APRL-003 (below reorder threshold).

- [ ] T026 Implement `InventoryRecord` domain model in `NexusOps.InventoryService/Models/InventoryRecord.cs` (all fields from data-model.md)
- [ ] T027 Implement `InventoryStore` with seed data (15 inventory records using `SeedDataConstants` SKUs — at least 1 with zero stock on `SKU-ELEC-001`, at least 1 below reorder threshold on `SKU-APRL-003`) in `NexusOps.InventoryService/Data/InventoryStore.cs`
- [ ] T028 Implement `InventoryEndpoints.MapInventoryEndpoints()` in `NexusOps.InventoryService/Endpoints/InventoryEndpoints.cs`: `GET /inventory/alerts?outOfStockOnly=` and `GET /inventory/{sku}` per `contracts/inventory-service-api.md`; map to `InventoryAlert`/`InventoryLevel` Contracts DTOs
- [ ] T029 Wire `NexusOps.InventoryService/Program.cs`: `builder.AddServiceDefaults()`, `app.MapDefaultEndpoints()`, `app.MapInventoryEndpoints()`
- [ ] T030 Register `inventory-service` in `NexusOps.AppHost/AppHost.cs` with `WithHttpHealthCheck("/health")`; add `.WithReference(inventoryService)` to `agent-host`
- [ ] T031 Register named HttpClient `"inventory-service"` with `.AddServiceDiscovery()` in `NexusOps.AgentHost/Program.cs`
- [ ] T032 Implement `InventoryTools` class in `NexusOps.AgentHost/Tools/InventoryTools.cs`: `GetInventoryAlertsAsync(bool outOfStockOnly)` and `GetInventoryLevelAsync(string sku)` using `ToolResult<T>` with failure path
- [ ] T033 Register `InventoryTools` and create `AIFunction` instances in `ToolHandlerExtensions.cs`; update agent instructions in `AzureAIOptions.cs` — replace the deprecated `get_inventory_status` routing entry with `get_inventory_alerts` and `get_inventory_level` as the canonical inventory tools

**Checkpoint**: `POST /api/chat "Which products are running low on stock?"` returns both SKU-ELEC-001 (zero stock) and SKU-APRL-003 (below threshold). `POST /api/chat "What is the stock level for SKU-APRL-003?"` returns the specific inventory record.

---

## Phase 5: User Story 3 — Query Product Catalogue Details (Priority: P3)

**Goal**: Agent answers "What are the details for SKU-X?" and "List all Electronics products" via the Product service.

**Independent Test**: `POST /api/chat {"prompt": "What are the details for SKU-ELEC-001?"}` returns full product details including name, description, price, and category.

- [ ] T034 Implement `Product` domain model in `NexusOps.ProductService/Models/Product.cs` (all fields from data-model.md)
- [ ] T035 Implement `ProductStore` with seed data (15 products across 3 categories — Electronics, Apparel, Home & Garden — using `SeedDataConstants` SKUs) in `NexusOps.ProductService/Data/ProductStore.cs`
- [ ] T036 Implement `ProductEndpoints.MapProductEndpoints()` in `NexusOps.ProductService/Endpoints/ProductEndpoints.cs`: `GET /products/{sku}` and `GET /products?category=` per `contracts/product-service-api.md`; map to `ProductDetail`/`ProductSummary` Contracts DTOs
- [ ] T037 Wire `NexusOps.ProductService/Program.cs`: `builder.AddServiceDefaults()`, `app.MapDefaultEndpoints()`, `app.MapProductEndpoints()`
- [ ] T038 Register `product-service` in `NexusOps.AppHost/AppHost.cs` with `WithHttpHealthCheck("/health")`; add `.WithReference(productService)` to `agent-host`
- [ ] T039 Register named HttpClient `"product-service"` with `.AddServiceDiscovery()` in `NexusOps.AgentHost/Program.cs`
- [ ] T040 Implement `ProductTools` class in `NexusOps.AgentHost/Tools/ProductTools.cs`: `GetProductDetailsAsync(string sku)` and `ListProductsByCategoryAsync(string? category)` — `category` is optional (null = return all products, matching FR-006 "list all products"); use `ToolResult<T>` with failure path
- [ ] T041 Register `ProductTools` and create `AIFunction` instances in `ToolHandlerExtensions.cs`; update agent instructions in `AzureAIOptions.cs` — replace the deprecated `get_product_catalog` routing entry with `get_product_details` and `list_products_by_category` as the canonical product tools; do a final review of the full instructions routing section to confirm no deprecated tool names (`get_product_catalog`, `get_inventory_status`) remain

**Checkpoint**: All three domain services healthy in Aspire dashboard. `POST /api/chat "List all Electronics products"` returns 5 products. `POST /api/chat "What are the details for SKU-ELEC-001?"` returns full product record.

---

## Phase 6: User Story 4 — Cross-Service Investigation (Priority: P2)

**Goal**: Agent answers "Are there orders for products that are out of stock?" by composing two tool calls in a single turn.

**Independent Test**: `POST /api/chat {"prompt": "Are there any orders for products that are currently out of stock?"}` triggers both `investigate_order_anomaly` and `get_inventory_alerts`, cross-references, and returns ORD-0003.

**Note**: This story requires no new services or tools — it validates multi-tool reasoning using components from Phases 3–5. It depends on US1 (order tools) and US2 (inventory tools) being complete.

- [ ] T042 [US4] Review `NexusOps.OrderService/Data/OrderStore.cs` and `NexusOps.InventoryService/Data/InventoryStore.cs` to confirm ORD-0003 references `SKU-ELEC-001` (zero stock) per FR-007 cross-service integrity requirement; fix seed data if inconsistent
- [ ] T043 [US4] Update agent instructions in `NexusOps.AgentHost/Configuration/AzureAIOptions.cs`: add explicit multi-tool reasoning guidance — when a cross-service query is detected, the agent should call both relevant read tools and synthesise results rather than stopping after one tool
- [ ] T044 [US4] Manual verification: run `POST /api/chat "Are there any orders for products that are currently out of stock?"` and confirm response references ORD-0003 and SKU-ELEC-001 together

**Checkpoint**: Agent demonstrates multi-tool composition in a single turn and surfaces the at-risk order. Cross-service story (SC-006) satisfied.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Finalize solution structure, validate observability requirements, and confirm all success criteria.

- [ ] T045 Add `NexusOps.Contracts`, `NexusOps.OrderService`, `NexusOps.InventoryService`, and `NexusOps.ProductService` entries to `NexusOps.deployable.slnf` (Contracts must be included because the three domain services reference it — omitting it breaks the solution filter build)
- [ ] T046 [P] Run `dotnet build NexusOps.deployable.slnf` and confirm zero warnings/errors across all deployable projects
- [ ] T047 [P] Verify all five services appear healthy (green) in the Aspire dashboard after `dotnet run --project NexusOps.AppHost` (satisfies SC-002)
- [ ] T048 [P] Open Aspire dashboard → Traces tab, send one chat request per tool, and confirm distributed traces span AgentHost → each domain service (satisfies SC-005)
- [ ] T049 Run full `specs/001-ecommerce-domain-services/quickstart.md` validation: all four curl scenarios return expected results (SC-001, SC-003, SC-006)
- [ ] T050 Update `CLAUDE.md` Current Build State section to document that Order, Inventory, and Product services are now implemented with in-memory seed data and Direct-path tools are wired

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately; T002/T003/T004 parallelisable after T001
- **Phase 2 (Foundational)**: Requires Phase 1 complete — **blocks Phases 3–6**
- **Phase 3 (US1)**: Requires Phase 2 complete — no dependency on US2/US3/US4
- **Phase 4 (US2)**: Requires Phase 2 complete — no dependency on US1/US3/US4
- **Phase 5 (US3)**: Requires Phase 2 complete — no dependency on US1/US2/US4
- **Phase 6 (US4)**: Requires Phase 3 (US1) AND Phase 4 (US2) complete
- **Phase 7 (Polish)**: Requires Phases 3–5 complete

### User Story Dependencies

- **US1 (P1)**: Independent after Phase 2
- **US2 (P2)**: Independent after Phase 2 — can run in parallel with US1
- **US3 (P3)**: Independent after Phase 2 — can run in parallel with US1/US2
- **US4 (P2)**: Depends on US1 + US2 (needs order tools + inventory tools)

### Within Each Phase

- Models before stores; stores before endpoints; endpoints before AppHost registration; AppHost registration before AgentHost tool handlers; tool handlers before instructions update

---

## Parallel Opportunities

### Phase 1

```
T001 (Contracts project) → unblocks T002, T003, T004 (all parallel)
                          → T005 (add Contracts refs, after T002–T004)
                          → T006 (add service refs to AppHost, after T002–T004)
```

### Phase 2

```
T008 (ToolResult), T009 (SeedDataConstants), T010 (ToolNames) → sequential foundation
T011, T012, T013, T014 → all parallel (different DTO files, no dependencies between them)
T015 (AgentServiceExtensions update) → parallel with T011–T014
T016 (ToolHandlerExtensions scaffold) → parallel with T011–T014
```

### Phases 3–5 (after Phase 2 complete)

```
Phase 3 (US1) and Phase 4 (US2) and Phase 5 (US3) → all three can run in parallel
Within each phase: T01x model → T01x+1 store → T01x+2 endpoints → T01x+3 program → T01x+4 apphost → T01x+5 httpclient → T01x+6 tools → T01x+7 registration
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Complete Phase 1: Setup (T001–T007)
2. Complete Phase 2: Foundational (T008–T016)
3. Complete Phase 3: US1 — Delayed Orders (T017–T025)
4. **STOP and VALIDATE**: `POST /api/chat "Show me all delayed orders"` returns correct data
5. Agent is demonstrably end-to-end on the Direct path — demo-ready

### Incremental Delivery

1. Phase 1 + Phase 2 → foundation
2. Phase 3 → US1 working → first demo
3. Phase 4 → US2 working → inventory queries added
4. Phase 5 → US3 working → product catalogue added
5. Phase 6 → US4 working → cross-service reasoning validated
6. Phase 7 → polish, observability verified, CLAUDE.md updated

### Parallel Team Strategy

With two developers after Phase 2:

- **Dev A**: Phase 3 (US1 — Order service + order tools)
- **Dev B**: Phase 4 (US2 — Inventory service + inventory tools) in parallel
- Both merge → Phase 6 (US4) immediately testable
- **Dev A or B**: Phase 5 (US3 — Product service) in parallel with Phase 6

---

## Notes

- `[P]` tasks target different files with no dependencies — safe to parallelise
- `[Story]` label maps each task to its user story for traceability
- Each user story phase delivers a complete, independently testable increment
- Seed data in all three services must use `SeedDataConstants` to guarantee cross-service SKU integrity (FR-007, clarification Q4)
- Tool handlers must never throw — always return `ToolResult<T>.Fail(reason)` on any exception (FR-013, clarification Q3)
- `delayed` is a first-class `OrderStatus` enum value set explicitly in seed data — not computed (clarification Q2)
- Tool handlers are `AIFunction` instances created via `AIFunctionFactory.Create(...)` and passed to `AsAIAgent(tools: ...)` at construction (research Decision 1)
