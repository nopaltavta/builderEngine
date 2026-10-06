using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace builder;

/// <summary>Docked toolbox: tabbed parts/props/images/sounds, categorized,
/// searchable. Drag items into the viewport (drop = place on the surface),
/// double-click = insert in front of the camera.</summary>
public partial class ToolboxPanel : UserControl
{
    public MainWindow Main { get; set; } = null!;

    public sealed record ToolboxItem(string Id, string Name);

    private static readonly string[] Tabs = { "Parts", "Props", "Images", "Sounds", "Saved" };

    private static readonly Dictionary<string, string[]> Categories = new()
    {
        ["Parts"] = new[] { "Primitives", "Lights", "Other" },
        ["Props"] = new[] { "Physics", "Visuals", "Other" },
        ["Images"] = new[] { "Built-in" },
        ["Sounds"] = new[] { "Built-in" },
        ["Saved"] = new[] { "Models" },
    };

    private static readonly List<(string Tab, string Category, string Id, string Name)> StaticItems = new()
    {
        ("Parts", "Primitives", "shape:Block", "Cube"),
        ("Parts", "Primitives", "shape:Ball", "Sphere"),
        ("Parts", "Primitives", "plane", "Plane"),
        ("Parts", "Primitives", "shape:Cylinder", "Cylinder"),
        ("Parts", "Primitives", "shape:Wedge", "Wedge"),
        ("Parts", "Primitives", "shape:Cone", "Cone"),
        ("Parts", "Primitives", "shape:Capsule", "Capsule"),
        ("Parts", "Primitives", "shape:Torus", "Torus"),
        ("Parts", "Primitives", "shape:Pyramid", "Pyramid"),
        ("Parts", "Primitives", "shape:CornerWedge", "Corner Wedge"),
        ("Parts", "Primitives", "shape:Stairs", "Stairs"),
        ("Parts", "Primitives", "shape:HalfBall", "Half Ball"),
        ("Parts", "Primitives", "shape:HollowCylinder", "Hollow Cylinder"),
        ("Parts", "Primitives", "shape:Bowl", "Bowl"),
        ("Parts", "Primitives", "shape:Arch", "Arch"),
        ("Parts", "Primitives", "shape:HexPrism", "Hex Prism"),
        ("Parts", "Primitives", "shape:Truss", "Truss"),
        ("Parts", "Primitives", "mesh", "Mesh…"),
        ("Parts", "Lights", "light", "Point Light"),
        ("Parts", "Other", "spawn", "Spawn"),
        ("Parts", "Other", "kill", "Killbrick"),
        ("Parts", "Other", "scriptblock", "Script Block"),
        ("Props", "Physics", "conveyor", "Conveyor"),
        ("Props", "Physics", "magnet", "Magnet"),
        ("Props", "Physics", "mover", "Moving Platform"),
        ("Props", "Physics", "spinner", "Spinner"),
        ("Props", "Physics", "breakable", "Breakable"),
        ("Props", "Physics", "bounce", "Bounce Pad"),
        ("Props", "Physics", "checkpoint", "Checkpoint"),
        ("Props", "Physics", "teleport", "Teleport Pads"),
        ("Props", "Physics", "timed", "Timed Part"),
        ("Props", "Physics", "water", "Water"),
        ("Props", "Visuals", "decal", "Decal…"),
        ("Props", "Visuals", "text", "Text…"),
        ("Props", "Other", "npc", "NPC"),
    };

    private string _tab = "Parts";
    private string _category = "Primitives";
    private Point _dragStart;
    private bool _dragArmed;

    public ToolboxPanel()
    {
        InitializeComponent();
        RefreshAll();
    }

    /// <summary>Play mode: blank the panel with a note (greyed lists glitch white).</summary>
    public void SetPlayMode(bool playing)
    {
        PlayModeOverlay.Visibility = playing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Re-scan saved models (after add/remove).</summary>
    public void RefreshModels()
    {
        if (_tab == "Saved") RefreshAll();
    }

    private void AddModel_Click(object sender, RoutedEventArgs e) => Main?.SaveSelectedAsModel();

    private void RemoveModel_Click(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItem is ToolboxItem item && item.Id.StartsWith("model:") && Main != null)
            Main.DeleteToolboxModel(item.Id[6..]);
    }

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        // Fires during InitializeComponent before fields exist; the ctor's
        // RefreshAll covers initial state (field defaults: Parts/Primitives).
        if (ItemSearch == null || CategoryList == null || ItemList == null) return;
        if (TabParts.IsChecked == true) _tab = "Parts";
        else if (TabProps.IsChecked == true) _tab = "Props";
        else if (TabImages.IsChecked == true) _tab = "Images";
        else if (TabSounds.IsChecked == true) _tab = "Sounds";
        else if (TabSaved.IsChecked == true) _tab = "Saved";
        else return;
        ItemSearch.Text = "";
        ModelButtons.Visibility = _tab == "Saved" ? Visibility.Visible : Visibility.Collapsed;
        RefreshAll();
    }

    private void Category_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryList.SelectedItem is string c) { _category = c; RefreshItems(); }
        else if (CategoryList.Items.Count > 0) { CategoryList.SelectedIndex = 0; }
    }

    private void ItemSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshItems();

    private void RefreshAll()
    {
        CategoryList.ItemsSource = Categories[_tab];
        CategoryList.SelectedItem = Categories[_tab].Contains(_category) ? _category : Categories[_tab][0];
        _category = (string)CategoryList.SelectedItem;
        RefreshItems();
    }

    private IEnumerable<ToolboxItem> AllItems()
    {
        foreach (var s in StaticItems)
            if (s.Tab == _tab)
                yield return new ToolboxItem(s.Id, s.Name);
        if (_tab == "Images")
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "Faces");
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.png").OrderBy(Path.GetFileName))
                    yield return new ToolboxItem("img:" + f, Path.GetFileNameWithoutExtension(f));
        }
        else if (_tab == "Sounds")
        {
            string dir = Path.Combine(AppContext.BaseDirectory, "SoundEffects");
            if (Directory.Exists(dir))
            {
                // mp3 + wav fallbacks of the same sound share a name: list once (mp3 wins).
                var files = Directory.GetFiles(dir).Where(f =>
                    f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
                foreach (var g in files.GroupBy(Path.GetFileNameWithoutExtension).OrderBy(g => g.Key))
                {
                    string f = g.OrderBy(x => x.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? 0 : 1).First();
                    yield return new ToolboxItem("snd:" + f, Path.GetFileNameWithoutExtension(f));
                }
            }
        }
        else if (_tab == "Saved")
        {
            string dir = MainWindow.ModelsFolder;
            if (Directory.Exists(dir))
                foreach (string f in Directory.GetFiles(dir, "*.bp").OrderBy(Path.GetFileName))
                    yield return new ToolboxItem("model:" + f, Path.GetFileNameWithoutExtension(f));
        }
    }

    private void RefreshItems()
    {
        string q = (ItemSearch.Text ?? "").Trim();
        ItemList.ItemsSource = AllItems()
            .Where(i => CategoryOf(i.Id) == _category)
            .Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private string CategoryOf(string id)
    {
        if (id.StartsWith("model:")) return "Models";
        foreach (var s in StaticItems)
            if (s.Id == id && s.Tab == _tab)
                return s.Category;
        return "Built-in";
    }

    private void ItemList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemList);
        _dragArmed = true;
    }

    private void ItemList_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(ItemList);
        if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _dragArmed = false;
        if (ItemsControl.ContainerFromElement(ItemList, e.OriginalSource as DependencyObject) is not ListBoxItem row) return;
        if (row.DataContext is not ToolboxItem item) return;
        try { DragDrop.DoDragDrop(ItemList, new DataObject("ToolboxItem", item.Id), DragDropEffects.Copy); }
        catch { /* drag cancelled */ }
        finally { Main?.ClearGhost(); } // drop landed elsewhere (or cancelled): no stale ghost
    }

    private void ItemList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemList.SelectedItem is ToolboxItem item && Main != null)
            Main.DefaultToolboxItem(item.Id);
    }
}
