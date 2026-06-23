namespace ClientManagementSystem.Services;

public sealed class UserPhotoFileStorage : IUserPhotoStorage
{
    public const long MaxPhotoBytes = 5 * 1024 * 1024;

    private readonly string _root;

    public UserPhotoFileStorage(string contentRootPath)
    {
        _root = Path.Combine(contentRootPath, "App_Data", "UserPhotos");
        Directory.CreateDirectory(_root);
    }

    public bool TryGetPhotoFile(int userId, out string physicalPath, out string contentType)
    {
        physicalPath = "";
        contentType = "";

        var matches = Directory.GetFiles(_root, $"{userId}.*");
        if (matches.Length == 0)
        {
            return false;
        }

        Array.Sort(matches, StringComparer.OrdinalIgnoreCase);
        physicalPath = matches[0];
        var ext = Path.GetExtension(physicalPath);
        contentType = MimeFromExtension(ext);
        return true;
    }

    public async Task SavePhotoAsync(int userId, Stream stream, long? declaredLength, CancellationToken cancellationToken)
    {
        if (declaredLength is > MaxPhotoBytes)
        {
            throw new InvalidOperationException($"Photo exceeds maximum size ({MaxPhotoBytes / 1024 / 1024} MB).");
        }

        await using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxPhotoBytes)
            {
                throw new InvalidOperationException($"Photo exceeds maximum size ({MaxPhotoBytes / 1024 / 1024} MB).");
            }

            await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (ms.Length == 0)
        {
            throw new InvalidOperationException("The uploaded file was empty.");
        }

        var bytes = ms.ToArray();
        if (!TryInferImage(bytes.AsSpan(), out var extension))
        {
            throw new InvalidOperationException("Upload must be a JPEG, PNG, GIF, or WebP image.");
        }

        DeletePhotos(userId);
        var path = Path.Combine(_root, $"{userId}{extension}");
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
    }

    public void DeletePhotos(int userId)
    {
        foreach (var path in Directory.GetFiles(_root, $"{userId}.*"))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // ignore — best effort (file may be locked briefly)
            }
            catch (UnauthorizedAccessException)
            {
                // ignore
            }
        }
    }

    public void CopyPhotos(int sourceUserId, int destUserId)
    {
        DeletePhotos(destUserId);
        var sources = Directory.GetFiles(_root, $"{sourceUserId}.*");
        if (sources.Length == 0)
        {
            return;
        }

        Array.Sort(sources, StringComparer.OrdinalIgnoreCase);
        var src = sources[0];
        var ext = Path.GetExtension(src);
        var destPath = Path.Combine(_root, $"{destUserId}{ext}");
        File.Copy(src, destPath, overwrite: true);
    }

    private static string MimeFromExtension(string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream",
        };
    }

    private static bool TryInferImage(ReadOnlySpan<byte> data, out string extension)
    {
        extension = "";

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            extension = ".jpg";
            return true;
        }

        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G')
        {
            extension = ".png";
            return true;
        }

        if (data.Length >= 6
            && data[0] == (byte)'G' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'8')
        {
            extension = ".gif";
            return true;
        }

        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            extension = ".webp";
            return true;
        }

        return false;
    }
}
