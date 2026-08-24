# Feature Specification: E-Commerce Domain Services with Direct-Path Tool Integration

**Feature Branch**: `001-ecommerce-domain-services`

**Created**: 2026-05-28

**Status**: Implemented

**Input**: User description: "E-Commerce Domain Services with Direct-Path Tool Integration: Create NexusOps.Contracts (curated tool definitions), implement Order/Inventory/Product ASP.NET Core Minimal API services with in-memory seed data, wire tools into AgentHost, and register all services in AppHost with service discovery and health checks. This is the first end-to-end slice enabling the agent to answer read queries like 'show me delayed orders' via the Direct path."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Query Delayed Orders (Priority: P1)

An operations user asks the AI agent a natural language question about delayed orders. The agent selects the appropriate curated tool, dispatches a read request to the Order service, and returns structured, human-readable results — all without the user knowing about any underlying service topology.

**Why this priority**: This is the northstar end-to-end slice. It validates the full Direct path from LLM reasoning → tool selection → domain service HTTP call → response. Every other capability builds on this foundation.

**Independent Test**: Can be fully tested by sending `POST /api/chat` with `{"prompt": "Show me all delayed orders"}` and verifying the response includes order data with status/delay details.

**Acceptance Scenarios**:

1. **Given** the Agent Host is running with tools wired and the Order service is healthy, **When** a user sends "Show me all delayed orders", **Then** the agent selects the `investigate_order_anomaly` tool, calls the Order service, and returns a list of delayed orders with order IDs, expected delivery dates, and current status.
2. **Given** there are no delayed orders in seed data, **When** a user asks about delayed orders, **Then** the agent returns a clear response indicating no delayed orders were found.
3. **Given** the Order service is unavailable, **When** a user asks about delayed orders, **Then** the agent returns a graceful error message indicating the information is temporarily unavailable.

---

### User Story 2 - Query Product Inventory Status (Priority: P2)

An operations user asks about inventory levels for products — e.g., "Which products are low on stock?" The agent routes this to the Inventory service via a curated tool and returns inventory status for relevant products.

**Why this priority**: Validates that the Direct path works across multiple domain services, not just Order. Demonstrates the tool routing logic selects the right service based on intent.

**Independent Test**: Can be fully tested by sending `POST /api/chat` with `{"prompt": "Which products are running low on stock?"}` and verifying the response contains product names and inventory levels.

**Acceptance Scenarios**:

1. **Given** the Agent Host and Inventory service are running, **When** a user asks "Which products are low on stock?", **Then** the agent selects the `get_inventory_alerts` tool and returns a list of products with low stock thresholds breached.
2. **Given** a user asks about a specific product's inventory, **When** the product exists in seed data, **Then** the agent returns the current stock level, reorder threshold, and warehouse location for that product.

---

### User Story 3 - Query Product Catalogue Details (Priority: P3)

An operations user asks about product details — e.g., "Tell me about the specifications of product SKU-12345." The agent routes this to the Product service and returns structured product information.

**Why this priority**: Completes the three-service coverage. Validates that tool definitions cover the full e-commerce read domain, making the agent capable of answering the widest range of operational queries.

**Independent Test**: Can be fully tested by sending `POST /api/chat` with `{"prompt": "What are the details for SKU-12345?"}` and verifying the response includes product name, description, price, and category.

**Acceptance Scenarios**:

1. **Given** the Agent Host and Product service are running with seed data, **When** a user asks about a specific product SKU, **Then** the agent selects the `get_product_details` tool and returns name, description, price, and category.
2. **Given** a user asks for products in a category, **When** matching products exist, **Then** the agent returns a list of matching products with summary details.

---

### User Story 4 - Cross-Service Investigation (Priority: P2)

An operations user asks a question that spans multiple services — e.g., "Are there any orders for out-of-stock products?" The agent identifies this as a multi-service read query, calls both the Order and Inventory tools, and synthesises the results into a coherent answer.

**Why this priority**: Validates that the agent can orchestrate multiple Direct-path tool calls within a single turn, demonstrating the reasoning layer's ability to compose information from disparate services.

**Independent Test**: Can be fully tested by sending a cross-service query and verifying the agent makes multiple tool calls and produces a synthesised response.

**Acceptance Scenarios**:

1. **Given** the Agent Host, Order service, and Inventory service are all running, **When** a user asks "Are there orders for products that are out of stock?", **Then** the agent calls both `investigate_order_anomaly` and `get_inventory_alerts`, cross-references the results, and returns a list of at-risk orders.

---

### Edge Cases

- What happens when the agent receives a query that matches no curated tool? The agent must respond with a clear message that it cannot fulfil the request rather than hallucinating a tool call.
- What happens when a domain service returns a partial dataset (e.g., Order service returns orders but Product service times out)? The tool handler returns a structured error result; the agent surfaces partial results from successful calls alongside a clear indication of which service was unavailable.
- What happens when seed data is empty for a given entity? The agent must return a meaningful "no results found" response rather than an error.
- What happens when the user's query is ambiguous between two tools (e.g., "check order 123" could mean status or anomaly investigation)? The agent must pick the most conservative read tool and state what it did.

## Clarifications

### Session 2026-05-28

- Q: When the Microsoft Agent Framework selects a tool, does it invoke a locally registered C# handler in AgentHost (which then calls the domain service via HTTP), or does it dispatch HTTP directly? → A: The agent invokes a locally registered C# handler in AgentHost; that handler owns the outbound HTTP call to the domain service via Aspire service discovery.
- Q: Should `delayed` be a first-class Order status or a value computed at query time from delivery dates? → A: First-class status — seed data explicitly marks orders as `delayed`; computed anomaly detection is deferred to a future saga feature.
- Q: When a tool handler's HTTP call to a domain service fails, what should the handler return to the agent? → A: A structured error result with a human-readable reason string — never throw an exception — so the agent can surface partial results from other successful tool calls in the same turn.
- Q: Should seed data across Order, Inventory, and Product services use shared, consistent SKUs so cross-service scenarios are testable out of the box? → A: Yes — seed data MUST use consistent SKUs across all three services, with at least one order referencing a product that is out of stock in the Inventory service.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a `NexusOps.Contracts` shared library containing curated tool definitions that are the sole interface between AgentHost and domain services.
- **FR-002**: Tool definitions MUST express domain intent at a high level; each tool name MUST clearly convey the operation without exposing service internals (e.g., `investigate_order_anomaly`, `get_inventory_alerts`, `get_product_details`).
- **FR-003**: Each tool definition MUST be unambiguously mapped to exactly one path type: Direct (single-service HTTP read) or Saga (multi-service or mutating workflow). This feature covers Direct-path tools only.
- **FR-004**: The system MUST provide an Order service exposing read operations: list orders by status, retrieve order details by ID, and list orders with anomalies (delayed, missing, payment-failed).
- **FR-005**: The system MUST provide an Inventory service exposing read operations: list low-stock alerts, retrieve stock levels by product ID or SKU, and list out-of-stock items.
- **FR-006**: The system MUST provide a Product service exposing read operations: retrieve product details by SKU or ID, list products by category, and list all products.
- **FR-007**: All three domain services MUST be pre-populated with in-memory seed data that shares a consistent set of SKUs across services. Minimum data set: 10 orders in varied states (including at least 2 with status `delayed` explicitly set, and at least 1 order referencing a product that is out of stock in the Inventory service), 15 products across 3 categories, and matching inventory records (at least 2 products below their reorder threshold and at least 1 with zero stock).
- **FR-008**: The AgentHost MUST register all tool definitions from `NexusOps.Contracts` with the AI agent so the agent can select and invoke them during reasoning. Each tool definition MUST include a locally registered C# handler in AgentHost that owns the outbound HTTP call to the appropriate domain service via Aspire service discovery.
- **FR-008a**: Tool handler implementations MUST reside in AgentHost, not in `NexusOps.Contracts`. Contracts defines the tool shape (name, description, parameters); AgentHost owns the execution logic.
- **FR-009**: The AppHost MUST register all three domain services with Aspire service discovery, health checks, and HTTP health check probes so they appear in the Aspire dashboard.
- **FR-010**: Each domain service MUST emit OpenTelemetry traces and structured logs, wired via `AddServiceDefaults()`.
- **FR-011**: The system MUST expose a `/health` endpoint on each domain service returning HTTP 200 when healthy.
- **FR-012**: Tool invocations from the agent MUST use Aspire service discovery to resolve domain service addresses (no hardcoded URLs).
- **FR-013**: When a domain service call fails (timeout, 5xx, network error), the tool handler MUST return a structured error result containing a human-readable reason string. Handlers MUST NOT throw exceptions to the agent framework. This ensures the agent can include partial results from other successful tool calls in the same response turn.

### Key Entities

- **Order**: Represents a customer purchase. Key attributes: order ID, customer ID, product line items (SKU, quantity), total amount, status (pending/processing/shipped/delivered/delayed/cancelled — `delayed` is a first-class status set explicitly, not computed), expected delivery date, actual delivery date.
- **OrderLineItem**: A product within an order. Key attributes: SKU, product name, quantity, unit price.
- **Product**: A catalogue item available for purchase. Key attributes: product ID, SKU, name, description, category, unit price, weight.
- **InventoryRecord**: Stock level for a product at a warehouse. Key attributes: SKU, warehouse ID, quantity on hand, reorder threshold, last updated.
- **OrderAnomaly**: A derived view representing an order in an abnormal state. Attributes: order ID, anomaly type (delayed/missing/payment-failed — canonical set, "overdue" is not a separate type), severity, days overdue.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A natural language query about delayed orders ("Show me all delayed orders") returns a correct, structured response within 10 seconds end-to-end (user prompt → agent response).
- **SC-002**: All three domain services (Order, Inventory, Product) appear as healthy in the Aspire dashboard when the stack is started.
- **SC-003**: The agent correctly routes at least 4 out of 5 varied read queries to the appropriate tool in manual testing against seed data (80% routing accuracy baseline).
- **SC-004**: No domain service is called directly from AgentHost by URL string — all calls route through named Aspire service discovery references.
- **SC-005**: All three services emit distributed traces visible in the Aspire telemetry dashboard for each agent-triggered tool invocation.
- **SC-006**: Seed data covers sufficient variety that at least one result is returned for each of the following query types: delayed orders, low-stock products, product details by SKU, cross-service anomaly detection.

## Assumptions

- In-memory seed data is sufficient for this feature; no persistent database is required in this slice. Persistence will be introduced with the Workflow Orchestrator feature.
- Authentication and session management are explicitly out of scope for this feature; the `POST /api/chat` endpoint remains unauthenticated. These will be addressed in a subsequent dedicated feature.
- Mutation operations (refunds, cancellations, updates) are out of scope; this feature covers read-only Direct-path tools only.
- The existing AgentHost agent instructions will be updated to describe the new curated tools and routing rules; no changes to the Azure AI Foundry deployment are required.
- Aspire service discovery is available in the local development environment via `dotnet run --project NexusOps.AppHost`; no cloud deployment is required.
- Cross-service queries (User Story 4) rely on the LLM's reasoning ability to compose multiple tool calls; no explicit orchestration layer is needed for reads.
- The three domain services are in-process .NET projects added to the existing solution; no containerisation of individual services is required at this stage.
