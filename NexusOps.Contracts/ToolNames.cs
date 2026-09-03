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
        "Pass the order ID (e.g., ORD-0001). May return a degraded or failed result if a downstream source is unavailable. " +
        "If the order finding comes back NotFound, tell the operator the order does not exist — this is a completed, " +
        "trustworthy result (not a degraded or failed one), distinct from a source being unavailable.";

    public const string RequestOrderRefund = "request_order_refund";
    public const string RequestOrderRefundDescription =
        "Request a refund for a specific, existing order. This does NOT execute the refund — it creates a pending " +
        "request that requires explicit human approval before anything changes. Pass the order ID (e.g., ORD-0001), " +
        "and optionally an amount (defaults to the order's full total if omitted) and a reason. " +
        "After calling this tool, you MUST tell the operator the refund is pending approval and give them the " +
        "returned reference identifier — you MUST NOT say or imply that the refund has happened. " +
        "If the result reports the order was not found, tell the operator the order does not exist and that no " +
        "refund request was created.";

    public const string RequestOrderCancellation = "request_order_cancellation";
    public const string RequestOrderCancellationDescription =
        "Request the cancellation of a specific, existing order. This does NOT execute the cancellation — it " +
        "creates a pending request that requires explicit human approval before anything changes. Pass the order " +
        "ID (e.g., ORD-0001) and optionally a reason. On approval, both the order and the inventory it reserved " +
        "are affected. After calling this tool, you MUST tell the operator the cancellation is pending approval " +
        "and give them the returned reference identifier — you MUST NOT say or imply that the cancellation has " +
        "happened. If the result reports the order was not found, tell the operator the order does not exist and " +
        "that no cancellation request was created.";
}
