# Research: E-Commerce Domain Services with Direct-Path Tool Integration

## Decision 1: Tool Registration with Microsoft.Agents.AI

**Decision**: Tools are registered as `Microsoft.Extensions.AI.AIFunction` instances and passed to `chatClient.AsAIAgent(...)` via its `tools` parameter (`IList<AITool>`).

**Rationale**: The `AsAIAgent` extension method signature (confirmed from `Microsoft.Agents.AI.OpenAI` 1.0.0 XML docs) accepts `IList<Microsoft.Extensions.AI.AITool>` at construction time. `AIFunction` (created via `AIFunctionFactory.Create(...)`) is the concrete `AITool` subtype used for function-calling tools. This is the standard Microsoft.Extensions.AI pattern and requires no external tool registry.

**Implication for AgentHost**: The `AddAgentServices` extension method must be updated to accept an `IList<AITool>` (resolved from DI) and pass it through to `AsAIAgent`. Tool handlers are registered individually in DI, then collected and passed at agent construction.

**Alternatives considered**:
- Post-construction `agent.RegisterTool(...)` — no such API exists in v1.0.0; tools must be declared at construction.
- `AIAgentBuilder` middleware pipeline — supports wrapping agents for context/middleware but not the right level for domain tool registration.

---

## Decision 2: Aspire Service-to-Service HTTP Communication

**Decision**: AgentHost uses named `HttpClient` instances resolved via Aspire service discovery. Each domain service gets its own named client (e.g., `"order-service"`, `"inventory-service"`, `"product-service"`). Service names match the Aspire resource names registered in AppHost.

**Rationale**: Aspire's `AddServiceDefaults()` already registers service discovery infrastructure. Named `HttpClient` instances registered with `AddHttpClient("name").AddServiceDiscovery()` resolve base addresses using the Aspire service name (`http://order-service`) — no hardcoded ports or hostnames. This satisfies FR-012 and SC-004.

**Wiring pattern**:
- AppHost: `agentHost.WithReference(orderService).WithReference(inventoryService).WithReference(productService)`
- AgentHost `Program.cs`: `builder.Services.AddHttpClient("order-service").AddServiceDiscovery()` (repeated for each service)
- Tool handler: `IHttpClientFactory.CreateClient("order-service")` → scoped HTTP call

**Alternatives considered**:
- Typed HttpClients — valid but adds a class per service; named clients are lighter for three simple services.
- Direct URL injection via env vars — violates SC-004 and FR-012 (hardcoded URL prohibition).

---

## Decision 3: NexusOps.Contracts Library Structure

**Decision**: `NexusOps.Contracts` is a plain `Microsoft.NET.Sdk` class library (not Aspire-aware). It contains:
1. **Tool descriptor constants** — `ToolNames` static class with string constants for tool names/descriptions (used by both the `AIFunctionFactory.Create` call in AgentHost and the agent instructions).
2. **Response DTOs** — read-model records shared between domain service HTTP responses and tool handler deserialization (e.g., `OrderSummary`, `InventoryAlert`, `ProductDetail`).
3. **Tool result types** — `ToolResult<T>` wrapper with `bool Success`, `T? Data`, `string? Error` — used by all tool handlers to satisfy FR-013.

**What Contracts does NOT contain**: No HttpClient code, no DI registrations, no framework references beyond `System.Text.Json`.

**Rationale**: Keeping Contracts as a pure data/descriptor library allows it to be referenced by AgentHost, domain services (for shared DTOs), and future test projects without pulling in infrastructure dependencies. Constitution Principle II requires tool definitions to be owned by Contracts; Principle V requires domain-agnostic core — a thin Contracts library satisfies both.

**Alternatives considered**:
- Embedding DTOs in each domain service — would require AgentHost to reference every domain service project, creating a dependency web.
- Generating contracts from OpenAPI specs — premature for in-memory services at this stage.

---

## Decision 4: Domain Service Project Layout

**Decision**: Each domain service (`NexusOps.OrderService`, `NexusOps.InventoryService`, `NexusOps.ProductService`) follows an identical minimal layout:

```
NexusOps.[X]Service/
├── Program.cs               — Aspire wiring, endpoint mapping, seed data registration
├── Models/                  — Internal domain models (may differ from Contracts DTOs)
├── Data/                    — In-memory store + seed data (static list, seeded at startup)
├── Endpoints/               — Minimal API endpoint map extensions
└── NexusOps.[X]Service.csproj
```

**Rationale**: Uniform layout across three services reduces cognitive load and makes the pattern easy to extend. `Models/` vs Contracts DTOs separation allows services to evolve their internal representation independently. Seed data lives in `Data/` as a static `SeedData` class to keep `Program.cs` lean.

**Alternatives considered**:
- Single `DomainServices` project with three namespaces — would violate Constitution Principle V (domain pluggability requires services to be independently swappable).
- Repository pattern with interfaces — unnecessary abstraction over a static in-memory list; deferred until persistent storage is introduced.

---

## Decision 5: Seed Data SKU Consistency Strategy

**Decision**: A single `SeedDataConstants` class in `NexusOps.Contracts` defines all shared SKUs, order IDs, and product IDs used across services. Each service's `SeedData` class references these constants to guarantee cross-service referential integrity (satisfies clarification Q4 and FR-007).

**Rationale**: Without a shared constant source, three independently written seed datasets will diverge. Putting constants in Contracts (already referenced by all services and AgentHost) is the lowest-friction approach.

**Seed data minimum (per FR-007)**:
- 10 orders: 2 `delayed`, 1 `cancelled`, 2 `shipped`, 2 `processing`, 2 `delivered`, 1 `pending`
- 1 order referencing a product with zero inventory stock
- 15 products across 3 categories (Electronics, Apparel, Home & Garden)
- 15 inventory records: 2 below reorder threshold, 1 at zero stock

---

## Decision 6: Tool Handler Error Handling

**Decision**: All tool handlers return `ToolResult<T>` (defined in Contracts). On HTTP failure (any exception, non-2xx status code, timeout), the handler catches the error, logs it, and returns `ToolResult<T>.Fail(reasonString)` — never rethrows (satisfies clarification Q3 and FR-013).

**Rationale**: Returning a typed failure result keeps the agent in control. The agent's instructions already encode graceful degradation behaviour ("present the data you have and explicitly warn the user that the information is incomplete"). A thrown exception would surface as an unhandled tool error in the agent framework, potentially swallowing partial results from other tool calls in the same turn.

**Pattern**:
```csharp
try {
    var response = await httpClient.GetFromJsonAsync<T>(endpoint, ct);
    return ToolResult<T>.Ok(response!);
} catch (Exception ex) {
    logger.LogWarning(ex, "Tool call to {Service} failed", serviceName);
    return ToolResult<T>.Fail($"{serviceName} is temporarily unavailable.");
}
```
