using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IImageCopyRepository
{
    Task<IReadOnlyList<ImageCopy>> FindAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ImageCopy>> FindByAssetIdAsync(Guid assetId, CancellationToken ct = default);
    Task<ImageCopy?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ErrorOr<ImageCopy>> AddAsync(ImageCopy copy, CancellationToken ct = default);
    Task<ErrorOr<Success>> UpdateAsync(ImageCopy copy, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);
}
