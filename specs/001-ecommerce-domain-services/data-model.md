# Data Model: E-Commerce Domain Services

## Shared Constants (NexusOps.Contracts)

`SeedDataConstants` — static string constants for all SKUs, order IDs, and product IDs referenced across services.

```
SKUs:        SKU-ELEC-001, SKU-ELEC-002, SKU-ELEC-003, SKU-ELEC-004, SKU-ELEC-005
             SKU-APRL-001, SKU-APRL-002, SKU-APRL-003, SKU-APRL-004, SKU-APRL-005
             SKU-HOME-001, SKU-HOME-002, SKU-HOME-003, SKU-HOME-004, SKU-HOME-005
Order IDs:   ORD-0001 through ORD-0010
Product IDs: PRD-0001 through PRD-0015
```

## Contracts DTOs (NexusOps.Contracts)

Response types returned by tool handlers and deserialized from domain service HTTP responses.

### ToolResult\<T\>

Generic wrapper for all tool handler responses.

| Field   | Type    | Description                                     |
|---------|---------|-------------------------------------------------|
| Success | bool    | True if the underlying service call succeeded   |
| Data    | T?      | Populated on success, null on failure           |
| Error   | string? | Human-readable failure reason; null on success  |

Factory methods: `ToolResult<T>.Ok(T data)`, `ToolResult<T>.Fail(string reason)`

---

### OrderSummary

Returned by `investigate_order_anomaly` and `get_order_details` tools.

| Field              | Type           | Description                                          |
|--------------------|----------------|------------------------------------------------------|
| OrderId            | string         | Unique order identifier (e.g., ORD-0001)             |
| CustomerId         | string         | Customer identifier                                  |
| Status             | string         | One of: pending, processing, shipped, delivered, delayed, cancelled |
| TotalAmount        | decimal        | Order total in USD                                   |
| ExpectedDelivery   | DateOnly       | Originally promised delivery date                   |
| ActualDelivery     | DateOnly?      | Actual delivery date; null if not yet delivered      |
| LineItems          | OrderLineItem[]| Products in the order                               |

---

### OrderLineItem

| Field       | Type    | Description                      |
|-------------|---------|----------------------------------|
| Sku         | string  | Product SKU                      |
| ProductName | string  | Display name of the product      |
| Quantity    | int     | Number of units ordered          |
| UnitPrice   | decimal | Price per unit at time of order  |

---

### OrderAnomaly

Returned in the `Anomalies` collection on `investigate_order_anomaly` responses.

| Field            | Type             | Description                                                                       |
|------------------|------------------|-----------------------------------------------------------------------------------|
| OrderId          | string           | Order with the anomaly                                                            |
| AnomalyType      | string           | One of: delayed, missing, payment-failed. Derived from the order's `AnomalyReason`, never from the query that selected it |
| Severity         | string           | One of: medium, high. `missing` and `payment-failed` are always high; `delayed` is high past 7 days overdue, medium at or below |
| DaysOverdue      | int?             | Days past expected delivery; null for anomalies that are not date-related. Never negative |
| CustomerId       | string           | Who placed the order                                                              |
| TotalAmount      | decimal          | Order value, so impact can be weighed without a second call                       |
| ExpectedDelivery | DateOnly         | The delivery date that was promised                                               |
| LineItems        | OrderLineItem\[] | Carries the SKUs that let an anomaly be correlated against inventory alerts without a per-order round trip |

> **Amended by feature 003 (FR-001, FR-002, FR-004).** The original four-field shape omitted the fields the order service contract already published, and — critically — had no SKU, so the cross-service correlation the agent is instructed to perform was impossible from the response alone. `Severity` no longer includes `low`; nothing emitted it.

---

### InventoryAlert

Returned by `get_inventory_alerts` tool.

| Field            | Type   | Description                                      |
|------------------|--------|--------------------------------------------------|
| Sku              | string | Product SKU                                      |
| ProductName      | string | Display name                                     |
| WarehouseId      | string | Warehouse holding this stock                     |
| QuantityOnHand   | int    | Current stock level                              |
| ReorderThreshold | int    | Quantity at which replenishment should trigger   |
| IsOutOfStock     | bool   | True when QuantityOnHand == 0                    |

---

### InventoryLevel

Returned by `get_inventory_level` tool (single SKU query).

| Field            | Type     | Description                             |
|------------------|----------|-----------------------------------------|
| Sku              | string   | Product SKU                             |
| ProductName      | string   | Display name                            |
| WarehouseId      | string   | Warehouse identifier                    |
| QuantityOnHand   | int      | Current stock                           |
| ReorderThreshold | int      | Replenishment trigger                   |
| LastUpdated      | DateTime | Timestamp of last stock movement        |

---

### ProductDetail

Returned by `get_product_details` tool.

| Field       | Type    | Description                              |
|-------------|---------|------------------------------------------|
| ProductId   | string  | Unique product identifier (PRD-XXXX)     |
| Sku         | string  | Stock Keeping Unit (SKU-XXXX-XXX)        |
| Name        | string  | Display name                             |
| Description | string  | Full product description                 |
| Category    | string  | One of: Electronics, Apparel, Home & Garden |
| UnitPrice   | decimal | Current selling price in USD             |
| WeightKg    | decimal | Shipping weight in kilograms             |

---

### ProductSummary

Returned in list operations (browse by category, list all).

| Field     | Type    | Description           |
|-----------|---------|-----------------------|
| ProductId | string  | Unique identifier     |
| Sku       | string  | SKU                   |
| Name      | string  | Display name          |
| Category  | string  | Category              |
| UnitPrice | decimal | Current price         |

---

## Domain Service Internal Models

Each service has internal models that may diverge from Contracts DTOs. Mapping occurs in the service's endpoint handlers.

### Order Service Internal: `Order`

| Field            | Type             | Notes                                  |
|------------------|------------------|----------------------------------------|
| OrderId          | string           | PK                                     |
| CustomerId       | string           |                                        |
| Status           | OrderStatus enum | pending/processing/shipped/delivered/delayed/cancelled — lifecycle position; first-class, not computed |
| TotalAmount      | decimal          |                                        |
| ExpectedDelivery | DateOnly         | Seeded relative to the current date, resolved via `TimeProvider` |
| ActualDelivery   | DateOnly?        | Seeded relative to the current date    |
| LineItems        | List\<LineItem\> |                                        |
| CreatedAt        | DateTime         | Seeded relative to the current date    |
| AnomalyReason    | AnomalyReason?   | Delayed/Missing/PaymentFailed, or null when the order is not anomalous. Orthogonal to `Status`: status is where the order is, this is what is wrong with it |

> **Amended by feature 003 (FR-001, FR-003, FR-005).** `AnomalyReason` was added because the anomaly endpoint previously derived its classification from the query string — the single cancelled order reported as `missing` under one filter and `payment-failed` under the next. Seed dates moved from absolute literals to offsets from the current date; the literals were fixed in May–June 2026, so `daysOverdue` grew by one every day and read as 106 by August.

### Inventory Service Internal: `InventoryRecord`

| Field            | Type     | Notes        |
|------------------|----------|--------------|
| Sku              | string   | PK           |
| WarehouseId      | string   |              |
| QuantityOnHand   | int      | ≥ 0          |
| ReorderThreshold | int      |              |
| LastUpdated      | DateTime |              |

### Product Service Internal: `Product`

| Field       | Type    | Notes |
|-------------|---------|-------|
| ProductId   | string  | PK    |
| Sku         | string  | Unique, matches SeedDataConstants |
| Name        | string  |       |
| Description | string  |       |
| Category    | string  |       |
| UnitPrice   | decimal |       |
| WeightKg    | decimal |       |

---

## Seed Data Summary

All three services seed from the same SKU constants. Cross-service integrity requirements (FR-007, clarification Q4):

| Constraint | Details |
|---|---|
| ≥ 2 delayed orders | ORD-0001 (14 days overdue → high), ORD-0002 (3 days overdue → medium) — chosen to exercise both sides of the severity threshold |
| ≥ 1 order referencing out-of-stock product | ORD-0003 contains SKU-ELEC-001; SKU-ELEC-001 has QuantityOnHand = 0 |
| ≥ 2 products below reorder threshold | SKU-ELEC-001 (0 stock), SKU-APRL-003 (5 stock, threshold 10) |
| ≥ 15 products across 3 categories | 5 Electronics, 5 Apparel, 5 Home & Garden |
| ≥ 10 orders in varied states | 11 orders: 2 delayed, 1 cancelled, 3 shipped, 2 processing, 2 delivered, 1 pending |
| Every anomaly reason represented | ORD-0001 and ORD-0002 `Delayed`; ORD-0009 `PaymentFailed`; ORD-0011 `Missing` — so each documented filter returns a distinct, non-empty result |

> **Amended by feature 003 (FR-003).** ORD-0011 was added because the seed set held exactly one non-delayed anomalous order, leaving two of the three documented filter values with no data of their own to return.
