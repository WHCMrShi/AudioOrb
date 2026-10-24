using AudioOrb.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace AudioOrb.Pages;

public sealed partial class AppearancePage : Page
{
    private static readonly double[] WidthPresets = [56, 168, 236, 280];

    private AppHost? _host;
    private bool _loading;

    public AppearancePage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _host = e.Parameter as AppHost;
        if (_host is null)
        {
            return;
        }

        _loading = true;
        SelectByTag(ThemePicker, _host.Settings.Theme);
        WidthSlider.Value = NearestWidthPresetIndex(_host.Settings.WidgetWidth);
        _loading = false;
    }

    private static void SelectByTag(ComboBox comboBox, string tag)
    {
        for (var i = 0; i < comboBox.Items.Count; i++)
        {
            if (comboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedIndex = i;
                return;
            }
        }

        comboBox.SelectedIndex = 0;
    }

    private static int NearestWidthPresetIndex(double width) =>
        WidthPresets
            .Select((value, index) => (value, index))
            .OrderBy(x => Math.Abs(x.value - width))
            .First()
            .index;

    private void ThemePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var tag = (ThemePicker.SelectedItem as ComboBoxItem)?.Tag as string;
        if (tag is null)
        {
            return;
        }

        _host.Settings.Theme = tag;
        _host.SaveSettings();
    }

    private void WidthSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || _host is null)
        {
            return;
        }

        var index = Math.Clamp((int)Math.Round(e.NewValue), 0, WidthPresets.Length - 1);
        _host.Settings.WidgetWidth = WidthPresets[index];
        _host.NotifySettingsPreview();
        _host.SaveSettingsDeferred();
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_host is null)
        {
            return;
        }

        _host.Settings.Theme = "System";
        _host.Settings.WidgetWidth = 236;

        _loading = true;
        SelectByTag(ThemePicker, "System");
        WidthSlider.Value = 2;
        _loading = false;

        _host.SaveSettings();

        StatusInfoBar.Severity = InfoBarSeverity.Success;
        StatusInfoBar.Message = "已恢复默认外观。";
        StatusInfoBar.IsOpen = true;
    }
}
