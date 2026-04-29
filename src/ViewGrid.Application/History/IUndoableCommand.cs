using System;
using System.Threading;
using System.Threading.Tasks;
using ErrorOr;

namespace ViewGrid.Application.History;

/// <summary>
/// Undo/Redo 履歴に積める操作を表す。1 操作 = 1 Command で atomic な単位とする。
/// 実装クラスは <see cref="ExecuteAsync"/> で正方向（新規実行 / Redo の双方）、
/// <see cref="UndoAsync"/> で逆方向の状態変化を行う。
/// </summary>
public interface IUndoableCommand
{
    /// <summary>
    /// UI 表示用の説明文（履歴 UI の各エントリ / メニューの「元に戻す: 配置: 「コピー1」→ (1,1)」等）。
    /// <para>
    /// <b>構築時に確定される必要がある</b>: 参照する Copy/Grid 等が後から削除されても表示が崩れないよう、
    /// 呼び出し側 VM が「操作時点の表示文字列」を組み立てて Command コンストラクタに渡す設計。
    /// </para>
    /// </summary>
    string Description { get; }

    /// <summary>
    /// この Command が影響するグリッドの ID。Undo/Redo 時に <see cref="IUndoRedoService"/> が
    /// 必要に応じてアクティブグリッドを切り替えるために利用する。グリッドに紐付かない操作
    /// （例: 共有特性編集）の場合は <c>null</c> を返す。
    /// </summary>
    Guid? AffectedGridId { get; }

    /// <summary>正方向適用。新規実行・Redo の双方で呼ばれる。</summary>
    Task<ErrorOr<Success>> ExecuteAsync(CancellationToken ct = default);

    /// <summary>逆方向適用。直前の <see cref="ExecuteAsync"/> 完了状態を取り消す。</summary>
    Task<ErrorOr<Success>> UndoAsync(CancellationToken ct = default);
}
