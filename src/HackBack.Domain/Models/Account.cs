namespace HackBack.Domain;

public class Account
{
    public long Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public long Balance { get; set; }
    public long WithdrawBalance { get; set; }
    public long TotalReplenished { get; set; }
    public long BonusBalance { get; set; }
    public string BonusState { get; set; } = string.Empty;
    public int? RiskScore { get; set; }
    public long MonthlyRevenue { get; set; }
    public long RevenueAtRisk { get; set; }
    public long BalanceDeficit { get; set; }
    public string? PrimaryReasons { get; set; }
    public DateTime? ScoredAt { get; set; }
}
