using System.Text;
using System.Text.Json;

namespace VerticalDistrict.Core.Persistence;

/// <summary>Same-directory flushed replacement with a recoverable previous save.</summary>
public static class SaveFileStore
{
    public static void Save(string path, string validatedJson, Func<string, bool>? validator = null)
    {
        ValidateDocument(validatedJson, validator, "New save");
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"Save directory does not exist: {directory}");
        // Validate the old document before allowing it to replace a known-good backup.
        if (File.Exists(target)) ValidateDocument(File.ReadAllText(target), validator, "Existing save");
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        try
        {
            WriteFlushed(temporary, validatedJson);
            if (File.Exists(target))
            {
                try { File.Replace(temporary, target, target + ".bak", ignoreMetadataErrors: true); }
                catch (PlatformNotSupportedException) { ReplaceWithoutFileReplace(temporary, target); }
            }
            else File.Move(temporary, target);
        }
        finally
        {
            // Delete only the exact temporary file created by this operation.
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public static string Load(string path) => File.ReadAllText(Path.GetFullPath(path));
    public static string LoadBackup(string path) => File.ReadAllText(Path.GetFullPath(path) + ".bak");

    /// <summary>The caller persists or derives the next slot at a consistent simulation boundary.</summary>
    public static string SaveAutosave(string directory, string baseName, int slot, string validatedJson,
        Func<string, bool>? validator = null)
    {
        if (slot is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(slot), "Autosave slot must be 0, 1, or 2.");
        if (string.IsNullOrWhiteSpace(baseName) || baseName != Path.GetFileName(baseName)
            || baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("Autosave name must be a plain filename.", nameof(baseName));
        var path = Path.Combine(Path.GetFullPath(directory), $"{baseName}.autosave-{slot + 1}.json");
        Save(path, validatedJson, validator);
        return path;
    }

    private static void ValidateDocument(string json, Func<string, bool>? validator, string label)
    {
        try { StrictJson.Validate(json); }
        catch (JsonException error) { throw new SaveValidationException($"{label} is invalid JSON: {error.Message}"); }
        if (validator is not null && !validator(json)) throw new SaveValidationException($"{label} failed validation; files were preserved.");
    }

    private static void WriteFlushed(string path, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, options: FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceWithoutFileReplace(string temporary, string target)
    {
        var backupTemporary = target + $".{Guid.NewGuid():N}.backup.tmp";
        try
        {
            WriteFlushed(backupTemporary, File.ReadAllText(target));
            File.Move(backupTemporary, target + ".bak", overwrite: true);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(backupTemporary)) File.Delete(backupTemporary);
        }
    }
}
