# Quickstart: E-Commerce Domain Services

## Prerequisites

- .NET 10 SDK
- Docker Desktop (for Aspire infrastructure containers)
- Azure AI Foundry credentials in `NexusOps.AgentHost/appsettings.Development.json`

## Start the stack

```bash
dotnet run --project NexusOps.AppHost
```

Aspire starts all five services (AgentHost, Server, OrderService, InventoryService, ProductService) and opens the developer dashboard.

## Verify services are healthy

Open the Aspire dashboard (URL printed on startup). All five resources should show green health status.

Or via curl:
```bash
curl http://localhost:<order-port>/health
curl http://localhost:<inventory-port>/health
curl http://localhost:<product-port>/health
```

Ports are assigned dynamically by Aspire. Check the dashboard Resources tab for the bound port for each service.

## Run a Direct-path query

```bash
curl -X POST http://localhost:<agent-port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Show me all delayed orders"}'
```

Expected: The agent selects `investigate_order_anomaly`, calls the Order service, and returns a formatted list of ORD-0001 and ORD-0002 with their delay details.

## Test inventory alerts

```bash
curl -X POST http://localhost:<agent-port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Which products are out of stock?"}'
```

Expected: The agent selects `get_inventory_alerts`, calls the Inventory service, and returns SKU-ELEC-001 (Wireless Headphones Pro) with zero stock.

## Test cross-service query

```bash
curl -X POST http://localhost:<agent-port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Are there any orders for products that are currently out of stock?"}'
```

Expected: The agent calls both `investigate_order_anomaly` and `get_inventory_alerts`, cross-references ORD-0003 (which contains SKU-ELEC-001), and returns the at-risk order.

## Test product category listing

```bash
curl -X POST http://localhost:<agent-port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "List all Apparel products"}'
```

Expected: The agent selects `list_products_by_category` with `category=Apparel`, calls the Product service, and returns the 5 Apparel products from seed data with names and prices.

---

*The five scenarios above map directly to SC-003 (80% routing accuracy across 5 varied queries): delayed orders → `investigate_order_anomaly`; inventory alerts → `get_inventory_alerts`; cross-service → both tools; product details → `get_product_details`; category list → `list_products_by_category`.*

## Query domain services directly (bypass agent)

```bash
# Delayed orders
curl http://localhost:<order-port>/orders/anomalies?status=delayed

# Low stock alerts
curl http://localhost:<inventory-port>/inventory/alerts

# Product by SKU
curl http://localhost:<product-port>/products/SKU-ELEC-001
```

## View distributed traces

Open the Aspire dashboard → Traces tab. After sending a chat request, you should see a trace spanning AgentHost → OrderService (or InventoryService/ProductService) for each tool invocation.
