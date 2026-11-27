namespace QuadBrowserLite;

public record AiPreset(string Name, string Url)
{
    // URL を手入力するときの選択肢
    public const string CustomName = "Custom";

    public static AiPreset Custom { get; } = new(CustomName, string.Empty);
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
