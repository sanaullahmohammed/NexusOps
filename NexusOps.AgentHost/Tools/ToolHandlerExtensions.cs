using Microsoft.Extensions.AI;
using NexusOps.Contracts;

namespace NexusOps.AgentHost.Tools;

public static class ToolHandlerExtensions
{
    public static IServiceCollection AddToolHandlers(this IServiceCollection services, IConfiguration config)
    {
        services.AddSingleton<OrderTools>();
        services.AddSingleton<InventoryTools>();
        services.AddSingleton<ProductTools>();

        services.AddSingleton<IList<AITool>>(sp =>
        {
            var orderTools = sp.GetRequiredService<OrderTools>();
            var inventoryTools = sp.GetRequiredService<InventoryTools>();
            var productTools = sp.GetRequiredService<ProductTools>();

            return
            [
                AIFunctionFactory.Create(
                    orderTools.InvestigateOrderAnomalyAsync,
                    ToolNames.InvestigateOrderAnomaly,
                    ToolNames.InvestigateOrderAnomalyDescription),

                AIFunctionFactory.Create(
                    orderTools.GetOrderDetailsAsync,
                    ToolNames.GetOrderDetails,
                    ToolNames.GetOrderDetailsDescription),

                AIFunctionFactory.Create(
                    orderTools.InvestigateOrderRootCauseAsync,
                    ToolNames.InvestigateOrderRootCause,
                    ToolNames.InvestigateOrderRootCauseDescription),

                AIFunctionFactory.Create(
                    orderTools.RequestOrderRefundAsync,
                    ToolNames.RequestOrderRefund,
                    ToolNames.RequestOrderRefundDescription),

                AIFunctionFactory.Create(
                    orderTools.RequestOrderCancellationAsync,
                    ToolNames.RequestOrderCancellation,
                    ToolNames.RequestOrderCancellationDescription),

                AIFunctionFactory.Create(
                    inventoryTools.GetInventoryAlertsAsync,
                    ToolNames.GetInventoryAlerts,
                    ToolNames.GetInventoryAlertsDescription),

                AIFunctionFactory.Create(
                    inventoryTools.GetInventoryLevelAsync,
                    ToolNames.GetInventoryLevel,
                    ToolNames.GetInventoryLevelDescription),

                AIFunctionFactory.Create(
                    productTools.GetProductDetailsAsync,
                    ToolNames.GetProductDetails,
                    ToolNames.GetProductDetailsDescription),

                AIFunctionFactory.Create(
                    productTools.ListProductsByCategoryAsync,
                    ToolNames.ListProductsByCategory,
                    ToolNames.ListProductsByCategoryDescription),
            ];
        });

        return services;
    }
}
