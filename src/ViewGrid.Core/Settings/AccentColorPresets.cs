namespace ViewGrid.Core.Settings;

/// <summary>
/// アクセント色プリセット一覧。 Tailwind パレットから 6 色を選定。
/// Light テーマ用は X-500 を base に X-200..800 の 7 段階、 Dark テーマ用は X-400 を base に
/// X-100..700 の 7 段階で構成する (App.axaml の既存 Sky 定義と同じ規則)。
/// </summary>
public static class AccentColorPresets
{
    /// <summary>全プリセット。 設定ダイアログの色ドット ItemsControl がこれを ItemsSource にバインド。</summary>
    public static IReadOnlyList<AccentColorPreset> All { get; } =
    [
        // Sky: 落ち着いた青 (現行既定)
        new AccentColorPreset(
            Id: "Sky",
            DisplayName: "スカイ",
            SwatchColor: "#0EA5E9",
            Light: new AccentColorPalette(
                Color:  "#0EA5E9", Dark1:  "#0284C7", Dark2:  "#0369A1", Dark3:  "#075985",
                Light1: "#38BDF8", Light2: "#7DD3FC", Light3: "#BAE6FD"),
            Dark: new AccentColorPalette(
                Color:  "#38BDF8", Dark1:  "#0EA5E9", Dark2:  "#0284C7", Dark3:  "#0369A1",
                Light1: "#7DD3FC", Light2: "#BAE6FD", Light3: "#E0F2FE")),

        // Emerald: 生命感のある緑
        new AccentColorPreset(
            Id: "Emerald",
            DisplayName: "エメラルド",
            SwatchColor: "#10B981",
            Light: new AccentColorPalette(
                Color:  "#10B981", Dark1:  "#059669", Dark2:  "#047857", Dark3:  "#065F46",
                Light1: "#34D399", Light2: "#6EE7B7", Light3: "#A7F3D0"),
            Dark: new AccentColorPalette(
                Color:  "#34D399", Dark1:  "#10B981", Dark2:  "#059669", Dark3:  "#047857",
                Light1: "#6EE7B7", Light2: "#A7F3D0", Light3: "#D1FAE5")),

        // Rose: 強調の赤
        new AccentColorPreset(
            Id: "Rose",
            DisplayName: "ローズ",
            SwatchColor: "#F43F5E",
            Light: new AccentColorPalette(
                Color:  "#F43F5E", Dark1:  "#E11D48", Dark2:  "#BE123C", Dark3:  "#9F1239",
                Light1: "#FB7185", Light2: "#FDA4AF", Light3: "#FECDD3"),
            Dark: new AccentColorPalette(
                Color:  "#FB7185", Dark1:  "#F43F5E", Dark2:  "#E11D48", Dark3:  "#BE123C",
                Light1: "#FDA4AF", Light2: "#FECDD3", Light3: "#FFE4E6")),

        // Violet: 創造性のある紫
        new AccentColorPreset(
            Id: "Violet",
            DisplayName: "バイオレット",
            SwatchColor: "#8B5CF6",
            Light: new AccentColorPalette(
                Color:  "#8B5CF6", Dark1:  "#7C3AED", Dark2:  "#6D28D9", Dark3:  "#5B21B6",
                Light1: "#A78BFA", Light2: "#C4B5FD", Light3: "#DDD6FE"),
            Dark: new AccentColorPalette(
                Color:  "#A78BFA", Dark1:  "#8B5CF6", Dark2:  "#7C3AED", Dark3:  "#6D28D9",
                Light1: "#C4B5FD", Light2: "#DDD6FE", Light3: "#EDE9FE")),

        // Amber: 暖色のオレンジ系
        new AccentColorPreset(
            Id: "Amber",
            DisplayName: "アンバー",
            SwatchColor: "#F59E0B",
            Light: new AccentColorPalette(
                Color:  "#F59E0B", Dark1:  "#D97706", Dark2:  "#B45309", Dark3:  "#92400E",
                Light1: "#FBBF24", Light2: "#FCD34D", Light3: "#FDE68A"),
            Dark: new AccentColorPalette(
                Color:  "#FBBF24", Dark1:  "#F59E0B", Dark2:  "#D97706", Dark3:  "#B45309",
                Light1: "#FCD34D", Light2: "#FDE68A", Light3: "#FEF3C7")),

        // Slate: 落ち着いたニュートラル
        new AccentColorPreset(
            Id: "Slate",
            DisplayName: "スレート",
            SwatchColor: "#64748B",
            Light: new AccentColorPalette(
                Color:  "#64748B", Dark1:  "#475569", Dark2:  "#334155", Dark3:  "#1E293B",
                Light1: "#94A3B8", Light2: "#CBD5E1", Light3: "#E2E8F0"),
            Dark: new AccentColorPalette(
                Color:  "#94A3B8", Dark1:  "#64748B", Dark2:  "#475569", Dark3:  "#334155",
                Light1: "#CBD5E1", Light2: "#E2E8F0", Light3: "#F1F5F9")),
    ];

    /// <summary>既定プリセット (Sky)。 不明な ID 時の fallback。</summary>
    public static AccentColorPreset Default => All[0];

    /// <summary>ID で取得。 該当なし or null/empty なら <see cref="Default"/>。</summary>
    public static AccentColorPreset Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return Default;
        foreach (var p in All)
        {
            if (p.Id == id) return p;
        }
        return Default;
    }
}
