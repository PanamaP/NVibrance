using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NVibrance;

public static class ExeIconLoader
{
    public static ImageSource? TryLoad(string exePath)
    {
        try
        {
            if (!File.Exists(exePath))
                return null;

            using var icon = Icon.ExtractAssociatedIcon(exePath);
            if (icon is null)
                return null;

            var handle = icon.Handle;

            var source = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(16, 16));

            source.Freeze();
            return source;
        }
        catch
        {
            return null;
        }
    }
}