# Tool Definitions: NexusOps.Contracts

All tools are Direct-path (read-only HTTP). No saga-path tools in this feature.

Implemented as `Microsoft.Extensions.AI.AIFunction` instances via `AIFunctionFactory.Create(...)` in AgentHost.
Descriptors (name + description strings) are `ToolNames` constants in `NexusOps.Contracts`.

---

## investigate_order_anomaly

**Intent**: Retrieve orders in an abnormal state — delayed, missing, or payment-failed.
**Path**: Direct (Order service)
**Agent instruction routing**: "Diagnose delays, analyze failures" → this tool

| Parameter | Type   | Required | Description                                              |
|-----------|--------|----------|----------------------------------------------------------|
| status    | string | No       | Filter by anomaly type: `delayed`, `missing`, `payment-failed`. Omit for all anomalies. |

**Returns**: `ToolResult<OrderAnomaly[]>`

**Failure return**: `ToolResult<OrderAnomaly[]>.Fail("Order service is temporarily unavailable.")`

---

## get_order_details

**Intent**: Retrieve full details for a specific order by ID.
**Path**: Direct (Order service)
**Agent instruction routing**: "Check order status only" → this tool

| Parameter | Type   | Required | Description              |
|-----------|--------|----------|--------------------------|
| orderId   | string | Yes      | Order identifier (e.g., ORD-0001) |

**Returns**: `ToolResult<OrderSummary>`

**Failure return**: `ToolResult<OrderSummary>.Fail("Order service is temporarily unavailable.")`

---

## get_inventory_alerts

**Intent**: List products with stock below reorder threshold or at zero.
**Path**: Direct (Inventory service)
**Agent instruction routing**: "Check stock levels" → this tool (when asking about low/out-of-stock)

| Parameter   | Type | Required | Description                              |
|-------------|------|----------|------------------------------------------|
| outOfStockOnly | bool | No    | When true, returns only zero-stock items |

**Returns**: `ToolResult<InventoryAlert[]>`

**Failure return**: `ToolResult<InventoryAlert[]>.Fail("Inventory service is temporarily unavailable.")`

---

## get_inventory_level

**Intent**: Retrieve current stock level for a specific product SKU.
**Path**: Direct (Inventory service)
**Agent instruction routing**: "Check stock levels" → this tool (when asking about a specific SKU)

| Parameter | Type   | Required | Description              |
|-----------|--------|----------|--------------------------|
| sku       | string | Yes      | Product SKU (e.g., SKU-ELEC-001) |

**Returns**: `ToolResult<InventoryLevel>`

**Failure return**: `ToolResult<InventoryLevel>.Fail("Inventory service is temporarily unavailable.")`

---

## get_product_details

**Intent**: Retrieve full details for a specific product by SKU.
**Path**: Direct (Product service)
**Agent instruction routing**: "Retrieve product details" → this tool

| Parameter | Type   | Required | Description              |
|-----------|--------|----------|--------------------------|
| sku       | string | Yes      | Product SKU (e.g., SKU-ELEC-001) |

**Returns**: `ToolResult<ProductDetail>`

**Failure return**: `ToolResult<ProductDetail>.Fail("Product service is temporarily unavailable.")`

---

## list_products_by_category

**Intent**: List products filtered by category, or all products when no category is specified.
**Path**: Direct (Product service)
**Agent instruction routing**: "Retrieve product details" → this tool (when listing by category or listing all products)

| Parameter | Type   | Required | Description                                                                 |
|-----------|--------|----------|-----------------------------------------------------------------------------|
| category  | string | No       | One of: `Electronics`, `Apparel`, `Home & Garden`. Omit to return all products. |

**Returns**: `ToolResult<ProductSummary[]>`

**Failure return**: `ToolResult<ProductSummary[]>.Fail("Product service is temporarily unavailable.")`
