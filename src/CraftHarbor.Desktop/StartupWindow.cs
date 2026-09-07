using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using CraftHarbor.Core;

namespace CraftHarbor.Desktop;

public sealed class StartupWindow : Window
{
    private readonly TextBlock message = new() { Text = "サーバー一覧を読み込んでいます…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) };
    private readonly RotateTransform rotation = new();
    private bool closed;
    private bool started;

    public StartupWindow()
    {
        Title = "CraftHarbor — 準備中"; Width = 460; Height = 260;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        SetResourceReference(BackgroundProperty, "Background"); SetResourceReference(ForegroundProperty, "Ink");
        var panel = new StackPanel { Margin = new Thickness(32) };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var ring = new Ellipse { Width = 28, Height = 28, StrokeThickness = 3, StrokeDashArray = [5, 2], RenderTransform = rotation, RenderTransformOrigin = new Point(.5, .5), Margin = new Thickness(0, 0, 14, 0) };
        ring.SetResourceReference(Shape.StrokeProperty, "Accent"); row.Children.Add(ring);
        row.Children.Add(new TextBlock { Text = "CraftHarbor", FontSize = 26, FontWeight = FontWeights.Bold }); panel.Children.Add(row);
        panel.Children.Add(message); Content = panel;
        if (SystemParameters.ClientAreaAnimation)
            rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1)) { RepeatBehavior = RepeatBehavior.Forever });
        Closed += (_, _) => { closed = true; rotation.BeginAnimation(RotateTransform.AngleProperty, null); };
    }

    // App calls this only after the first ContentRendered event.
    public async Task LoadAsync(Func<Task<HarborStore>> loadStore)
    {
        if (started || closed) return;
        started = true;
        try
        {
            var store = await loadStore();
            if (closed) return;
            var main = new MainWindow(store) { ShowActivated = ShowActivated };
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        }
        catch (Exception ex)
        {
            if (closed) return;
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            Title = "CraftHarbor — 起動できませんでした";
            message.Text = "起動できませんでした。既存データはそのままです。\n" + ex.Message;
        }
    }
}
