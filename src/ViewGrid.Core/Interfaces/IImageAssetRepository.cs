using ErrorOr;
using ViewGrid.Core.Entities;

namespace ViewGrid.Core.Interfaces;

public interface IImageAssetRepository
{
    Task<IReadOnlyList<ImageAsset>> FindAllAsync(CancellationToken ct = default);
    Task<ImageAsset?> FindByIdAsync(Guid id, CancellationToken ct = default);
    Task<ImageAsset?> FindByHashAsync(string fileHash, CancellationToken ct = default);
    Task<ErrorOr<ImageAsset>> AddAsync(ImageAsset asset, CancellationToken ct = default);
    Task<ErrorOr<Success>> DeleteAsync(Guid id, CancellationToken ct = default);
}
