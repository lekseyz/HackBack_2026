using HackBack.Domain;

namespace HackBack.Api;

public static class CustomerItems
{
    private const string NoDataReason = "Недостаточно данных для оценки (скоринг будет подключён позже)";

    public static object ToItem(Account account) => new
    {
        account.Id,
        account.Email,
        account.Balance,
        account.WithdrawBalance,
        account.BonusBalance,
        account.BonusState,
        account.TotalReplenished,
        riskScore = (double?)null,
        riskLevel = "no_data",
        status = "insufficient_data",
        revenueAtRisk = 0L,
        primaryReasons = new[] { NoDataReason },
    };
}
