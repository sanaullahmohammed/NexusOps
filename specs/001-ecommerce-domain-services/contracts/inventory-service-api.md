# Inventory Service API Contract

**Aspire resource name**: `inventory-service`
**Base URL (via service discovery)**: `http://inventory-service`

All responses are JSON. All endpoints are read-only (GET).

---

## GET /inventory/alerts

Returns products with stock below reorder threshold or at zero.

**Query parameters**:
- `outOfStockOnly` (optional, bool): When `true`, returns only zero-stock items.

**Response 200**:
```json
[
  {
    "sku": "SKU-ELEC-001",
    "productName": "Wireless Headphones Pro",
    "warehouseId": "WH-EAST",
    "quantityOnHand": 0,
    "reorderThreshold": 20,
    "isOutOfStock": true
  }
]
```

**Response 200 (empty)**: `[]`

---

## GET /inventory/{sku}

Returns stock level for a specific SKU.

**Path parameters**:
- `sku` (required, string): e.g., `SKU-ELEC-001`

**Response 200**:
```json
{
  "sku": "SKU-ELEC-001",
  "productName": "Wireless Headphones Pro",
  "warehouseId": "WH-EAST",
  "quantityOnHand": 0,
  "reorderThreshold": 20,
  "lastUpdated": "2026-05-28T10:00:00Z"
}
```

**Response 404**: SKU not found — tool handler returns `ToolResult.Fail("No inventory record found for SKU-XXXX.")`

---

## GET /health

**Response 200** (all environments):

```json
{ "status": "healthy" }
```

`Content-Type: application/json; charset=utf-8`. The `status` value is the health report status
lowercased — `healthy`, `degraded` or `unhealthy`.

> **Amended by feature 003 (FR-013).** Two corrections. The endpoint was registered only when
> `ASPNETCORE_ENVIRONMENT=Development`, while the Aspire AppHost probes this path and `WaitFor`s it
> unconditionally — so any non-Development start could never reach a healthy state. And the default
> health writer returned the bare string `Healthy` as `text/plain`, not the JSON body documented
> here. Exposing readiness publicly carries the security implications the Aspire template warns
> about; reachability is restricted at the ingress rather than by removing the endpoint.

## GET /alive

Liveness. Registered in **Development only** — nothing outside the Aspire dashboard consumes it.

**Response 200**: `{ "status": "healthy" }`
