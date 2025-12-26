using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NVibrance.Services;

public static class ExeIconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string path)
    {
        return _cache.GetOrAdd(path, p =>
        {
            var img = ExeIconLoader.TryLoad(p); // keep existing loader
            if (img is BitmapSource { CanFreeze: true, IsFrozen: false } bs)
                bs.Freeze();
            return img;
        });
    }
}