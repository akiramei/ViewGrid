using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ViewGrid.Core.Services;
using ViewGrid.Core.Settings;

namespace ViewGrid.Infrastructure.Services;

/// <summary>
/// <see cref="AppSettings"/> を <c>{DataDirectory}/settings.json</c> に永続化する実装。
/// 起動時にコンストラクタ内で同期読み込み、 失敗時 (ファイル不在 / JSON 破損) は既定値で復帰。
/// 既定値で復帰した場合は次回 <see cref="SaveAsync"/> 時に正常な JSON で上書きされる。
/// </summary>
internal sealed partial class JsonAppSettingsService : IAppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _filePath;
    private readonly ILogger<JsonAppSettingsService> _logger;
    private AppSettings _current;

    public JsonAppSettingsService(StorageOptions options, ILogger<JsonAppSettingsService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _filePath = Path.Combine(options.DataDirectory, "settings.json");
        _current = LoadOrDefault();
    }

    public AppSettings Current => _current;

    public event EventHandler<AppSettings>? Changed;

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);

        _current = settings;
        Changed?.Invoke(this, settings);
    }

    private AppSettings LoadOrDefault()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return loaded ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 破損 / 読み取り失敗時は既定値で続行 (次回 Save で正常な JSON に上書きされる)
            LogSettingsLoadFailed(_logger, _filePath, ex);
            return new AppSettings();
        }
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Warning,
        Message = "settings.json の読み込みに失敗しました。 既定値で起動します。 path={Path}")]
    private static partial void LogSettingsLoadFailed(ILogger logger, string path, Exception exception);
}
