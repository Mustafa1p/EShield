namespace EShield.Models;

public class BlockRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Domain
    {
        get; set;
    }
    public string? ExecutablePath
    {
        get; set;
    }
    public string? AppName
    {
        get; set;
    }
    public RuleType RuleType
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName => Domain is { Length: > 0 } ? (AppName is { Length: > 0 } ? $"{AppName} → {Domain}" : Domain)
        : AppName ?? System.IO.Path.GetFileNameWithoutExtension(ExecutablePath ?? "");
    public string DisplayTarget => Domain ?? ExecutablePath ?? "";
    public string TypeLabel => RuleType switch
    {
        RuleType.App => "App",
        RuleType.Domain => "Domain",
        RuleType.Both => "App + Domain",
        _ => "Unknown"
    };

    public static BlockRule FromProcess(NetworkProcess p) => new()
    {
        ExecutablePath = p.ExecutablePath,
        AppName = p.Name,
        RuleType = RuleType.App
    };

    public static BlockRule FromDomain(string domain) => new()
    {
        Domain = domain,
        RuleType = RuleType.Domain
    };
}

public class AllowRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Domain
    {
        get; set;
    }
    public string? ExecutablePath
    {
        get; set;
    }
    public string? AppName
    {
        get; set;
    }
    public RuleType RuleType
    {
        get; set;
    }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string DisplayName => Domain is { Length: > 0 } ? (AppName is { Length: > 0 } ? $"{AppName} → {Domain}" : Domain)
        : AppName ?? System.IO.Path.GetFileNameWithoutExtension(ExecutablePath ?? "");
    public string DisplayTarget => Domain ?? ExecutablePath ?? "";

    public static AllowRule FromProcess(NetworkProcess p) => new()
    {
        ExecutablePath = p.ExecutablePath,
        AppName = p.Name,
        RuleType = RuleType.App
    };

    public static AllowRule FromDomain(string domain) => new()
    {
        Domain = domain,
        RuleType = RuleType.Domain
    };
}
