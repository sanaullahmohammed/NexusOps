namespace NexusOps.Contracts;

public static class ToolNames
{
    public const string InvestigateOrderAnomaly = "investigate_order_anomaly";
    public const string InvestigateOrderAnomalyDescription =
        "Retrieve orders in an abnormal state — delayed, missing, or payment-failed. " +
        "Use when diagnosing order delays, failures, or investigating anomalies across the order stream. " +
        "Pass a status filter (delayed, missing, payment-failed) to narrow results, or omit for all anomalies.";

    public const string GetOrderDetails = "get_order_details";
    public const string GetOrderDetailsDescription =
        "Retrieve full details for a specific order by its order ID (e.g., ORD-0001). " +
        "Use when the user is checking the status of a single known order.";

    public const string GetInventoryAlerts = "get_inventory_alerts";
    public const string GetInventoryAlertsDescription =
        "List products with stock below reorder threshold or at zero. " +
        "Use when checking which products are low on stock or out of stock. " +
        "Set outOfStockOnly=true to return only zero-stock items.";

    public const string GetInventoryLevel = "get_inventory_level";
    public const string GetInventoryLevelDescription =
        "Retrieve the current stock level for a specific product SKU (e.g., SKU-ELEC-001). " +
        "Use when the user asks about inventory for a specific product by SKU.";

    public const string GetProductDetails = "get_product_details";
    public const string GetProductDetailsDescription =
        "Retrieve full details for a specific product by SKU, including name, description, price, category, and weight. " +
        "Use when the user asks about specifications or details of a known product SKU.";

    public const string ListProductsByCategory = "list_products_by_category";
    public const string ListProductsByCategoryDescription =
        "List products filtered by category (Electronics, Apparel, or Home & Garden), or all products when no category is specified. " +
        "Use when the user asks to browse or list products, either all products or within a specific category.";

    public const string InvestigateOrderRootCause = "investigate_order_root_cause";
    public const string InvestigateOrderRootCauseDescription =
        "Investigate why a specific order is broken by cross-referencing the order, its items' stock levels, and their " +
        "product details. Use when the operator asks *why* one named order is delayed, missing, failing, or otherwise " +
        "problematic — not for listing anomalous orders in general, and not for a plain status check with no 'why'. " +
        "Pass the order ID (e.g., ORD-0001). May return a degraded or failed result if a downstream source is unavailable.";
}
