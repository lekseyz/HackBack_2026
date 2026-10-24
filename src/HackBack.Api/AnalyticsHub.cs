using Microsoft.AspNetCore.SignalR;

namespace HackBack.Api;

public sealed class AnalyticsHub : Hub
{
}

public record ModelRecalculatedEvent(string Trigger, DateTime CalculatedAt, RecalcSummary Summary);

public record RecalcSummary(long RevenueAtRisk, int HighRiskCount);

public record CustomerRiskUpdatedEvent(List<object> Updated);
