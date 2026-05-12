using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace ViewGrid.Presentation.Localization;

/// <summary>
/// XAML 用ローカライズ拡張。 <c>{loc:Tr Menu_File}</c> の形で使用し、 <see cref="LocService.Instance"/>
/// の indexer に対する OneWay binding を組む。 言語切替時は LocService の "Item[]" 通知で
/// 自動再評価される。
/// </summary>
/// <remarks>
/// <para>
/// Avalonia 12 では <c>ReflectionBindingExtension</c> 戻しが trim/AOT 警告を出すが、
/// ViewGrid は AOT 配布対象外 (EF Core 非対応のため) なので reflection 経路でも安全。
/// 戻り値は <see cref="Binding"/> (= <see cref="BindingBase"/> 派生) で、 XAML マークアップが
/// 期待する任意の binding 受け口に流せる。
/// </para>
/// </remarks>
public sealed class TrExtension : MarkupExtension
{
    public string Key { get; set; } = string.Empty;

    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return new Binding($"[{Key}]")
        {
            Source = LocService.Instance,
            Mode = BindingMode.OneWay,
        };
    }
}
