using System.IO;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ViewGrid.Presentation.Views;

/// <summary>
/// THIRD-PARTY-NOTICES.md (EmbeddedResource: <c>ViewGrid.ThirdPartyNotices.md</c>) を読み込んで
/// アプリ内に表示するためのダイアログ。 メニュー 「ヘルプ → ライセンス情報...」 から開く。
/// </summary>
/// <remarks>
/// Markdown を rich text として整形描画はせず、 単なる monospace text として表示する。
/// 全文 ScrollViewer 内に流し込む方針 (リッチ表示は依存追加が割に合わない)。
/// </remarks>
public partial class LicenseNoticesWindow : Window
{
    private const string ResourceLogicalName = "ViewGrid.ThirdPartyNotices.md";

    public LicenseNoticesWindow()
    {
        InitializeComponent();
        NoticesText.Text = LoadNoticesText();
    }

    private static string LoadNoticesText()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceLogicalName);
        if (stream is null)
            return $"ライセンス情報ファイル ({ResourceLogicalName}) が見つかりませんでした。";
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
