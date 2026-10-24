namespace HackBack.Domain;

public class Service
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long OwnerId { get; set; }
    public string PaymentTerm { get; set; } = string.Empty;
    public bool AutoProlong { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public long Price { get; set; }
}
