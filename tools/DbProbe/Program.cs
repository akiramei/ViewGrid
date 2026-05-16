using Microsoft.EntityFrameworkCore;
using ViewGrid.Infrastructure.Persistence;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/DbProbe -- <path-to-viewgrid.db>");
    return 1;
}

var dbPath = args[0];
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"DB not found: {dbPath}");
    return 1;
}

Console.WriteLine($"=== Inspecting {dbPath} ===");
var fi = new FileInfo(dbPath);
Console.WriteLine($"  Size: {fi.Length} bytes");
Console.WriteLine($"  LastWrite: {fi.LastWriteTime}");
foreach (var aux in new[] { dbPath + "-wal", dbPath + "-shm" })
{
    if (File.Exists(aux))
    {
        var afi = new FileInfo(aux);
        Console.WriteLine($"  Aux: {Path.GetFileName(aux)} {afi.Length} bytes / {afi.LastWriteTime}");
    }
}

var opt = new DbContextOptionsBuilder<ViewGridDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
using var db = new ViewGridDbContext(opt);

Console.WriteLine();
Console.WriteLine("=== Counts ===");
Console.WriteLine($"  ImageAssets: {db.ImageAssets.Count()}");
Console.WriteLine($"  ImageCopies: {db.ImageCopies.Count()}");
Console.WriteLine($"  ProtectedRegions: {db.ProtectedRegions.Count()}");
Console.WriteLine($"  GridCanvases: {db.GridCanvases.Count()}");
Console.WriteLine($"  GridPlacements: {db.GridPlacements.Count()}");

Console.WriteLine();
Console.WriteLine("=== ImageAssets (first 20) ===");
foreach (var a in db.ImageAssets.AsNoTracking().Take(20))
{
    Console.WriteLine($"  {a.Id} | {a.OriginalFilename,-30} | hash={a.FileHash[..16]}... | created={a.CreatedAt:yyyy-MM-dd HH:mm:ss}");
}

return 0;
