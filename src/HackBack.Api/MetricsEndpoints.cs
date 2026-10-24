using System.Globalization;
using HackBack.Infrastructure;

namespace HackBack.Api;

public static class MetricsEndpoints
{
    public static IEndpointRouteBuilder MapMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /metrics - выгрузка метрик Prometheus (без авторизации, по спеке).
        app.MapGet("/metrics", async (AccountRepository accounts) =>
        {
            var total = await accounts.CountAsync();
            // замокал кринж нейронкой, пока хз как этот формат работает
            var payload = string.Create(CultureInfo.InvariantCulture, $$"""
                # HELP churn_customers_total Общее количество клиентов в системе
                # TYPE churn_customers_total gauge
                churn_customers_total {{total}}

                # HELP churn_customers_by_risk_level Количество клиентов по уровням риска
                # TYPE churn_customers_by_risk_level gauge
                churn_customers_by_risk_level{level="high"} 0
                churn_customers_by_risk_level{level="medium"} 0
                churn_customers_by_risk_level{level="low"} 0
                churn_customers_by_risk_level{level="insufficient_data"} {{total}}

                # HELP churn_revenue_at_risk_eurocents Потенциальная сумма потерь бизнеса в еврокопейках
                # TYPE churn_revenue_at_risk_eurocents gauge
                churn_revenue_at_risk_eurocents 0

                # HELP churn_expected_loss_eurocents Математическое ожидание потерь в еврокопейках
                # TYPE churn_expected_loss_eurocents gauge
                churn_expected_loss_eurocents 0
                """);

            return Results.Text(payload, "text/plain; version=0.0.4");
        })
        .WithSummary("Метрики Prometheus")
        .WithDescription("Выгрузка агрегированных churn-метрик в формате text/plain; version=0.0.4. Доступно без авторизации.")
        .Produces(StatusCodes.Status200OK);

        return app;
    }
}
