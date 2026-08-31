# Order Service API Contract

**Aspire resource name**: `order-service`
**Base URL (via service discovery)**: `http://order-service`

All responses are JSON. All endpoints are read-only (GET).

---

## GET /orders/anomalies

Returns orders in an abnormal state.

An order is anomalous because of what it is, not because of how it was queried. The `status`
parameter **selects** among anomalies; it never determines how one is classified. The same order
reports the same `anomalyType` and `severity` under every filter that matches it and when no
filter is supplied.

**Query parameters**:
- `status` (optional, string): `delayed` | `missing` | `payment-failed`. Omit for all anomaly types.

**Response 200**:
```json
[
  {
    "orderId": "ORD-0001",
    "anomalyType": "delayed",
    "severity": "high",
    "daysOverdue": 14,
    "customerId": "CUST-001",
    "totalAmount": 249.99,
    "expectedDelivery": "2026-08-10",
    "lineItems": [
      { "sku": "SKU-ELEC-002", "productName": "Bluetooth Speaker Pro", "quantity": 1, "unitPrice": 249.99 }
    ]
  }
]
```

`lineItems` is present so that an anomaly can be correlated against `GET /inventory/alerts` on
SKU without a further request per order.

**Severity**:

| Anomaly type | Severity |
|---|---|
| `missing` | always `high` |
| `payment-failed` | always `high` |
| `delayed` | `high` when more than 7 days overdue, otherwise `medium` |

**`daysOverdue`**: days past `expectedDelivery`, and only meaningful for `delayed` — `null` for
the other types, which are not late but wrong. Never negative.

**Response 200 (empty)**: `[]`

**Response 400**: an unrecognised `status` value, with a body naming the accepted ones:

```
Unknown anomaly status 'bogus'. Valid values are: delayed, missing, payment-failed.
```

The tool handler surfaces this to the agent as a correctable argument error rather than as a
service outage, so the model can retry with a valid value.

> **Amended by feature 003 (FR-001, FR-002, FR-004, FR-006).** The implementation previously mapped
> both `missing` and `payment-failed` onto cancelled orders and then labelled the result from the
> query string, so one order reported two identities; it also omitted the four fields this contract
> had always published. The `daysOverdue` and `expectedDelivery` values above are illustrative —
> seed dates are now relative to the current date.

---

## GET /orders/{orderId}

Returns full details for a single order.

**Path parameters**:
- `orderId` (required, string): e.g., `ORD-0001`

**Response 200**:
```json
{
  "orderId": "ORD-0001",
  "customerId": "CUST-001",
  "status": "delayed",
  "totalAmount": 249.99,
  "expectedDelivery": "2026-05-20",
  "actualDelivery": null,
  "lineItems": [...]
}
```

**Response 404**: Order not found — tool handler returns `ToolResult.Fail("Order ORD-XXXX not found.")`

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
