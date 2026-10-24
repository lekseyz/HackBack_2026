namespace HackBack.Domain;

public sealed record CustomersCursor(long Id, string? TextValue, long? NumberValue)
{
    public static bool TryParse(string? raw, string column, out CustomersCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(raw))
            return false;

        var separator = raw.LastIndexOf(':');
        if (separator < 0 || separator == raw.Length - 1)
            return false;

        if (!long.TryParse(raw[(separator + 1)..], out var id))
            return false;

        var value = raw[..separator];
        string? textValue = null;
        long? numberValue = null;

        switch (column)
        {
            case "id":
                if (!long.TryParse(value, out var idValue))
                    return false;
                numberValue = idValue;
                break;
            case "email":
                textValue = value;
                break;
            case "balance":
            case "totalreplenished":
                if (!long.TryParse(value, out var numberValueParsed))
                    return false;
                numberValue = numberValueParsed;
                break;
            case "riskscore":
                if (value.Length > 0)
                {
                    if (!int.TryParse(value, out var scoreParsed))
                        return false;
                    numberValue = scoreParsed;
                }
                break;
            default:
                return false;
        }

        cursor = new CustomersCursor(id, textValue, numberValue);
        return true;
    }

    public static string Format(Account account, string column) => column switch
    {
        "id" => $"{account.Id}:{account.Id}",
        "email" => $"{account.Email}:{account.Id}",
        "balance" => $"{account.Balance}:{account.Id}",
        "totalreplenished" => $"{account.TotalReplenished}:{account.Id}",
        "riskscore" => account.RiskScore.HasValue ? $"{account.RiskScore.Value}:{account.Id}" : $":{account.Id}",
        _ => $"{account.Id}:{account.Id}",
    };
}
