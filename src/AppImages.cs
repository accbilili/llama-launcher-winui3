using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace LlamaLauncher;

internal static class AppImages
{
    public static Image Image(string file, double size) => new()
    {
        Source = new SvgImageSource(new Uri("ms-appx:///Assets/" + file)),
        Width = size, Height = size, Stretch = Stretch.Uniform
    };
    public static Border Model(string icon, double size) => new()
    {
        Width = size, Height = size, CornerRadius = new(size / 4),
        Background = new SolidColorBrush(icon switch
        {
            "qwen-color.svg" => Color.FromArgb(255, 241, 237, 255),
            "openai.svg" => Color.FromArgb(255, 232, 243, 237),
            "hunyuan-color.svg" => Color.FromArgb(255, 234, 244, 255),
            _ => Color.FromArgb(255, 232, 240, 250)
        }),
        Child = Image(icon, size * .68), VerticalAlignment = VerticalAlignment.Top
    };
}
