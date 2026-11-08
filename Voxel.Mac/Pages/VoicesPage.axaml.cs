using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using VoiceChanger.Core;
using VoiceChanger.Mac;

namespace Voxel.Mac.Pages;

public partial class VoicesPage : UserControl
{
    private MacVoiceChangerService? _service;
    private readonly List<VoiceItem> _items;
    private readonly List<CategoryItem> _chips;
    private readonly DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(40) };

    public VoicesPage()
    {
        InitializeComponent();
        SearchIcon.Data = Icons.Get("search");
        ClearIcon.Data = Icons.Get("x");
        HearMyself.Tag = Icons.Get("headphones");

        _chips = [new CategoryItem("All", null, Icons.Get("layout-grid")) { IsSelected = true }];
        _chips.AddRange(Enum.GetValues<VoiceCategory>().Select(c => new CategoryItem(c.ToString(), c, Icons.Get(CategoryIcons[c]))));
        Chips.ItemsSource = _chips;

        _items = VoiceCatalog.All.Select(v => new VoiceItem(v, Icons.Get(v.Icon))).ToList();
        VoiceList.ItemsSource = _items;

        // Feed the live level into the active row's meter 25 times a second.
        _meter.Tick += (_, _) =>
        {
            var level = _service?.Level ?? 0;
            foreach (var i in _items) if (i.IsOn) i.Level = level;
        };
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window w) w.Closed += (_, _) => _service?.Dispose();
            SearchBox.Focus();
        };
        Render(null);
    }

    private static readonly Dictionary<VoiceCategory, string> CategoryIcons = new()
    {
        [VoiceCategory.AI] = "atom",
        [VoiceCategory.People] = "user",
        [VoiceCategory.Creatures] = "skull",
        [VoiceCategory.Spooky] = "ghost",
        [VoiceCategory.Machines] = "robot",
        [VoiceCategory.Places] = "map-pin",
        [VoiceCategory.Devices] = "device-speaker",
        [VoiceCategory.Fun] = "confetti",
        [VoiceCategory.Animals] = "paw",
    };

    // Opened on first use: it touches Core Audio, and the page should appear before that.
    private MacVoiceChangerService Service
    {
        get
        {
            if (_service != null) return _service;
            _service = new MacVoiceChangerService();
            _service.Failed += ex => Dispatcher.UIThread.Post(() => { TurnOff(); Render($"Audio error: {ex.Message}"); });
            return _service;
        }
    }

    private void Refilter()
    {
        var q = SearchBox.Text?.Trim() ?? "";
        var category = _chips.FirstOrDefault(c => c.IsSelected)?.Category;
        foreach (var item in _items)
            item.IsShown = (category == null || item.Voice.Category == category)
                           && (q.Length == 0 || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        NoResults.IsVisible = !_items.Any(i => i.IsShown);
    }

    private void Search_TextChanged(object? sender, TextChangedEventArgs e) => Refilter();

    private void Chip_Click(object? sender, RoutedEventArgs e) => SelectCategory((CategoryItem)((Control)sender!).DataContext!);

    private void SelectCategory(CategoryItem chip)
    {
        foreach (var c in _chips) c.IsSelected = c == chip;
        Refilter();
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        SearchBox.Focus();
    }

    private void Row_Click(object? sender, RoutedEventArgs e)
    {
        var item = (VoiceItem)((Control)sender!).DataContext!;
        try
        {
            if (item.IsOn)
            {
                TurnOff();
                Render(null);
                return;
            }
            Service.TurnOn(item.Voice);
            foreach (var other in _items) other.IsOn = false;
            item.IsOn = true;
            _meter.Start();
            Render(null);
        }
        catch (Exception ex)
        {
            TurnOff();
            Render($"Couldn't start: {ex.Message}");
        }
    }

    private void TurnOff()
    {
        _meter.Stop();
        _service?.TurnOff();
        foreach (var i in _items) { i.IsOn = false; i.Level = 0; }
    }

    private void HearMyself_Changed(object? sender, RoutedEventArgs e)
    {
        try
        {
            Service.HearMyself = HearMyself.IsChecked == true;
            if (Service.HearMyself && !Service.IsOn) _meter.Start();
            else if (!Service.IsOn) _meter.Stop();
            Render(null);
        }
        catch (Exception ex)
        {
            HearMyself.IsChecked = false;
            Render($"Couldn't start: {ex.Message}");
        }
    }

    /// <summary>Opens the settings page over this one; the running voice keeps running.</summary>
    public void OpenSettings()
    {
        var window = (MainWindow)TopLevel.GetTopLevel(this)!;
        window.ShowSettings(new SettingsPage(beforeSetup: () => { TurnOff(); Render(null); }));
    }

    private void Render(string? message)
    {
        if (message != null) { StatusText.Text = message; return; }
        // A Mac that wasn't allowed to share the mic gives Voxel silence, not an error: say so.
        var denied = _service != null && OperatingSystem.IsMacOS() && MacPermissions.Microphone() is MicrophoneAccess.Denied or MicrophoneAccess.Restricted;
        var current = _service?.CurrentVoice;
        StatusText.Text = denied && (current != null || _service!.HearMyself)
            ? "Voxel isn't allowed to use the microphone. Open Settings (gear, top right) to allow it."
            : current == null
                ? (_service?.HearMyself == true ? "Mic test: you're hearing your plain voice. Use headphones to avoid echo." : "Click a voice to turn it on.")
                : _service!.IsPreviewOnly
                    ? $"{current.Name} is on — preview only, virtual microphone not found."
                    : _service.HearMyself
                        ? $"{current.Name} is on — every app hears it, and so do you (use headphones to avoid echo)."
                        : $"{current.Name} is on — every app hears it. (One doesn't? Pick “VB-Cable” as its mic.)";
    }

    /// <summary>For the screenshot tool: a category picked and a voice shown as on, without starting audio.</summary>
    internal void Preview(string? category, string? onVoice, double level)
    {
        SelectCategory(_chips.First(c => c.Category?.ToString() == category));
        foreach (var i in _items) { i.IsOn = i.Name == onVoice; i.Level = i.IsOn ? level : 0; }
        if (onVoice != null) StatusText.Text = $"{onVoice} is on — every app hears it. (One doesn't? Pick “VB-Cable” as its mic.)";
    }
}

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string name = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A sidebar category entry. Category null = "All".</summary>
public sealed class CategoryItem(string name, VoiceCategory? category, Geometry icon) : Notifier
{
    private bool _isSelected;

    public string Name { get; } = name;
    public VoiceCategory? Category { get; } = category;
    public Geometry Icon { get; } = icon;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
}

/// <summary>A voice as shown in the list: the preset, its icon shape, and whether it's active.</summary>
public sealed class VoiceItem(Voice voice, Geometry icon) : Notifier
{
    private bool _isOn, _isShown = true;
    private double _level;

    public Voice Voice { get; } = voice;
    public string Name => Voice.Name;
    public Geometry Icon { get; } = icon;
    public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }
    public bool IsShown { get => _isShown; set => Set(ref _isShown, value); }

    /// <summary>Live voice level 0..1 (only meaningful while on).</summary>
    public double Level { get => _level; set => Set(ref _level, value); }
}
