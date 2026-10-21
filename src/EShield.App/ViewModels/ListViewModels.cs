using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows;
using EShield.Models;
using EShield.Services;

namespace EShield.ViewModels;

public partial class BlockListViewModel : ObservableObject
{
    private readonly BlockRuleService _service;
    private readonly MainViewModel _main;

    [ObservableProperty] private ObservableCollection<BlockRule> _rules = new();
    [ObservableProperty] private BlockRule? _selectedRule;
    [ObservableProperty] private string _newRuleInput = string.Empty;
    [ObservableProperty] private bool _isListEnabled = true;

    public BlockListViewModel(BlockRuleService service, MainViewModel main)
    {
        _service = service;
        _main = main;
        service.RulesChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Rules.Clear();
            foreach (var r in _service.Rules)
                Rules.Add(r);
            IsListEnabled = _service.IsEnabled;
        });
    }

    [RelayCommand]
    public void AddDomainRule()
    {
        var input = NewRuleInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;
        try
        {
            foreach (var target in input.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (System.IO.File.Exists(target))
                    _service.Add(new BlockRule { ExecutablePath = System.IO.Path.GetFullPath(target), AppName = System.IO.Path.GetFileNameWithoutExtension(target), RuleType = RuleType.App });
                else
                    _service.Add(BlockRule.FromDomain(EShield.Services.RuleMatcher.NormalizeTarget(target)));
            }
        }
        catch (ArgumentException e) { MessageBox.Show(e.Message, "Invalid rule"); return; }
        NewRuleInput = string.Empty;
    }

    [RelayCommand]
    public void RemoveRule(BlockRule rule)
    {
        if (rule == null)
            return;
        _service.Remove(rule.Id);
    }

    [RelayCommand]
    public void RemoveSelectedRule()
    {
        if (SelectedRule != null)
            _service.Remove(SelectedRule.Id);
    }

    partial void OnIsListEnabledChanged(bool value)
    {
        _main.Settings.Settings.BlockListEnabled = value;
        _main.Settings.Save();
        _service.IsEnabled = value;
        _main.RefreshPolicy();
    }
}

public partial class AllowListViewModel : ObservableObject
{
    private readonly AllowRuleService _service;
    private readonly MainViewModel _main;

    [ObservableProperty] private ObservableCollection<AllowRule> _rules = new();
    [ObservableProperty] private AllowRule? _selectedRule;
    [ObservableProperty] private string _newRuleInput = string.Empty;

    public AllowListViewModel(AllowRuleService service, MainViewModel main)
    {
        _service = service;
        _main = main;
        service.RulesChanged += Refresh;
        Refresh();
    }

    private void Refresh()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            Rules.Clear();
            foreach (var r in _service.Rules)
                Rules.Add(r);
        });
    }

    [RelayCommand]
    public void AddDomainRule()
    {
        var input = NewRuleInput.Trim();
        if (string.IsNullOrWhiteSpace(input))
            return;
        try
        {
            foreach (var target in input.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (System.IO.File.Exists(target))
                    _service.Add(new AllowRule { ExecutablePath = System.IO.Path.GetFullPath(target), AppName = System.IO.Path.GetFileNameWithoutExtension(target), RuleType = RuleType.App });
                else
                    _service.Add(AllowRule.FromDomain(EShield.Services.RuleMatcher.NormalizeTarget(target)));
            }
        }
        catch (ArgumentException e) { MessageBox.Show(e.Message, "Invalid rule"); return; }
        NewRuleInput = string.Empty;
    }

    [RelayCommand]
    public void RemoveRule(AllowRule rule)
    {
        if (rule == null)
            return;
        _service.Remove(rule.Id);
    }

    [RelayCommand]
    public void RemoveSelectedRule()
    {
        if (SelectedRule != null)
            _service.Remove(SelectedRule.Id);
    }
}
