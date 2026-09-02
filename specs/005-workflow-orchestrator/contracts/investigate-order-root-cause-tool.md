# Contract: `investigate_order_root_cause` Tool (Saga Path)

**Version**: 1.0 (new — additive alongside the existing Direct-path tools)
**Owner**: `NexusOps.Contracts` (tool name + message contracts), `NexusOps.AgentHost` (handler), `NexusOps.WorkflowOrchestrator` (saga)
**Invocation**: Agent-selected tool call, routed to the Saga path

---

## Tool Definition

| Field | Value |
|---|---|
| Name | `investigate_order_root_cause` |
| Description | "Investigate why a specific order is broken by cross-referencing the order, its items' stock levels, and their product details. Use when the operator asks *why* one named order is delayed, missing, failing, or otherwise problematic — not for listing anomalous orders in general, and not for a plain status check with no 'why'." |
| Path | Saga (fans out across Order, Inventory, and Product services; tolerates partial failure) |

### Input

```json
{ "orderId": "ORD-1002" }
```

| Field | Required | Type | Constraints |
|---|---|---|---|
| `orderId` | Yes | `string` | Non-empty; matches the format used by `get_order_details` and `investigate_order_anomaly` |

### Output — `ToolResult<RootCauseInvestigationResult>`

**Order found, fully investigated** (`Completeness: "Complete"`):

```json
{
  "success": true,
  "data": {
    "orderId": "ORD-1002",
    "orderFinding": "Succeeded",
    "order": { "orderId": "ORD-1002", "customerId": "CUST-042", "status": "Delayed", "...": "..." },
    "inventoryFinding": "Succeeded",
    "inventoryLevels": [ { "sku": "SKU-ELEC-001", "quantityOnHand": 0, "reorderThreshold": 10, "...": "..." } ],
    "productFinding": "Succeeded",
    "productDetails": [ { "sku": "SKU-ELEC-001", "name": "...", "...": "..." } ],
    "completeness": "Complete",
    "degradedSources": []
  }
}
```

**One source degraded** (`Completeness: "Degraded"`):

```json
{
  "success": true,
  "data": {
    "orderId": "ORD-1002",
    "orderFinding": "Succeeded",
    "order": { "...": "..." },
    "inventoryFinding": "Unavailable",
    "inventoryLevels": [],
    "productFinding": "Succeeded",
    "productDetails": [ { "...": "..." } ],
    "completeness": "Degraded",
    "degradedSources": ["Inventory"]
  }
}
```

**Order not found** (a *completed* investigation per Edge Cases, not a failure):

```json
{
  "success": true,
  "data": {
    "orderId": "ORD-9999",
    "orderFinding": "NotFound",
    "order": null,
    "inventoryFinding": "NotFound",
    "inventoryLevels": [],
    "productFinding": "NotFound",
    "productDetails": [],
    "completeness": "Complete",
    "degradedSources": []
  }
}
```

*(When the order is confirmed not to exist, there are no line-item SKUs to check, so Inventory/Product findings are trivially `NotFound`/empty — this is "nothing to check" per the spec's Edge Cases, not a degradation.)*

**Investigation could not be completed** (all sources unavailable, or the order source itself unavailable — `Completeness: "Failed"`):

```json
{
  "success": false,
  "error": "The investigation for order ORD-1002 could not be completed: the order service did not respond in time."
}
```

**Request timeout** (AgentHost's request client gave up waiting on the saga entirely):

```json
{
  "success": false,
  "error": "The investigation for order ORD-1002 timed out before a result was available."
}
```

---

## Agent Routing Rules (delta to routing instructions)

| Operator phrasing | Tool selected |
|---|---|
| "Show me all delayed/missing/failed-payment orders" | `investigate_order_anomaly` (unchanged) |
| "What's the status of ORD-1002?" | `get_order_details` (unchanged) |
| "Why is ORD-1002 stuck/delayed/failing?" / "Investigate the root cause for ORD-1002" | `investigate_order_root_cause` (new) |

The distinguishing signal is **(a) a single, specific order identifier** combined with **(b) an explanatory "why" framing**, not a status- or list-framing. See `spec.md` User Story 3 and FR-007.

---

## Backward Compatibility

Fully additive. `investigate_order_anomaly`'s name, input, and response shape are byte-for-byte unchanged (FR-002); no existing tool registration, contract, or routing rule is modified by this feature. Existing anomaly-listing test prompts must continue to select `investigate_order_anomaly` with no change in behavior (SC-004).
