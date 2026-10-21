using EShield.Models;

namespace EShield.Services;

public class BlockRuleService
{
    private readonly DatabaseService _db;
    private readonly FirewallService _fw;
    private List<BlockRule> _rules = new();

    public IReadOnlyList<BlockRule> Rules => _rules;
    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled; set
        {
            if (_isEnabled == value)
                return;
            _isEnabled = value;
            RulesChanged?.Invoke();
        }
    }

    public event Action? RulesChanged;

    public BlockRuleService(DatabaseService db, FirewallService fw)
    {
        _db = db;
        _fw = fw;
    }

    public void Load()
    {
        _rules = _db.GetBlockRules();
        RulesChanged?.Invoke();
    }

    public void Add(BlockRule rule)
    {
        if (_rules.Any(r => r.ExecutablePath == rule.ExecutablePath && r.Domain == rule.Domain))
            return;
        _rules.Add(rule);
        _db.SaveBlockRule(rule);
        RulesChanged?.Invoke();
    }

    public void Remove(Guid id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null)
            return;
        _rules.Remove(rule);
        _db.DeleteBlockRule(id);
        RulesChanged?.Invoke();
    }

    public bool IsBlocked(NetworkProcess proc, NetworkFlow flow)
    {
        if (!IsEnabled)
            return false;
        return _rules.Any(r => _fw.Matches(r.ExecutablePath, r.Domain, proc, flow));
    }

    public void ApplyAll() => RulesChanged?.Invoke();
}

public class AllowRuleService
{
    private readonly DatabaseService _db;
    private readonly FirewallService _fw;
    private List<AllowRule> _rules = new();

    public IReadOnlyList<AllowRule> Rules => _rules;

    public event Action? RulesChanged;

    public AllowRuleService(DatabaseService db, FirewallService fw)
    {
        _db = db;
        _fw = fw;
    }

    public void Load()
    {
        _rules = _db.GetAllowRules();
        RulesChanged?.Invoke();
    }

    public void Add(AllowRule rule)
    {
        if (_rules.Any(r => r.ExecutablePath == rule.ExecutablePath && r.Domain == rule.Domain))
            return;
        _rules.Add(rule);
        _db.SaveAllowRule(rule);
        RulesChanged?.Invoke();
    }

    public void Remove(Guid id)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == id);
        if (rule == null)
            return;
        _rules.Remove(rule);
        _db.DeleteAllowRule(id);
        RulesChanged?.Invoke();
    }

    public bool IsAllowed(NetworkProcess proc, NetworkFlow? flow)
    {
        return _rules.Any(r => flow != null ? _fw.Matches(r.ExecutablePath, r.Domain, proc, flow)
            : string.IsNullOrEmpty(r.Domain) && !string.IsNullOrEmpty(r.ExecutablePath) && r.ExecutablePath.Equals(proc.ExecutablePath, StringComparison.OrdinalIgnoreCase));
    }
}
