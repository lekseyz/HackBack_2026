using System.Text.Json;
using HackBack.Domain;

namespace HackBack.Api;

public static class CustomerItems
{
    private const string NoDataReason = "Недостаточно данных для оценки (нет активных услуг и транзакций)";

    public static object ToItem(Account account)
    {
        var codes = string.IsNullOrEmpty(account.PrimaryReasons)
            ? []
            : JsonSerializer.Deserialize<string[]>(account.PrimaryReasons) ?? [];
        var primaryReasons = account.RiskScore is null
            ? new[] { NoDataReason }
            : codes.Select(RiskScorer.TitleOf).OfType<string>().ToArray();
        return new
        {
            account.Id,
            account.Email,
            account.Balance,
            account.WithdrawBalance,
            account.BonusBalance,
            account.BonusState,
            account.TotalReplenished,
            riskScore = account.RiskScore,
            status = account.RiskScore is null ? "insufficient_data" : "evaluated",
            revenueAtRisk = account.RevenueAtRisk,
            primaryReasons,
        };
    }
}
