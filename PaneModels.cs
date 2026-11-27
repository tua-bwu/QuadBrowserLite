namespace QuadBrowserLite;

public record AiPreset(string Name, string Url)
{
    // URL を手入力するときの選択肢
    public const string CustomName = "Custom";

    public static AiPreset Custom { get; } = new(CustomName, string.Empty);
}

public static class PaneUrl
{
    // スキームが省略されていたら https:// を付ける
    public static string Normalize(string url)
    {
        var trimmed = url.Trim();
        if (trimmed.Length > 0 &&
            !trimmed.StartsWith("http://", System.StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("https://", System.StringComparison.OrdinalIgnoreCase) &&
            !trimmed.StartsWith("about:", System.StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + trimmed;
        }

        return trimmed;
    }
}

public record PaneConfig(string SlotLabel, string SelectedAiName, string Url);

// 4画面の位置の名前。並び順は .lst の pane0～pane3 に対応する
public static class PaneSlots
{
    public const string TopLeft = "左上";
    public const string TopRight = "右上";
    public const string BottomLeft = "左下";
    public const string BottomRight = "右下";

    public static readonly string[] All = { TopLeft, TopRight, BottomLeft, BottomRight };
}
