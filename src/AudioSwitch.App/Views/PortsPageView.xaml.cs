using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

public partial class PortsPageView : UserControl
{
    private static readonly (PortConnectorType Type, string Name)[] Connectors =
    [
        (PortConnectorType.UsbA, "usb-a"),
        (PortConnectorType.UsbC, "usb-c"),
        (PortConnectorType.Hdmi, "hdmi"),
        (PortConnectorType.AudioJack, "audio-jack")
    ];

    private static readonly PortEdge[] Edges = [PortEdge.Left, PortEdge.Right, PortEdge.Rear, PortEdge.Front];
    private bool _syncing;

    public PortsPageView()
    {
        InitializeComponent();
        foreach (var (type, name) in Connectors) Palette.Items.Add(CreatePaletteButton(type, name));
        EdgeChoice.ItemsSource = Edges.Select(PortDiagramViewModel.EdgeLabel).ToArray();
        AddButton.ContextMenu = CreateAddMenu();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is PortSettingsViewModel oldModel)
            {
                oldModel.PropertyChanged -= OnModelChanged;
                oldModel.DiagramChanged -= OnDiagramChanged;
                oldModel.NameEditRequested -= OnNameEditRequested;
            }
            if (args.NewValue is PortSettingsViewModel newModel)
            {
                newModel.PropertyChanged += OnModelChanged;
                newModel.DiagramChanged += OnDiagramChanged;
                newModel.NameEditRequested += OnNameEditRequested;
                RefreshDiagram();
                SyncSelection();
            }
        };
    }

    private PortSettingsViewModel? Model => DataContext as PortSettingsViewModel;

    public void RefreshDiagram()
    {
        if (Model is null) return;
        var items = Model.Diagram.GetItems();
        Diagram.Items = items;
        _syncing = true;
        try
        {
            PortList.ItemsSource = items.OrderBy(item => item.Edge).ThenBy(item => item.Order).ToArray();
        }
        finally
        {
            _syncing = false;
        }
        SyncSelection();
    }

    private void OnDiagramChanged(object? sender, EventArgs e) => RefreshDiagram();

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PortSettingsViewModel.SelectedInterface))
            SyncSelection();
    }

    private void OnNameEditRequested(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        NameBox.Focus();
        NameBox.SelectAll();
    });

    private void SyncSelection()
    {
        if (Model is null) return;
        var selected = Model.SelectedInterface;
        Diagram.SelectedId = selected?.Id;
        _syncing = true;
        try
        {
            PortList.SelectedItem = (PortList.ItemsSource as IEnumerable<PortDiagramItem>)?.FirstOrDefault(item => item.Id == selected?.Id);
            if (PortList.SelectedItem is not null) PortList.ScrollIntoView(PortList.SelectedItem);
            var edge = PortDiagramViewModel.ParseEdge(selected?.Edge);
            EdgeChoice.SelectedIndex = selected is null ? -1 : Array.IndexOf(Edges, edge);
            var vertical = edge is PortEdge.Left or PortEdge.Right;
            SetMoveButton(MoveEarlierButton, MoveEarlierGlyph, vertical ? Glyphs.ChevronUp : Glyphs.ChevronLeft, vertical ? "往後移" : "往左移");
            SetMoveButton(MoveLaterButton, MoveLaterGlyph, vertical ? Glyphs.ChevronDown : Glyphs.ChevronRight, vertical ? "往前移" : "往右移");
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SetMoveButton(Button button, TextBlock glyph, string text, string description)
    {
        glyph.Text = text;
        button.ToolTip = description;
        AutomationProperties.SetName(button, description);
    }

    private Button CreatePaletteButton(PortConnectorType type, string name)
    {
        var label = PortDiagramViewModel.ConnectorLabel(type);
        var description = $"{label}：拖到機身邊緣新增";
        var button = new Button
        {
            Width = 48,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 4, 0),
            Content = new PortGlyph { ConnectorType = name, Scale = 1 },
            ToolTip = description
        };
        AutomationProperties.SetName(button, description);
        button.IsEnabledChanged += (_, _) => button.Opacity = button.IsEnabled ? 1 : 0.4;
        button.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || Model?.IsEnabled != true) return;
            DragDrop.DoDragDrop(button, new DataObject(LaptopDiagramView.PortTypeFormat, name), DragDropEffects.Copy);
        };
        return button;
    }

    private ContextMenu CreateAddMenu()
    {
        var menu = new ContextMenu();
        foreach (var (type, _) in Connectors)
        {
            var item = new MenuItem { Header = PortDiagramViewModel.ConnectorLabel(type) };
            item.Click += (_, _) =>
            {
                var edge = PortDiagramViewModel.ParseEdge(Model?.SelectedInterface?.Edge);
                Model?.Add(type, edge, int.MaxValue);
            };
            menu.Items.Add(item);
        }
        return menu;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        AddButton.ContextMenu.PlacementTarget = AddButton;
        AddButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        AddButton.ContextMenu.IsOpen = true;
    }

    private void Diagram_PortSelected(object? sender, string id) => Model?.Select(id);

    private void Diagram_PortDropped(object? sender, PortDropEventArgs e)
    {
        if (Model is null) return;
        if (e.PortId is { } id)
        {
            Model.Move(id, e.Edge, e.Order);
            return;
        }
        var connector = Connectors.FirstOrDefault(item => item.Name == e.NewType);
        if (connector.Name is not null) Model.Add(connector.Type, e.Edge, e.Order);
    }

    private void PortList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || PortList.SelectedItem is not PortDiagramItem item) return;
        Model?.Select(item.Id);
    }

    private void EdgeChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || EdgeChoice.SelectedIndex < 0) return;
        Model?.MoveSelectedToEdge(Edges[EdgeChoice.SelectedIndex]);
    }

    private void MoveEarlier_Click(object sender, RoutedEventArgs e) => Model?.MoveSelectedBy(-1);

    private void MoveLater_Click(object sender, RoutedEventArgs e) => Model?.MoveSelectedBy(1);

    private void SaveName_Click(object sender, RoutedEventArgs e) => Model?.SaveName();

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        Model?.SaveName();
        e.Handled = true;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Model?.SelectedInterface is not { } port) return;
        var owner = Window.GetWindow(this);
        var answer = owner is null
            ? MessageBox.Show($"刪除「{port.Name}」？已記錄的校準資料也會一併刪除。", "刪除接口", MessageBoxButton.YesNo, MessageBoxImage.Question)
            : MessageBox.Show(owner, $"刪除「{port.Name}」？已記錄的校準資料也會一併刪除。", "刪除接口", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) Model.DeleteSelected();
    }

    private void Calibrate_Click(object sender, RoutedEventArgs e) => Model?.BeginCalibration();

    private void DetectNewDevice_Click(object sender, RoutedEventArgs e) => Model?.BeginDetectNewDevice();

    private void ConfirmUnplugged_Click(object sender, RoutedEventArgs e) => Model?.ConfirmUnplugged();

    private void CompleteDetection_Click(object sender, RoutedEventArgs e) => Model?.CompleteDetection();

    private void ApplyDetected_Click(object sender, RoutedEventArgs e) => Model?.ApplyDetectedToSelected();

    private void CancelCalibration_Click(object sender, RoutedEventArgs e) => Model?.CancelCalibration();

    private void ApplyAlias_Click(object sender, RoutedEventArgs e) => Model?.ApplyDetectedAlias();

    private void SkipAlias_Click(object sender, RoutedEventArgs e) => Model?.SkipAlias();
}
