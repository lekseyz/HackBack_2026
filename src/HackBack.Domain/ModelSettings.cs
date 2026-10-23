namespace HackBack.Domain;

public class ModelSettings
{
    public int Id { get; set; }
    public double ProlongWaitWithoutFundsWeight { get; set; }
    public double AutoprolongDisabledExpiringWeight { get; set; }
    public double AutoprolongEnabledDeficitWeight { get; set; }
    public double SuspendedServiceWeight { get; set; }
    public double RecentFailedProlongationWeight { get; set; }
    public double HighRiskThreshold { get; set; }
    public double MediumRiskThreshold { get; set; }
    public int ImminentExpiryDays { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
