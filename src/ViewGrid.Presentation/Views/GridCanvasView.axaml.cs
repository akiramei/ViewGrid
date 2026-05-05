using System.Collections.Immutable;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using ViewGrid.Application.UseCases;
using ViewGrid.Application.ViewModels;
using ViewGrid.Core.Entities;
using ViewGrid.Core.Geometry;

namespace ViewGrid.Presentation.Views;

public partial class GridCanvasView : UserControl
{
    private const double DragThreshold = 2.0;
    private const string CopyPrefix = "copy:";
    private const string PlacementPrefix = "placement:";

    private GridWorkspaceViewModel? _vm;

    private Point? _placementPressOrigin;
    private PlacementItemViewModel? _placementPressItem;
    private PointerPressedEventArgs? _placementPressEvent;
    private Border? _placementPressBorder;

    // Shift+ドラッグでの PixelOffset 微調整モード状態
    private bool _pixelOffsetDragging;
    private Point _pixelOffsetStart;
    private int _pixelOffsetStartX;
    private int _pixelOffsetStartY;
    private PlacementItemViewModel? _pixelOffsetTarget;
    private Border? _pixelOffsetBorder;

    // セル位置 → セル Border 参照（範囲ハイライトの一括クリアに使う）
    private readonly Dictionary<CellPosition, Border> _cellBorders = new();

    // 配置済み Border → 対応する placement VM。SizeChanged 時に PixelOffset の換算を再適用する。
    private readonly Dictionary<Border, PlacementItemViewModel> _placementBorders = new();

    // ---------- 占有セル → 配置 VM の高速逆引きマップ ----------
    // DragOver は高頻度で発火し、AnalyzeHoverRangeRaw が NxM のホバー範囲セル
    // それぞれに対して FindOccupantPlacement を呼ぶ。線形走査だと配置数 N と
    // ホバー範囲 M で O(N×M) になるため、Dictionary 索引で O(1) 化する。
    // Rebuild 時に全構築、Position/OccupySize 変更時に該当 placement の登録を
    // 差し替える（_placementBorders と同じ寿命）。
    private readonly Dictionary<CellPosition, PlacementItemViewModel> _occupantMap = new();

    private void AddPlacementToOccupantMap(PlacementItemViewModel placement)
    {
        var w = Math.Max(1, placement.OccupyWidth);
        var h = Math.Max(1, placement.OccupyHeight);
        for (var dy = 0; dy < h; dy++)
            for (var dx = 0; dx < w; dx++)
                _occupantMap[new CellPosition(placement.GridX + dx, placement.GridY + dy)] = placement;
    }

    private void RemovePlacementFromOccupantMap(PlacementItemViewModel placement)
    {
        // 値 == placement のエントリを線形検索して削除（dict 全走査だが、
        // Move/Swap/OccupySize 編集の頻度は低いので問題なし）。
        var keysToRemove = new List<CellPosition>();
        foreach (var (key, value) in _occupantMap)
        {
            if (value == placement) keysToRemove.Add(key);
        }
        foreach (var key in keysToRemove) _occupantMap.Remove(key);
    }

    // ---------- 焼き込み済みサムネ Bitmap キャッシュ（LoadAndPreRotateBitmap） ----------
    // Rebuild の都度 disk read + Skia decode/encode が走るのを避けるため、
    // (ThumbnailPath, Rotation, FlipX, FlipY, Crop) でキャッシュ。LRU 上限 64 件、
    // evict 時に Bitmap.Dispose() で確実にメモリ解放する。
    private const int BitmapCacheCapacity = 64;

    private readonly LinkedList<KeyValuePair<BitmapCacheKey, Bitmap>> _bitmapCacheLru = new();
    private readonly Dictionary<BitmapCacheKey, LinkedListNode<KeyValuePair<BitmapCacheKey, Bitmap>>> _bitmapCacheIndex = new();

    private readonly record struct BitmapCacheKey(
        string Path,
        ViewGrid.Core.Entities.Rotation Rotation,
        bool FlipX,
        bool FlipY,
        double CropX,
        double CropY,
        double CropW,
        double CropH);

    private Bitmap GetOrCreatePreRotatedBitmap(
        string thumbnailPath,
        ViewGrid.Core.Entities.Rotation rotation,
        bool flipX, bool flipY,
        ViewGrid.Core.Entities.CropFraction? cropFraction)
    {
        // null の Crop は (0,0,1,1) と同じ「変換なし」扱いで正規化（キーが一意になる）。
        var c = cropFraction ?? new ViewGrid.Core.Entities.CropFraction(0, 0, 1, 1);
        var key = new BitmapCacheKey(thumbnailPath, rotation, flipX, flipY, c.X, c.Y, c.Width, c.Height);

        if (_bitmapCacheIndex.TryGetValue(key, out var node))
        {
            // LRU 末尾へ昇格
            _bitmapCacheLru.Remove(node);
            _bitmapCacheLru.AddLast(node);
            return node.Value.Value;
        }

        var bitmap = LoadAndPreRotateBitmap(thumbnailPath, rotation, flipX, flipY, cropFraction);
        var newNode = _bitmapCacheLru.AddLast(new KeyValuePair<BitmapCacheKey, Bitmap>(key, bitmap));
        _bitmapCacheIndex[key] = newNode;

        // 上限超え分は最古を evict + Dispose
        while (_bitmapCacheLru.Count > BitmapCacheCapacity)
        {
            var oldest = _bitmapCacheLru.First!;
            _bitmapCacheLru.RemoveFirst();
            _bitmapCacheIndex.Remove(oldest.Value.Key);
            oldest.Value.Value.Dispose();
        }
        return bitmap;
    }

    public GridCanvasView()
    {
        InitializeComponent();
        this.GetObservable(DataContextProperty).Subscribe(new AnonymousObserver<object?>(OnDataContextChanged));
        CanvasGrid.SizeChanged += OnCanvasGridSizeChanged;
        // ドラッグ中の PointerMoved/Released は BoundaryOverlay 全体で受ける。
        // Capture は handle ではなく BoundaryOverlay 側に張り直す（OnBoundaryPointerPressed 内）。
        BoundaryOverlay.PointerMoved += OnOverlayPointerMoved;
        BoundaryOverlay.PointerReleased += OnOverlayPointerReleased;
        BoundaryOverlay.PointerCaptureLost += OnOverlayPointerCaptureLost;
        // Ctrl+Arrow（および Ctrl+Shift+Arrow）でアクティブ配置の PixelOffset を 1px / 10px 微調整。
        // UserControl 自体が Focusable のとき、placement クリック後にここでキーを受け取る。
        KeyDown += OnUserControlKeyDown;
    }

    private void OnCanvasGridSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // 表示サイズが変わったら全配置の PixelOffset 換算を再計算する。
        foreach (var (border, placement) in _placementBorders)
            ApplyPixelOffsetTransform(border, placement);
    }

    private void OnDataContextChanged(object? newDataContext)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
            _vm.Placements.CollectionChanged -= OnPlacementsChanged;
        }

        _vm = newDataContext as GridWorkspaceViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            _vm.Placements.CollectionChanged += OnPlacementsChanged;
        }

        Rebuild();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GridWorkspaceViewModel.CurrentGrid))
        {
            // グリッド切替は全体再構築 (RowDefinitions/ColumnDefinitions / Layer 1/2 の総取り替え)。
            Rebuild();
        }
        else if (e.PropertyName == nameof(GridWorkspaceViewModel.SelectedPlacement))
        {
            // 選択変更は SelectionOverlay の位置更新のみで完結 (Border 自体は不変)。
            // 旧実装は Rebuild を呼んでいたが Layer 2 全 Border の再構築が無駄だった。
            UpdateSelectionOverlay();
        }
    }

    /// <summary>
    /// 選択枠 Adornment (axaml の SelectionOverlay) を SelectedPlacement のセル位置に追従させる。
    /// IsHitTestVisible=False なので D&D / クリックを阻害しない。layout に参加しないので
    /// Image の位置は変わらない (Alignment.Right などでも選択時にズレない)。
    /// </summary>
    private void UpdateSelectionOverlay()
    {
        var sel = _vm?.SelectedPlacement;
        if (sel is null)
        {
            SelectionOverlay.IsVisible = false;
            return;
        }
        Grid.SetRow(SelectionOverlay, sel.GridY);
        Grid.SetColumn(SelectionOverlay, sel.GridX);
        Grid.SetRowSpan(SelectionOverlay, Math.Max(1, sel.OccupyHeight));
        Grid.SetColumnSpan(SelectionOverlay, Math.Max(1, sel.OccupyWidth));
        SelectionOverlay.IsVisible = true;
    }

    private void OnPlacementsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    /// <summary>
    /// 配置 VM のプロパティ変更を検知して View を最小コストで更新する。
    /// PixelOffsetX/Y は Border の TranslateTransform 再計算で済むが、
    /// Position 変更（Move/Swap で発生）は Grid.Row/Column が変わるため
    /// Rebuild が必要。
    /// 購読は <see cref="Rebuild"/> の Layer 2 ループで配置ごとに張り、
    /// <see cref="UnsubscribePlacementChanges"/> で一括解除する。
    /// </summary>
    private void OnPlacementItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not PlacementItemViewModel placement) return;

        if (e.PropertyName is nameof(PlacementItemViewModel.PixelOffsetX)
            or nameof(PlacementItemViewModel.PixelOffsetY))
        {
            // 対応する Border を逆引き（_placementBorders は Border → VM の dict）
            foreach (var (border, vm) in _placementBorders)
            {
                if (vm == placement)
                {
                    ApplyPixelOffsetTransform(border, placement);
                    return;
                }
            }
            return;
        }

        if (e.PropertyName == nameof(PlacementItemViewModel.Position))
        {
            // Move/Swap で Position が変わった: 占有サイズ・回転・サムネは不変なので
            // Grid.Row/Column の差し替えだけで View が反応する。Border 自体の再構築は
            // 不要 → LoadAndPreRotateBitmap (disk read + Skia decode/encode) を完全に
            // スキップでき、Swap で 2 回連続呼ばれても軽量。
            foreach (var (border, vm) in _placementBorders)
            {
                if (vm == placement)
                {
                    Grid.SetRow(border, placement.GridY);
                    Grid.SetColumn(border, placement.GridX);
                    Grid.SetRowSpan(border, Math.Max(1, placement.OccupyHeight));
                    Grid.SetColumnSpan(border, Math.Max(1, placement.OccupyWidth));
                    // 占有マップも当該 placement だけ更新する（高頻度呼び出しではないので
                    // 「全削除 + 再登録」で十分シンプル）
                    RemovePlacementFromOccupantMap(placement);
                    AddPlacementToOccupantMap(placement);
                    // 選択中の placement なら SelectionOverlay も追従させる。
                    if (ReferenceEquals(placement, _vm?.SelectedPlacement))
                        UpdateSelectionOverlay();
                    return;
                }
            }
            return;
        }

        if (e.PropertyName == nameof(PlacementItemViewModel.OccupySize))
        {
            // Inspector の配置固有 OccupySize 編集（保存後）で発火。
            // RowSpan/ColumnSpan を更新して View に反映 + 占有マップを再登録。
            foreach (var (border, vm) in _placementBorders)
            {
                if (vm == placement)
                {
                    Grid.SetRowSpan(border, Math.Max(1, placement.OccupyHeight));
                    Grid.SetColumnSpan(border, Math.Max(1, placement.OccupyWidth));
                    RemovePlacementFromOccupantMap(placement);
                    AddPlacementToOccupantMap(placement);
                    if (ReferenceEquals(placement, _vm?.SelectedPlacement))
                        UpdateSelectionOverlay();
                    return;
                }
            }
            return;
        }

        // 共有特性 (Crop / Transform / Scaling / Alignment) の変更は Border 内の Image の
        // Source / Stretch / W/H 等に幅広く影響するため、Border を再構築する必要がある。
        // ApplyCopyChanges が複数プロパティを連続代入するため Dispatcher.UIThread.Post で
        // 1 ディスパッチサイクルにデバウンスし、20 placements × 7 events のような連発で
        // Rebuild が複数回走るのを防ぐ。_bitmapCacheLru で焼き込み済み Bitmap が
        // キャッシュされるため、同一値ならディスク I/O は生じない。
        if (e.PropertyName is nameof(PlacementItemViewModel.EffectiveCropFraction)
            or nameof(PlacementItemViewModel.AutoCrop)
            or nameof(PlacementItemViewModel.ManualCrop)
            or nameof(PlacementItemViewModel.Rotation)
            or nameof(PlacementItemViewModel.FlipX)
            or nameof(PlacementItemViewModel.FlipY)
            or nameof(PlacementItemViewModel.ScalingMode)
            or nameof(PlacementItemViewModel.Alignment))
        {
            RequestRebuildForSharedChanges();
            return;
        }
    }

    private bool _pendingSharedChangesRebuild;

    /// <summary>
    /// 共有特性変更時の Rebuild をディスパッチサイクルにデバウンスする。
    /// ApplyCopyChanges 内で AutoCrop / ManualCrop / Rotation 等が連続代入されても、
    /// 最終的な Rebuild は 1 回だけ走る。
    /// </summary>
    private void RequestRebuildForSharedChanges()
    {
        if (_pendingSharedChangesRebuild) return;
        _pendingSharedChangesRebuild = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pendingSharedChangesRebuild = false;
            Rebuild();
        }, DispatcherPriority.Background);
    }

    private void UnsubscribePlacementChanges()
    {
        foreach (var item in _placementBorders.Values)
            item.PropertyChanged -= OnPlacementItemPropertyChanged;
    }

    private void Rebuild()
    {
        // 古い配置 VM の PropertyChanged 購読を解除してから _placementBorders をクリアする。
        // 購読は Layer 2 のループで張り直す。Rebuild 経路（CurrentGrid/SelectedPlacement 変更、
        // Placements コレクション変更、DataContext 変更）すべてで漏れなく解除される。
        UnsubscribePlacementChanges();

        // SelectionOverlay は Adornment なので Layer 1/2 の再構築でも消さない（常駐）。
        for (int i = CanvasGrid.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(CanvasGrid.Children[i], SelectionOverlay))
                CanvasGrid.Children.RemoveAt(i);
        }
        CanvasGrid.RowDefinitions.Clear();
        CanvasGrid.ColumnDefinitions.Clear();
        _cellBorders.Clear();
        _placementBorders.Clear();
        _occupantMap.Clear();

        var grid = _vm?.CurrentGrid;
        if (grid is null)
            return;

        // 表示キャンバスのアスペクト比をユーザーの CanvasSize に合わせる。
        // 最大辺を CanvasFixedSize（= 600 logical）にして等比縮小。
        // これにより「表示セル比率 = ユーザーセル比率」となり、ScalingMode.None で
        // bitmap を 1:displayScale で読み込めば PNG 出力と表示が完全に一致する。
        // 旧実装は 600×600 固定だったため、ユーザーの CanvasSize アスペクトと表示が
        // 乖離して、原寸固定モードで画像がはみ出してクリップされる現象があった。
        ApplyCanvasDisplaySize(grid);

        // 重み配列があれば各行・列に Star 重みを反映、無ければ均等。
        for (var r = 0; r < grid.Rows; r++)
        {
            var weight = r < grid.RowWeights.Length ? Math.Max(1, grid.RowWeights[r]) : 1;
            CanvasGrid.RowDefinitions.Add(new RowDefinition(new GridLength(weight, GridUnitType.Star)));
        }
        for (var c = 0; c < grid.Cols; c++)
        {
            var weight = c < grid.ColWeights.Length ? Math.Max(1, grid.ColWeights[c]) : 1;
            CanvasGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)));
        }

        // Layer 1: セル枠（D&D ドロップターゲット）
        for (var r = 0; r < grid.Rows; r++)
        {
            for (var c = 0; c < grid.Cols; c++)
            {
                var pos = new CellPosition(c, r);
                var cell = new Border
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5),
                    Background = Brushes.Transparent,
                    Tag = pos,
                };
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                DragDrop.SetAllowDrop(cell, true);
                cell.AddHandler(DragDrop.DragOverEvent, OnCellDragOver);
                cell.AddHandler(DragDrop.DragLeaveEvent, OnCellDragLeave);
                cell.AddHandler(DragDrop.DropEvent, OnCellDrop);
                CanvasGrid.Children.Add(cell);
                _cellBorders[pos] = cell;
            }
        }

        if (_vm is null)
            return;

        // Layer 2: 配置済み（自身もドロップ対象 = 入れ替え）
        foreach (var placement in _vm.Placements)
        {
            // 選択強調は SelectionOverlay (CanvasGrid 直下、ZIndex=100) が担当するため、
            // BuildPlacementVisual には isSelected を伝えなくてよい (常に非選択スタイル)。
            var visual = BuildPlacementVisual(placement);
            Grid.SetRow(visual, placement.GridY);
            Grid.SetColumn(visual, placement.GridX);
            Grid.SetRowSpan(visual, Math.Max(1, placement.OccupyHeight));
            Grid.SetColumnSpan(visual, Math.Max(1, placement.OccupyWidth));
            CanvasGrid.Children.Add(visual);

            _placementBorders[visual] = placement;
            AddPlacementToOccupantMap(placement);
            // 配置 VM の PixelOffsetX/Y 変更をキャンバスに即時反映するため購読する。
            // Inspector の「保存」ボタンや RevertAsync などキャンバスを介さない経路で値が
            // 変わっても、対応する Border の TranslateTransform を再計算して追従させる。
            // Shift+ドラッグ中は UpdatePixelOffsetFromDrag が ApplyPixelOffsetTransform を直接
            // 呼ぶため、本ハンドラと二重計算になるが ApplyPixelOffsetTransform は idempotent。
            placement.PropertyChanged += OnPlacementItemPropertyChanged;
            ApplyPixelOffsetTransform(visual, placement);
        }

        // Layer 3: 境界ドラッグハンドル（A2: 列・行比率の動的調整）
        BuildBoundaryHandles(grid);

        // ヘッダバー: 行/列ロック切替トグル
        BuildHeaderBars(grid);

        // 環境差（PowerShell 親プロセスから起動した場合等）で RowDefinitions/ColumnDefinitions
        // の Clear/Add が自動レイアウト更新を発火しないケースの保険として、明示的に
        // InvalidateMeasure を呼んで Star Sizing の再計算を強制する。
        CanvasGrid.InvalidateMeasure();

        // SelectionOverlay は Layer 1/2 の再構築でも常駐させているが、
        // 新しい RowDefinitions/ColumnDefinitions に追従させるため Grid.Row/Column を再設定。
        UpdateSelectionOverlay();
    }

    /// <summary>
    /// 列・行ヘッダバーを再構築する。各セルにロック切替ボタンを配置し、
    /// CanvasGrid と同じ Star 重みで列/行幅を揃える（クリック位置と意味が一致）。
    /// </summary>
    private void BuildHeaderBars(GridCanvasItemViewModel grid)
    {
        // 列ヘッダバー
        ColHeaderBar.Children.Clear();
        ColHeaderBar.RowDefinitions.Clear();
        ColHeaderBar.ColumnDefinitions.Clear();
        for (var c = 0; c < grid.Cols; c++)
        {
            var weight = c < grid.ColWeights.Length ? Math.Max(1, grid.ColWeights[c]) : 1;
            ColHeaderBar.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)));
        }
        for (var c = 0; c < grid.Cols; c++)
        {
            var locked = c < grid.ColLocked.Length && grid.ColLocked[c];
            var btn = BuildHeaderToggle(c, locked, isCol: true);
            Grid.SetColumn(btn, c);
            ColHeaderBar.Children.Add(btn);
        }

        // 行ヘッダバー
        RowHeaderBar.Children.Clear();
        RowHeaderBar.RowDefinitions.Clear();
        RowHeaderBar.ColumnDefinitions.Clear();
        for (var r = 0; r < grid.Rows; r++)
        {
            var weight = r < grid.RowWeights.Length ? Math.Max(1, grid.RowWeights[r]) : 1;
            RowHeaderBar.RowDefinitions.Add(new RowDefinition(new GridLength(weight, GridUnitType.Star)));
        }
        for (var r = 0; r < grid.Rows; r++)
        {
            var locked = r < grid.RowLocked.Length && grid.RowLocked[r];
            var btn = BuildHeaderToggle(r, locked, isCol: false);
            Grid.SetRow(btn, r);
            RowHeaderBar.Children.Add(btn);
        }
    }

    private Button BuildHeaderToggle(int index, bool locked, bool isCol)
    {
        var btn = new Button
        {
            Content = locked ? "🔒" : index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 10,
            Padding = new Thickness(0),
            Margin = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = locked
                ? new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xA5, 0x00)) // ロック中: オレンジ
                : new SolidColorBrush(Color.FromArgb(0x40, 0x88, 0x88, 0x88)),
            Foreground = locked ? Brushes.White : Brushes.Black,
            BorderThickness = new Thickness(0),
            MinWidth = 0,
            MinHeight = 0,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        // クリックでロック切替（fire-and-forget で OK、結果は VM のステータスに反映）
        btn.Click += (_, _) =>
        {
            if (_vm is null) return;
            if (isCol) _ = _vm.ToggleColLockAsync(index);
            else _ = _vm.ToggleRowLockAsync(index);
        };
        return btn;
    }

    // ---------- A2: 境界ドラッグで列・行重みを動的調整 ----------

    /// <summary>
    /// 表示キャンバスの最大辺（logical）。ユーザーの CanvasSize アスペクトを保ったまま、
    /// max(CanvasWidth, CanvasHeight) がこの値になるよう等比縮小する。
    /// 旧来の "正方形 600×600 固定" 設計の名残だが、現在は「最大辺」の意味。
    /// </summary>
    private const double CanvasFixedSize = 600.0;

    /// <summary>
    /// ユーザーの CanvasSize に対する表示キャンバスのスケール係数。
    /// 例: CanvasSize=640×800 なら displayScale = 600/800 = 0.75。
    /// 表示キャンバス logical サイズ = (CanvasWidth × scale, CanvasHeight × scale)。
    /// </summary>
    private static double ComputeDisplayScale(GridCanvasItemViewModel grid)
    {
        var maxEdge = Math.Max(grid.CanvasWidth, grid.CanvasHeight);
        return maxEdge > 0 ? CanvasFixedSize / maxEdge : 1.0;
    }

    /// <summary>
    /// <see cref="OuterCanvasGrid"/> と <see cref="CanvasContainer"/> のサイズを、
    /// ユーザーの CanvasSize アスペクトに合わせて設定する（max edge=<see cref="CanvasFixedSize"/>）。
    /// 表示セルのアスペクトがユーザーセルのアスペクトと一致するため、
    /// PNG 出力と表示の比率が完全に揃う。Rebuild() の冒頭で呼ぶ。
    /// </summary>
    private void ApplyCanvasDisplaySize(GridCanvasItemViewModel grid)
    {
        var scale = ComputeDisplayScale(grid);
        var canvasW = grid.CanvasWidth * scale;
        var canvasH = grid.CanvasHeight * scale;
        const double headerSize = 24.0;

        CanvasContainer.Width = canvasW;
        CanvasContainer.Height = canvasH;
        OuterCanvasGrid.Width = canvasW + headerSize;
        OuterCanvasGrid.Height = canvasH + headerSize;
    }
    private const double HandleHitWidth = 12.0;   // ドラッグハンドルの掴み幅（px）。視認性も兼ねて広めに。

    private GridCanvasItemViewModel? _draggingGrid;
    private bool _draggingIsCol;
    private int _draggingBoundaryIndex; // i (0-based, between cell i-1 and cell i)
    private double _dragStartPos;        // Canvas 座標での押下位置 (col=x, row=y)
    private ImmutableArray<int> _dragStartWeights;
    private Rectangle? _draggingHandle;

    private void BuildBoundaryHandles(GridCanvasItemViewModel grid)
    {
        // Rebuild が走るタイミングではドラッグ状態を保険でリセット
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null;
        BoundaryOverlay.Children.Clear();

        // ハンドルは CanvasGrid 内に Grid.SetColumn/Row で配置する。
        // これにより Avalonia の Grid Layout が決定する実セル境界と必ず一致する
        // （BoundaryOverlay の Canvas 絶対座標とは独立に、Grid のレイアウト計算を信頼する）。
        // 通常境界（青）と「ロック隣接境界（オレンジ）」の 2 種類を用意し、
        // ロック中の列/行に隣接する境界はフィット時に重み変動が制限されることを示す。
        // 色は BuildHeaderToggle のロック中ボタン背景（FFA500 系）と統一して直感を揃える。
        var idleFill = new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x99, 0xFF));
        var hoverFill = new SolidColorBrush(Color.FromArgb(0xAA, 0x33, 0x99, 0xFF));
        var idleLockedFill = new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xA5, 0x00));
        var hoverLockedFill = new SolidColorBrush(Color.FromArgb(0xAA, 0xFF, 0xA5, 0x00));

        // 列境界ハンドル: 列 i-1 と列 i の境界 = col=i セルの左端中心に配置
        for (var i = 1; i < grid.Cols; i++)
        {
            // 隣接する 2 セル（i-1, i）のどちらかがロック中なら「ロック隣接」と判定。
            // フィット動作（WeightRedistributor.FitToOccupant）でロック列を飛ばす意味論と一致。
            var locked = IsColLocked(grid, i - 1) || IsColLocked(grid, i);
            var idle = locked ? idleLockedFill : idleFill;
            var hover = locked ? hoverLockedFill : hoverFill;
            var handle = new Rectangle
            {
                Width = HandleHitWidth,
                Fill = idle,
                Cursor = new Cursor(StandardCursorType.SizeWestEast),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(-HandleHitWidth / 2, 0, 0, 0),
                Tag = ("col", i),
            };
            Grid.SetColumn(handle, i);
            Grid.SetRow(handle, 0);
            Grid.SetRowSpan(handle, grid.Rows);
            handle.PointerPressed += OnBoundaryPointerPressed;
            handle.PointerEntered += (_, _) => handle.Fill = hover;
            handle.PointerExited += (_, _) => handle.Fill = idle;
            CanvasGrid.Children.Add(handle);
        }

        // 行境界ハンドル: 行 i-1 と行 i の境界 = row=i セルの上端中心に配置
        for (var i = 1; i < grid.Rows; i++)
        {
            var locked = IsRowLocked(grid, i - 1) || IsRowLocked(grid, i);
            var idle = locked ? idleLockedFill : idleFill;
            var hover = locked ? hoverLockedFill : hoverFill;
            var handle = new Rectangle
            {
                Height = HandleHitWidth,
                Fill = idle,
                Cursor = new Cursor(StandardCursorType.SizeNorthSouth),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -HandleHitWidth / 2, 0, 0),
                Tag = ("row", i),
            };
            Grid.SetColumn(handle, 0);
            Grid.SetColumnSpan(handle, grid.Cols);
            Grid.SetRow(handle, i);
            handle.PointerPressed += OnBoundaryPointerPressed;
            handle.PointerEntered += (_, _) => handle.Fill = hover;
            handle.PointerExited += (_, _) => handle.Fill = idle;
            CanvasGrid.Children.Add(handle);
        }
    }

    /// <summary>
    /// 指定列がロック中か判定する。<c>ColLocked</c> の長さが <c>Cols</c> と
    /// 一致しない過渡状態（マイグレーション直後など）では <c>false</c> 扱い。
    /// </summary>
    private static bool IsColLocked(GridCanvasItemViewModel grid, int colIndex)
    {
        if (colIndex < 0 || colIndex >= grid.Cols) return false;
        if (grid.ColLocked.Length != grid.Cols) return false;
        return grid.ColLocked[colIndex];
    }

    /// <summary>指定行がロック中か判定する。詳細は <see cref="IsColLocked"/> 参照。</summary>
    private static bool IsRowLocked(GridCanvasItemViewModel grid, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows) return false;
        if (grid.RowLocked.Length != grid.Rows) return false;
        return grid.RowLocked[rowIndex];
    }

    /// <summary>
    /// 境界ハンドルがダブルクリックされた時、アクティブ配置（<see cref="GridWorkspaceViewModel.SelectedPlacement"/>）の
    /// 占有列群/行群の左右枠/上下枠と一致すれば、列幅/行高を画像の実描画矩形に合わせて縮める。
    /// 一致しない境界（別の配置の枠など）では何もしない。
    /// </summary>
    private async void TryFitGridWeight(string axis, int idx)
    {
        if (_vm?.SelectedPlacement is not { } placement) return;

        var isCol = axis == "col";
        if (isCol)
        {
            var leftBoundary = placement.GridX;
            var rightBoundary = placement.GridX + Math.Max(1, placement.OccupyWidth);
            if (idx != leftBoundary && idx != rightBoundary) return;
        }
        else
        {
            var topBoundary = placement.GridY;
            var bottomBoundary = placement.GridY + Math.Max(1, placement.OccupyHeight);
            if (idx != topBoundary && idx != bottomBoundary) return;
        }

        await _vm.FitGridWeightAsync(
            placement.PlacementId,
            isCol ? FitAxis.Column : FitAxis.Row);
    }

    private static double ComputeBoundaryX(GridCanvasItemViewModel grid, int colIndex)
    {
        long total = 0;
        for (var k = 0; k < grid.ColWeights.Length; k++) total += grid.ColWeights[k];
        long prefix = 0;
        for (var k = 0; k < colIndex; k++) prefix += grid.ColWeights[k];
        return CanvasFixedSize * prefix / Math.Max(1L, total);
    }

    private static double ComputeBoundaryY(GridCanvasItemViewModel grid, int rowIndex)
    {
        long total = 0;
        for (var k = 0; k < grid.RowWeights.Length; k++) total += grid.RowWeights[k];
        long prefix = 0;
        for (var k = 0; k < rowIndex; k++) prefix += grid.RowWeights[k];
        return CanvasFixedSize * prefix / Math.Max(1L, total);
    }

    private void OnBoundaryPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Rectangle handle || handle.Tag is not (string axis, int idx)) return;
        var grid = _vm?.CurrentGrid;
        if (grid is null) return;

        // Ctrl+クリックは「列幅/行高を画像にフィット」アクションへ。
        // ダブルクリックは Pressed で e.Handled=true を立てる以上 Tapped/DoubleTapped が発火せず
        // ClickCount でも安定しなかったため、修飾キーで明示的に分岐する。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            TryFitGridWeight(axis, idx);
            e.Handled = true;
            return;
        }

        _draggingGrid = grid;
        _draggingIsCol = axis == "col";
        _draggingBoundaryIndex = idx;
        _draggingHandle = handle;
        _dragStartWeights = _draggingIsCol ? grid.ColWeights : grid.RowWeights;
        var pos = e.GetPosition(BoundaryOverlay);
        _dragStartPos = _draggingIsCol ? pos.X : pos.Y;

        // ドラッグ中のみ BoundaryOverlay 全体を hit-test 対象にして PointerMoved/Released を確実に受ける。
        BoundaryOverlay.Background = Brushes.Transparent;

        // Avalonia は Pressed が発火した Control（= handle）に implicit capture を取る。
        // すると押下中の Move/Released は handle にしか届かず、handle の兄弟である
        // BoundaryOverlay の PointerMoved/Released が呼ばれない。結果として「ボタンを
        // 離しても確定せず、マウスがハンドルに追従し続けて 2 回目のクリックでようやく確定」
        // という非ドラッグ的挙動になる。Capture を BoundaryOverlay に張り直して回避する。
        e.Pointer.Capture(BoundaryOverlay);
        e.Handled = true;
    }

    private void OnOverlayPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        // Capture が外部要因（フォーカス喪失等）で外れたときの保険。状態をクリアして
        // 「ハンドルだけ動いてリリースが拾えない」ゾンビ状態を防ぐ。
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null;
    }

    /// <summary>
    /// BoundaryOverlay 上の PointerMoved。ドラッグ中（_draggingHandle != null）にのみ
    /// ハンドル位置をプレビュー移動する。ハンドルは CanvasGrid 内に置かれ、
    /// Margin 経由でセル境界からの相対オフセットを動かす。
    /// </summary>
    private void OnOverlayPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingGrid is null || _draggingHandle is null) return;

        var pos = e.GetPosition(BoundaryOverlay);
        var current = _draggingIsCol ? pos.X : pos.Y;
        var deltaPx = current - _dragStartPos;

        // ハンドルの Margin はセル境界中心（負の HandleHitWidth/2）+ ドラッグ差分
        var baseOffset = -HandleHitWidth / 2;
        if (_draggingIsCol)
            _draggingHandle.Margin = new Thickness(baseOffset + deltaPx, 0, 0, 0);
        else
            _draggingHandle.Margin = new Thickness(0, baseOffset + deltaPx, 0, 0);
    }

    /// <summary>
    /// BoundaryOverlay 上の PointerReleased。ドラッグ確定 → 重み再計算 → UseCase 実行。
    /// </summary>
    private async void OnOverlayPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_draggingGrid is null || _draggingHandle is null)
        {
            // ドラッグ中でない（ハンドル外でクリック等）→ 念のため hit-test を解除
            BoundaryOverlay.Background = null;
            return;
        }

        var isCol = _draggingIsCol;
        var idx = _draggingBoundaryIndex;
        var startWeights = _dragStartWeights;

        var pos = e.GetPosition(BoundaryOverlay);
        var current = isCol ? pos.X : pos.Y;
        var deltaPx = current - _dragStartPos;

        // ドラッグ状態クリア（再 Rebuild されるため）
        _draggingGrid = null;
        _draggingHandle = null;
        BoundaryOverlay.Background = null; // 通常時は hit-test を子の Rectangle にだけ任せる
        e.Handled = true;

        if (Math.Abs(deltaPx) < 1.0) return;

        // 表示キャンバスはユーザーの CanvasSize アスペクトに合わせてあるため、
        // 軸ごとの logical サイズを Bounds から取得する（旧実装は CanvasFixedSize 固定で
        // 非正方形キャンバスでは比率がズレていた）。
        var axisLogicalSize = isCol ? CanvasGrid.Bounds.Width : CanvasGrid.Bounds.Height;
        if (axisLogicalSize <= 0) axisLogicalSize = CanvasFixedSize;
        var newWeights = WeightRedistributor.Redistribute(startWeights, idx, deltaPx, axisLogicalSize);
        if (newWeights.SequenceEqual(startWeights)) return;

        if (_vm is null) return;
        await _vm.ApplyGridWeightsAsync(
            colWeights: isCol ? newWeights : null,
            rowWeights: isCol ? null : newWeights);
    }

    /// <summary>
    /// PlacementItem の PixelOffsetX/Y を View 上のピクセル座標に換算して、
    /// Border 内部の <see cref="Image"/> の <see cref="TransformGroup"/> に
    /// <see cref="TranslateTransform"/> として加算する。
    /// Border 自体は移動させず、ClipToBounds=true により画像のはみ出しはセル境界で
    /// クリップされる（Renderer の <c>SKCanvas.ClipRect</c> と整合）。
    /// </summary>
    private void ApplyPixelOffsetTransform(Border container, PlacementItemViewModel placement)
    {
        // Border 自体は動かさない。前バージョンで Border に設定された transform があれば消す。
        container.RenderTransform = null;

        if (container.Child is not Image image)
            return; // Label fallback には適用しない

        // BuildPlacementTransform で TransformGroup（Flip/Rotate）が設定されている前提。
        var group = image.RenderTransform as TransformGroup;
        if (group is null)
        {
            group = new TransformGroup();
            image.RenderTransform = group;
        }

        // 既存の TranslateTransform（過去の更新で追加されたもの）を削除して付け直す。
        for (var i = group.Children.Count - 1; i >= 0; i--)
        {
            if (group.Children[i] is TranslateTransform)
                group.Children.RemoveAt(i);
        }

        var grid = _vm?.CurrentGrid;
        var viewW = CanvasGrid.Bounds.Width;
        var viewH = CanvasGrid.Bounds.Height;

        if (grid is null || grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0
            || viewW <= 0 || viewH <= 0
            || (placement.PixelOffsetX == 0 && placement.PixelOffsetY == 0))
        {
            return;
        }

        var sx = viewW / grid.CanvasWidth;
        var sy = viewH / grid.CanvasHeight;
        group.Children.Add(new TranslateTransform(
            placement.PixelOffsetX * sx,
            placement.PixelOffsetY * sy));
    }

    private Border BuildPlacementVisual(PlacementItemViewModel placement)
    {
        // C 案: placement Border は常に Margin 0 + 枠線/背景なしで Renderer (PNG 出力) と
        // 完全同一 geometry に保つ。選択強調は SelectionOverlay (axaml の CanvasGrid 直下、
        // ZIndex=100, IsHitTestVisible=False) が担当するため、ここでは isSelected を見ない。
        // これにより Alignment.Right などで配置した Image が選択時にズレる問題が解消する。
        var defaultBackground = (IBrush)Brushes.Transparent;

        Control content;
        if (!string.IsNullOrEmpty(placement.ThumbnailPath) && File.Exists(placement.ThumbnailPath))
        {
            try
            {
                // 回転・反転は事前に Bitmap に焼き込む（renderer の ApplyTransform と同じ順序）。
                // これにより Avalonia の Stretch が「回転後のアスペクト比」で計算され、
                // PNG 出力（ピクセル合成）と UI 近似の見た目が一致する。
                // すべての ScalingMode で thumbnail-bound bitmap（max 1024px）を使う。
                // ScalingMode.None については explicit Width/Height + Stretch.Uniform で
                // 視覚サイズを source × displayScale DIPs に固定する（後述）。
                var grid = _vm?.CurrentGrid;
                Bitmap bitmap = GetOrCreatePreRotatedBitmap(
                    placement.ThumbnailPath, placement.Rotation, placement.FlipX, placement.FlipY,
                    placement.EffectiveCropFraction);
                var cropFractionW = placement.EffectiveCropFraction?.Width ?? 1.0;
                var cropFractionH = placement.EffectiveCropFraction?.Height ?? 1.0;

                var (stretch, direction) = MapScalingMode(placement.ScalingMode);
                // 全 ScalingMode で Alignment を使う（旧版は ScalingMode.None で TrimmingAnchor、
                // それ以外で Alignment という分岐があったが、TrimmingAnchor は Alignment に統合された）。
                var (hAlign, vAlign) = MapAlignment(placement.Alignment);

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = stretch,
                    StretchDirection = direction,
                    HorizontalAlignment = hAlign,
                    VerticalAlignment = vAlign,
                    RenderTransform = BuildPlacementTransform(placement),
                    RenderTransformOrigin = RelativePoint.Center,
                    // Image.DesiredSize はソース Bitmap のピクセル寸法を返すため、
                    // Avalonia の Grid Star Sizing がこれを MinWidth/Height として
                    // 採用すると、列・行が重みではなく画像サイズに引きずられて拡張される
                    // 結果、ハンドル位置（Grid.SetColumn/Row 配置）と視覚境界がズレ、
                    // 重みドラッグ更新もレイアウト的に反映されない症状が出る。
                    MinWidth = 0,
                    MinHeight = 0,
                };

                // ScalingMode.None: 視覚サイズを「source × displayScale DIPs」に明示固定する。
                // bitmap 自体は thumbnail bound（max 1024px）のままで、Stretch.Uniform が
                // explicit Width/Height へリサンプリングする。これにより:
                //   - メモリは thumbnail サイズで bound（巨大ソースでも ~4MB 上限）
                //   - 視覚サイズは source × displayScale で PNG 出力と一致（WYSIWYG 維持）
                //   - 画像 > セルなら HorizontalAlignment/VerticalAlignment（TrimmingAnchor 由来）
                //     によって ClipToBounds でセル境界クリップされる挙動も維持
                // 旧実装は LoadAndResizeAtNativeSize で bitmap pixel size を可変にしていたが、
                // (1) 巨大ソースで ~108MB 確保（メモリ退行）、(2) cap=600 で WYSIWYG 破綻、
                // の二律背反だった。explicit W/H + thumbnail bitmap でこの問題を解消する。
                // プレビュー品質は thumbnail 解像度に依存するため、ソースが thumbnail 上限
                // (1024px) を超える場合は upscale で若干ぼやけるが、PNG 出力には影響しない。
                if (placement.ScalingMode == ViewGrid.Core.Entities.ScalingMode.None
                    && placement.SourceWidth > 0 && placement.SourceHeight > 0
                    && grid is not null)
                {
                    var rotateSwap = placement.Rotation
                        is ViewGrid.Core.Entities.Rotation.Cw90
                        or ViewGrid.Core.Entities.Rotation.Cw270;
                    // AutoCrop 適用後の論理画像サイズ（原画像座標系、回転前）。
                    // fraction は LoadAndPreRotateBitmap がサムネ走査で算出した「回転前の原画像
                    // 座標系での比率」で、原画像の実寸に乗算するだけで AutoCrop 後の論理サイズを得る。
                    var croppedSourceW = (int)Math.Max(1.0, Math.Round(placement.SourceWidth * cropFractionW));
                    var croppedSourceH = (int)Math.Max(1.0, Math.Round(placement.SourceHeight * cropFractionH));
                    var sourceW = rotateSwap ? croppedSourceH : croppedSourceW;
                    var sourceH = rotateSwap ? croppedSourceW : croppedSourceH;
                    var displayScale = ComputeDisplayScale(grid);
                    image.Stretch = Stretch.Uniform; // explicit W/H へ uniform リサンプリング
                    image.Width = Math.Max(1.0, sourceW * displayScale);
                    image.Height = Math.Max(1.0, sourceH * displayScale);
                }
                content = image;
            }
            catch
            {
                content = BuildLabelFallback(placement);
            }
        }
        else
        {
            content = BuildLabelFallback(placement);
        }

        // C 案: 選択強調は SelectionOverlay が担当するため、placement Border 自体は常に
        // 透明 + 0px。layout に参加する枠線がなくなり Alignment.Right などでも Image が動かない。
        var defaultBorderBrush = (IBrush)Brushes.Transparent;
        var defaultBorderThickness = new Thickness(0);

        var container = new Border
        {
            BorderBrush = defaultBorderBrush,
            BorderThickness = defaultBorderThickness,
            Background = defaultBackground,
            Margin = new Thickness(0),
            Child = content,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Tag = placement,
            ClipToBounds = true, // Stretch.None で大きい画像がはみ出さないように
            // Border 自身も子の Image DesiredSize に引っ張られないよう MinSize=0 を強制。
            MinWidth = 0,
            MinHeight = 0,
        };

        // 移動ドラッグソース
        container.PointerPressed += OnPlacementPointerPressed;
        container.PointerMoved += OnPlacementPointerMoved;
        container.PointerReleased += OnPlacementPointerReleased;

        // 入れ替えのドロップターゲット
        DragDrop.SetAllowDrop(container, true);
        container.AddHandler(DragDrop.DragOverEvent, OnPlacementDragOver);
        container.AddHandler(DragDrop.DragLeaveEvent, OnPlacementDragLeave);
        container.AddHandler(DragDrop.DropEvent, OnPlacementDrop);

        return container;
    }

    // ---------- 画像特性 → Avalonia 表示パラメータのマッピング ----------

    private static (Stretch Stretch, StretchDirection Direction) MapScalingMode(
        ViewGrid.Core.Entities.ScalingMode mode) => mode switch
    {
        ViewGrid.Core.Entities.ScalingMode.None => (Stretch.None, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.UniformContain => (Stretch.Uniform, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.UniformContainShrinkOnly => (Stretch.Uniform, StretchDirection.DownOnly),
        ViewGrid.Core.Entities.ScalingMode.UniformContainEnlargeOnly => (Stretch.Uniform, StretchDirection.UpOnly),
        ViewGrid.Core.Entities.ScalingMode.UniformCover => (Stretch.UniformToFill, StretchDirection.Both),
        ViewGrid.Core.Entities.ScalingMode.Fill => (Stretch.Fill, StretchDirection.Both),
        _ => (Stretch.Uniform, StretchDirection.Both),
    };

    private static (HorizontalAlignment H, VerticalAlignment V) MapAlignment(
        ViewGrid.Core.Entities.Alignment alignment)
    {
        var h = alignment.X switch
        {
            ViewGrid.Core.Entities.AnchorX.Left => HorizontalAlignment.Left,
            ViewGrid.Core.Entities.AnchorX.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };
        var v = alignment.Y switch
        {
            ViewGrid.Core.Entities.AnchorY.Top => VerticalAlignment.Top,
            ViewGrid.Core.Entities.AnchorY.Bottom => VerticalAlignment.Bottom,
            _ => VerticalAlignment.Center,
        };
        return (h, v);
    }


    /// <summary>
    /// 配置に適用する RenderTransform。回転・反転は <see cref="LoadAndPreRotateBitmap"/> で
    /// Bitmap に焼き込み済みなので、ここでは PixelOffset 用の TranslateTransform を後で追加できる
    /// 空の TransformGroup だけを返す（<see cref="ApplyPixelOffsetTransform"/> が積む）。
    /// </summary>
    private static TransformGroup BuildPlacementTransform(PlacementItemViewModel placement)
        => new();

    /// <summary>
    /// サムネイルを読み込み、Crop（オプション、ManualCrop 優先で解決済みの比率）→ Flip → Rotate の順で
    /// SkiaSharp 上に焼き込んだ Avalonia <see cref="Bitmap"/> を返す。<br/>
    /// Crop は VM 層で <see cref="ViewGrid.Core.Services.IImageCropResolver"/> 経由で解決済みの
    /// <see cref="ViewGrid.Core.Entities.CropFraction"/> をサムネに適用するので、Renderer / View /
    /// Use case で同一座標系の比率を共有でき、自動と手動の表示が揃う。
    /// </summary>
    private static Bitmap LoadAndPreRotateBitmap(
        string thumbnailPath,
        ViewGrid.Core.Entities.Rotation rotation,
        bool flipX, bool flipY,
        ViewGrid.Core.Entities.CropFraction? cropFraction)
    {
        var hasCrop = cropFraction is { } f && !f.IsFull();
        if (rotation == ViewGrid.Core.Entities.Rotation.None && !flipX && !flipY && !hasCrop)
        {
            // 変換不要なら直接 Avalonia.Bitmap で読み込む（最速パス）。
            using var stream = File.OpenRead(thumbnailPath);
            return new Bitmap(stream);
        }

        using var skBitmap = SKBitmap.Decode(thumbnailPath);
        SKBitmap toTransform = skBitmap;
        SKBitmap? cropped = null;
        try
        {
            if (hasCrop)
            {
                cropped = TryApplyCropFraction(skBitmap, cropFraction!.Value);
                if (cropped is not null)
                    toTransform = cropped;
            }

            using var transformed = ApplySkiaTransform(toTransform, rotation, flipX, flipY);
            using var skImage = SKImage.FromBitmap(transformed);
            using var encoded = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(encoded.ToArray());
            return new Bitmap(ms);
        }
        finally
        {
            cropped?.Dispose();
        }
    }

    /// <summary>
    /// サムネ <see cref="SKBitmap"/> に <see cref="ViewGrid.Core.Entities.CropFraction"/>
    /// （0–1 比率）をサムネ寸法に展開して切り出した新しい <see cref="SKBitmap"/> を返す。
    /// 結果が空・元サイズと同一なら <c>null</c>。
    /// </summary>
    private static SKBitmap? TryApplyCropFraction(
        SKBitmap source, ViewGrid.Core.Entities.CropFraction fraction)
    {
        if (source.Width <= 0 || source.Height <= 0) return null;
        var (x, y, w, h) = fraction.ToPixelBbox(source.Width, source.Height);
        if (w <= 0 || h <= 0) return null;
        if (x == 0 && y == 0 && w == source.Width && h == source.Height) return null;

        var dst = new SKBitmap(w, h, source.ColorType, source.AlphaType);
        try
        {
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);
            using var srcImage = SKImage.FromBitmap(source);
            canvas.DrawImage(
                srcImage,
                new SKRect(x, y, x + w, y + h),
                new SKRect(0, 0, w, h));
            return dst;
        }
        catch
        {
            dst.Dispose();
            throw;
        }
    }

    private static SKBitmap ApplySkiaTransform(
        SKBitmap source, ViewGrid.Core.Entities.Rotation rotation, bool flipX, bool flipY)
    {
        var rotateSwap = rotation is ViewGrid.Core.Entities.Rotation.Cw90
            or ViewGrid.Core.Entities.Rotation.Cw270;
        var dstW = rotateSwap ? source.Height : source.Width;
        var dstH = rotateSwap ? source.Width : source.Height;
        var dst = new SKBitmap(dstW, dstH, source.ColorType, source.AlphaType);
        try
        {
            using var canvas = new SKCanvas(dst);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(dstW / 2f, dstH / 2f);
            canvas.RotateDegrees((int)rotation);
            canvas.Scale(flipX ? -1f : 1f, flipY ? -1f : 1f);
            canvas.Translate(-source.Width / 2f, -source.Height / 2f);
            canvas.DrawBitmap(source, 0, 0);
            return dst;
        }
        catch
        {
            dst.Dispose();
            throw;
        }
    }

    private static TextBlock BuildLabelFallback(PlacementItemViewModel placement) => new()
    {
        Text = placement.Label,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        FontSize = 10,
        Opacity = 0.85,
    };

    // ---------- 配置済み Border のドラッグソース ----------

    private void OnPlacementPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not PlacementItemViewModel placement)
            return;
        if (!e.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
            return;

        // 配置をクリックしたら、UserControl 自体にフォーカスを移して Ctrl+Arrow で
        // PixelOffset 微調整できるようにする（IsTabStop=false なので Tab には現れない）。
        Focus();

        // Shift 押下中はピクセル微調整モード。通常の D&D を抑止して PixelOffsetX/Y を
        // ドラッグで連続更新する。Avalonia の implicit pointer capture を border に
        // 明示的に張り直して、押下中の Move/Released を確実に同 border で受ける。
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // 対象 placement を選択状態にして Inspector を当該画像に切り替える。
            // Inspector は source の PixelOffset 変更を購読しているので、ドラッグ中も
            // リアルタイムで数値が追従する。
            if (_vm is not null && _vm.SelectedPlacement?.PlacementId != placement.PlacementId)
                _vm.SelectedPlacement = placement;

            _pixelOffsetDragging = true;
            // CanvasGrid の論理座標（Viewbox 内、固定 600×600）で測る。Viewbox が拡縮しても
            // 論理座標は不変なので、表示倍率に依存しない換算ができる。
            _pixelOffsetStart = e.GetPosition(CanvasGrid);
            _pixelOffsetStartX = placement.PixelOffsetX;
            _pixelOffsetStartY = placement.PixelOffsetY;
            _pixelOffsetTarget = placement;
            _pixelOffsetBorder = border;
            e.Pointer.Capture(border);
            e.Handled = true;
            return;
        }

        // 押下時点では選択を更新しない（Rebuild が押下中の Border を破棄して
        // PointerMoved が届かなくなる事象を避けるため）。
        _placementPressOrigin = e.GetPosition(this);
        _placementPressItem = placement;
        _placementPressEvent = e;
        _placementPressBorder = border;
    }

    private async void OnPlacementPointerMoved(object? sender, PointerEventArgs e)
    {
        // Shift+ドラッグ中: PixelOffset を即時更新してプレビュー反映（DB 永続化は Released 時）
        if (_pixelOffsetDragging && _pixelOffsetTarget is not null && _pixelOffsetBorder is not null)
        {
            UpdatePixelOffsetFromDrag(e);
            return;
        }

        if (_placementPressOrigin is null || _placementPressItem is null || _placementPressEvent is null)
            return;

        var current = e.GetPosition(this);
        var dx = Math.Abs(current.X - _placementPressOrigin.Value.X);
        var dy = Math.Abs(current.Y - _placementPressOrigin.Value.Y);
        if (dx < DragThreshold && dy < DragThreshold)
            return;

        var item = _placementPressItem;
        var trigger = _placementPressEvent;
        var border = _placementPressBorder;
        ResetPlacementPressState();

        // 掴んだセルのオフセットを計算（NxM 配置の右下端を掴んでドラッグした場合、
        // ドロップ位置から (-W+1, -H+1) ずれた位置が新左上になる）。
        var (ox, oy) = ComputeGrabOffset(border, trigger, item);

        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.CreateText($"{PlacementPrefix}{item.PlacementId}:{ox},{oy}"));

        try
        {
            await DragDrop.DoDragDropAsync(trigger, transfer, DragDropEffects.Move);
        }
        catch
        {
            // ユーザー操作起点の例外は握りつぶす
        }
    }

    private void OnPlacementPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // Shift+ドラッグの終了: ドラッグ中に PlacementItemViewModel.PixelOffsetX/Y を直接更新済み。
        // ここでは状態クリアのみ。Inspector が source の PropertyChanged を購読していて
        // IsDirty=true を立てるので、ユーザーが「保存」ボタンを押すまで永続化されない。
        // Inspector 数値直接入力と同じ「編集中バッファ → IsDirty → 保存／Revert」のフローに
        // 統一する設計（Shift+ドラッグ・Ctrl+Arrow は Inspector 数値入力の直感的な UI 版）。
        if (_pixelOffsetDragging)
        {
            _pixelOffsetDragging = false;
            _pixelOffsetTarget = null;
            _pixelOffsetBorder = null;
            e.Handled = true;
            return;
        }

        // 閾値を超えずに離した場合 = クリック → ここで選択を確定する。
        if (_placementPressItem is not null && _vm is not null)
            _vm.SelectedPlacement = _placementPressItem;

        ResetPlacementPressState();
    }

    /// <summary>
    /// Shift+ドラッグ中の PointerMoved 処理。マウスの delta を「キャンバス座標系の
    /// ピクセル値」に換算して <see cref="PlacementItemViewModel.PixelOffsetX"/> /
    /// <c>Y</c> を更新し、<see cref="ApplyPixelOffsetTransform"/> を呼んで即時再描画。
    /// 永続化は Released 時にまとめて行う（毎フレーム DB に書かない）。
    /// 座標は <see cref="CanvasGrid"/> の論理座標（固定 <see cref="CanvasFixedSize"/>）で
    /// 測るので、Viewbox の拡縮倍率に依存せず一貫した換算ができる。
    /// </summary>
    private void UpdatePixelOffsetFromDrag(PointerEventArgs e)
    {
        var grid = _vm?.CurrentGrid;
        if (grid is null || grid.CanvasWidth <= 0 || grid.CanvasHeight <= 0)
            return;

        var current = e.GetPosition(CanvasGrid);
        var dx = current.X - _pixelOffsetStart.X;
        var dy = current.Y - _pixelOffsetStart.Y;

        // 表示 logical 上の delta を「ユーザー CanvasWidth×CanvasHeight」上の
        // ピクセル量に換算（見たまま動くスケール）。表示キャンバスはユーザーの CanvasSize
        // アスペクトに合わせてあるため（max edge=CanvasFixedSize）、軸ごとの Bounds で割る。
        var displayW = CanvasGrid.Bounds.Width > 0 ? CanvasGrid.Bounds.Width : CanvasFixedSize;
        var displayH = CanvasGrid.Bounds.Height > 0 ? CanvasGrid.Bounds.Height : CanvasFixedSize;
        var sx = grid.CanvasWidth / displayW;
        var sy = grid.CanvasHeight / displayH;
        var max = PlacementInspectorViewModel.MaxPixelOffset;
        var newX = Math.Clamp(_pixelOffsetStartX + (int)Math.Round(dx * sx), -max, max);
        var newY = Math.Clamp(_pixelOffsetStartY + (int)Math.Round(dy * sy), -max, max);

        var target = _pixelOffsetTarget!;
        target.PixelOffsetX = newX;
        target.PixelOffsetY = newY;
        ApplyPixelOffsetTransform(_pixelOffsetBorder!, target);
    }

    private void ResetPlacementPressState()
    {
        _placementPressOrigin = null;
        _placementPressItem = null;
        _placementPressEvent = null;
        _placementPressBorder = null;
    }

    private static (int Ox, int Oy) ComputeGrabOffset(
        Border? border, PointerPressedEventArgs? trigger, PlacementItemViewModel item)
    {
        if (border is null || trigger is null) return (0, 0);
        var w = Math.Max(1, item.OccupyWidth);
        var h = Math.Max(1, item.OccupyHeight);
        if (w == 1 && h == 1) return (0, 0);

        var local = trigger.GetPosition(border);
        var bw = border.Bounds.Width;
        var bh = border.Bounds.Height;
        if (bw <= 0 || bh <= 0) return (0, 0);

        var cellW = bw / w;
        var cellH = bh / h;
        var ox = Math.Clamp((int)(local.X / cellW), 0, w - 1);
        var oy = Math.Clamp((int)(local.Y / cellH), 0, h - 1);
        return (ox, oy);
    }

    // ---------- ハイライトブラシ ----------

    private static readonly IBrush DragOverValidBrush = new SolidColorBrush(Color.FromArgb(0x88, 0x33, 0xFF, 0x66));
    private static readonly IBrush DragOverInvalidBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0x44, 0x44));
    private static readonly IBrush DragOverSwapBrush = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xCC, 0x33));
    private static readonly IBrush PlacementSwapBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0xCC, 0x00));
    private static readonly IBrush PlacementInvalidBorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0xFF, 0x33, 0x33));

    // ---------- セル DragOver/Drop ----------

    private void OnCellDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border cell)
            return;

        if (!e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        if (src.Kind == DragKind.Unknown || cell.Tag is not CellPosition pos)
        {
            ClearAllCellHighlights();
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var occupy = src.OccupySize ?? OccupySize.OneByOne;
        var newTopLeftX = pos.X - src.Offset.X;
        var newTopLeftY = pos.Y - src.Offset.Y;
        var (cellsToHighlight, isValid) = AnalyzeHoverRangeRaw(newTopLeftX, newTopLeftY, occupy, src);

        ClearAllCellHighlights();

        // 境界外や重複は Placement ドラッグでも一律「不可（赤）」とする。
        // Swap の黄色ハイライトは「配置済み Border を直接ホバーしたとき」だけに限定する
        // （その経路は OnPlacementDragOver で処理される）。
        var brush = isValid ? DragOverValidBrush : DragOverInvalidBrush;

        foreach (var c in cellsToHighlight)
        {
            if (_cellBorders.TryGetValue(c, out var border))
                border.Background = brush;
        }

        e.DragEffects = src.Kind switch
        {
            DragKind.Copy when isValid => DragDropEffects.Copy,
            DragKind.Placement when isValid => DragDropEffects.Move,
            _ => DragDropEffects.None,
        };
        e.Handled = true;
    }

    private void OnCellDragLeave(object? sender, DragEventArgs e)
    {
        // hover が外れた瞬間でも他セルがまだホバーしている可能性があるため、
        // セル単位ではなく一括クリアは Drop / 別セル DragOver 時に実施する。
        if (sender is Border cell)
            cell.Background = Brushes.Transparent;
    }

    private async void OnCellDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border cell)
            return;

        ClearAllCellHighlights();
        e.Handled = true;

        if (cell.Tag is not CellPosition position || _vm is null)
            return;

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);
        var corrected = ApplyOffset(position, src.Offset);
        if (corrected is null)
            return;

        await DispatchPositionedDropAsync(src, corrected.Value);
    }

    // ---------- 配置済み Border DragOver/Drop ----------

    private void OnPlacementDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Border border || border.Tag is not PlacementItemViewModel target)
            return;

        if (!e.DataTransfer.Contains(DataFormat.Text))
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        switch (src.Kind)
        {
            case DragKind.Copy:
                ApplyPlacementHighlight(target, PlacementInvalidBorderBrush, DragOverInvalidBrush);
                e.DragEffects = DragDropEffects.None;
                break;

            case DragKind.Placement when src.PlacementSource?.PlacementId == target.PlacementId:
                // 自分自身の Border 上だが、NxM 配置を「元位置と部分重複する位置」に移す操作
                // をサポートするため、マウス位置のセルを移動先候補として扱う。
                HandleSelfPlacementHover(e, src);
                break;

            case DragKind.Placement:
                ApplyPlacementHighlight(target, PlacementSwapBorderBrush, DragOverSwapBrush);
                e.DragEffects = DragDropEffects.Move;
                break;

            default:
                e.DragEffects = DragDropEffects.None;
                break;
        }
        e.Handled = true;
    }

    private void HandleSelfPlacementHover(DragEventArgs e, DragSourceInfo src)
    {
        var cellPos = ResolveCellAtPointer(e);
        if (cellPos is null)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var occupy = src.OccupySize ?? OccupySize.OneByOne;
        var newTopLeftX = cellPos.Value.X - src.Offset.X;
        var newTopLeftY = cellPos.Value.Y - src.Offset.Y;
        var (cellsToHighlight, isValid) = AnalyzeHoverRangeRaw(newTopLeftX, newTopLeftY, occupy, src);

        ClearAllCellHighlights();
        var brush = isValid ? DragOverValidBrush : DragOverInvalidBrush;
        foreach (var c in cellsToHighlight)
        {
            if (_cellBorders.TryGetValue(c, out var b))
                b.Background = brush;
        }

        e.DragEffects = isValid ? DragDropEffects.Move : DragDropEffects.None;
    }

    private CellPosition? ResolveCellAtPointer(DragEventArgs e)
    {
        if (_vm?.CurrentGrid is not { } grid) return null;
        var local = e.GetPosition(CanvasGrid);
        var width = CanvasGrid.Bounds.Width;
        var height = CanvasGrid.Bounds.Height;
        if (width <= 0 || height <= 0) return null;
        if (local.X < 0 || local.Y < 0 || local.X >= width || local.Y >= height) return null;

        var cellWidth = width / grid.Cols;
        var cellHeight = height / grid.Rows;
        var col = Math.Clamp((int)(local.X / cellWidth), 0, grid.Cols - 1);
        var row = Math.Clamp((int)(local.Y / cellHeight), 0, grid.Rows - 1);
        return new CellPosition(col, row);
    }

    private void OnPlacementDragLeave(object? sender, DragEventArgs e)
    {
        ClearDragHighlight();
    }

    private async void OnPlacementDrop(object? sender, DragEventArgs e)
    {
        if (sender is not Border border)
            return;

        ClearDragHighlight();
        ClearAllCellHighlights();
        e.Handled = true;

        if (border.Tag is not PlacementItemViewModel target || _vm is null)
            return;

        var text = e.DataTransfer.TryGetText() ?? string.Empty;
        var src = ResolveDragSource(text);

        // 自分自身の上にドロップした場合はマウス位置のセルを基準に offset 補正する
        // （NxM 配置の「ずらし移動」をサポート）。
        if (src.Kind == DragKind.Placement && src.PlacementSource?.PlacementId == target.PlacementId)
        {
            var cellPos = ResolveCellAtPointer(e);
            if (cellPos is null) return;
            var corrected = ApplyOffset(cellPos.Value, src.Offset);
            if (corrected is null) return;
            await DispatchPositionedDropAsync(src, corrected.Value);
            return;
        }

        // 別配置上 = swap：target.Position をそのまま使う（offset 補正は適用しない）。
        await DispatchPositionedDropAsync(src, new CellPosition(target.GridX, target.GridY));
    }

    private static CellPosition? ApplyOffset(CellPosition mouseCell, GrabOffset offset)
    {
        var nx = mouseCell.X - offset.X;
        var ny = mouseCell.Y - offset.Y;
        if (nx < 0 || ny < 0) return null;
        return new CellPosition(nx, ny);
    }

    private async Task DispatchPositionedDropAsync(DragSourceInfo src, CellPosition position)
    {
        if (_vm is null) return;
        switch (src.Kind)
        {
            case DragKind.Copy when src.CopySource is not null:
                await _vm.PlaceCopyAtAsync(src.CopySource.CopyId, position);
                break;
            case DragKind.Placement when src.PlacementSource is not null:
                await _vm.MoveOrSwapPlacementAsync(src.PlacementSource.PlacementId, position);
                break;
        }
    }

    /// <summary>
    /// D&amp;D ハイライト (Swap 黄 / Invalid 赤) を Adornment (DragHighlightOverlay) で表示する。
    /// 旧実装は placement Border の BorderThickness=4 を直接書き換えていたが layout に参加して
    /// 画像が 4px 内側にズレる問題があったため Adornment 化。
    /// </summary>
    private void ApplyPlacementHighlight(PlacementItemViewModel target, IBrush borderBrush, IBrush background)
    {
        Grid.SetRow(DragHighlightOverlay, target.GridY);
        Grid.SetColumn(DragHighlightOverlay, target.GridX);
        Grid.SetRowSpan(DragHighlightOverlay, Math.Max(1, target.OccupyHeight));
        Grid.SetColumnSpan(DragHighlightOverlay, Math.Max(1, target.OccupyWidth));
        DragHighlightOverlay.BorderBrush = borderBrush;
        DragHighlightOverlay.Background = background;
        DragHighlightOverlay.IsVisible = true;
    }

    /// <summary>D&amp;D ハイライトを消す (DragLeave / Drop 完了時)。</summary>
    private void ClearDragHighlight()
    {
        DragHighlightOverlay.IsVisible = false;
    }

    private void ClearAllCellHighlights()
    {
        foreach (var border in _cellBorders.Values)
            border.Background = Brushes.Transparent;
    }

    // ---------- ヘルパ ----------

    /// <summary>
    /// hover 中セル + 占有サイズから、ハイライトすべきセル群と妥当性を返す。
    /// </summary>
    private (IReadOnlyList<CellPosition> Cells, bool IsValid) AnalyzeHoverRange(
        CellPosition origin, OccupySize occupy, DragSourceInfo src)
        => AnalyzeHoverRangeRaw(origin.X, origin.Y, occupy, src);

    /// <summary>
    /// 左上候補（負も許容）と占有サイズから、ハイライトすべきセル群と妥当性を返す。
    /// オフセット補正で負座標が出るケースに対応するためのオーバーロード。
    /// </summary>
    private (IReadOnlyList<CellPosition> Cells, bool IsValid) AnalyzeHoverRangeRaw(
        int originX, int originY, OccupySize occupy, DragSourceInfo src)
    {
        if (_vm?.CurrentGrid is not { } grid)
            return (Array.Empty<CellPosition>(), false);

        var endX = originX + occupy.Width;
        var endY = originY + occupy.Height;
        var inBounds = originX >= 0 && originY >= 0 && endX <= grid.Cols && endY <= grid.Rows;

        var cells = new List<CellPosition>(occupy.Width * occupy.Height);
        for (var dy = 0; dy < occupy.Height; dy++)
        {
            for (var dx = 0; dx < occupy.Width; dx++)
            {
                var x = originX + dx;
                var y = originY + dy;
                if (x >= 0 && x < grid.Cols && y >= 0 && y < grid.Rows)
                    cells.Add(new CellPosition(x, y));
            }
        }

        if (!inBounds)
            return (cells, false);

        var conflicts = false;
        foreach (var cell in cells)
        {
            var occupant = FindOccupantPlacement(cell);
            if (occupant is null)
                continue;

            // Placement ドラッグで自分自身に重なるのは衝突扱いしない（自己除外）
            if (src.Kind == DragKind.Placement &&
                src.PlacementSource is not null &&
                occupant.PlacementId == src.PlacementSource.PlacementId)
            {
                continue;
            }

            conflicts = true;
            break;
        }

        return (cells, !conflicts);
    }

    private PlacementItemViewModel? FindOccupantPlacement(CellPosition pos)
        => _occupantMap.TryGetValue(pos, out var p) ? p : null;

    private DragSourceInfo ResolveDragSource(string text)
    {
        if (text.StartsWith(CopyPrefix, StringComparison.Ordinal))
        {
            var (id, offset) = ParseIdAndOffset(text[CopyPrefix.Length..]);
            if (id is not null)
            {
                var source = _vm?.Candidates.FirstOrDefault(c => c.CopyId == id.Value);
                return new DragSourceInfo(DragKind.Copy, source?.OccupySize, null, source, offset);
            }
            return new DragSourceInfo(DragKind.Copy, null, null, null, default);
        }

        if (text.StartsWith(PlacementPrefix, StringComparison.Ordinal))
        {
            var (id, offset) = ParseIdAndOffset(text[PlacementPrefix.Length..]);
            if (id is not null)
            {
                var source = _vm?.Placements.FirstOrDefault(p => p.PlacementId == id.Value);
                return new DragSourceInfo(
                    DragKind.Placement,
                    source is null ? null : new OccupySize(source.OccupyWidth, source.OccupyHeight),
                    source,
                    null,
                    offset);
            }
            return new DragSourceInfo(DragKind.Placement, null, null, null, default);
        }
        return new DragSourceInfo(DragKind.Unknown, null, null, null, default);
    }

    private static (Guid? Id, GrabOffset Offset) ParseIdAndOffset(string s)
    {
        var colonIdx = s.IndexOf(':');
        string idStr;
        var offset = default(GrabOffset);
        if (colonIdx < 0)
        {
            idStr = s;
        }
        else
        {
            idStr = s[..colonIdx];
            var rest = s[(colonIdx + 1)..];
            var commaIdx = rest.IndexOf(',');
            if (commaIdx > 0
                && int.TryParse(rest[..commaIdx], out var ox)
                && int.TryParse(rest[(commaIdx + 1)..], out var oy))
            {
                offset = new GrabOffset(ox, oy);
            }
        }
        return Guid.TryParse(idStr, out var id) ? (id, offset) : (null, default);
    }

    private readonly record struct GrabOffset(int X, int Y);

    private readonly record struct DragSourceInfo(
        DragKind Kind,
        OccupySize? OccupySize,
        PlacementItemViewModel? PlacementSource,
        CopyCandidateViewModel? CopySource,
        GrabOffset Offset);

    /// <summary>
    /// Ctrl+Arrow（1px）/ Ctrl+Shift+Arrow（10px）でアクティブ配置の PixelOffset を微調整する。
    /// <para>
    /// 動作条件: Ctrl 修飾必須、矢印キーのみ、<see cref="GridWorkspaceViewModel.SelectedPlacement"/>
    /// が設定済み。Ctrl なしの矢印キーは Inspector の NumericUpDown 等が自然に処理するため
    /// 干渉しない。
    /// </para>
    /// <para>
    /// 仕様: <see cref="PlacementItemViewModel.PixelOffsetX"/> / <c>Y</c> を直接更新するだけ。
    /// Inspector が source の <see cref="PropertyChangedEventArgs"/> を購読していて
    /// IsDirty=true を立てるので、ユーザーが「保存」ボタンを押すまで永続化されない
    /// （Shift+ドラッグ・Inspector 数値直接入力と同じ「編集中バッファ → IsDirty → 保存／Revert」
    /// のフローに統一する設計）。連続押下は 1 履歴エントリ（保存ボタン押下時）にまとまる。
    /// </para>
    /// </summary>
    private void OnUserControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm?.SelectedPlacement is not { } placement)
            return;
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;

        int dx = 0, dy = 0;
        switch (e.Key)
        {
            case Key.Left:  dx = -1; break;
            case Key.Right: dx =  1; break;
            case Key.Up:    dy = -1; break;
            case Key.Down:  dy =  1; break;
            default: return;
        }

        // Shift 同時押しで 10px ステップ（粗調整）
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        dx *= step;
        dy *= step;

        e.Handled = true;

        var max = PlacementInspectorViewModel.MaxPixelOffset;
        placement.PixelOffsetX = Math.Clamp(placement.PixelOffsetX + dx, -max, max);
        placement.PixelOffsetY = Math.Clamp(placement.PixelOffsetY + dy, -max, max);
    }

    private enum DragKind { Unknown, Copy, Placement }

    private sealed class AnonymousObserver<T>(Action<T?> onNext) : IObserver<T?>
    {
        public void OnCompleted() { }
        public void OnError(Exception error) { }
        public void OnNext(T? value) => onNext(value);
    }
}
