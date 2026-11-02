using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using VoiceChanger.Core;

namespace Voxel.Pages;

public partial class VoicesPage : UserControl
{
    private readonly VoiceChangerService _service = new();
    private readonly List<VoiceItem> _items;
    private readonly List<CategoryItem> _chips;
    private readonly ICollectionView _view;
    private readonly System.Windows.Threading.DispatcherTimer _meter = new() { Interval = TimeSpan.FromMilliseconds(40) };

    public VoicesPage()
    {
        InitializeComponent();

        var fallback = (Geometry)FindResource("Icon.microphone");
        Geometry Icon(string key) => TryFindResource("Icon." + key) as Geometry ?? fallback;

        // Categories first: the list filter reads the selected one as soon as the view is attached.
        _chips = [new CategoryItem("All", null, Icon("layout-grid")) { IsSelected = true }];
        _chips.AddRange(Enum.GetValues<VoiceCategory>().Select(c => new CategoryItem(c.ToString(), c, Icon(CategoryIcons[c]))));
        Chips.ItemsSource = _chips;

        _items = VoiceCatalog.All.Select(v => new VoiceItem(v, Icon(v.Icon))).ToList();
        _view = CollectionViewSource.GetDefaultView(_items);
        _view.Filter = o => Matches((VoiceItem)o);
        VoiceList.ItemsSource = _view;

        _service.Failed += ex => Dispatcher.BeginInvoke(() => { TurnOff(); Render($"Audio error: {ex.Message}"); });
        // Feed the live level into the active row's meter 25 times a second.
        _meter.Tick += (_, _) =>
        {
            var level = _service.Level;
            foreach (var i in _items) if (i.IsOn) i.Level = level;
        };
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } w) w.Closed += (_, _) => _service.Dispose();
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

    private VoiceCategory? SelectedCategory => _chips.FirstOrDefault(c => c.IsSelected)?.Category;

    private bool Matches(VoiceItem item)
    {
        var q = SearchBox.Text.Trim();
        var category = SelectedCategory;
        return (category == null || item.Voice.Category == category)
            && (q.Length == 0 || item.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    private void Refilter()
    {
        _view.Refresh();
        NoResults.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refilter();

    private void Chip_Click(object sender, RoutedEventArgs e)
    {
        var chip = (CategoryItem)((FrameworkElement)sender).DataContext;
        foreach (var c in _chips) c.IsSelected = c == chip;
        Refilter();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        var item = (VoiceItem)((FrameworkElement)sender).DataContext;
        try
        {
            if (item.IsOn)
            {
                TurnOff();
            }
            else
            {
                _service.TurnOn(item.Voice);
                foreach (var other in _items) other.IsOn = false;
                item.IsOn = true;
                _meter.Start();
            }
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
        _service.TurnOff();
        foreach (var i in _items) { i.IsOn = false; i.Level = 0; }
    }

    private void HearMyself_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            _service.HearMyself = HearMyself.IsChecked == true;
            if (_service.HearMyself && !_service.IsOn) _meter.Start();
            else if (!_service.IsOn) _meter.Stop();
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
        var window = (MainWindow)Window.GetWindow(this)!;
        window.ShowSettings(new SettingsPage(beforeSetup: () => { TurnOff(); Render(null); }));
    }

    private void Render(string? message)
    {
        if (message != null) { StatusText.Text = message; return; }
        var current = _service.CurrentVoice;
        StatusText.Text = current == null
            ? (_service.HearMyself ? "Mic test: you're hearing your plain voice. Use headphones to avoid echo." : "Click a voice to turn it on.")
            : _service.IsPreviewOnly
                ? $"{current.Name} is on — preview only, virtual microphone not found."
                : _service.HearMyself
                    ? $"{current.Name} is on — every app hears it, and so do you (use headphones to avoid echo)."
                    : $"{current.Name} is on — every app hears it. (One doesn't? Pick “CABLE Output” as its mic.)";
    }
}

/// <summary>A sidebar category entry. Category null = "All".</summary>
public sealed class CategoryItem(string name, VoiceCategory? category, Geometry icon) : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; } = name;
    public VoiceCategory? Category { get; } = category;
    public Geometry Icon { get; } = icon;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected == value) return; _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A voice as shown in the list: the preset, its icon shape, and whether it's active.</summary>
public sealed class VoiceItem(Voice voice, Geometry icon) : INotifyPropertyChanged
{
    private bool _isOn;

    public Voice Voice { get; } = voice;
    public string Name => Voice.Name;
    public Geometry Icon { get; } = icon;

    public bool IsOn
    {
        get => _isOn;
        set { if (_isOn == value) return; _isOn = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsOn))); }
    }

    private double _level;

    /// <summary>Live voice level 0..1 (only meaningful while on).</summary>
    public double Level
    {
        get => _level;
        set { _level = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Level))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
