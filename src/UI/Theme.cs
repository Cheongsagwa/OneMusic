using Microsoft.UI.Xaml.Markup;
using Windows.UI.Text;

namespace OneMusic.UI;

/// <summary>One UI 토큰 (pear-desktop 테마 style.css 맨 위 토큰과 같은 값)</summary>
public static class Theme
{
    public static readonly Color Bg = Color.FromArgb(255, 0x17, 0x17, 0x1a);
    public static readonly Color OnSurface = Colors.White;
    public static readonly Color Secondary = Color.FromArgb(255, 160, 160, 164);
    public static readonly Color Chip = Color.FromArgb(26, 255, 255, 255);
    public static readonly Color ChipHover = Color.FromArgb(40, 255, 255, 255);
    public static readonly Color Selected = Color.FromArgb(56, 255, 255, 255);
    public static readonly Color Divider = Color.FromArgb(31, 255, 255, 255);
    public static readonly Color GlassTint = Color.FromArgb(255, 36, 36, 40);

    public static readonly FontFamily Font = new("Pretendard Variable, Pretendard, Segoe UI Variable Text, Malgun Gothic");

    public const double RadiusArt = 16;
    public const double RadiusThumb = 10;
    public const double RadiusCard = 26;
    public const double FloatGap = 12;
    public const double BarHeight = 96;

    public static SolidColorBrush B(Color c) => new(c);
    public static SolidColorBrush B(byte a, byte r, byte g, byte b) => new(Color.FromArgb(a, r, g, b));
    public static Color WithAlpha(this Color c, double a) => Color.FromArgb((byte)Math.Clamp(a * 255, 0, 255), c.R, c.G, c.B);

    /// <summary>앱 안의 유리(뒤 내용이 흐리게 비치는 표면)</summary>
    public static Brush Glass(double tintOpacity = 0.55, Color? tint = null) => new AcrylicBrush
    {
        TintColor = tint ?? GlassTint,
        TintOpacity = tintOpacity,
        TintLuminosityOpacity = 0.6,
        FallbackColor = (tint ?? GlassTint).WithAlpha(0.92),
    };

    public static TextBlock Text(string text, double size, FontWeight weight, Color color, int maxLines = 1) => new()
    {
        Text = text,
        FontFamily = Font,
        FontSize = size,
        FontWeight = weight,
        Foreground = B(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = maxLines == 1 ? TextWrapping.NoWrap : TextWrapping.WrapWholeWords,
        MaxLines = maxLines,
        IsTextSelectionEnabled = false,
    };

    /// <summary>SVG path 문자열 → XAML 도형 (도형은 요소마다 새로 만들어야 한다)</summary>
    public static Geometry Geo(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    /// <summary>24×24 좌표계 아이콘 (SVG path). stroke 면 선, 아니면 채움</summary>
    public static FrameworkElement Icon(string data, double size, Color color, bool stroke = false, double strokeWidth = 1.8)
    {
        var path = new XPath { Data = Geo(data) };
        if (stroke)
        {
            path.Stroke = B(color);
            path.StrokeThickness = strokeWidth;
            path.StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
            path.StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
            path.StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round;
        }
        else path.Fill = B(color);
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>채우고 테두리도 그리는 아이콘 (pear liked · disliked: fill + stroke 1.7 round)</summary>
    public static FrameworkElement IconFilledStroke(string data, double size, Color color)
    {
        var path = new XPath
        {
            Data = Geo(data),
            Fill = B(color),
            Stroke = B(color),
            StrokeThickness = 1.7,
            StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round,
            StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
            StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round,
        };
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(path);
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>여러 조각(선 / 채움)으로 된 24×24 아이콘</summary>
    public static FrameworkElement Icon(double size, Color color, params (string Data, bool Stroke)[] parts)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        foreach (var (data, stroke) in parts)
        {
            var path = new XPath { Data = Geo(data) };
            if (stroke)
            {
                path.Stroke = B(color);
                path.StrokeThickness = 1.7;
                path.StrokeStartLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
                path.StrokeEndLineCap = Microsoft.UI.Xaml.Media.PenLineCap.Round;
                path.StrokeLineJoin = Microsoft.UI.Xaml.Media.PenLineJoin.Round;
            }
            else path.Fill = B(color);
            canvas.Children.Add(path);
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, IsHitTestVisible = false };
    }

    /// <summary>
    /// 썸네일 주소를 크기에 맞게 고치고, 동영상 썸네일(hqdefault 등 4:3 — 위아래 검은 띠)은 띠 없는 16:9(hq720) 로.
    /// fallback 은 hq720 이 없을 때 쓸 mqdefault
    /// </summary>
    public static (string? Url, string? Fallback) ImageUrl(string? url, int decodeWidth)
    {
        var u = Core.Fmt.Thumb(url, Math.Max(decodeWidth * 2, 120));
        string? fallback = null;
        if (u != null)
        {
            var m = System.Text.RegularExpressions.Regex.Match(u, @"^https?://i\d?\.ytimg\.com/vi(?:_webp)?/([^/]+)/");
            if (m.Success)
            {
                fallback = $"https://i.ytimg.com/vi/{m.Groups[1].Value}/mqdefault.jpg";
                u = $"https://i.ytimg.com/vi/{m.Groups[1].Value}/hq720.jpg";
            }
        }
        return (string.IsNullOrEmpty(u) ? null : u, fallback);
    }

    public static BitmapImage Image(string? url, int decodeWidth)
    {
        var bmp = new BitmapImage { DecodePixelWidth = decodeWidth, DecodePixelType = DecodePixelType.Logical };
        var (u, fallback) = ImageUrl(url, decodeWidth);
        if (u != null && Uri.TryCreate(u, UriKind.Absolute, out var uri)) bmp.UriSource = uri;
        if (fallback != null)
            bmp.ImageFailed += (_, _) =>
            {
                if (bmp.UriSource?.AbsoluteUri != fallback) bmp.UriSource = new Uri(fallback);
            };
        return bmp;
    }
}

/// <summary>아이콘 모양 (24×24, One UI 선 아이콘)</summary>
public static class Icons
{
    public const string Play = "M7.5 4.8 L19.2 12 L7.5 19.2 Z";
    public const string Pause = "M6.5 5 L10.3 5 L10.3 19 L6.5 19 Z M13.7 5 L17.5 5 L17.5 19 L13.7 19 Z";
    public const string Previous = "M4.8 5.2h2.2v13.6H4.8z M19.2 6.7v10.6c0 .9-1 1.4-1.7.9l-7.6-5.3c-.6-.4-.6-1.4 0-1.8l7.6-5.3c.7-.5 1.7 0 1.7.9z";
    public const string Next = "M17 5.2h2.2v13.6H17z M4.8 6.7v10.6c0 .9 1 1.4 1.7.9l7.6-5.3c.6-.4.6-1.4 0-1.8L6.5 5.8c-.7-.5-1.7 0-1.7.9z";
    public const string Speaker = "M3.3 9.6v4.8c0 .7.5 1.2 1.2 1.2h2.5l4.1 3.5c.7.6 1.7.1 1.7-.8V5.7c0-.9-1-1.4-1.7-.8L7 8.4H4.5c-.7 0-1.2.5-1.2 1.2z";
    public const string Waves = "M15.7 8.9a4.4 4.4 0 0 1 0 6.2 M18.1 6.5a7.8 7.8 0 0 1 0 11";
    public const string MuteX = "M15.9 9.6l4.8 4.8 M20.7 9.6l-4.8 4.8";
    public const string Repeat = "M7.4 5.2h9.2a3.8 3.8 0 0 1 3.8 3.8v6a3.8 3.8 0 0 1-3.8 3.8H7.4a3.8 3.8 0 0 1-3.8-3.8V9a3.8 3.8 0 0 1 3.8-3.8z M14.2 2.6 16.8 5.2 14.2 7.8";
    public const string RepeatOne = "M10.9 10.4l1.4-1v5.2";
    public const string Shuffle = "M3.6 18.6H6.4c4.8 0 6.4-13.2 11.2-13.2h2.8 M3.6 5.4H6.4c4.8 0 6.4 13.2 11.2 13.2h2.8 M17.8 2.8l2.6 2.6-2.6 2.6 M17.8 16l2.6 2.6-2.6 2.6";
    public const string ChevronDown = "M6 9.4 12 15.4l6-6";
    public const string ChevronLeft = "M14.6 6 8.6 12l6 6";
    public const string PlayNextIcon = "M4 6h10 M4 11h7 M4 16h7 M15 11v8l5-4z";
    public const string QueueAdd = "M4 6h12 M4 11h12 M4 16h7 M17 14v6 M14 17h6";
    public const string Album = "M12 3.5a8.5 8.5 0 1 1 0 17a8.5 8.5 0 1 1 0-17z M12 9.5a2.5 2.5 0 1 1 0 5a2.5 2.5 0 1 1 0-5z";
    public const string Person = "M12 4.5a3.5 3.5 0 1 1 0 7a3.5 3.5 0 1 1 0-7z M5 19.5c.8-3.4 3.6-5.5 7-5.5s6.2 2.1 7 5.5";
    public const string Download = "M12 4v11 M7.5 10.5 12 15l4.5-4.5 M5 19.5h14";
    /// <summary>⋮ — sesl-androidx 원본 경로 (pear icons.ts more)</summary>
    public const string More = "M12,16.75C12.868,16.75 13.5,17.382 13.5,18.25C13.5,19.118 12.868,19.75 12,19.75C11.211,19.75 10.5,19.118 10.5,18.25C10.5,17.382 11.132,16.75 12,16.75ZM12,10.5C12.868,10.5 13.5,11.132 13.5,12C13.5,12.868 12.868,13.5 12,13.5C11.211,13.5 10.5,12.868 10.5,12C10.5,11.132 11.132,10.5 12,10.5ZM12,4.25C12.789,4.25 13.5,4.882 13.5,5.75C13.5,6.618 12.868,7.25 12,7.25C11.211,7.25 10.5,6.618 10.5,5.75C10.5,4.882 11.132,4.25 12,4.25Z";
    /// <summary>pear icons.ts THUMB (좋아요). 싫어요는 180° 돌린 것</summary>
    public const string ThumbUp = "M7.6 10.6v8.9M7.6 10.6l3.5-6.1c.3-.6 1-.9 1.6-.7 1 .3 1.6 1.3 1.4 2.3l-.6 3.2h4.3c1.3 0 2.3 1.2 2 2.5l-1.4 5.8c-.2 1-1.1 1.7-2.1 1.7H7.6M7.6 10.6H5.2c-.6 0-1.1.5-1.1 1.1v6.7c0 .6.5 1.1 1.1 1.1h2.4";
    public const string ThumbDown = "M16.4 13.4v-8.9M16.4 13.4l-3.5 6.1c-.3.6-1 .9-1.6.7-1-.3-1.6-1.3-1.4-2.3l.6-3.2H6.2c-1.3 0-2.3-1.2-2-2.5l1.4-5.8c.2-1 1.1-1.7 2.1-1.7h8.7M16.4 13.4h2.4c.6 0 1.1-.5 1.1-1.1V5.6c0-.6-.5-1.1-1.1-1.1h-2.4";
    public const string MinusCircle = "M12 3.6a8.4 8.4 0 1 1 0 16.8a8.4 8.4 0 1 1 0-16.8z M8 12h8";
    public const string Share = "M14 5l6 6-6 6 M20 11h-7.5c-4 0-7.5 3-8 8";
    public const string Check = "M6 12.5l4 4 8-9";
    public const string Star = "M12 3.8l2.5 5.2 5.7.8-4.1 4 1 5.7L12 16.8l-5.1 2.7 1-5.7-4.1-4 5.7-.8z";
    public const string QueueClose = "M4 6.5h12 M4 11.5h12 M4 16.5h7 M15.5 15l4.5 4.5 M20 15l-4.5 4.5";
    public const string Stats = "M5 4.5h14v15H5z M8.5 16v-4 M12 16V8 M15.5 16v-6";
    public const string LibraryAdd = "M4.5 6h11 M4.5 10.5h11 M4.5 15h6 M17.5 13v7 M14 16.5h7";
    public const string LibrarySaved = "M4.5 6h11 M4.5 10.5h11 M4.5 15h6 M13.5 17l2.5 2.5 4.5-5";
    public const string PlaylistAddPath = "M4.5 6.5h12M4.5 11.5h12M4.5 16.5h7M17.5 13.5v7M14 17h7";
    public const string Flag = "M6 20.5V4.5 M6 5h10.5l-2 4 2 4H6";
    public const string Pin = "M14.5 4.5l5 5-3 1.5-3.5 3.5.5 3.5-1.5 1.5-8-8 1.5-1.5 3.5.5 3.5-3.5z M4.5 19.5l4-4";
    public const string ChevronRight = "M9.4 6l6 6-6 6";
    public const string Link = "M10.2 13.8a3.6 3.6 0 0 0 5.1 0l3-3a3.6 3.6 0 0 0-5.1-5.1l-1 1 M13.8 10.2a3.6 3.6 0 0 0-5.1 0l-3 3a3.6 3.6 0 0 0 5.1 5.1l1-1";
    public const string Radio = "M12 10.6a1.4 1.4 0 1 1 0 2.8a1.4 1.4 0 1 1 0-2.8z M8.6 8.6a4.8 4.8 0 0 0 0 6.8 M15.4 8.6a4.8 4.8 0 0 1 0 6.8 M5.8 5.8a8.8 8.8 0 0 0 0 12.4 M18.2 5.8a8.8 8.8 0 0 1 0 12.4";
    public const string ChevronUp = "M6 14.6 12 8.6l6 6";
    public const string Search = "M10.8 4.2a6.6 6.6 0 1 1 0 13.2 6.6 6.6 0 0 1 0-13.2z M15.6 15.6 20 20";
    public const string Home = "M4.5 10.2 12 4.2l7.5 6v8.4c0 .7-.5 1.2-1.2 1.2H14v-5.4h-4v5.4H5.7c-.7 0-1.2-.5-1.2-1.2z";
    public const string Close = "M6.5 6.5l11 11 M17.5 6.5l-11 11";

    // ── pear-desktop One UI 테마(icons.ts)와 같은 아이콘 ──
    public static readonly (string, bool)[] HomeOutline = [("M4.2 10.3 11.2 4.7c.5-.4 1.1-.4 1.6 0l7 5.6c.3.2.4.6.4 1v7.9c0 .8-.6 1.4-1.4 1.4h-3.6v-5.1c0-.6-.4-1-1-1h-4.4c-.6 0-1 .4-1 1v5.1H5.2c-.8 0-1.4-.6-1.4-1.4v-7.9c0-.4.1-.8.4-1z", true)];
    public static readonly (string, bool)[] HomeFilled = [("M3.4 10.3 10.8 4.4c.7-.6 1.7-.6 2.4 0l7.4 5.9c.4.3.6.8.6 1.3v7.6c0 1.2-1 2.1-2.1 2.1h-3.7c-.4 0-.8-.4-.8-.8v-4.7c0-.6-.4-1-1-1h-3.2c-.6 0-1 .4-1 1v4.7c0 .4-.4.8-.8.8H4.9c-1.2 0-2.1-1-2.1-2.1v-7.6c0-.5.2-1 .6-1.3z", false)];
    public static readonly (string, bool)[] ExploreOutline = [("M12 3.6a8.4 8.4 0 1 1 0 16.8a8.4 8.4 0 1 1 0-16.8z", true), ("M15.4 8.6l-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z", false)];
    public static readonly (string, bool)[] ExploreFilled = [("F0 M12 2.8a9.2 9.2 0 1 0 0 18.4 9.2 9.2 0 0 0 0-18.4z M15.4 8.6l-1.8 4.6c-.1.2-.2.3-.4.4l-4.6 1.8 1.8-4.6c.1-.2.2-.3.4-.4z", false)];
    public static readonly (string, bool)[] LibraryOutline = [
        ("M5.2 4.4h1.8a1.2 1.2 0 0 1 1.2 1.2v12.8a1.2 1.2 0 0 1-1.2 1.2H5.2a1.2 1.2 0 0 1-1.2-1.2V5.6a1.2 1.2 0 0 1 1.2-1.2z", true),
        ("M11.2 4.4h1.8a1.2 1.2 0 0 1 1.2 1.2v12.8a1.2 1.2 0 0 1-1.2 1.2h-1.8a1.2 1.2 0 0 1-1.2-1.2V5.6a1.2 1.2 0 0 1 1.2-1.2z", true),
        ("M14.8 5.6l2.3-.7c.6-.2 1.3.2 1.4.8l3 12.3c.1.6-.2 1.2-.8 1.4l-2.3.7c-.6.2-1.3-.2-1.4-.8l-3-12.3c-.1-.6.2-1.2.8-1.4z", true)];
    public static readonly (string, bool)[] LibraryFilled = [
        ("M4.8 3.6h2.4a1.6 1.6 0 0 1 1.6 1.6v13.6a1.6 1.6 0 0 1-1.6 1.6H4.8a1.6 1.6 0 0 1-1.6-1.6V5.2a1.6 1.6 0 0 1 1.6-1.6z", false),
        ("M11.2 3.6h2.4a1.6 1.6 0 0 1 1.6 1.6v13.6a1.6 1.6 0 0 1-1.6 1.6h-2.4a1.6 1.6 0 0 1-1.6-1.6V5.2a1.6 1.6 0 0 1 1.6-1.6z", false),
        ("M13.6 5.4l2.6-.8c.8-.2 1.7.3 1.9 1.1l3 12.4c.2.8-.3 1.7-1.1 1.9l-2.6.8c-.8.2-1.7-.3-1.9-1.1l-3-12.4c-.2-.8.3-1.7 1.1-1.9z", false)];
    public static readonly (string, bool)[] Headphones = [
        ("M4.6 14.6v-2.4a7.4 7.4 0 0 1 14.8 0v2.4", true),
        ("M5.6 13.4H6.2a2 2 0 0 1 2 2V18.4a2 2 0 0 1-2 2H5.6a2 2 0 0 1-2-2V15.4a2 2 0 0 1 2-2z", false),
        ("M17.8 13.4H18.4a2 2 0 0 1 2 2V18.4a2 2 0 0 1-2 2H17.8a2 2 0 0 1-2-2V15.4a2 2 0 0 1 2-2z", false)];
    public static readonly (string, bool)[] Video = [
        ("M6.4 5.4H17.6a3 3 0 0 1 3 3V15.6a3 3 0 0 1-3 3H6.4a3 3 0 0 1-3-3V8.4a3 3 0 0 1 3-3z", true),
        ("M10.2 9.3v5.4c0 .6.6.9 1.1.6l4.2-2.7c.4-.3.4-.9 0-1.2l-4.2-2.7c-.5-.3-1.1 0-1.1.6z", false)];
    public static readonly (string, bool)[] Drawer = [("M4.5 7h15M4.5 12h15M4.5 17h15", true)];
    public static readonly (string, bool)[] SearchSesl = [("M10.625,5C7.518,5 5,7.519 5,10.625C5,13.731 7.518,16.25 10.625,16.25C13.732,16.25 16.25,13.731 16.25,10.625C16.25,7.519 13.732,5 10.625,5ZM3.5,10.625C3.5,6.69 6.69,3.5 10.625,3.5C14.56,3.5 17.75,6.69 17.75,10.625C17.75,12.322 17.156,13.881 16.166,15.105L20.705,19.645C20.998,19.937 20.998,20.412 20.705,20.705C20.412,20.998 19.937,20.998 19.645,20.705L15.105,16.165C13.881,17.156 12.322,17.75 10.625,17.75C6.69,17.75 3.5,14.56 3.5,10.625Z", false)];
    public static readonly (string, bool)[] PlaylistAdd = [("M4.5 6.5h12M4.5 11.5h12M4.5 16.5h7M17.5 13.5v7M14 17h7", true)];
    public static readonly (string, bool)[] Settings = [
        ("M12 9a3 3 0 1 1 0 6a3 3 0 1 1 0-6z", true),
        ("M10.3 3.6h3.4l.5 2.4 1.6.9 2.3-.8 1.7 2.9-1.8 1.7v1.8l1.8 1.7-1.7 2.9-2.3-.8-1.6.9-.5 2.4h-3.4l-.5-2.4-1.6-.9-2.3.8-1.7-2.9 1.8-1.7v-1.8L4.2 8.9l1.7-2.9 2.3.8 1.6-.9z", true)];
}
