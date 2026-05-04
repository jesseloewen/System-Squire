using System;
using System.Reflection;

namespace SystemSquire
{
    internal static class BuildVersion
    {
        private static readonly Lazy<string> DisplayVersion = new(CreateDisplayVersion);

        public static string Display => DisplayVersion.Value;

        private static string CreateDisplayVersion()
        {
            Assembly assembly = typeof(BuildVersion).Assembly;
            string? informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            string? normalizedInformationalVersion = NormalizeInformationalVersion(informationalVersion);
            if (!string.IsNullOrWhiteSpace(normalizedInformationalVersion))
            {
                return normalizedInformationalVersion;
            }

            Version? assemblyVersion = assembly.GetName().Version;
            if (assemblyVersion == null)
            {
                return "unknown";
            }

            if (assemblyVersion.Build >= 0)
            {
                return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build}";
            }

            return $"{assemblyVersion.Major}.{assemblyVersion.Minor}";
        }

        private static string? NormalizeInformationalVersion(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string normalized = value.Trim();
            int metadataSeparatorIndex = normalized.IndexOf('+');
            if (metadataSeparatorIndex >= 0)
            {
                normalized = normalized[..metadataSeparatorIndex].Trim();
            }

            return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
        }
    }
}
