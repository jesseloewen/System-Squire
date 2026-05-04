using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SystemSquire
{
    internal sealed class UpdateReleaseInfo
    {
        public string TagName { get; init; } = string.Empty;
        public string VersionText { get; init; } = string.Empty;
        public Version ParsedVersion { get; init; } = new(0, 0);
        public string AssetName { get; init; } = string.Empty;
        public string DownloadUrl { get; init; } = string.Empty;
    }

    internal sealed class UpdateCheckResult
    {
        public bool Success { get; init; }
        public bool UpdateAvailable { get; init; }
        public string Message { get; init; } = string.Empty;
        public string CurrentVersionText { get; init; } = string.Empty;
        public string LatestVersionText { get; init; } = string.Empty;
        public UpdateReleaseInfo? LatestRelease { get; init; }
    }

    internal sealed class UpdateDownloadResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string DownloadedFilePath { get; init; } = string.Empty;
    }

    internal sealed class GitHubUpdateService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly string _latestReleaseApiUrl;

        public GitHubUpdateService(string owner, string repo)
        {
            _latestReleaseApiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
        }

        public string UpdateDirectoryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SystemSquire",
            "Updates");

        public async Task<UpdateCheckResult> CheckForUpdateAsync(string currentVersionText, CancellationToken cancellationToken = default)
        {
            if (!TryParseVersion(currentVersionText, out Version currentVersion, out string normalizedCurrentVersion))
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = "Unable to determine current app version.",
                    CurrentVersionText = currentVersionText
                };
            }

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _latestReleaseApiUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                using HttpResponseMessage response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = "GitHub rate limit reached. Try again shortly.",
                        CurrentVersionText = normalizedCurrentVersion
                    };
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = $"GitHub update check failed ({(int)response.StatusCode}).",
                        CurrentVersionText = normalizedCurrentVersion
                    };
                }

                await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                GitHubReleasePayload? payload = await JsonSerializer
                    .DeserializeAsync<GitHubReleasePayload>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (payload == null || string.IsNullOrWhiteSpace(payload.TagName))
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = "GitHub release payload is missing tag information.",
                        CurrentVersionText = normalizedCurrentVersion
                    };
                }

                if (!TryParseVersion(payload.TagName, out Version latestVersion, out string normalizedLatestVersion))
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        Message = $"GitHub tag '{payload.TagName}' is not a supported version format.",
                        CurrentVersionText = normalizedCurrentVersion
                    };
                }

                bool updateAvailable = latestVersion > currentVersion;
                if (!updateAvailable)
                {
                    return new UpdateCheckResult
                    {
                        Success = true,
                        UpdateAvailable = false,
                        Message = $"System Squire is up to date (v{normalizedCurrentVersion}).",
                        CurrentVersionText = normalizedCurrentVersion,
                        LatestVersionText = normalizedLatestVersion
                    };
                }

                GitHubAssetPayload? asset = SelectInstallerAsset(payload.Assets, normalizedLatestVersion);
                if (asset == null)
                {
                    return new UpdateCheckResult
                    {
                        Success = false,
                        UpdateAvailable = true,
                        Message = $"Update v{normalizedLatestVersion} found, but no installer asset is available.",
                        CurrentVersionText = normalizedCurrentVersion,
                        LatestVersionText = normalizedLatestVersion
                    };
                }

                return new UpdateCheckResult
                {
                    Success = true,
                    UpdateAvailable = true,
                    Message = $"Update available: v{normalizedLatestVersion}.",
                    CurrentVersionText = normalizedCurrentVersion,
                    LatestVersionText = normalizedLatestVersion,
                    LatestRelease = new UpdateReleaseInfo
                    {
                        TagName = payload.TagName,
                        VersionText = normalizedLatestVersion,
                        ParsedVersion = latestVersion,
                        AssetName = asset.Name ?? string.Empty,
                        DownloadUrl = asset.BrowserDownloadUrl ?? string.Empty
                    }
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Success = false,
                    Message = $"Unable to check updates: {ex.Message}",
                    CurrentVersionText = normalizedCurrentVersion
                };
            }
        }

        public async Task<UpdateDownloadResult> DownloadInstallerAsync(UpdateReleaseInfo release, CancellationToken cancellationToken = default)
        {
            if (release == null || string.IsNullOrWhiteSpace(release.DownloadUrl))
            {
                return new UpdateDownloadResult
                {
                    Success = false,
                    Message = "No release installer URL is available."
                };
            }

            string outputDirectory = UpdateDirectoryPath;
            string outputPath = GetExpectedInstallerPath(release);
            string tempPath = outputPath + ".download";

            try
            {
                Directory.CreateDirectory(outputDirectory);

                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
                using HttpResponseMessage response = await HttpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateDownloadResult
                    {
                        Success = false,
                        Message = $"Installer download failed ({(int)response.StatusCode})."
                    };
                }

                await using (FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await using Stream responseStream = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);

                    await responseStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                }

                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }

                File.Move(tempPath, outputPath);

                return new UpdateDownloadResult
                {
                    Success = true,
                    Message = $"Update installer downloaded: {Path.GetFileName(outputPath)}",
                    DownloadedFilePath = outputPath
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                TryDeleteFile(tempPath);

                return new UpdateDownloadResult
                {
                    Success = false,
                    Message = $"Unable to download installer: {ex.Message}"
                };
            }
        }

        public int DeleteDownloadedInstallerFiles()
        {
            if (!Directory.Exists(UpdateDirectoryPath))
            {
                return 0;
            }

            int deletedCount = 0;
            IEnumerable<string> files = Directory
                .EnumerateFiles(UpdateDirectoryPath, "*", SearchOption.TopDirectoryOnly)
                .Where(path => IsUpdateArtifact(path));

            foreach (string filePath in files)
            {
                try
                {
                    File.Delete(filePath);
                    deletedCount++;
                }
                catch
                {
                    // Ignore per-file cleanup failures.
                }
            }

            return deletedCount;
        }

        public int GetDownloadedInstallerFileCount()
        {
            if (!Directory.Exists(UpdateDirectoryPath))
            {
                return 0;
            }

            return Directory
                .EnumerateFiles(UpdateDirectoryPath, "*", SearchOption.TopDirectoryOnly)
                .Count(path => IsUpdateArtifact(path));
        }

        public int DeleteDownloadedInstallerFilesAtOrBelowVersion(string currentVersionText)
        {
            if (!TryParseVersion(currentVersionText, out Version currentVersion, out _))
            {
                return 0;
            }

            if (!Directory.Exists(UpdateDirectoryPath))
            {
                return 0;
            }

            int deletedCount = 0;

            foreach (string filePath in Directory.EnumerateFiles(UpdateDirectoryPath, "*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(filePath);

                if (string.Equals(extension, ".download", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryDeleteUpdateFile(filePath))
                    {
                        deletedCount++;
                    }

                    continue;
                }

                if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryGetInstallerVersion(filePath, out Version installerVersion))
                {
                    continue;
                }

                if (installerVersion <= currentVersion && TryDeleteUpdateFile(filePath))
                {
                    deletedCount++;
                }
            }

            return deletedCount;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("SystemSquire-Updater/1.0");
            return client;
        }

        private static bool TryParseVersion(string versionText, out Version version, out string normalizedVersionText)
        {
            version = new Version(0, 0);
            normalizedVersionText = string.Empty;

            if (string.IsNullOrWhiteSpace(versionText))
            {
                return false;
            }

            string candidate = versionText.Trim();
            if (candidate.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                candidate = candidate[1..];
            }

            int metadataIndex = candidate.IndexOf('+');
            if (metadataIndex >= 0)
            {
                candidate = candidate[..metadataIndex];
            }

            int prereleaseIndex = candidate.IndexOf('-');
            if (prereleaseIndex >= 0)
            {
                candidate = candidate[..prereleaseIndex];
            }

            string[] numericParts = candidate
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(4)
                .ToArray();

            if (numericParts.Length < 2)
            {
                return false;
            }

            var parsedParts = new List<int>(numericParts.Length);
            foreach (string part in numericParts)
            {
                if (!int.TryParse(part, out int parsedPart) || parsedPart < 0)
                {
                    return false;
                }

                parsedParts.Add(parsedPart);
            }

            while (parsedParts.Count < 3)
            {
                parsedParts.Add(0);
            }

            version = parsedParts.Count switch
            {
                2 => new Version(parsedParts[0], parsedParts[1]),
                3 => new Version(parsedParts[0], parsedParts[1], parsedParts[2]),
                _ => new Version(parsedParts[0], parsedParts[1], parsedParts[2], parsedParts[3])
            };

            normalizedVersionText = parsedParts.Count >= 3
                ? $"{parsedParts[0]}.{parsedParts[1]}.{parsedParts[2]}"
                : $"{parsedParts[0]}.{parsedParts[1]}";

            return true;
        }

        private static GitHubAssetPayload? SelectInstallerAsset(IReadOnlyCollection<GitHubAssetPayload>? assets, string normalizedVersion)
        {
            if (assets == null || assets.Count == 0)
            {
                return null;
            }

            string expectedAssetName = $"SystemSquireSetup-{normalizedVersion}.exe";

            GitHubAssetPayload? exact = assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name) &&
                string.Equals(asset.Name, expectedAssetName, StringComparison.OrdinalIgnoreCase));

            if (exact != null && !string.IsNullOrWhiteSpace(exact.BrowserDownloadUrl))
            {
                return exact;
            }

            return assets.FirstOrDefault(asset =>
                !string.IsNullOrWhiteSpace(asset.Name) &&
                asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl));
        }

        private static string GetSafeInstallerFileName(string assetName, string versionText)
        {
            string fallbackName = $"SystemSquireSetup-{versionText}.exe";
            string candidate = string.IsNullOrWhiteSpace(assetName)
                ? fallbackName
                : assetName.Trim();

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                candidate = candidate.Replace(invalidChar, '_');
            }

            if (!candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                candidate += ".exe";
            }

            return candidate;
        }

        private string GetExpectedInstallerPath(UpdateReleaseInfo release)
        {
            string safeAssetName = GetSafeInstallerFileName(release.AssetName, release.VersionText);
            return Path.Combine(UpdateDirectoryPath, safeAssetName);
        }

        public string? GetDownloadedInstallerPath(UpdateReleaseInfo? release)
        {
            if (release == null)
            {
                return null;
            }

            string expectedPath = GetExpectedInstallerPath(release);
            if (File.Exists(expectedPath))
            {
                return expectedPath;
            }

            if (!Directory.Exists(UpdateDirectoryPath))
            {
                return null;
            }

            string versionPrefix = $"SystemSquireSetup-{release.VersionText}";
            return Directory
                .EnumerateFiles(UpdateDirectoryPath, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path =>
                    Path.GetFileName(path).StartsWith(versionPrefix, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUpdateArtifact(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return string.Equals(extension, ".download", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetInstallerVersion(string filePath, out Version version)
        {
            version = new Version(0, 0);

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
            const string setupPrefix = "SystemSquireSetup-";

            if (fileNameWithoutExtension.StartsWith(setupPrefix, StringComparison.OrdinalIgnoreCase))
            {
                string suffix = fileNameWithoutExtension[setupPrefix.Length..];
                string candidateVersion = new(suffix
                    .TakeWhile(ch => char.IsDigit(ch) || ch == '.')
                    .ToArray());

                if (!string.IsNullOrWhiteSpace(candidateVersion) &&
                    TryParseVersion(candidateVersion, out Version parsedVersion, out _))
                {
                    version = parsedVersion;
                    return true;
                }
            }

            try
            {
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                string? candidate = !string.IsNullOrWhiteSpace(versionInfo.ProductVersion)
                    ? versionInfo.ProductVersion
                    : versionInfo.FileVersion;

                if (!string.IsNullOrWhiteSpace(candidate) &&
                    TryParseVersion(candidate, out Version parsedVersion, out _))
                {
                    version = parsedVersion;
                    return true;
                }
            }
            catch
            {
                // Ignore version extraction failures and keep file if version cannot be determined.
            }

            return false;
        }

        private static bool TryDeleteUpdateFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private sealed class GitHubReleasePayload
        {
            [JsonPropertyName("tag_name")]
            public string? TagName { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAssetPayload> Assets { get; set; } = new();
        }

        private sealed class GitHubAssetPayload
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string? BrowserDownloadUrl { get; set; }
        }
    }
}
