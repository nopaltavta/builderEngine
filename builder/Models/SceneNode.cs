using System.Collections.ObjectModel;
using System.ComponentModel;

namespace builder.Models;

public sealed class SceneNode : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
    public string Type { get; set; } = "Part";
    private string _icon = "📦"; // emoji fallback, no image assets needed
    public string Icon
    {
        get => _icon;
        set
        {
            if (_icon != value)
            {
                _icon = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
            }
        }
    }
    private string? _iconPath; // embedded PNG (e.g. "Icons/object.png"), null = emoji fallback
    public string? IconPath
    {
        get => _iconPath;
        set
        {
            if (_iconPath != value)
            {
                _iconPath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconPath)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIconPath)));
            }
        }
    }
    public bool HasIconPath => IconPath != null;

    private bool _isMultiSelected;
    /// <summary>Explorer-only highlight for members beyond the active TreeView selection.</summary>
    public bool IsMultiSelected
    {
        get => _isMultiSelected;
        set
        {
            if (_isMultiSelected == value) return;
            _isMultiSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMultiSelected)));
        }
    }

    /// <summary>Live back-reference (a <see cref="Viewport.SceneObject"/> for part nodes, else null).</summary>
    public object? Tag { get; set; }
    public ObservableCollection<SceneNode> Children { get; } = new();

    public SceneNode() { }

    public SceneNode(string name, string type = "Part", string icon = "📦", string? iconPath = null)
    {
        Name = name;
        Type = type;
        _icon = icon;
        _iconPath = iconPath;
    }

    public override string ToString() => Name;

    /// <summary>Tree back-reference for waypoint child nodes (platform + index).</summary>
    public sealed class WaypointRef
    {
        public builder.Viewport.SceneObject Obj;
        public int Index;
        public WaypointRef(builder.Viewport.SceneObject obj, int index) { Obj = obj; Index = index; }
    }

    public static ObservableCollection<SceneNode> CreateDefaultWorkspace()
    {
        // Workspace children are synced from the live 3D scene (see RefreshExplorer).
        var workspace = new SceneNode("World", "World", "🌍", "Icons/earth.png");

        var lighting = new SceneNode("Lighting", "Service", "💡", "Icons/light.png");
        lighting.Children.Add(new SceneNode("Sun", "Light", "☀", "Icons/sun.png"));
        lighting.Children.Add(new SceneNode("Atmosphere", "Effect", "☁", "Icons/atmosphere.png"));

        var players = new SceneNode("Players", "Service", "👥");
        players.Children.Add(new SceneNode("Player1", "Player", "🧍"));

        var starterPack = new SceneNode("StarterPack", "Container", "🎒", "Icons/backpack.png");
        var replicatedStorage = new SceneNode("ReplicatedStorage", "Service", "🗄");
        var serverScript = new SceneNode("ServerScriptService", "Service", "📜", "Icons/script.png");

        return new ObservableCollection<SceneNode>
        {
            workspace,
            lighting,
            players,
            starterPack,
            replicatedStorage,
            serverScript
        };
    }
}

public sealed class PropertyRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Category { get; set; } = "General";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    private string _value = "";
    public string Value
    {
        get => _value;
        set
        {
            string v = TidyNumber(value);
            if (_value != v)
            {
                _value = v;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NumericValue)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortValue)));
            }
        }
    }

    /// <summary>
    /// Keep displayed integers tidy ("007" -&gt; "7") while leaving everything else
    /// byte-identical. Components containing '.' pass through untouched when they
    /// parse (or are mid-typing like "0."): reformatting those would eat the dot
    /// out from under the keystroke. Vectors ("0.0, 1.0, 2.0") tidy per component.
    /// </summary>
    private static string TidyNumber(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        // Scientific notation stays verbatim (formatting could collapse it to 0).
        if (s.IndexOf('e') >= 0 || s.IndexOf('E') >= 0) return s;
        if (!s.Contains(','))
        {
            if (TryTidyDecimal(s, out string? single) && single != null) return single;
            return s;
        }
        string[] parts = s.Split(',');
        var clean = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            string p = parts[i].Trim();
            if (!TryTidyDecimal(p, out string? cp) || cp == null) return s; // mixed text: leave fully alone
            clean[i] = cp;
        }
        return string.Join(", ", clean);
    }

    /// <summary>Typing-safe tidy for one component: dotted decimals ("0.", "0.50")
    /// pass through verbatim so entry is never corrupted; dotless integers tidy.</summary>
    private static bool TryTidyDecimal(string s, out string? clean)
    {
        clean = null;
        string t = s.Trim();
        if (t.Contains('.'))
        {
            if (t is "." or "-." or "+.") { clean = t; return true; }
            if (double.TryParse(t, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d) &&
                !double.IsNaN(d) && !double.IsInfinity(d)) { clean = t; return true; }
            return false;
        }
        return TryTidy(t, out clean);
    }

    private static bool TryTidy(string s, out string? clean)
    {
        clean = null;
        if (!double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ||
            double.IsNaN(d) || double.IsInfinity(d))
            return false;
        clean = d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }
    public string Type { get; set; } = "string";
    public IReadOnlyList<string>? Choices { get; set; }
    public bool ShowCategory { get; set; }
    /// <summary>Hide this row's editor (collapsed section header keeps its
    /// category bar so the chevron can re-expand). Set by the filter pass.</summary>
    public bool HideEditor { get; set; }
    private string _catChevron = "▾";
    /// <summary>Category collapse chevron (▾ open, ▸ closed). Set by the filter pass.</summary>
    public string CatChevron
    {
        get => _catChevron;
        set
        {
            if (_catChevron != value)
            {
                _catChevron = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CatChevron)));
            }
        }
    }
    /// <summary>Index into <see cref="Viewport.SceneObject.Decals"/> for decal rows, else -1.</summary>
    public int DecalIndex { get; set; } = -1;
    /// <summary>Index into <see cref="Viewport.SceneObject.Texts"/> for text rows, else -1.</summary>
    public int TextIndex { get; set; } = -1;
    /// <summary>Index into <see cref="Viewport.SceneObject.Textures"/> for texture rows, else -1.</summary>
    public int TextureIndex { get; set; } = -1;
    public bool IsBoolean => Type == "bool" || Name is "Anchored";
    public bool IsChoice => Choices is { Count: > 0 };
    public bool IsVector => Type == "vector";
    public bool IsColor => Type == "color";
    public bool IsNumber => Type == "number";
    public bool IsFile => Type == "file";
    public bool IsReadOnly => Type == "readonly";
    public bool IsAction => Type == "action";
    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var value) && value;
        set => Value = value.ToString();
    }

    /// <summary>0-1 slider mirror of <see cref="Value"/> (for Transparency/Blur rows).</summary>
    public double NumericValue
    {
        get => double.TryParse(Value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        set => Value = value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    private System.Windows.Media.Brush? _swatch;
    /// <summary>Color preview brush (for Color rows). Updated live, no converter needed.</summary>
    public System.Windows.Media.Brush? Swatch
    {
        get => _swatch;
        set
        {
            if (!Equals(_swatch, value))
            {
                _swatch = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Swatch)));
            }
        }
    }

    private System.Windows.Media.ImageSource? _thumbnail;
    /// <summary>Small decal preview (for Image rows). Null = no image.</summary>
    public System.Windows.Media.ImageSource? Thumbnail
    {
        get => _thumbnail;
        set
        {
            if (!Equals(_thumbnail, value))
            {
                _thumbnail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
            }
        }
    }

    /// <summary>Short file name for tooltips / display (full path stays in <see cref="Value"/>).</summary>
    public string ShortValue
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Value) || Value == "None") return "None";
            try { return System.IO.Path.GetFileName(Value); }
            catch { return Value; }
        }
    }
}
