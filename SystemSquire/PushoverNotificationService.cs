using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SystemSquire
{
    /// <summary>
    /// Sends Pushover notifications when enabled in configuration.
    /// </summary>
    public sealed class PushoverNotificationService
    {
        private static readonly HttpClient HttpClient = new();
        private const string PushoverApiUrl = "https://api.pushover.net/1/messages.json";

        public bool IsReady(PushoverConfig? config)
        {
            return config != null &&
                   config.Enabled &&
                   !string.IsNullOrWhiteSpace(config.ApiToken) &&
                   !string.IsNullOrWhiteSpace(config.UserKey);
        }

        public async Task<bool> SendAsync(
            PushoverConfig? config,
            string title,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (!IsReady(config))
            {
                return false;
            }

            try
            {
                using var payload = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = config!.ApiToken.Trim(),
                    ["user"] = config.UserKey.Trim(),
                    ["title"] = title,
                    ["message"] = message
                });

                using HttpResponseMessage response = await HttpClient
                    .PostAsync(PushoverApiUrl, payload, cancellationToken)
                    .ConfigureAwait(false);

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Pushover send failed: {ex.Message}");
                return false;
            }
        }
    }
}
