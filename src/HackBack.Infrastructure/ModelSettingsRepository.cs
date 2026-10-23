using HackBack.Domain;
using Microsoft.EntityFrameworkCore;

namespace HackBack.Infrastructure;

public class ModelSettingsRepository(AppDbContext db)
{
    public async Task<ModelSettings> GetAsync()
    {
        var settings = await db.ModelSettings.FirstOrDefaultAsync(m => m.Id == 1);
        if (settings is not null)
            return settings;

        settings = new ModelSettings
        {
            Id = 1,
            ProlongWaitWithoutFundsWeight = 45,
            AutoprolongDisabledExpiringWeight = 25,
            AutoprolongEnabledDeficitWeight = 20,
            SuspendedServiceWeight = 15,
            RecentFailedProlongationWeight = 20,
            HighRiskThreshold = 70,
            MediumRiskThreshold = 40,
            ImminentExpiryDays = 3,
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "system",
        };
        db.ModelSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    public async Task SaveAsync(ModelSettings settings)
    {
        db.ModelSettings.Update(settings);
        await db.SaveChangesAsync();
    }
}
