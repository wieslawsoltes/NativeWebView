using Avalonia.Controls;

namespace NativeWebView.Integration;

internal sealed class IntegrationWindow : Window
{
    public IntegrationWindow(Control content)
    {
        Title = "NativeWebView Integration";
        Width = 1280;
        Height = 900;
        MinWidth = 900;
        MinHeight = 700;
        Content = content;
    }
}
