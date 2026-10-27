using System.Windows;
using System.Windows.Threading;
using AudioSwitch.Presentation;

namespace AudioSwitch.App.Views;

/// <summary>The Devices page: the device tree and the selected item's card.</summary>
public partial class DevicesPageView : System.Windows.Controls.UserControl
{
    private readonly DispatcherTimer _peakTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public DevicesPageView()
    {
        InitializeComponent();
        _peakTimer.Tick += (_, _) => Model?.Settings.RefreshPeak();
        DataContextChanged += (_, args) =>
        {
            if (args.OldValue is DevicePageViewModel oldModel) oldModel.PropertyChanged -= OnModelChanged;
            if (args.NewValue is DevicePageViewModel newModel) newModel.PropertyChanged += OnModelChanged;
        };
        Unloaded += (_, _) => _peakTimer.Stop();
    }

    public event EventHandler? IdentifyScreensRequested;

    private DevicePageViewModel? Model => DataContext as DevicePageViewModel;

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (Model is null) return;
        if (e.PropertyName == nameof(DevicePageViewModel.SelectedNode)) ShowSelectionInTree(Model.SelectedNode);
        if (e.PropertyName != nameof(DevicePageViewModel.IsListening)) return;
        if (Model.IsListening) _peakTimer.Start();
        else _peakTimer.Stop();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Model?.Refresh();

    private async void DeviceTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        // A refresh rebuilds the tree and briefly leaves nothing selected; the model keeps its selection and the row is selected again.
        if (Model is { } model && e.NewValue is DeviceNodeViewModel node) await model.SelectAsync(node);
    }

    /// <summary>Selects the row of a node the model picked itself, for example the same device after the tree was rebuilt.</summary>
    private void ShowSelectionInTree(DeviceNodeViewModel? node)
    {
        if (node is null || ReferenceEquals(DeviceTree.SelectedItem, node)) return;
        if (FindTreeItem(DeviceTree, node) is { } item)
        {
            item.IsSelected = true;
            return;
        }
        // Rows of a rebuilt tree are created at the next layout pass.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ReferenceEquals(Model?.SelectedNode, node) && FindTreeItem(DeviceTree, node) is { } created) created.IsSelected = true;
        });
    }

    private static System.Windows.Controls.TreeViewItem? FindTreeItem(System.Windows.Controls.ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.TreeViewItem direct) return direct;
        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is System.Windows.Controls.TreeViewItem container &&
                FindTreeItem(container, item) is { } found)
                return found;
        }
        return null;
    }

    private void SetDefault_Click(object sender, RoutedEventArgs e)
    {
        Model?.SetAsDefault();
        // The click toggled the button; show the real state again without dropping the binding.
        DefaultButton.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Model?.IsSelectedDefault == true);
    }

    private void DeviceTree_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Only a double-click on the selected endpoint row switches; double-clicks on expanders or group rows do nothing.
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left || Model?.SelectedNode is not { EndpointId: not null } node) return;
        if (e.OriginalSource is not FrameworkElement { DataContext: DeviceNodeViewModel clicked } || !ReferenceEquals(clicked, node)) return;
        Model.SetAsDefault();
        e.Handled = true;
    }

    private async void TestTone_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.PlayTestToneAsync();
    }

    private async void Listen_Click(object sender, RoutedEventArgs e)
    {
        if (Model is { } model) await model.ToggleListeningAsync();
        ListenButton.IsChecked = Model?.IsListening;
    }

    private void IdentifyScreen_Click(object sender, RoutedEventArgs e) => IdentifyScreensRequested?.Invoke(this, EventArgs.Empty);

    private void ApplyName_Click(object sender, RoutedEventArgs e) => Model?.ApplyName();

    private void RestoreName_Click(object sender, RoutedEventArgs e) => Model?.RestoreName();

    private void ApplyDockName_Click(object sender, RoutedEventArgs e) => Model?.ApplyDockName();

    private void RestoreDockName_Click(object sender, RoutedEventArgs e) => Model?.RestoreDockName();

    private void AssignPort_Click(object sender, RoutedEventArgs e)
    {
        if (PortChoice.SelectedValue is string interfaceId) Model?.AssignToPort(interfaceId);
    }
}
