namespace ViewGrid.Application.ViewModels;

/// <summary>
/// トリミングモードの tri-state（OFF / 自動 / 手動）。CopyPropertiesView の「OFF/自動/手動」ラジオ 3 択を
/// <see cref="CopyPropertiesViewModel.AutoCropEnabled"/> / <see cref="CopyPropertiesViewModel.ManualCropEnabled"/>
/// の 2 つの bool に直接 two-way バインドすると、RadioButton グループの排他制御が選択解除側へ
/// <c>false</c> を書き戻し、DataContext 切替時に AutoCrop/ManualCrop が意図せず OFF 化される
/// （その後の保存で永続化済みトリミングが消える）データ消失が起きる。これを防ぐため、ラジオは
/// 単一の <see cref="CopyPropertiesViewModel.CropMode"/> プロパティへ enum⇔bool コンバータ経由で
/// バインドし、未チェック側の書き戻しはコンバータが握りつぶす。
/// </summary>
public enum CropMode
{
    /// <summary>トリミング無効。</summary>
    Off = 0,

    /// <summary>単色余白の自動トリミング (AutoCrop)。</summary>
    Auto = 1,

    /// <summary>任意矩形トリミング (ManualCrop)。</summary>
    Manual = 2,
}
