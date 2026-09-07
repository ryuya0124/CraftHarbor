using System.Windows;
using System.Windows.Controls;

namespace CraftHarbor.Desktop;

public static class HarborDialog
{
    public static MessageBoxResult Show(string text, string title = "CraftHelm")
    {
        var dialog = Create(null, text, title, MessageBoxButton.OK, out var result);
        dialog.ShowDialog(); return result();
    }
    public static MessageBoxResult Show(Window owner, string text, string title = "CraftHelm", MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = Create(owner, text, title, buttons, out var result);
        dialog.ShowDialog(); return result();
    }
    public static Window Create(Window? owner, string text, string title, MessageBoxButton buttons, out Func<MessageBoxResult> result)
    {
        var answer = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : MessageBoxResult.OK;
        var dialog = new Window { Owner = owner, Title = title, Width = 560, SizeToContent = SizeToContent.Height, MaxHeight = 620, MinHeight = 180, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        Theme.Attach(dialog);
        var body = new StackPanel { Margin = new Thickness(24) };
        body.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        body.Children.Add(new ScrollViewer { MaxHeight = 400, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        if (buttons == MessageBoxButton.YesNo)
        {
            var no = new Button { Content = "いいえ", IsCancel = true, IsDefault = true }; no.Click += (_, _) => { answer = MessageBoxResult.No; dialog.Close(); }; actions.Children.Add(no);
            var yes = new Button { Content = "はい" }; yes.Click += (_, _) => { answer = MessageBoxResult.Yes; dialog.Close(); }; actions.Children.Add(yes);
        }
        else
        {
            var ok = new Button { Content = "OK", IsDefault = true, IsCancel = true }; ok.Click += (_, _) => dialog.Close(); actions.Children.Add(ok);
        }
        body.Children.Add(actions); dialog.Content = body; result = () => answer; return dialog;
    }
}
