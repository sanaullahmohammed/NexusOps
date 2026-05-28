# Order Service API Contract

**Aspire resource name**: `order-service`
**Base URL (via service discovery)**: `http://order-service`

All responses are JSON. All endpoints are read-only (GET).

---

## GET /orders/anomalies

Returns orders in an abnormal state.

**Query parameters**:
- `status` (optional, string): `delayed` | `missing` | `payment-failed`. Omit for all anomaly types.

**Response 200**:
```json
[
  {
    "orderId": "ORD-0001",
    "anomalyType": "delayed",
    "severity": "high",
    "daysOverdue": 5,
    "customerId": "CUST-001",
    "totalAmount": 249.99,
    "expectedDelivery": "2026-05-20",
    "lineItems": [
      { "sku": "SKU-ELEC-001", "productName": "...", "quantity": 1, "unitPrice": 249.99 }
    ]
  }
]
```

**Response 200 (empty)**: `[]`

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

**Response 200**: `{ "status": "healthy" }`
