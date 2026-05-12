using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ViewGrid.Application.Localization;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;

namespace ViewGrid.Presentation.Views;

public partial class CopyPropertiesView : UserControl
{
    /// <summary>
    /// CopyPropertiesView 内部の「保存 / リセット」ボタンバーを表示するか。
    /// 既定は <c>true</c>（準備タブなどスタンドアロン使用時）。配置タブの
    /// PlacementInspectorView に inline embed する場合は親側で「両方を一括保存」する
    /// ボタンを提供するため、こちらを <c>false</c> にして二重表示を避ける。
    /// </summary>
    public static readonly StyledProperty<bool> ShowSaveBarProperty =
        AvaloniaProperty.Register<CopyPropertiesView, bool>(nameof(ShowSaveBar), defaultValue: true);

    public bool ShowSaveBar
    {
        get => GetValue(ShowSaveBarProperty);
        set => SetValue(ShowSaveBarProperty, value);
    }

    public static readonly StyledProperty<bool> IsPropertiesTabSelectedProperty =
        AvaloniaProperty.Register<CopyPropertiesView, bool>(nameof(IsPropertiesTabSelected), defaultValue: true);

    public bool IsPropertiesTabSelected
    {
        get => GetValue(IsPropertiesTabSelectedProperty);
        set => SetValue(IsPropertiesTabSelectedProperty, value);
    }

    public static readonly StyledProperty<bool> IsCropTabSelectedProperty =
        AvaloniaProperty.Register<CopyPropertiesView, bool>(nameof(IsCropTabSelected), defaultValue: false);

    public bool IsCropTabSelected
    {
        get => GetValue(IsCropTabSelectedProperty);
        set => SetValue(IsCropTabSelectedProperty, value);
    }

    public static readonly StyledProperty<bool> IsRegionsTabSelectedProperty =
        AvaloniaProperty.Register<CopyPropertiesView, bool>(nameof(IsRegionsTabSelected), defaultValue: false);

    /// <summary>「保護領域」 タブが選択中か (Phase 1 PhotoBoard 限定機能)。</summary>
    public bool IsRegionsTabSelected
    {
        get => GetValue(IsRegionsTabSelectedProperty);
        set => SetValue(IsRegionsTabSelectedProperty, value);
    }

    private CopyPropertiesViewModel? _vm;

    private enum DragMode
    {
        None,
        CreateNew,
        Move,
        // 4 隅: 両軸リサイズ
        ResizeNW,
        ResizeNE,
        ResizeSW,
        ResizeSE,
        // 4 辺中央: 片軸のみリサイズ
        ResizeN,
        ResizeS,
        ResizeE,
        ResizeW,
    }

    private bool _isDragging;
    private DragMode _dragMode;
    private Point _dragStartPoint;
    private (int X, int Y, int W, int H) _dragStartRect;

    private const double HandleSize = 12;
    private const double HandleHitSlack = 4;

    public CopyPropertiesView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // 旧版は LayoutUpdated + Bounds 変化を監視して内部 ScrollViewer の MaxHeight を
        // 動的に決めていたが、子 View 内で実 viewport を推測する経路は計測タイミングが
        // 親レイアウトと乖離するため信頼できない（バーは出るがスクロール量 0 になる症状）。
        // スクロール責務は親 (PlacementInspectorView) が finite height の * 行で所有する
        // 設計に変更し、子側は自然な高さでコンテンツを縦に流すだけにした。
        // ManualCropImage のサイズ変更（コンテナリサイズや初回 Source 設定時）で再描画。
        // 旧版は ManualCropOverlay.LayoutUpdated を購読していたが、
        // Canvas.Children.Clear/Add が InvalidateMeasure → LayoutUpdated を再発火させて
        // 無限再帰 → StackOverflow でプロセス即終了する不具合があった。
        // PropertyChanged + BoundsProperty に絞ると Children 操作では再発火しない（Image 自体の
        // レイアウトは Children 操作で変わらないため）。
        // 加えて、Canvas (overlay) のサイズを Image.Bounds に同期する。
        // Canvas は HorizontalAlignment/VerticalAlignment=Center で Grid 内に配置しているため、
        // Width/Height を Image.Bounds.Width/Height に合わせれば Canvas (0,0) = Image control の左上
        // が保証され、overlay 矩形が Image 表示部分と完全一致する。
        ManualCropImage.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                ManualCropOverlay.Width = ManualCropImage.Bounds.Width;
                ManualCropOverlay.Height = ManualCropImage.Bounds.Height;
                UpdateOverlay();
            }
        };
        AutoCropPreviewImage.PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                AutoCropPreviewOverlay.Width = AutoCropPreviewImage.Bounds.Width;
                AutoCropPreviewOverlay.Height = AutoCropPreviewImage.Bounds.Height;
                UpdateAutoCropPreviewOverlay();
            }
        };
    }

    private void OnPropertiesTabClicked(object? sender, RoutedEventArgs e)
    {
        IsPropertiesTabSelected = true;
        IsCropTabSelected = false;
        IsRegionsTabSelected = false;
    }

    private void OnCropTabClicked(object? sender, RoutedEventArgs e)
    {
        IsPropertiesTabSelected = false;
        IsCropTabSelected = true;
        IsRegionsTabSelected = false;
    }

    private void OnRegionsTabClicked(object? sender, RoutedEventArgs e)
    {
        IsPropertiesTabSelected = false;
        IsCropTabSelected = false;
        IsRegionsTabSelected = true;
    }

    /// <summary>
    /// 保護領域 「編集...」 ボタンのハンドラ。 ManualCropEditorWindow を rect editor として
    /// 流用 (Title だけ書き換え) し、 OK で閉じられたら結果を <see cref="CopyPropertiesViewModel.UpdateRegionRect"/>
    /// に渡して反映する。 SourceImagePath / SourceWidth / SourceHeight が必要なので、 未取得
    /// (取り込み済みでない論理コピー) では StatusMessage で告知して開かない。
    /// async void は Avalonia Click ハンドラの慣例 (既存 <see cref="OnOpenDetailEditorClicked"/> と同パターン)。
    /// </summary>
    private async void OnEditRegionClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (_vm.SelectedRegion is not { } region) return;
        if (string.IsNullOrEmpty(_vm.SourceImagePath))
        {
            _vm.StatusMessage = "保護領域の編集には元画像が必要です。";
            return;
        }
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var window = OpenRegionEditorWindow(region.Rect, _vm.SourceImagePath, _vm.SourceWidth, _vm.SourceHeight);
        if (window is null)
        {
            // 画像読み込み失敗。 ユーザーに状況を告げて静かに諦める (旧版は黙っていたが、
            // ゲート指摘で例外握りつぶしを明示メッセージ化)。
            _vm.StatusMessage = "画像の読み込みに失敗したため、 保護領域エディタを開けませんでした。";
            return;
        }

        await window.ShowDialog(owner);

        if (window.GetResult() is { } r)
        {
            var newRect = PixelRectToRegionFraction(r, _vm.SourceWidth, _vm.SourceHeight);
            _vm.UpdateRegionRect(region, newRect);
        }
    }

    /// <summary>
    /// region 編集用の <see cref="ManualCropEditorWindow"/> を初期化して返す。 画像読み込み失敗時は
    /// <c>null</c>。 例外は内部で吸収する (caller がメッセージ表示する)。
    /// </summary>
    private static ManualCropEditorWindow? OpenRegionEditorWindow(
        RegionRectFraction initialRect, string imagePath, int sourceWidth, int sourceHeight)
    {
        var (initX, initY, initW, initH) = RegionFractionToPixelRect(initialRect, sourceWidth, sourceHeight);
        var window = new ManualCropEditorWindow { Title = LocAccessor.Current["CopyProps_Regions_EditorTitle"] };
        try
        {
            window.Initialize(imagePath, sourceWidth, sourceHeight, initX, initY, initW, initH);
            return window;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>0–1 fraction → ピクセル整数 (rect editor の入出力単位)。</summary>
    private static (int X, int Y, int W, int H) RegionFractionToPixelRect(
        RegionRectFraction rect, int sourceWidth, int sourceHeight)
        => (
            (int)Math.Round(rect.X * sourceWidth),
            (int)Math.Round(rect.Y * sourceHeight),
            (int)Math.Round(rect.Width * sourceWidth),
            (int)Math.Round(rect.Height * sourceHeight));

    /// <summary>ピクセル整数 → 0–1 fraction (永続化単位)。</summary>
    private static RegionRectFraction PixelRectToRegionFraction(
        (int X, int Y, int W, int H) rect, int sourceWidth, int sourceHeight)
        => new(
            (double)rect.X / sourceWidth,
            (double)rect.Y / sourceHeight,
            (double)rect.W / sourceWidth,
            (double)rect.H / sourceHeight);

    /// <summary>
    /// AutoCrop の「画像クリックピッカー」用ハンドラ。サムネ <see cref="Image"/> 上の
    /// クリック座標を bitmap pixel 座標に換算して <see cref="CopyPropertiesViewModel.PickColorFromThumbnailAsync"/>
    /// を呼ぶ。<see cref="Stretch.Uniform"/> でアスペクト維持表示しているため、表示倍率と
    /// パディング（左右 or 上下の余白）を計算してクリック点を bitmap 座標に戻す。
    /// </summary>
    private async void OnColorPickerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image image) return;
        if (DataContext is not CopyPropertiesViewModel vm) return;
        if (image.Source is not Bitmap bitmap) return;
        if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed) return;
        // Custom プリセット時のみ色採取が意味を持つ（プリセット時はクリックしても何も起きない）。
        if (!vm.IsAutoCropCustom) return;

        var pos = e.GetPosition(image);
        var imgW = image.Bounds.Width;
        var imgH = image.Bounds.Height;
        var bmpW = bitmap.PixelSize.Width;
        var bmpH = bitmap.PixelSize.Height;
        if (imgW <= 0 || imgH <= 0 || bmpW <= 0 || bmpH <= 0) return;

        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;

        var localX = pos.X - padX;
        var localY = pos.Y - padY;
        if (localX < 0 || localX >= displayW || localY < 0 || localY >= displayH) return;

        var px = (int)(localX / scale);
        var py = (int)(localY / scale);

        try
        {
            await vm.PickColorFromThumbnailAsync(px, py, bmpW, bmpH);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす
        }
    }

    /// <summary>
    /// 保護領域の 「画像クリックピッカー」 用ハンドラ。 <see cref="OnColorPickerPointerPressed"/> と
    /// 同じ座標換算を行い、 採取した色を <see cref="CopyPropertiesViewModel.PickColorForSelectedRegionAsync"/>
    /// に渡す。 SelectedRegion=null / Source 未設定時は黙ってスキップ。
    /// FillMode は採取後に自動的に Custom に切り替わる仕様 (VM 側で実施)。
    /// </summary>
    private async void OnRegionColorPickerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Image image) return;
        if (DataContext is not CopyPropertiesViewModel vm) return;
        if (vm.SelectedRegion is null) return;
        if (image.Source is not Bitmap bitmap) return;
        if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(image);
        var imgW = image.Bounds.Width;
        var imgH = image.Bounds.Height;
        var bmpW = bitmap.PixelSize.Width;
        var bmpH = bitmap.PixelSize.Height;
        if (imgW <= 0 || imgH <= 0 || bmpW <= 0 || bmpH <= 0) return;

        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;

        var localX = pos.X - padX;
        var localY = pos.Y - padY;
        if (localX < 0 || localX >= displayW || localY < 0 || localY >= displayH) return;

        var px = (int)(localX / scale);
        var py = (int)(localY / scale);

        try
        {
            await vm.PickColorForSelectedRegionAsync(px, py, bmpW, bmpH);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = DataContext as CopyPropertiesViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        UpdateOverlay();
        UpdateAutoCropPreviewOverlay();
    }

    /// <summary>
    /// AutoCrop プレビューサムネ（<see cref="Stretch.Uniform"/> 表示）の表示倍率と
    /// パディングを計算する。<c>null</c> はサムネ未ロード等で計算不能を意味する。
    /// </summary>
    private (double DisplayW, double DisplayH, double Scale, double PadX, double PadY)?
        GetAutoCropPreviewMetrics()
    {
        if (AutoCropPreviewImage.Source is not Bitmap bmp) return null;
        var imgW = AutoCropPreviewImage.Bounds.Width;
        var imgH = AutoCropPreviewImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return null;

        var bmpW = (double)bmp.PixelSize.Width;
        var bmpH = (double)bmp.PixelSize.Height;
        if (bmpW <= 0 || bmpH <= 0) return null;

        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;
        return (displayW, displayH, scale, padX, padY);
    }

    /// <summary>
    /// AutoCrop プレビュー用 Canvas overlay を再描画する。
    /// VM の <see cref="CopyPropertiesViewModel.AutoCropPreviewFraction"/> から bbox を計算し、
    /// 赤枠 + 4 領域マットを描画する。
    /// AutoCrop OFF / preview null / metrics 取得失敗のいずれかなら overlay 全消去。
    /// </summary>
    private void UpdateAutoCropPreviewOverlay()
    {
        if (AutoCropPreviewOverlay is null) return;
        AutoCropPreviewOverlay.Children.Clear();

        if (_vm is null || !_vm.AutoCropEnabled) return;
        if (_vm.AutoCropPreviewFraction is not { } fraction) return;

        var metrics = GetAutoCropPreviewMetrics();
        if (metrics is null) return;
        var (displayW, displayH, _, padX, padY) = metrics.Value;

        // fraction (0-1) は元画像比率だが、サムネ Stretch.Uniform 表示でも比率は同じ。
        // displayW/H に乗算するだけで overlay 座標 (Canvas 内座標) になる。
        var rectX = padX + fraction.X * displayW;
        var rectY = padY + fraction.Y * displayH;
        var rectW = fraction.Width * displayW;
        var rectH = fraction.Height * displayH;
        if (rectW <= 0 || rectH <= 0) return;

        // マット 4 領域: bbox 外側を半透明黒で覆って、クロップで残る範囲を強調する。
        var mattBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        AddAutoCropMatt(padX, padY, displayW, rectY - padY, mattBrush);                          // 上
        AddAutoCropMatt(padX, rectY + rectH, displayW, padY + displayH - (rectY + rectH), mattBrush); // 下
        AddAutoCropMatt(padX, rectY, rectX - padX, rectH, mattBrush);                            // 左
        AddAutoCropMatt(rectX + rectW, rectY, padX + displayW - (rectX + rectW), rectH, mattBrush); // 右

        // bbox 赤枠（クロップで残る範囲を視覚化）
        var border = new Rectangle
        {
            Width = rectW,
            Height = rectH,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x33, 0x33)),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(border, rectX);
        Canvas.SetTop(border, rectY);
        AutoCropPreviewOverlay.Children.Add(border);
    }

    private void AddAutoCropMatt(double x, double y, double w, double h, IBrush brush)
    {
        if (w <= 0 || h <= 0) return;
        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        AutoCropPreviewOverlay.Children.Add(rect);
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CopyPropertiesViewModel.ManualCropPixelX)
            or nameof(CopyPropertiesViewModel.ManualCropPixelY)
            or nameof(CopyPropertiesViewModel.ManualCropPixelWidth)
            or nameof(CopyPropertiesViewModel.ManualCropPixelHeight)
            or nameof(CopyPropertiesViewModel.ManualCropEnabled)
            or nameof(CopyPropertiesViewModel.SourceWidth)
            or nameof(CopyPropertiesViewModel.SourceHeight)
            or nameof(CopyPropertiesViewModel.ThumbnailPath))
        {
            UpdateOverlay();
        }

        if (e.PropertyName is nameof(CopyPropertiesViewModel.AutoCropPreviewFraction)
            or nameof(CopyPropertiesViewModel.AutoCropEnabled)
            or nameof(CopyPropertiesViewModel.ThumbnailPath))
        {
            UpdateAutoCropPreviewOverlay();
        }
    }

    /// <summary>
    /// 元画像ピクセル空間（VM 値）→ オーバーレイ座標空間（サムネ表示領域）の換算。
    /// サムネは Stretch.Uniform でアスペクト維持表示されているため、表示倍率とパディングを考慮。
    /// </summary>
    private (double X, double Y, double W, double H, double Scale, double PadX, double PadY)?
        GetThumbnailDisplayMetrics()
    {
        if (_vm is null) return null;
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return null;
        if (ManualCropImage.Source is not Bitmap bmp) return null;

        var imgW = ManualCropImage.Bounds.Width;
        var imgH = ManualCropImage.Bounds.Height;
        if (imgW <= 0 || imgH <= 0) return null;

        // サムネ画像（bmp）と元画像（_vm.SourceWidth/Height）はサイズが違う場合があるが、
        // 比率は一致するはず。Stretch.Uniform 表示の倍率はサムネ Bitmap 基準で計算する。
        var bmpW = (double)bmp.PixelSize.Width;
        var bmpH = (double)bmp.PixelSize.Height;
        var scale = Math.Min(imgW / bmpW, imgH / bmpH);
        var displayW = bmpW * scale;
        var displayH = bmpH * scale;
        var padX = (imgW - displayW) / 2.0;
        var padY = (imgH - displayH) / 2.0;

        // 元画像ピクセル → オーバーレイ座標の倍率（サムネ → オーバーレイの倍率を経由）
        // 元画像ピクセル × (bmpW / SourceWidth) × scale = オーバーレイ座標
        var srcToOverlayScale = (bmpW / _vm.SourceWidth) * scale;

        return (0, 0, displayW, displayH, srcToOverlayScale, padX, padY);
    }

    /// <summary>VM の ManualCrop ピクセル値からオーバーレイ座標 (x, y, w, h) を返す。</summary>
    private (double X, double Y, double W, double H)? ComputeOverlayRect()
    {
        if (_vm is null) return null;
        var m = GetThumbnailDisplayMetrics();
        if (m is null) return null;
        var (_, _, _, _, scale, padX, padY) = m.Value;
        // null（入力中で空欄）は 0 として扱う (int? → double 暗黙昇格)
        double mx = _vm.ManualCropPixelX ?? 0;
        double my = _vm.ManualCropPixelY ?? 0;
        double mw = _vm.ManualCropPixelWidth ?? 0;
        double mh = _vm.ManualCropPixelHeight ?? 0;
        var x = padX + mx * scale;
        var y = padY + my * scale;
        var w = mw * scale;
        var h = mh * scale;
        return (x, y, w, h);
    }

    /// <summary>オーバーレイ座標 (px, py) を元画像ピクセル座標に逆変換。</summary>
    private (double SrcX, double SrcY)? OverlayToSource(double overlayX, double overlayY)
    {
        var m = GetThumbnailDisplayMetrics();
        if (m is null) return null;
        var (_, _, _, _, scale, padX, padY) = m.Value;
        if (scale <= 0) return null;
        return ((overlayX - padX) / scale, (overlayY - padY) / scale);
    }

    /// <summary>
    /// マット 4 領域 + 矩形 + 4 隅ハンドル + 4 辺中央ハンドルを Canvas 上に再描画する。
    /// VM 値変更時 / Image レイアウト変更時に呼ばれる。
    /// LayoutUpdated は再帰の原因になるため購読しない（Phase 5 のクラッシュ修正済み）。
    /// </summary>
    private void UpdateOverlay()
    {
        if (ManualCropOverlay is null) return;
        ManualCropOverlay.Children.Clear();

        if (_vm is null || !_vm.ManualCropEnabled || !_vm.IsManualCropDefined) return;

        var metrics = GetThumbnailDisplayMetrics();
        if (metrics is null) return;
        var (_, _, displayW, displayH, _, padX, padY) = metrics.Value;

        var rect = ComputeOverlayRect();
        if (rect is not { } r) return;
        if (r.W <= 0 || r.H <= 0) return;

        // マット 4 領域: サムネ表示領域 (padX, padY, displayW, displayH) のうち、
        // 矩形外側を半透明黒で覆って選択範囲を強調する。座標系はオーバーレイ Canvas。
        var mattBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x00, 0x00, 0x00));
        AddMatt(padX, padY, displayW, r.Y - padY, mattBrush);                          // 上
        AddMatt(padX, r.Y + r.H, displayW, padY + displayH - (r.Y + r.H), mattBrush); // 下
        AddMatt(padX, r.Y, r.X - padX, r.H, mattBrush);                               // 左
        AddMatt(r.X + r.W, r.Y, padX + displayW - (r.X + r.W), r.H, mattBrush);       // 右

        // 矩形枠（マットで内部は明るく見える形）
        var border = new Rectangle
        {
            Width = r.W,
            Height = r.H,
            Stroke = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        ManualCropOverlay.Children.Add(border);

        // 4 隅ハンドル（両軸リサイズ）
        AddHandle(r.X, r.Y);
        AddHandle(r.X + r.W, r.Y);
        AddHandle(r.X, r.Y + r.H);
        AddHandle(r.X + r.W, r.Y + r.H);

        // 4 辺中央ハンドル（片軸リサイズ）
        AddHandle(r.X + r.W / 2, r.Y);
        AddHandle(r.X + r.W / 2, r.Y + r.H);
        AddHandle(r.X, r.Y + r.H / 2);
        AddHandle(r.X + r.W, r.Y + r.H / 2);
    }

    private void AddMatt(double x, double y, double w, double h, IBrush brush)
    {
        if (w <= 0 || h <= 0) return;
        var rect = new Rectangle
        {
            Width = w,
            Height = h,
            Fill = brush,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        ManualCropOverlay.Children.Add(rect);
    }

    private void AddHandle(double cx, double cy)
    {
        var h = new Rectangle
        {
            Width = HandleSize,
            Height = HandleSize,
            Fill = new SolidColorBrush(Color.FromArgb(0xFF, 0x33, 0x99, 0xFF)),
            Stroke = Brushes.White,
            StrokeThickness = 1,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(h, cx - HandleSize / 2);
        Canvas.SetTop(h, cy - HandleSize / 2);
        ManualCropOverlay.Children.Add(h);
    }

    /// <summary>クリック点が 4 隅 / 4 辺中央のハンドルに当たれば、対応するリサイズモードを返す。
    /// 4 隅優先（隅と辺が重なる位置では隅のリサイズを優先、両軸変えられる方が直感的）。</summary>
    private static DragMode HitTestHandle(Point p, (double X, double Y, double W, double H) r)
    {
        var slack = HandleSize / 2 + HandleHitSlack;

        // 4 隅: 両軸リサイズ
        if (Distance(p, new Point(r.X, r.Y)) <= slack) return DragMode.ResizeNW;
        if (Distance(p, new Point(r.X + r.W, r.Y)) <= slack) return DragMode.ResizeNE;
        if (Distance(p, new Point(r.X, r.Y + r.H)) <= slack) return DragMode.ResizeSW;
        if (Distance(p, new Point(r.X + r.W, r.Y + r.H)) <= slack) return DragMode.ResizeSE;

        // 4 辺中央: 片軸リサイズ
        if (Distance(p, new Point(r.X + r.W / 2, r.Y)) <= slack) return DragMode.ResizeN;
        if (Distance(p, new Point(r.X + r.W / 2, r.Y + r.H)) <= slack) return DragMode.ResizeS;
        if (Distance(p, new Point(r.X, r.Y + r.H / 2)) <= slack) return DragMode.ResizeW;
        if (Distance(p, new Point(r.X + r.W, r.Y + r.H / 2)) <= slack) return DragMode.ResizeE;

        return DragMode.None;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsInsideRect(Point p, (double X, double Y, double W, double H) r)
        => p.X >= r.X && p.X <= r.X + r.W && p.Y >= r.Y && p.Y <= r.Y + r.H;

    private void OnManualCropOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null || !_vm.ManualCropEnabled) return;
        if (!e.GetCurrentPoint(ManualCropOverlay).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(ManualCropOverlay);

        // 既存矩形あり → ハンドル / 内部 / 外部の判定
        if (_vm.IsManualCropDefined && ComputeOverlayRect() is { } existing)
        {
            var hit = HitTestHandle(pos, existing);
            if (hit != DragMode.None)
            {
                _dragMode = hit;
            }
            else if (IsInsideRect(pos, existing))
            {
                _dragMode = DragMode.Move;
            }
            else
            {
                _dragMode = DragMode.CreateNew;
            }
        }
        else
        {
            _dragMode = DragMode.CreateNew;
        }

        _isDragging = true;
        _dragStartPoint = pos;
        _dragStartRect = (_vm.ManualCropPixelX ?? 0, _vm.ManualCropPixelY ?? 0,
                          _vm.ManualCropPixelWidth ?? 0, _vm.ManualCropPixelHeight ?? 0);
        e.Pointer.Capture(ManualCropOverlay);
        e.Handled = true;
    }

    private void OnManualCropOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isDragging || _vm is null) return;
        var pos = e.GetPosition(ManualCropOverlay);
        ApplyDragUpdate(pos);
    }

    private void OnManualCropOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        _dragMode = DragMode.None;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    /// <summary>
    /// 「詳細編集（拡大表示）」ボタン。<see cref="ManualCropEditorWindow"/> をモーダル表示し、
    /// OK で閉じられたら結果を VM の ManualCropPixel* に書き戻す（IsDirty が立つ → 既存 Save フロー）。
    /// SourceImagePath / SourceWidth / SourceHeight が必要なので、未取得（取り込み済みでない論理コピー）
    /// では IsEnabled=false でガード（XAML 側で HasThumbnail にバインド済み）。
    /// </summary>
    private async void OnOpenDetailEditorClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (!_vm.ManualCropEnabled) return;
        if (string.IsNullOrEmpty(_vm.SourceImagePath)) return;
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var window = new ManualCropEditorWindow();
        try
        {
            window.Initialize(
                _vm.SourceImagePath,
                _vm.SourceWidth, _vm.SourceHeight,
                _vm.ManualCropPixelX ?? 0, _vm.ManualCropPixelY ?? 0,
                _vm.ManualCropPixelWidth ?? 0, _vm.ManualCropPixelHeight ?? 0);
        }
        catch
        {
            // 画像読み込み失敗等。ユーザー操作起点なので静かに諦める（StatusMessage 等で表示できるが省略）。
            return;
        }

        await window.ShowDialog(owner);

        if (window.GetResult() is { } r)
        {
            _vm.ManualCropPixelX = r.X;
            _vm.ManualCropPixelY = r.Y;
            _vm.ManualCropPixelWidth = r.W;
            _vm.ManualCropPixelHeight = r.H;
        }
    }

    /// <summary>
    /// PointerMoved 中の VM 値更新。各モードに応じてオーバーレイ座標 → 元画像ピクセルに換算し、
    /// VM の ManualCropPixelX/Y/W/H をクランプ付きで更新する。
    /// </summary>
    private void ApplyDragUpdate(Point pos)
    {
        if (_vm is null) return;
        if (_vm.SourceWidth <= 0 || _vm.SourceHeight <= 0) return;

        var srcStart = OverlayToSource(_dragStartPoint.X, _dragStartPoint.Y);
        var srcCurrent = OverlayToSource(pos.X, pos.Y);
        if (srcStart is null || srcCurrent is null) return;

        var sw = _vm.SourceWidth;
        var sh = _vm.SourceHeight;

        switch (_dragMode)
        {
            case DragMode.CreateNew:
            {
                var x1 = Math.Clamp(srcStart.Value.SrcX, 0, sw);
                var y1 = Math.Clamp(srcStart.Value.SrcY, 0, sh);
                var x2 = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);
                var y2 = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);
                _vm.ManualCropPixelX = (int)Math.Round(Math.Min(x1, x2));
                _vm.ManualCropPixelY = (int)Math.Round(Math.Min(y1, y2));
                _vm.ManualCropPixelWidth = (int)Math.Round(Math.Abs(x2 - x1));
                _vm.ManualCropPixelHeight = (int)Math.Round(Math.Abs(y2 - y1));
                break;
            }
            case DragMode.Move:
            {
                var dx = srcCurrent.Value.SrcX - srcStart.Value.SrcX;
                var dy = srcCurrent.Value.SrcY - srcStart.Value.SrcY;
                var newX = Math.Clamp(_dragStartRect.X + dx, 0, sw - _dragStartRect.W);
                var newY = Math.Clamp(_dragStartRect.Y + dy, 0, sh - _dragStartRect.H);
                _vm.ManualCropPixelX = (int)Math.Round(newX);
                _vm.ManualCropPixelY = (int)Math.Round(newY);
                break;
            }
            case DragMode.ResizeNW:
            case DragMode.ResizeNE:
            case DragMode.ResizeSW:
            case DragMode.ResizeSE:
            {
                // 4 隅: 両軸リサイズ。対角の隅を fixed として、ドラッグ点との bbox を作る。
                var startX = _dragStartRect.X;
                var startY = _dragStartRect.Y;
                var startRight = _dragStartRect.X + _dragStartRect.W;
                var startBottom = _dragStartRect.Y + _dragStartRect.H;

                var fixedX = (_dragMode is DragMode.ResizeNE or DragMode.ResizeSE) ? startX : startRight;
                var fixedY = (_dragMode is DragMode.ResizeSW or DragMode.ResizeSE) ? startY : startBottom;
                var moveX = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);
                var moveY = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);

                _vm.ManualCropPixelX = (int)Math.Round(Math.Min(fixedX, moveX));
                _vm.ManualCropPixelY = (int)Math.Round(Math.Min(fixedY, moveY));
                _vm.ManualCropPixelWidth = (int)Math.Round(Math.Abs(moveX - fixedX));
                _vm.ManualCropPixelHeight = (int)Math.Round(Math.Abs(moveY - fixedY));
                break;
            }
            case DragMode.ResizeN:
            case DragMode.ResizeS:
            {
                // 上辺/下辺: Y 軸のみリサイズ。X / W は startRect そのまま、対辺を fixed Y として bbox 作る。
                var startY = _dragStartRect.Y;
                var startBottom = _dragStartRect.Y + _dragStartRect.H;
                var fixedY = _dragMode is DragMode.ResizeN ? startBottom : startY;
                var moveY = Math.Clamp(srcCurrent.Value.SrcY, 0, sh);

                _vm.ManualCropPixelX = _dragStartRect.X;
                _vm.ManualCropPixelWidth = _dragStartRect.W;
                _vm.ManualCropPixelY = (int)Math.Round(Math.Min(fixedY, moveY));
                _vm.ManualCropPixelHeight = (int)Math.Round(Math.Abs(moveY - fixedY));
                break;
            }
            case DragMode.ResizeE:
            case DragMode.ResizeW:
            {
                // 右辺/左辺: X 軸のみリサイズ。
                var startX = _dragStartRect.X;
                var startRight = _dragStartRect.X + _dragStartRect.W;
                var fixedX = _dragMode is DragMode.ResizeW ? startRight : startX;
                var moveX = Math.Clamp(srcCurrent.Value.SrcX, 0, sw);

                _vm.ManualCropPixelY = _dragStartRect.Y;
                _vm.ManualCropPixelHeight = _dragStartRect.H;
                _vm.ManualCropPixelX = (int)Math.Round(Math.Min(fixedX, moveX));
                _vm.ManualCropPixelWidth = (int)Math.Round(Math.Abs(moveX - fixedX));
                break;
            }
        }
    }

    /// <summary>
    /// 非 nullable int にバインドした NumericUpDown のテキストを空にすると Avalonia の Value が
    /// null になり「Could not convert '(null)' to System.Int32」のバインディングエラーが赤字で
    /// 表示され、レイアウトが崩れる。フォーカスが外れたタイミングで空のまま残っていれば
    /// <see cref="NumericUpDown.Minimum"/> に強制的に戻すことで二度と null が source に書かれない
    /// 状態にする（連続入力中の一時的 null は許容）。
    /// </summary>
    private void OnNumericUpDownLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is NumericUpDown nud && nud.Value is null)
            nud.Value = nud.Minimum;
    }
}
