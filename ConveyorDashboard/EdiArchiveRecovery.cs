using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

// Temporary, authenticated recovery of one preverified archive bundle; removed after use.
static class EdiArchiveRecovery
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public static void Map(WebApplication app) => app.MapPost("/api/maintenance/restore-forecast-archives", async (HttpRequest request, IWebHostEnvironment environment) =>
    {
        var token = request.Headers.Authorization.ToString();
        if (!token.StartsWith("Bearer ", StringComparison.Ordinal) || !CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(token[7..])), Convert.FromHexString("DF1926C92A406BC0DF03DE15B72C966A039AF5F76361256550FABDA8C4EEAD38")))
            return Results.Unauthorized();
        if (request.ContentLength is null or > 2097152) return Results.BadRequest("Invalid bundle size.");
        using var bytes = new MemoryStream();
        await request.Body.CopyToAsync(bytes);
        if (Convert.ToHexString(SHA256.HashData(bytes.ToArray())) != "51ABA1C1F6BDDA104C397C48B773A4E9A6C0BAE7BB6FEBB6E4740FE13CDF4FDB") return Results.BadRequest("Unexpected bundle.");
        await Gate.WaitAsync();
        try
        {
            var root = Path.GetFullPath(Environment.GetEnvironmentVariable("EDI_FORECAST_PATH") ?? Path.Combine(environment.ContentRootPath, "App_Data", "edi-forecasts"));
            var backup = Path.Combine(Path.GetDirectoryName(root)!, "recovery-backups", "2026-09-13");
            Directory.CreateDirectory(backup);
            var zipBackup = Path.Combine(backup, "original.zip");
            if (!File.Exists(zipBackup)) await File.WriteAllBytesAsync(zipBackup, bytes.ToArray());
            bytes.Position = 0;
            using var zip = new ZipArchive(bytes, ZipArchiveMode.Read);
            var restored = 0; var existing = 0; var conflicts = 0;
            foreach (var entry in zip.Entries)
            {
                var destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal) || !destination.EndsWith(".json", StringComparison.Ordinal))
                    return Results.BadRequest("Invalid archive path.");
                using var content = new MemoryStream();
                using (var input = entry.Open()) await input.CopyToAsync(content);
                var fileBytes = content.ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                if (!File.Exists(destination))
                {
                    var temporary = destination + ".recovery-" + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        await File.WriteAllBytesAsync(temporary, fileBytes);
                        try { File.Move(temporary, destination, false); restored++; continue; }
                        catch (IOException) when (File.Exists(destination)) { }
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                if ((await File.ReadAllBytesAsync(destination)).AsSpan().SequenceEqual(fileBytes)) existing++;
                else
                {
                    var conflict = Path.Combine(backup, entry.FullName);
                    Directory.CreateDirectory(Path.GetDirectoryName(conflict)!);
                    if (!File.Exists(conflict)) await File.WriteAllBytesAsync(conflict, fileBytes);
                    conflicts++;
                }
            }
            return Results.Ok(new { restored, existing, conflicts });
        }
        finally { Gate.Release(); }
    });
}
