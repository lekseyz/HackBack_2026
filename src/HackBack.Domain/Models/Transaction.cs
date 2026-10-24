namespace HackBack.Domain;

public class Transaction
{
    public long Id { get; set; }
    public int Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? PerformedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public long ServiceId { get; set; }
}
