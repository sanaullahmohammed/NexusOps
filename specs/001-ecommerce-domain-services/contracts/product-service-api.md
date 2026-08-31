# Product Service API Contract

**Aspire resource name**: `product-service`
**Base URL (via service discovery)**: `http://product-service`

All responses are JSON. All endpoints are read-only (GET).

---

## GET /products/{sku}

Returns full details for a product by SKU.

**Path parameters**:
- `sku` (required, string): e.g., `SKU-ELEC-001`

**Response 200**:
```json
{
  "productId": "PRD-0001",
  "sku": "SKU-ELEC-001",
  "name": "Wireless Headphones Pro",
  "description": "Over-ear noise-cancelling wireless headphones with 30hr battery.",
  "category": "Electronics",
  "unitPrice": 249.99,
  "weightKg": 0.35
}
```

**Response 404**: SKU not found — tool handler returns `ToolResult.Fail("Product SKU-XXXX not found.")`

---

## GET /products

Returns summary list of all products. Supports optional category filter.

**Query parameters**:
- `category` (optional, string): `Electronics` | `Apparel` | `Home & Garden`

**Response 200**:
```json
[
  {
    "productId": "PRD-0001",
    "sku": "SKU-ELEC-001",
    "name": "Wireless Headphones Pro",
    "category": "Electronics",
    "unitPrice": 249.99
  }
]
```

**Response 200 (empty)**: `[]`

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
