// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Noel Danjou

using System.Security.Cryptography;

namespace SvgSquarer;

// Stores a pristine copy of a file before it is modified in place, mirroring
// the scanned root's relative folder structure under a backup root. Writes
// are content-addressed by hash: re-running the tool never overwrites a
// backup that already holds the same original bytes, and a genuine name
// collision (different content at the same relative path) is disambiguated
// with a hash suffix instead of being silently overwritten.
internal sealed class BackupStore(string backupRoot)
{
    public void BackupIfNeeded(string relativePath, byte[] originalBytes, bool dryRun)
    {
        var targetPath = Path.Combine(backupRoot, relativePath);
        var newHash = ComputeHash(originalBytes);

        if (File.Exists(targetPath))
        {
            var existingHash = ComputeHash(File.ReadAllBytes(targetPath));
            if (existingHash == newHash)
            {
                return;
            }

            targetPath = WithHashSuffix(targetPath, newHash);
            if (File.Exists(targetPath))
            {
                return;
            }
        }

        if (dryRun)
        {
            return;
        }

        FileSystemHelper.EnsureDirectoryExists(targetPath);
        File.WriteAllBytes(targetPath, originalBytes);
    }

    private static string WithHashSuffix(string path, string hash)
    {
        var directory = Path.GetDirectoryName(path);
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var suffixed = $"{name}.{hash[..8]}{extension}";

        return string.IsNullOrEmpty(directory) ? suffixed : Path.Combine(directory, suffixed);
    }

    private static string ComputeHash(byte[] content)
    {
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }
}
