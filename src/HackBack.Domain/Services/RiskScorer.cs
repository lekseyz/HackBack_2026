namespace HackBack.Domain;

public sealed record RiskInput(Account Account, IReadOnlyList<Service> Services,
    IReadOnlyList<Transaction> Transactions, ModelSettings Settings, DateTime Now);

public sealed record RiskFactor(string Code, string Title, string Description,
    double Points, double MaxPoints, long? ServiceId);

public sealed record RiskResult(int? Score, IReadOnlyList<RiskFactor> Factors);

public sealed record RiskAssessment(int? Score, long MonthlyRevenue, long RevenueAtRisk,
    long BalanceDeficit, IReadOnlyList<string> ReasonCodes);

public static class RiskScorer
{
    // короче, выносить это в эндпоинт - бред, потом в конфиг можно
    public const int ExpiredToleranceDays = 30;
    public const int FailedProlongWindowDays = 30;
    public const int FailedProlongSaturation = 3;

    private const double LoyaltyTenureWeight = 25;
    private const double LoyaltyActiveWeight = 20;
    private const double LoyaltyMonetaryWeight = 25;
    private const double LoyaltyRecencyWeight = 20;
    private const double LoyaltyAutoprolongWeight = 10;

    public static readonly (string Code, string Reason, string Title)[] FactorReasons =
    [
        ("PROLONG_WAIT_INSUFFICIENT_FUNDS", "prolong_wait_no_funds", "Ожидание продления без средств"),
        ("AUTOPROLONG_DISABLED_EXPIRING", "autoprolong_disabled_expiring", "Автопродление отключено, услуга истекает"),
        ("AUTOPROLONG_DEFICIT", "autoprolong_deficit", "Средств не хватит на автопродление"),
        ("SUSPENDED_SERVICE", "suspended_service", "Приостановленные услуги"),
        ("FAILED_RECENT_TRANSACTION", "recent_failed_prolongation", "Неудачные продления"),
    ];

    public static RiskResult Score(RiskInput input)
    {
        if (input.Services.Count == 0)
            return new RiskResult(null, []);

        var now = input.Now;
        var settings = input.Settings;
        var effectiveBalance = input.Account.Balance +
            (input.Account.BonusState == "unlocked" ? input.Account.BonusBalance : 0);
        var windowStart = now.AddDays(-ExpiredToleranceDays);
        var windowEnd = now.AddDays(settings.ImminentExpiryDays);

        var factors = new List<RiskFactor>();

        var prolongWait = input.Services.Where(s => s.Status == "prolong_wait").ToList();
        var prolongWaitTotal = prolongWait.Sum(s => s.Price);
        var prolongWaitDeficit = Math.Max(0L, prolongWaitTotal - effectiveBalance);
        var f1Severity = prolongWaitTotal == 0 ? 0 : Math.Min(1, prolongWaitDeficit / (double)prolongWaitTotal);
        AddFactor(factors, "PROLONG_WAIT_INSUFFICIENT_FUNDS", "Ожидание продления без средств",
            $"В ожидании продления {prolongWait.Count} услуг на {prolongWaitTotal} евроцентов, дефицит баланса — {prolongWaitDeficit} евроцентов",
            settings.ProlongWaitWithoutFundsWeight, f1Severity, null);

        Service? worstExpiring = null;
        var worstExpiringSeverity = 0.0;
        var disabledExpiringCount = 0;
        var autoprolongExpiring = new List<Service>();
        foreach (var service in input.Services)
        {
            if (service.Status != "active" || service.ExpiresAt < windowStart || service.ExpiresAt > windowEnd)
                continue;

            if (service.AutoProlong)
            {
                autoprolongExpiring.Add(service);
                continue;
            }

            disabledExpiringCount++;
            var daysLeft = (service.ExpiresAt - now).TotalDays;
            var severity = daysLeft <= 0 ? 1 : Math.Max(0, 1 - daysLeft / settings.ImminentExpiryDays);
            if (severity > worstExpiringSeverity)
            {
                worstExpiringSeverity = severity;
                worstExpiring = service;
            }
        }

        var f2Description = worstExpiring is null
            ? string.Empty
            : $"Без автопродления истекает {disabledExpiringCount} услуг, ближайняя — услуга {worstExpiring.Id}";
        AddFactor(factors, "AUTOPROLONG_DISABLED_EXPIRING", "Автопродление отключено, услуга истекает",
            f2Description, settings.AutoprolongDisabledExpiringWeight, worstExpiringSeverity, worstExpiring?.Id);

        var autoprolongTotal = autoprolongExpiring.Sum(s => s.Price);
        var autoprolongDeficit = Math.Max(0L, autoprolongTotal - effectiveBalance);
        var f3Severity = autoprolongTotal == 0 ? 0 : Math.Min(1, autoprolongDeficit / (double)autoprolongTotal);
        AddFactor(factors, "AUTOPROLONG_DEFICIT", "Средств не хватит на автопродление",
            $"Для автопродления {autoprolongExpiring.Count} истекающих услуг требуется {autoprolongTotal} евроцентов, дефицит — {autoprolongDeficit} евроцентов",
            settings.AutoprolongEnabledDeficitWeight, f3Severity, null);

        var denominator = input.Services.Count(s => s.Status != "deleted" && s.Status != "blocked");
        var suspendedCount = input.Services.Count(s => s.Status == "suspended");
        var f4Severity = denominator == 0 ? 0 : suspendedCount / (double)denominator;
        AddFactor(factors, "SUSPENDED_SERVICE", "Приостановленные услуги",
            $"Приостановлено {suspendedCount} из {denominator} учитываемых услуг",
            settings.SuspendedServiceWeight, f4Severity, null);

        var failedCutoff = now.AddDays(-FailedProlongWindowDays);
        var failedCount = input.Transactions.Count(t =>
            t.Type == "prolong" && t.Status == "not_performed" && t.CreatedAt >= failedCutoff);
        var f5Severity = Math.Min(1, failedCount / (double)FailedProlongSaturation);
        AddFactor(factors, "FAILED_RECENT_TRANSACTION", "Неудачные продления",
            $"Неуспешных продлений за последние {FailedProlongWindowDays} дней: {failedCount}",
            settings.RecentFailedProlongationWeight, f5Severity, null);

        // нормализация норм чтоб без сигмоиды
        var maxSettingsSum = settings.ProlongWaitWithoutFundsWeight +
                             settings.AutoprolongDisabledExpiringWeight +
                             settings.AutoprolongEnabledDeficitWeight +
                             settings.SuspendedServiceWeight +
                             settings.RecentFailedProlongationWeight;

        var sum = factors.Sum(f => f.Points);
        var normalized = maxSettingsSum <= 0 ? 0 : sum / maxSettingsSum * 100;

        var score = (int)Math.Clamp(Math.Round(sum), 0, 100);
        return new RiskResult(score, factors.OrderByDescending(f => f.Points).ToList());
    }

    public static int? LoyaltyScore(RiskInput input)
    {
        if (input.Services.Count == 0)
            return null;

        var tenureDays = (input.Now - input.Services.Min(s => s.CreatedAt)).TotalDays;
        var tenureSeverity = Math.Min(1, tenureDays / 365.0);

        var activeCount = input.Services.Count(s => s.Status == "active");
        var activeSeverity = Math.Min(1, activeCount / 5.0);

        var monetarySeverity = Math.Min(1, input.Account.TotalReplenished / 200_000.0);

        var recencySeverity = 0.0;
        var lastPerformed = input.Transactions
            .Where(t => t.PerformedAt.HasValue)
            .Select(t => t.PerformedAt!.Value)
            .ToList();
        if (lastPerformed.Count > 0)
        {
            var daysSince = (input.Now - lastPerformed.Max()).TotalDays;
            recencySeverity = Math.Min(1, Math.Max(0, 1 - daysSince / 30.0));
        }

        var autoprolongShare = activeCount == 0
            ? 0.0
            : input.Services.Count(s => s.Status == "active" && s.AutoProlong) / (double)activeCount;

        var total = LoyaltyTenureWeight * tenureSeverity
            + LoyaltyActiveWeight * activeSeverity
            + LoyaltyMonetaryWeight * monetarySeverity
            + LoyaltyRecencyWeight * recencySeverity
            + LoyaltyAutoprolongWeight * autoprolongShare;
        return (int)Math.Clamp(Math.Round(total), 0, 100);
    }

    public static long MonthlyPrice(Service service) => service.PaymentTerm switch
    {
        "hour" => service.Price * 730,
        "week" => service.Price * 52 / 12,
        "month" => service.Price,
        "quarter_year" => service.Price / 3,
        "half_year" => service.Price / 6,
        "year" => service.Price / 12,
        _ => service.Price,
    };

    public static RiskAssessment Assess(RiskInput input)
    {
        var result = Score(input);
        var monthlyRevenue = input.Services
            .Where(s => s.Status is "active" or "prolong_wait")
            .Sum(MonthlyPrice);
        var revenueAtRisk = result.Score >= input.Settings.HighRiskThreshold ? monthlyRevenue : 0L;
        IReadOnlyList<string> reasonCodes = result.Score is null
            ? []
            : result.Factors.Take(2).Select(f => f.Code).ToArray();
        return new RiskAssessment(result.Score, monthlyRevenue, revenueAtRisk,
            BalanceDeficit(input), reasonCodes);
    }

    public static string? TitleOf(string code) =>
        FactorReasons.FirstOrDefault(f => f.Code == code).Title;

    public static long BalanceDeficit(RiskInput input)
    {
        var effectiveBalance = input.Account.Balance +
            (input.Account.BonusState == "unlocked" ? input.Account.BonusBalance : 0);
        var windowStart = input.Now.AddDays(-ExpiredToleranceDays);
        var windowEnd = input.Now.AddDays(input.Settings.ImminentExpiryDays);
        var required = input.Services
            .Where(s => s.Status == "prolong_wait"
                || (s.Status == "active" && s.AutoProlong && s.ExpiresAt >= windowStart && s.ExpiresAt <= windowEnd))
            .Sum(s => s.Price);
        return Math.Max(0L, required - effectiveBalance);
    }

    private static void AddFactor(List<RiskFactor> factors, string code, string title, string description,
        double weight, double severity, long? serviceId)
    {
        var points = weight * severity;
        if (points > 0)
            factors.Add(new RiskFactor(code, title, description, points, weight, serviceId));
    }
}
