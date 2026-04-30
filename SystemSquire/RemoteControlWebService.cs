using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SystemSquire
{
    public sealed class RemoteControlWebService : IDisposable
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private readonly Func<Task<RemoteControlState>> _getStateAsync;
        private readonly Func<Task<RemoteOperationResponse>> _triggerShutdownAsync;
        private readonly Func<Task<RemoteOperationResponse>> _triggerBlackoutAsync;
        private readonly Func<Task<RemoteOperationResponse>> _triggerLockDesktopAsync;
        private readonly Func<RemoteConfigUpdateRequest, Task<RemoteOperationResponse>> _saveConfigAsync;
        private readonly Func<RemoteWebAuthSettings> _getWebAuthSettings;
        private readonly Func<string, bool> _verifyWebPassword;
        private readonly object _syncRoot = new();

        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _listenLoopTask;

        public event EventHandler<string>? ServiceStatusChanged;

        public bool IsRunning
        {
            get
            {
                lock (_syncRoot)
                {
                    return _listener?.IsListening == true;
                }
            }
        }

        public int Port { get; private set; }

        public bool IsRemoteAccessible { get; private set; }

        public string BaseUrl => Port > 0 ? $"http://0.0.0.0:{Port}/" : "http://0.0.0.0:7745/";

        public string LocalBaseUrl => Port > 0 ? $"http://localhost:{Port}/" : "http://localhost:7745/";

        public RemoteControlWebService(
            Func<Task<RemoteControlState>> getStateAsync,
            Func<Task<RemoteOperationResponse>> triggerShutdownAsync,
            Func<Task<RemoteOperationResponse>> triggerBlackoutAsync,
            Func<Task<RemoteOperationResponse>> triggerLockDesktopAsync,
            Func<RemoteConfigUpdateRequest, Task<RemoteOperationResponse>> saveConfigAsync,
            Func<RemoteWebAuthSettings> getWebAuthSettings,
            Func<string, bool> verifyWebPassword)
        {
            _getStateAsync = getStateAsync;
            _triggerShutdownAsync = triggerShutdownAsync;
            _triggerBlackoutAsync = triggerBlackoutAsync;
            _triggerLockDesktopAsync = triggerLockDesktopAsync;
            _saveConfigAsync = saveConfigAsync;
            _getWebAuthSettings = getWebAuthSettings;
            _verifyWebPassword = verifyWebPassword;
        }

        public bool Start(int port, out string message)
        {
            lock (_syncRoot)
            {
                message = string.Empty;

                if (_listener?.IsListening == true)
                {
                    message = $"Web service is already running at {LocalBaseUrl} (LAN: {BaseUrl}).";
                    return true;
                }

                if (port is < 1 or > 65535)
                {
                    message = "Port must be between 1 and 65535.";
                    return false;
                }

                HttpListener? listener;
                bool started = false;

                if (TryCreateAndStartListener(port, "+", out listener, out string wildcardBindError))
                {
                    IsRemoteAccessible = true;
                    message = $"Web service started at http://localhost:{port}/ and is available on LAN via http://0.0.0.0:{port}/.";
                    started = true;
                }
                else
                {
                    string? urlAclSetupMessage = null;
                    string? wildcardRetryError = null;

                    if (IsAccessDeniedError(wildcardBindError))
                    {
                        bool aclReady = TryEnsureUrlAclForWildcardPrefix(port, out string aclAttemptMessage);
                        urlAclSetupMessage = aclAttemptMessage;
                        string retryError = string.Empty;

                        if (aclReady && TryCreateAndStartListener(port, "+", out listener, out retryError))
                        {
                            IsRemoteAccessible = true;
                            message =
                                $"Web service started at http://localhost:{port}/ and is available on LAN via http://0.0.0.0:{port}/. " +
                                "Wildcard binding was enabled after URL ACL setup.";
                            started = true;
                        }
                        else
                        {
                            wildcardRetryError = retryError;
                        }
                    }

                    if (!started)
                    {
                        string[] hostnameFallbackHosts = GetHostnameFallbackHosts();
                        string hostnameFallbackError = string.Empty;
                        bool startedWithHostnameFallback = hostnameFallbackHosts.Length > 0 &&
                            TryCreateAndStartListener(port, hostnameFallbackHosts, out listener, out hostnameFallbackError);

                        if (!startedWithHostnameFallback &&
                            !TryCreateAndStartListener(port, "localhost", out listener, out string localhostError))
                        {
                            message =
                                $"Failed to start web service. Wildcard binding error: {wildcardBindError} " +
                                (string.IsNullOrWhiteSpace(wildcardRetryError)
                                    ? string.Empty
                                    : $"Wildcard retry error: {wildcardRetryError} ") +
                                (string.IsNullOrWhiteSpace(urlAclSetupMessage)
                                    ? string.Empty
                                    : $"URL ACL setup: {urlAclSetupMessage} ") +
                                $"Hostname fallback error: {hostnameFallbackError} " +
                                $"Localhost binding error: {localhostError}";
                            return false;
                        }

                        if (startedWithHostnameFallback)
                        {
                            IsRemoteAccessible = true;
                            message =
                                $"Web service started at http://localhost:{port}/ and is available on this machine via " +
                                $"http://{hostnameFallbackHosts[0]}:{port}/.";
                        }
                        else
                        {
                            IsRemoteAccessible = false;
                            message =
                                $"Web service started on localhost only at http://localhost:{port}/." +
                                (string.IsNullOrWhiteSpace(urlAclSetupMessage)
                                    ? string.Empty
                                    : $" {urlAclSetupMessage}");
                        }

                        started = true;
                    }
                }

                if (!started || listener == null)
                {
                    message = "Web service failed to start for an unknown reason.";
                    return false;
                }

                Port = port;
                _listener = listener;
                _cts = new CancellationTokenSource();
                _listenLoopTask = Task.Run(() => ListenLoopAsync(listener!, _cts.Token));
                ServiceStatusChanged?.Invoke(this, message);
                return true;
            }
        }

        public void Stop()
        {
            lock (_syncRoot)
            {
                if (_listener == null)
                {
                    return;
                }

                try
                {
                    _cts?.Cancel();
                    if (_listener.IsListening)
                    {
                        _listener.Stop();
                    }

                    _listener.Close();
                }
                catch
                {
                    // Ignore cleanup exceptions.
                }
                finally
                {
                    _listener = null;
                    _cts?.Dispose();
                    _cts = null;
                    _listenLoopTask = null;
                    IsRemoteAccessible = false;
                    ServiceStatusChanged?.Invoke(this, "Web service stopped.");
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private static bool IsAccessDeniedError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            return error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Error: 5", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryEnsureUrlAclForWildcardPrefix(int port, out string message)
        {
            string wildcardUrl = $"http://+:{port}/";

            if (HasUrlAclReservation(wildcardUrl))
            {
                message = $"URL ACL already exists for {wildcardUrl}.";
                return true;
            }

            string currentUser = $"{Environment.UserDomainName}\\{Environment.UserName}";
            string addArgs = $"http add urlacl url={wildcardUrl} user=\"{currentUser}\"";

            bool addedWithoutElevation = TryRunNetsh(addArgs, elevate: false, out int addExitCode, out string addOutput, out string addError) &&
                addExitCode == 0;

            if (!addedWithoutElevation)
            {
                bool addedWithElevation = TryRunNetsh(addArgs, elevate: true, out int elevatedExitCode, out _, out string elevatedError) &&
                    elevatedExitCode == 0;

                if (!addedWithElevation)
                {
                    message = string.IsNullOrWhiteSpace(elevatedError)
                        ? "Unable to create URL ACL for wildcard host binding."
                        : $"Unable to create URL ACL for wildcard host binding: {elevatedError}";
                    return false;
                }
            }

            if (!HasUrlAclReservation(wildcardUrl))
            {
                message = "URL ACL command ran but wildcard reservation was not detected.";
                return false;
            }

            message = $"Added URL ACL for {wildcardUrl} for user {currentUser}.";
            return true;
        }

        private static bool HasUrlAclReservation(string url)
        {
            string args = $"http show urlacl url={url}";

            if (!TryRunNetsh(args, elevate: false, out int exitCode, out string output, out _))
            {
                return false;
            }

            if (exitCode != 0)
            {
                return false;
            }

            return output.Contains("Reserved URL", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryRunNetsh(
            string arguments,
            bool elevate,
            out int exitCode,
            out string standardOutput,
            out string errorMessage)
        {
            exitCode = -1;
            standardOutput = string.Empty;
            errorMessage = string.Empty;

            try
            {
                ProcessStartInfo startInfo = new("netsh", arguments)
                {
                    UseShellExecute = elevate,
                    CreateNoWindow = !elevate
                };

                if (elevate)
                {
                    startInfo.Verb = "runas";
                    startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                }
                else
                {
                    startInfo.RedirectStandardOutput = true;
                    startInfo.RedirectStandardError = true;
                }

                using Process? process = Process.Start(startInfo);
                if (process == null)
                {
                    errorMessage = "Failed to start netsh process.";
                    return false;
                }

                if (!elevate)
                {
                    standardOutput = process.StandardOutput.ReadToEnd();
                    string stdErr = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrWhiteSpace(stdErr))
                    {
                        errorMessage = stdErr.Trim();
                    }
                }

                process.WaitForExit();
                exitCode = process.ExitCode;

                if (exitCode != 0 && string.IsNullOrWhiteSpace(errorMessage))
                {
                    errorMessage = $"netsh exited with code {exitCode}.";
                }

                return true;
            }
            catch (Win32Exception ex) when (elevate && ex.NativeErrorCode == 1223)
            {
                errorMessage = "Administrator permission prompt was canceled.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static bool TryCreateAndStartListener(int port, string host, out HttpListener? listener, out string error)
        {
            listener = null;
            error = string.Empty;

            try
            {
                HttpListener tempListener = new();
                string prefix = $"http://{host}:{port}/";

                tempListener.Prefixes.Add(prefix);
                tempListener.Start();
                listener = tempListener;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                listener?.Close();
                listener = null;
                return false;
            }
        }

        private static bool TryCreateAndStartListener(
            int port,
            IEnumerable<string> hosts,
            out HttpListener? listener,
            out string error)
        {
            listener = null;
            error = string.Empty;

            string[] normalizedHosts = hosts
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedHosts.Length == 0)
            {
                error = "No fallback hostnames available.";
                return false;
            }

            try
            {
                HttpListener tempListener = new();

                foreach (string host in normalizedHosts)
                {
                    string prefix = $"http://{host}:{port}/";
                    tempListener.Prefixes.Add(prefix);
                }

                tempListener.Start();
                listener = tempListener;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                listener?.Close();
                listener = null;
                return false;
            }
        }

        private static string[] GetHostnameFallbackHosts()
        {
            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddHostCandidate(hosts, Environment.MachineName);
            AddHostCandidate(hosts, Dns.GetHostName());

            try
            {
                IPHostEntry localHostEntry = Dns.GetHostEntry(Dns.GetHostName());
                AddHostCandidate(hosts, localHostEntry.HostName);

                foreach (string alias in localHostEntry.Aliases)
                {
                    AddHostCandidate(hosts, alias);
                }

                foreach (IPAddress address in localHostEntry.AddressList)
                {
                    if (IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    if (address.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                    {
                        continue;
                    }

                    AddHostCandidate(hosts, address.ToString());

                    try
                    {
                        IPHostEntry reverseLookup = Dns.GetHostEntry(address);
                        AddHostCandidate(hosts, reverseLookup.HostName);

                        foreach (string reverseAlias in reverseLookup.Aliases)
                        {
                            AddHostCandidate(hosts, reverseAlias);
                        }
                    }
                    catch
                    {
                        // Reverse lookup is best-effort and may fail depending on DNS setup.
                    }
                }
            }
            catch
            {
                // DNS enumeration is best-effort; keep base machine names if resolution fails.
            }

            return hosts.ToArray();
        }

        private static void AddHostCandidate(ISet<string> hosts, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            string normalized = value.Trim().TrimEnd('.');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            if (normalized.StartsWith("[", StringComparison.Ordinal) &&
                normalized.EndsWith("]", StringComparison.Ordinal))
            {
                normalized = normalized[1..^1];
            }

            int colonCount = normalized.Count(ch => ch == ':');
            if (colonCount == 1)
            {
                int index = normalized.IndexOf(':');
                if (index > 0)
                {
                    normalized = normalized[..index];
                }
            }

            if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            hosts.Add(normalized);

            int dotIndex = normalized.IndexOf('.');
            if (dotIndex > 0)
            {
                string shortName = normalized[..dotIndex];
                if (!string.IsNullOrWhiteSpace(shortName))
                {
                    hosts.Add(shortName);
                }
            }
        }

        private async Task ListenLoopAsync(HttpListener listener, CancellationToken token)
        {
            while (!token.IsCancellationRequested && listener.IsListening)
            {
                HttpListenerContext? context = null;

                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (context == null)
                {
                    continue;
                }

                _ = Task.Run(() => ProcessRequestAsync(context), token);
            }
        }

        private async Task ProcessRequestAsync(HttpListenerContext context)
        {
            try
            {
                string path = (context.Request.Url?.AbsolutePath ?? "/").TrimEnd('/');
                if (string.IsNullOrWhiteSpace(path))
                {
                    path = "/";
                }

                string method = context.Request.HttpMethod.ToUpperInvariant();
                RemoteWebAuthSettings authSettings = GetEffectiveAuthSettings();

                if (method == "GET" && path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] faviconBytes = GetFaviconBytes();
                    if (faviconBytes.Length == 0)
                    {
                        await WriteTextAsync(context, 404, "Favicon unavailable.", "text/plain; charset=utf-8")
                            .ConfigureAwait(false);
                        return;
                    }

                    await WriteBinaryAsync(context, 200, faviconBytes, "image/x-icon")
                        .ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path.Equals("/login", StringComparison.OrdinalIgnoreCase))
                {
                    if (!authSettings.RequirePassword || IsRequestAuthenticated(context.Request, authSettings))
                    {
                        RedirectTo(context.Response, "/");
                        return;
                    }

                    await WriteTextAsync(context, 200, BuildLoginPageHtml(), "text/html; charset=utf-8")
                        .ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path.Equals("/api/auth/status", StringComparison.OrdinalIgnoreCase))
                {
                    bool authenticated = !authSettings.RequirePassword ||
                        IsRequestAuthenticated(context.Request, authSettings);

                    await WriteJsonAsync(context, 200, new RemoteAuthStatusResponse
                    {
                        AuthenticationRequired = authSettings.RequirePassword,
                        Authenticated = authenticated
                    }).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase))
                {
                    if (!authSettings.RequirePassword)
                    {
                        await WriteJsonAsync(context, 200, new RemoteOperationResponse
                        {
                            Success = true,
                            Message = "Authentication is not required."
                        }).ConfigureAwait(false);
                        return;
                    }

                    RemoteLoginRequest? request = await ReadJsonRequestAsync<RemoteLoginRequest>(context.Request)
                        .ConfigureAwait(false);

                    if (request == null || string.IsNullOrWhiteSpace(request.Password) ||
                        !_verifyWebPassword(request.Password))
                    {
                        await WriteUnauthorizedAsync(context).ConfigureAwait(false);
                        return;
                    }

                    int portForCookie = context.Request.LocalEndPoint?.Port ?? Port;
                    SetAuthCookie(context.Response, portForCookie, authSettings, request.RememberMe);

                    await WriteJsonAsync(context, 200, new RemoteOperationResponse
                    {
                        Success = true,
                        Message = "Login successful."
                    }).ConfigureAwait(false);
                    return;
                }

                bool isApiPath = path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase);
                if (authSettings.RequirePassword && !IsRequestAuthenticated(context.Request, authSettings))
                {
                    if (isApiPath)
                    {
                        await WriteUnauthorizedAsync(context).ConfigureAwait(false);
                    }
                    else
                    {
                        RedirectTo(context.Response, "/login");
                    }

                    return;
                }

                if (method == "GET" && path == "/")
                {
                    await WriteTextAsync(context, 200, BuildWebPageHtml(), "text/html; charset=utf-8")
                        .ConfigureAwait(false);
                    return;
                }

                if (method == "GET" && path.Equals("/api/state", StringComparison.OrdinalIgnoreCase))
                {
                    RemoteControlState state = await _getStateAsync().ConfigureAwait(false);
                    await WriteJsonAsync(context, 200, state).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path.Equals("/api/action/shutdown-toggle", StringComparison.OrdinalIgnoreCase))
                {
                    RemoteOperationResponse response = await _triggerShutdownAsync().ConfigureAwait(false);
                    await WriteJsonAsync(context, response.Success ? 200 : 400, response).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path.Equals("/api/action/blackout", StringComparison.OrdinalIgnoreCase))
                {
                    RemoteOperationResponse response = await _triggerBlackoutAsync().ConfigureAwait(false);
                    await WriteJsonAsync(context, response.Success ? 200 : 400, response).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path.Equals("/api/action/lock-desktop", StringComparison.OrdinalIgnoreCase))
                {
                    RemoteOperationResponse response = await _triggerLockDesktopAsync().ConfigureAwait(false);
                    await WriteJsonAsync(context, response.Success ? 200 : 400, response).ConfigureAwait(false);
                    return;
                }

                if (method == "POST" && path.Equals("/api/config/save", StringComparison.OrdinalIgnoreCase))
                {
                    RemoteConfigUpdateRequest? request = await ReadJsonRequestAsync<RemoteConfigUpdateRequest>(context.Request)
                        .ConfigureAwait(false);

                    if (request == null)
                    {
                        await WriteJsonAsync(context, 400, new RemoteOperationResponse
                        {
                            Success = false,
                            Message = "Invalid config payload."
                        }).ConfigureAwait(false);
                        return;
                    }

                    RemoteOperationResponse response = await _saveConfigAsync(request).ConfigureAwait(false);
                    await WriteJsonAsync(context, response.Success ? 200 : 400, response).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, 404, new RemoteOperationResponse
                {
                    Success = false,
                    Message = "Endpoint not found."
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(context, 500, new RemoteOperationResponse
                {
                    Success = false,
                    Message = $"Server error: {ex.Message}"
                }).ConfigureAwait(false);
            }
        }

        private static async Task<T?> ReadJsonRequestAsync<T>(HttpListenerRequest request)
            where T : class
        {
            if (!request.HasEntityBody)
            {
                return null;
            }

            using var reader = new StreamReader(
                request.InputStream,
                request.ContentEncoding ?? Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: false);

            string body = await reader.ReadToEndAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }

        private static async Task WriteJsonAsync(HttpListenerContext context, int statusCode, object payload)
        {
            string json = JsonSerializer.Serialize(payload, JsonOptions);
            await WriteTextAsync(context, statusCode, json, "application/json; charset=utf-8")
                .ConfigureAwait(false);
        }

        private static async Task WriteTextAsync(HttpListenerContext context, int statusCode, string content, string contentType)
        {
            byte[] data = Encoding.UTF8.GetBytes(content);
            await WriteBinaryAsync(context, statusCode, data, contentType).ConfigureAwait(false);
        }

        private static async Task WriteBinaryAsync(HttpListenerContext context, int statusCode, byte[] data, string contentType)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = data.Length;
            context.Response.AddHeader("Cache-Control", "no-store");

            await context.Response.OutputStream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            context.Response.Close();
        }

        private static async Task WriteUnauthorizedAsync(HttpListenerContext context)
        {
            await WriteJsonAsync(context, 401, new RemoteOperationResponse
            {
                Success = false,
                Message = "Authentication required."
            }).ConfigureAwait(false);
        }

        private static void RedirectTo(HttpListenerResponse response, string location)
        {
            response.Redirect(location);
            response.Close();
        }

        private static string GetAuthCookieName(int port)
        {
            return $"SystemSquireAuth_{port}";
        }

        private static string CreateSessionToken(RemoteWebAuthSettings settings)
        {
            long expiresUnixSeconds = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();
            string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            string payload = $"{expiresUnixSeconds}:{nonce}";
            string signature = ComputeTokenSignature(payload, settings);
            return $"{payload}:{signature}";
        }

        private static string ComputeTokenSignature(string payload, RemoteWebAuthSettings settings)
        {
            string seed = $"{settings.PasswordHash}:{settings.PasswordSalt}:SystemSquireRemoteAuth";
            byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(seed));

            using var hmac = new HMACSHA256(key);
            byte[] signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(signature);
        }

        private static bool IsRequestAuthenticated(HttpListenerRequest request, RemoteWebAuthSettings settings)
        {
            if (!settings.RequirePassword)
            {
                return true;
            }

            int cookiePort = request.LocalEndPoint?.Port ?? 0;
            if (cookiePort <= 0)
            {
                return false;
            }

            Cookie? cookie = request.Cookies[GetAuthCookieName(cookiePort)];
            if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value))
            {
                return false;
            }

            return ValidateSessionToken(cookie.Value, settings);
        }

        private static bool ValidateSessionToken(string token, RemoteWebAuthSettings settings)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            string[] parts = token.Split(':');
            if (parts.Length != 3)
            {
                return false;
            }

            if (!long.TryParse(parts[0], out long expiresUnixSeconds))
            {
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresUnixSeconds)
            {
                return false;
            }

            string payload = $"{parts[0]}:{parts[1]}";
            string expectedSignature = ComputeTokenSignature(payload, settings);

            try
            {
                byte[] expectedBytes = Convert.FromBase64String(expectedSignature);
                byte[] providedBytes = Convert.FromBase64String(parts[2]);
                return CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
            }
            catch
            {
                return false;
            }
        }

        private static void SetAuthCookie(
            HttpListenerResponse response,
            int port,
            RemoteWebAuthSettings settings,
            bool rememberMe)
        {
            Cookie cookie = new(GetAuthCookieName(port), CreateSessionToken(settings), "/")
            {
                HttpOnly = true
            };

            if (rememberMe)
            {
                cookie.Expires = DateTime.UtcNow.AddDays(30);
            }

            response.Cookies.Add(cookie);
        }

        private RemoteWebAuthSettings GetEffectiveAuthSettings()
        {
            RemoteWebAuthSettings settings = _getWebAuthSettings() ?? new RemoteWebAuthSettings();

            settings.PasswordHash = settings.PasswordHash?.Trim() ?? string.Empty;
            settings.PasswordSalt = settings.PasswordSalt?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(settings.PasswordHash) || string.IsNullOrWhiteSpace(settings.PasswordSalt))
            {
                settings.RequirePassword = false;
            }

            return settings;
        }

        private static byte[] GetFaviconBytes()
        {
            try
            {
                string? executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    return Array.Empty<byte>();
                }

                using Icon? appIcon = Icon.ExtractAssociatedIcon(executablePath);
                if (appIcon == null)
                {
                    return Array.Empty<byte>();
                }

                using var memoryStream = new MemoryStream();
                appIcon.Save(memoryStream);
                return memoryStream.ToArray();
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private static string BuildLoginPageHtml()
        {
            return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link rel="icon" type="image/x-icon" href="/favicon.ico" />
    <title>System Squire Login</title>
    <style>
        :root {
            --bg: #0c0f14;
            --panel: #161c25;
            --edge: #2d394b;
            --text: #e8edf4;
            --muted: #a3b0c1;
            --action: #2470e0;
            --warn: #d94b58;
        }

        * { box-sizing: border-box; }

        body {
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            background:
                radial-gradient(circle at 10% 15%, #26364f 0%, transparent 34%),
                radial-gradient(circle at 86% 90%, #2d3f2a 0%, transparent 30%),
                var(--bg);
            color: var(--text);
            font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
            padding: 20px;
        }

        .card {
            width: min(430px, 100%);
            background: linear-gradient(180deg, #1d2633, var(--panel));
            border: 1px solid var(--edge);
            border-radius: 16px;
            padding: 20px;
            box-shadow: 0 18px 40px rgba(0, 0, 0, 0.35);
        }

        h1 {
            margin: 0;
            font-size: 1.45rem;
        }

        p {
            color: var(--muted);
            margin: 8px 0 0;
        }

        label {
            display: block;
            margin-top: 16px;
            margin-bottom: 6px;
            color: #d4dcec;
            font-size: 0.9rem;
        }

        input[type="password"] {
            width: 100%;
            border-radius: 10px;
            border: 1px solid #3a4658;
            background: #0e131b;
            color: var(--text);
            padding: 11px 12px;
            font-size: 1rem;
        }

        .remember {
            margin-top: 12px;
            display: flex;
            align-items: center;
            gap: 8px;
            color: #d4dcec;
            font-size: 0.9rem;
        }

        button {
            margin-top: 14px;
            width: 100%;
            background: var(--action);
            border: none;
            color: #fff;
            padding: 11px 12px;
            border-radius: 10px;
            cursor: pointer;
            font-size: 0.95rem;
            font-weight: 600;
        }

        button:hover { filter: brightness(1.1); }

        #msg {
            min-height: 20px;
            margin-top: 12px;
            color: var(--warn);
            font-size: 0.9rem;
        }
    </style>
</head>
<body>
    <main class="card">
        <h1>System Squire Remote Login</h1>
        <p>Enter the configured password to access remote controls.</p>

        <label for="password">Password</label>
        <input id="password" type="password" autocomplete="current-password" />

        <label class="remember">
            <input id="rememberMe" type="checkbox" checked />
            Remember this device
        </label>

        <button id="loginBtn" type="button">Sign In</button>
        <div id="msg"></div>
    </main>

    <script>
        async function tryLogin() {
            const msg = document.getElementById("msg");
            msg.textContent = "";

            const password = document.getElementById("password").value || "";
            if (!password.trim()) {
                msg.textContent = "Password is required.";
                return;
            }

            try {
                const response = await fetch("/api/auth/login", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        password,
                        rememberMe: document.getElementById("rememberMe").checked
                    })
                });

                const payload = await response.json();
                if (!response.ok || payload.success === false) {
                    msg.textContent = payload.message || "Login failed.";
                    return;
                }

                window.location.replace("/");
            } catch (error) {
                msg.textContent = error.message || "Unable to reach server.";
            }
        }

        document.getElementById("loginBtn").addEventListener("click", () => tryLogin());
        document.getElementById("password").addEventListener("keydown", (event) => {
            if (event.key === "Enter") {
                event.preventDefault();
                tryLogin();
            }
        });

        document.getElementById("password").focus();
    </script>
</body>
</html>
""";
        }

        private static string BuildWebPageHtml()
        {
            return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <link rel="icon" type="image/x-icon" href="/favicon.ico" />
    <title>System Squire Remote</title>
    <style>
        :root {
            --bg: #0f1115;
            --panel: #1b1f27;
            --panel-alt: #212734;
            --accent: #2ea043;
            --accent-soft: #238636;
            --danger: #d73a49;
            --text: #f0f3f7;
            --muted: #9aa4b2;
            --border: #2f3745;
        }

        * { box-sizing: border-box; }

        body {
            margin: 0;
            font-family: "Segoe UI", Tahoma, Geneva, Verdana, sans-serif;
            color: var(--text);
            background:
                radial-gradient(circle at 20% 10%, #24314b 0%, transparent 35%),
                radial-gradient(circle at 90% 90%, #243a2e 0%, transparent 30%),
                var(--bg);
            min-height: 100vh;
            padding: 20px;
        }

        .shell {
            max-width: 1180px;
            margin: 0 auto;
        }

        .hero {
            background: linear-gradient(130deg, #2e3f5f, #233044 62%, #1d2d25);
            border-radius: 16px;
            border: 1px solid #3a4760;
            padding: 18px;
            margin-bottom: 14px;
            box-shadow: 0 12px 30px rgba(0, 0, 0, 0.25);
        }

        .hero h1 {
            margin: 0;
            font-size: 1.6rem;
        }

        .hero .status {
            margin-top: 8px;
            color: #d8e0ea;
        }

        .panel {
            background: linear-gradient(180deg, var(--panel), var(--panel-alt));
            border: 1px solid var(--border);
            border-radius: 14px;
            padding: 12px;
            box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
        }

        .group {
            border: 1px solid #2b3443;
            border-radius: 12px;
            background: rgba(9, 13, 18, 0.35);
            margin: 8px 0;
            overflow: hidden;
        }

        .group > summary {
            list-style: none;
            cursor: pointer;
            font-weight: 700;
            color: #e3ecf8;
            font-size: 0.95rem;
            letter-spacing: 0.02em;
            padding: 12px 14px;
            background: linear-gradient(90deg, #1f2836, #1a222f);
            border-bottom: 1px solid #2b3443;
            user-select: none;
        }

        .group > summary::-webkit-details-marker {
            display: none;
        }

        .group > summary::before {
            content: "+";
            display: inline-block;
            margin-right: 8px;
            color: #8fb1e6;
            font-weight: 800;
        }

        .group[open] > summary::before {
            content: "-";
        }

        .group-body {
            padding: 14px;
        }

        .actions {
            display: flex;
            gap: 10px;
            flex-wrap: wrap;
        }

        button {
            background: #2f6feb;
            border: none;
            color: white;
            padding: 10px 14px;
            border-radius: 10px;
            font-size: 0.9rem;
            cursor: pointer;
        }

        button:hover { filter: brightness(1.1); }

        button.secondary { background: #4b5563; }
        button.success { background: var(--accent-soft); }
        button.danger { background: var(--danger); }

        label {
            display: block;
            font-size: 0.85rem;
            margin: 10px 0 5px;
            color: #d0d7e1;
        }

        input[type="text"],
        input[type="number"],
        select {
            width: 100%;
            background: #0f141b;
            color: var(--text);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 9px 10px;
        }

        .inline {
            display: flex;
            gap: 10px;
            align-items: end;
        }

        .inline > * { flex: 1; }

        .check {
            display: flex;
            align-items: center;
            gap: 8px;
            margin-top: 10px;
            color: var(--text);
        }

        .list {
            margin-top: 10px;
            border: 1px solid var(--border);
            border-radius: 10px;
            padding: 8px;
            min-height: 110px;
            max-height: 220px;
            overflow: auto;
            background: #0f141b;
        }

        .entry {
            display: grid;
            grid-template-columns: 24px 1fr auto;
            align-items: center;
            gap: 8px;
            padding: 6px;
            border-bottom: 1px solid #232a36;
        }

        .entry:last-child { border-bottom: none; }

        .entry button {
            padding: 5px 8px;
            border-radius: 6px;
            font-size: 0.75rem;
            background: #6b7280;
        }

        .banner {
            margin-top: 12px;
            padding: 10px;
            border-radius: 8px;
            font-size: 0.9rem;
            display: none;
        }

        .banner.ok {
            display: block;
            background: rgba(46, 160, 67, 0.15);
            border: 1px solid rgba(46, 160, 67, 0.45);
        }

        .banner.error {
            display: block;
            background: rgba(215, 58, 73, 0.15);
            border: 1px solid rgba(215, 58, 73, 0.45);
        }
    </style>
</head>
<body>
    <div class="shell">
        <section class="hero">
            <h1>System Squire Remote</h1>
            <div id="statusText" class="status">Loading state...</div>
            <div class="actions">
                <button id="shutdownBtn" class="danger">Toggle Shutdown</button>
                <button id="blackoutBtn" class="secondary">Trigger Blackout</button>
                <button id="lockDesktopBtn" class="secondary">Lock Desktop</button>
            </div>
            <div id="messageBanner" class="banner"></div>
        </section>

        <section class="panel">
            <details class="group" open>
                <summary>Main System Controls</summary>
                <div class="group-body">
                    <label for="shutdownHotkey">Shutdown Hotkey</label>
                    <input id="shutdownHotkey" type="text" />

                    <label for="blackoutHotkey">Blackout Hotkey</label>
                    <input id="blackoutHotkey" type="text" />

                    <div class="check">
                        <input id="startMinimized" type="checkbox" />
                        <span>Start Minimized to Tray</span>
                    </div>

                    <div class="inline">
                        <div>
                            <label for="watchDuration">Watch Duration (minutes)</label>
                            <input id="watchDuration" type="number" min="1" />
                        </div>
                        <div>
                            <label for="minimizeDelay">Delay Before Minimize (sec)</label>
                            <input id="minimizeDelay" type="number" min="0" />
                        </div>
                    </div>
                </div>
            </details>

            <details class="group" open>
                <summary>App Lists</summary>
                <div class="group-body">
                    <h2>Kill Apps Before Shutdown</h2>
                    <div class="inline">
                        <div>
                            <label for="runningAppsKill">Running Applications</label>
                            <select id="runningAppsKill"></select>
                        </div>
                        <div>
                            <button id="addKillAppBtn">Add</button>
                        </div>
                    </div>
                    <div id="killAppsList" class="list"></div>

                    <h2 style="margin-top:14px;">Watch Apps At Launch</h2>
                    <div class="inline">
                        <div>
                            <label for="runningAppsWatch">Running Applications</label>
                            <select id="runningAppsWatch"></select>
                        </div>
                        <div>
                            <button id="addWatchAppBtn">Add</button>
                        </div>
                    </div>
                    <div id="watchAppsList" class="list"></div>
                </div>
            </details>

            <details class="group" open>
                <summary>Pushover Core Settings</summary>
                <div class="group-body">
                    <h2>Pushover Credentials and Events</h2>

                    <div class="check">
                        <input id="pushoverEnabled" type="checkbox" />
                        <span>Enable Pushover notifications</span>
                    </div>

                    <label for="pushoverApiToken">API Token</label>
                    <input id="pushoverApiToken" type="text" />

                    <label for="pushoverUserKey">User Key</label>
                    <input id="pushoverUserKey" type="text" />

                    <div class="check">
                        <input id="notifyOnSystemSquireStart" type="checkbox" />
                        <span>Notify when System Squire starts</span>
                    </div>

                    <div class="check">
                        <input id="notifyOnSystemSquireClose" type="checkbox" />
                        <span>Notify when System Squire exits</span>
                    </div>

                    <div class="check">
                        <input id="notifyOnInactivity" type="checkbox" />
                        <span>Notify on inactivity</span>
                    </div>

                    <div class="inline">
                        <div>
                            <label for="inactivityNotificationMinutes">Inactivity Interval (minutes)</label>
                            <input id="inactivityNotificationMinutes" type="number" min="1" />
                        </div>
                        <div class="check" style="margin-top:26px;">
                            <input id="repeatInactivityNotifications" type="checkbox" />
                            <span>Repeat inactivity notifications</span>
                        </div>
                    </div>
                </div>
            </details>

            <details class="group" open>
                <summary>Pushover App Lifecycle Rules</summary>
                <div class="group-body">
                    <h2>Pushover App Lifecycle Rules</h2>
                    <div class="inline">
                        <div>
                            <label for="runningAppsPushover">Running Applications</label>
                            <select id="runningAppsPushover"></select>
                        </div>
                        <div>
                            <button id="addPushoverAppBtn">Add</button>
                        </div>
                    </div>
                    <div id="pushoverAppsList" class="list"></div>
                </div>
            </details>

            <details class="group" open>
                <summary>Pushover Folder Watch Rules</summary>
                <div class="group-body">
                    <h2>Pushover Folder Watch Rules</h2>

                    <div class="inline">
                        <div>
                            <label for="pushoverFolderPath">Folder Path</label>
                            <input id="pushoverFolderPath" type="text" placeholder="C:\\Path\\To\\Folder" />
                        </div>
                        <div>
                            <button id="addFolderWatchBtn">Add Folder</button>
                        </div>
                    </div>

                    <div class="inline">
                        <div>
                            <label for="folderPollingSeconds">Folder Polling (seconds)</label>
                            <input id="folderPollingSeconds" type="number" min="1" />
                        </div>
                        <div>
                            <label for="folderInactivityMinutes">Folder Inactivity (minutes)</label>
                            <input id="folderInactivityMinutes" type="number" min="1" />
                        </div>
                    </div>

                    <div class="check">
                        <input id="repeatFolderInactivityNotifications" type="checkbox" />
                        <span>Repeat folder inactivity notifications</span>
                    </div>

                    <div id="folderWatchList" class="list"></div>
                </div>
            </details>
        </section>
    </div>

    <script>
        let currentState = null;
        let suppressAutoSave = false;
        let autoSaveTimerId = null;
        let isSavingConfig = false;
        let lastStateFingerprint = "";

        function redirectToLogin() {
            window.location.replace("/login");
        }

        async function apiGet(path) {
            const response = await fetch(path, { method: "GET" });
            if (response.status === 401) {
                redirectToLogin();
                throw new Error("Authentication required.");
            }

            if (!response.ok) {
                throw new Error(`GET ${path} failed (${response.status})`);
            }

            return response.json();
        }

        async function apiPost(path, payload) {
            const response = await fetch(path, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload || {})
            });

            if (response.status === 401) {
                redirectToLogin();
                throw new Error("Authentication required.");
            }

            const data = await response.json();
            if (!response.ok || data.success === false) {
                throw new Error(data.message || `POST ${path} failed (${response.status})`);
            }

            return data;
        }

        function getStateFingerprint(state) {
            return JSON.stringify(state || {});
        }

        function showMessage(message, isError = false) {
            const banner = document.getElementById("messageBanner");
            banner.textContent = message;
            banner.className = `banner ${isError ? "error" : "ok"}`;
        }

        function clearMessage() {
            const banner = document.getElementById("messageBanner");
            banner.textContent = "";
            banner.className = "banner";
        }

        function renderAppList(containerId, entries) {
            const container = document.getElementById(containerId);
            container.innerHTML = "";

            if (!entries || entries.length === 0) {
                container.innerHTML = `<div style="color:#8b96a8;padding:6px;">No apps configured.</div>`;
                return;
            }

            entries.forEach((entry) => {
                const row = document.createElement("div");
                row.className = "entry";
                row.dataset.name = entry.name;

                const enabled = document.createElement("input");
                enabled.type = "checkbox";
                enabled.checked = !!entry.enabled;
                enabled.addEventListener("change", () => scheduleSaveConfig());

                const name = document.createElement("div");
                name.textContent = entry.name;

                const remove = document.createElement("button");
                remove.textContent = "Remove";
                remove.addEventListener("click", () => {
                    row.remove();
                    scheduleSaveConfig();
                });

                row.appendChild(enabled);
                row.appendChild(name);
                row.appendChild(remove);
                container.appendChild(row);
            });
        }

        function renderLifecyclePushoverList(entries) {
            const container = document.getElementById("pushoverAppsList");
            container.innerHTML = "";

            if (!entries || entries.length === 0) {
                container.innerHTML = `<div style="color:#8b96a8;padding:6px;">No app lifecycle rules configured.</div>`;
                return;
            }

            entries.forEach((entry) => {
                const row = document.createElement("div");
                row.className = "entry";
                row.dataset.name = entry.name;
                row.style.gridTemplateColumns = "1fr 80px 80px auto";

                const name = document.createElement("div");
                name.textContent = entry.name;

                const notifyStart = document.createElement("input");
                notifyStart.type = "checkbox";
                notifyStart.checked = !!entry.notifyOnStart;
                notifyStart.title = "Notify on start";
                notifyStart.addEventListener("change", () => scheduleSaveConfig());

                const notifyClose = document.createElement("input");
                notifyClose.type = "checkbox";
                notifyClose.checked = !!entry.notifyOnClose;
                notifyClose.title = "Notify on close";
                notifyClose.addEventListener("change", () => scheduleSaveConfig());

                const remove = document.createElement("button");
                remove.textContent = "Remove";
                remove.addEventListener("click", () => {
                    row.remove();
                    scheduleSaveConfig();
                });

                row.appendChild(name);
                row.appendChild(notifyStart);
                row.appendChild(notifyClose);
                row.appendChild(remove);
                container.appendChild(row);
            });
        }

        function renderFolderWatchList(entries) {
            const container = document.getElementById("folderWatchList");
            container.innerHTML = "";

            if (!entries || entries.length === 0) {
                container.innerHTML = `<div style="color:#8b96a8;padding:6px;">No folder watch rules configured.</div>`;
                return;
            }

            entries.forEach((entry) => {
                const row = document.createElement("div");
                row.className = "entry";
                row.dataset.folderPath = entry.folderPath;
                row.style.gridTemplateColumns = "1fr 48px 48px 48px 48px auto";

                const path = document.createElement("div");
                path.textContent = entry.folderPath;
                path.title = entry.folderPath;

                const created = document.createElement("input");
                created.type = "checkbox";
                created.checked = !!entry.notifyOnCreated;
                created.title = "Notify on create";
                created.addEventListener("change", () => scheduleSaveConfig());

                const removed = document.createElement("input");
                removed.type = "checkbox";
                removed.checked = !!entry.notifyOnRemoved;
                removed.title = "Notify on remove";
                removed.addEventListener("change", () => scheduleSaveConfig());

                const modified = document.createElement("input");
                modified.type = "checkbox";
                modified.checked = !!entry.notifyOnModified;
                modified.title = "Notify on modify";
                modified.addEventListener("change", () => scheduleSaveConfig());

                const inactivity = document.createElement("input");
                inactivity.type = "checkbox";
                inactivity.checked = !!entry.notifyOnInactivity;
                inactivity.title = "Notify on inactivity";
                inactivity.addEventListener("change", () => scheduleSaveConfig());

                const remove = document.createElement("button");
                remove.textContent = "Remove";
                remove.addEventListener("click", () => {
                    row.remove();
                    scheduleSaveConfig();
                });

                row.appendChild(path);
                row.appendChild(created);
                row.appendChild(removed);
                row.appendChild(modified);
                row.appendChild(inactivity);
                row.appendChild(remove);
                container.appendChild(row);
            });
        }

        function collectEntries(containerId) {
            const rows = document.querySelectorAll(`#${containerId} .entry`);
            const entries = [];
            const seen = new Set();

            rows.forEach((row) => {
                const name = (row.dataset.name || "").trim();
                if (!name) {
                    return;
                }

                const key = name.toLowerCase();
                if (seen.has(key)) {
                    return;
                }

                seen.add(key);
                const enabled = row.querySelector('input[type="checkbox"]').checked;
                entries.push({ name, enabled });
            });

            return entries;
        }

        function collectLifecyclePushoverEntries() {
            const rows = document.querySelectorAll("#pushoverAppsList .entry");
            const entries = [];
            const seen = new Set();

            rows.forEach((row) => {
                const name = (row.dataset.name || "").trim();
                if (!name) {
                    return;
                }

                const key = name.toLowerCase();
                if (seen.has(key)) {
                    return;
                }

                seen.add(key);
                const checkboxes = row.querySelectorAll('input[type="checkbox"]');

                entries.push({
                    name,
                    notifyOnStart: checkboxes[0]?.checked === true,
                    notifyOnClose: checkboxes[1]?.checked === true
                });
            });

            return entries;
        }

        function collectFolderWatchEntries() {
            const rows = document.querySelectorAll("#folderWatchList .entry");
            const entries = [];
            const seen = new Set();

            rows.forEach((row) => {
                const folderPath = (row.dataset.folderPath || "").trim();
                if (!folderPath) {
                    return;
                }

                const key = folderPath.toLowerCase();
                if (seen.has(key)) {
                    return;
                }

                seen.add(key);
                const checkboxes = row.querySelectorAll('input[type="checkbox"]');

                entries.push({
                    folderPath,
                    notifyOnCreated: checkboxes[0]?.checked === true,
                    notifyOnRemoved: checkboxes[1]?.checked === true,
                    notifyOnModified: checkboxes[2]?.checked === true,
                    notifyOnInactivity: checkboxes[3]?.checked === true
                });
            });

            return entries;
        }

        function addSelectedApp(selectId, listId) {
            const select = document.getElementById(selectId);
            const appName = (select.value || "").trim();
            if (!appName) {
                return;
            }

            const list = document.getElementById(listId);
            const existing = Array.from(list.querySelectorAll(".entry"))
                .some((entry) => (entry.dataset.name || "").toLowerCase() === appName.toLowerCase());

            if (existing) {
                return;
            }

            const row = document.createElement("div");
            row.className = "entry";
            row.dataset.name = appName;

            const enabled = document.createElement("input");
            enabled.type = "checkbox";
            enabled.checked = true;
            enabled.addEventListener("change", () => scheduleSaveConfig());

            const name = document.createElement("div");
            name.textContent = appName;

            const remove = document.createElement("button");
            remove.textContent = "Remove";
            remove.addEventListener("click", () => {
                row.remove();
                scheduleSaveConfig();
            });

            row.appendChild(enabled);
            row.appendChild(name);
            row.appendChild(remove);

            if (list.textContent.includes("No apps configured")) {
                list.innerHTML = "";
            }

            list.appendChild(row);
            scheduleSaveConfig();
        }

        function addSelectedPushoverLifecycleApp() {
            const select = document.getElementById("runningAppsPushover");
            const appName = (select.value || "").trim();
            if (!appName) {
                return;
            }

            const list = document.getElementById("pushoverAppsList");
            const exists = Array.from(list.querySelectorAll(".entry"))
                .some((entry) => (entry.dataset.name || "").toLowerCase() === appName.toLowerCase());

            if (exists) {
                return;
            }

            const row = document.createElement("div");
            row.className = "entry";
            row.dataset.name = appName;
            row.style.gridTemplateColumns = "1fr 80px 80px auto";

            const name = document.createElement("div");
            name.textContent = appName;

            const notifyStart = document.createElement("input");
            notifyStart.type = "checkbox";
            notifyStart.checked = true;
            notifyStart.addEventListener("change", () => scheduleSaveConfig());

            const notifyClose = document.createElement("input");
            notifyClose.type = "checkbox";
            notifyClose.checked = true;
            notifyClose.addEventListener("change", () => scheduleSaveConfig());

            const remove = document.createElement("button");
            remove.textContent = "Remove";
            remove.addEventListener("click", () => {
                row.remove();
                scheduleSaveConfig();
            });

            row.appendChild(name);
            row.appendChild(notifyStart);
            row.appendChild(notifyClose);
            row.appendChild(remove);

            if (list.textContent.includes("No app lifecycle rules configured")) {
                list.innerHTML = "";
            }

            list.appendChild(row);
            scheduleSaveConfig();
        }

        function addFolderWatchEntry() {
            const input = document.getElementById("pushoverFolderPath");
            const folderPath = (input.value || "").trim();
            if (!folderPath) {
                return;
            }

            const list = document.getElementById("folderWatchList");
            const exists = Array.from(list.querySelectorAll(".entry"))
                .some((entry) => (entry.dataset.folderPath || "").toLowerCase() === folderPath.toLowerCase());

            if (exists) {
                return;
            }

            const row = document.createElement("div");
            row.className = "entry";
            row.dataset.folderPath = folderPath;
            row.style.gridTemplateColumns = "1fr 48px 48px 48px 48px auto";

            const path = document.createElement("div");
            path.textContent = folderPath;
            path.title = folderPath;

            const created = document.createElement("input");
            created.type = "checkbox";
            created.checked = true;
            created.addEventListener("change", () => scheduleSaveConfig());

            const removed = document.createElement("input");
            removed.type = "checkbox";
            removed.checked = true;
            removed.addEventListener("change", () => scheduleSaveConfig());

            const modified = document.createElement("input");
            modified.type = "checkbox";
            modified.checked = true;
            modified.addEventListener("change", () => scheduleSaveConfig());

            const inactivity = document.createElement("input");
            inactivity.type = "checkbox";
            inactivity.checked = false;
            inactivity.addEventListener("change", () => scheduleSaveConfig());

            const remove = document.createElement("button");
            remove.textContent = "Remove";
            remove.addEventListener("click", () => {
                row.remove();
                scheduleSaveConfig();
            });

            row.appendChild(path);
            row.appendChild(created);
            row.appendChild(removed);
            row.appendChild(modified);
            row.appendChild(inactivity);
            row.appendChild(remove);

            if (list.textContent.includes("No folder watch rules configured")) {
                list.innerHTML = "";
            }

            list.appendChild(row);
            input.value = "";
            scheduleSaveConfig();
        }

        function collectConfigPayload() {
            return {
                shutdownHotkey: document.getElementById("shutdownHotkey").value,
                blackoutHotkey: document.getElementById("blackoutHotkey").value,
                startMinimized: document.getElementById("startMinimized").checked,
                launchWatchDurationMinutes: Number(document.getElementById("watchDuration").value || 1),
                launchMinimizeDelaySeconds: Number(document.getElementById("minimizeDelay").value || 0),
                appsToKillBeforeShutdown: collectEntries("killAppsList"),
                appsToWatchAfterLaunch: collectEntries("watchAppsList"),
                pushover: {
                    enabled: document.getElementById("pushoverEnabled").checked,
                    apiToken: document.getElementById("pushoverApiToken").value,
                    userKey: document.getElementById("pushoverUserKey").value,
                    notifyOnSystemSquireStart: document.getElementById("notifyOnSystemSquireStart").checked,
                    notifyOnSystemSquireClose: document.getElementById("notifyOnSystemSquireClose").checked,
                    notifyOnInactivity: document.getElementById("notifyOnInactivity").checked,
                    repeatInactivityNotifications: document.getElementById("repeatInactivityNotifications").checked,
                    inactivityNotificationMinutes: Number(document.getElementById("inactivityNotificationMinutes").value || 1),
                    folderPollingSeconds: Number(document.getElementById("folderPollingSeconds").value || 60),
                    folderInactivityMinutes: Number(document.getElementById("folderInactivityMinutes").value || 10),
                    repeatFolderInactivityNotifications: document.getElementById("repeatFolderInactivityNotifications").checked,
                    lifecycleAppEventEntries: collectLifecyclePushoverEntries(),
                    folderWatchEntries: collectFolderWatchEntries()
                }
            };
        }

        async function saveConfigAsync() {
            if (suppressAutoSave) {
                return;
            }

            isSavingConfig = true;
            try {
                const response = await apiPost("/api/config/save", collectConfigPayload());
                if (response.state) {
                    renderState(response.state);
                }

                showMessage(response.message || "Configuration saved.");
            } catch (error) {
                showMessage(error.message, true);
            } finally {
                isSavingConfig = false;
            }
        }

        function scheduleSaveConfig() {
            if (suppressAutoSave) {
                return;
            }

            if (autoSaveTimerId !== null) {
                clearTimeout(autoSaveTimerId);
            }

            autoSaveTimerId = setTimeout(() => {
                autoSaveTimerId = null;
                saveConfigAsync();
            }, 250);
        }

        function attachAutoSaveHandlers() {
            const controlIds = [
                "shutdownHotkey",
                "blackoutHotkey",
                "startMinimized",
                "watchDuration",
                "minimizeDelay",
                "pushoverEnabled",
                "pushoverApiToken",
                "pushoverUserKey",
                "notifyOnSystemSquireStart",
                "notifyOnSystemSquireClose",
                "notifyOnInactivity",
                "repeatInactivityNotifications",
                "inactivityNotificationMinutes",
                "folderPollingSeconds",
                "folderInactivityMinutes",
                "repeatFolderInactivityNotifications"
            ];

            controlIds.forEach((id) => {
                const control = document.getElementById(id);
                if (!control) {
                    return;
                }

                control.addEventListener("change", () => scheduleSaveConfig());
                control.addEventListener("blur", () => scheduleSaveConfig());
                control.addEventListener("input", () => scheduleSaveConfig());
            });
        }

        function renderState(state) {
            suppressAutoSave = true;
            currentState = state;
            lastStateFingerprint = getStateFingerprint(state);
            document.getElementById("statusText").textContent = `Status: ${state.statusText} | Service port: ${state.webServicePort}`;

            document.getElementById("shutdownHotkey").value = state.shutdownHotkey || "";
            document.getElementById("blackoutHotkey").value = state.blackoutHotkey || "";
            document.getElementById("startMinimized").checked = !!state.startMinimized;
            document.getElementById("watchDuration").value = state.launchWatchDurationMinutes || 1;
            document.getElementById("minimizeDelay").value = state.launchMinimizeDelaySeconds || 0;

            const pushover = state.pushover || {};
            document.getElementById("pushoverEnabled").checked = !!pushover.enabled;
            document.getElementById("pushoverApiToken").value = pushover.apiToken || "";
            document.getElementById("pushoverUserKey").value = pushover.userKey || "";
            document.getElementById("notifyOnSystemSquireStart").checked = !!pushover.notifyOnSystemSquireStart;
            document.getElementById("notifyOnSystemSquireClose").checked = !!pushover.notifyOnSystemSquireClose;
            document.getElementById("notifyOnInactivity").checked = !!pushover.notifyOnInactivity;
            document.getElementById("repeatInactivityNotifications").checked = pushover.repeatInactivityNotifications !== false;
            document.getElementById("inactivityNotificationMinutes").value = pushover.inactivityNotificationMinutes || 30;
            document.getElementById("folderPollingSeconds").value = pushover.folderPollingSeconds || 60;
            document.getElementById("folderInactivityMinutes").value = pushover.folderInactivityMinutes || 10;
            document.getElementById("repeatFolderInactivityNotifications").checked = pushover.repeatFolderInactivityNotifications !== false;

            const runningAppsKill = document.getElementById("runningAppsKill");
            const runningAppsWatch = document.getElementById("runningAppsWatch");
            const runningAppsPushover = document.getElementById("runningAppsPushover");
            runningAppsKill.innerHTML = "";
            runningAppsWatch.innerHTML = "";
            runningAppsPushover.innerHTML = "";

            (state.runningApplications || []).forEach((name) => {
                const optionA = document.createElement("option");
                optionA.value = name;
                optionA.textContent = name;
                runningAppsKill.appendChild(optionA);

                const optionB = document.createElement("option");
                optionB.value = name;
                optionB.textContent = name;
                runningAppsWatch.appendChild(optionB);

                const optionC = document.createElement("option");
                optionC.value = name;
                optionC.textContent = name;
                runningAppsPushover.appendChild(optionC);
            });

            renderAppList("killAppsList", state.appsToKillBeforeShutdown || []);
            renderAppList("watchAppsList", state.appsToWatchAfterLaunch || []);
            renderLifecyclePushoverList(pushover.lifecycleAppEventEntries || []);
            renderFolderWatchList(pushover.folderWatchEntries || []);
            suppressAutoSave = false;
        }

        async function loadState() {
            clearMessage();
            const state = await apiGet("/api/state");
            renderState(state);
        }

        async function pullStateIfChanged() {
            if (suppressAutoSave || isSavingConfig || autoSaveTimerId !== null) {
                return;
            }

            try {
                const state = await apiGet("/api/state");
                const fingerprint = getStateFingerprint(state);

                if (fingerprint !== lastStateFingerprint) {
                    renderState(state);
                }
            } catch {
                // Ignore transient pull failures.
            }
        }

        function startLiveSync() {
            setInterval(() => {
                pullStateIfChanged();
            }, 700);
        }

        function applySectionDefaultsForViewport() {
            const isMobile = window.matchMedia("(max-width: 768px)").matches;
            document.querySelectorAll("details.group").forEach((section) => {
                section.open = !isMobile;
            });
        }

        async function runAction(path, payload, successMessage) {
            try {
                const response = await apiPost(path, payload);
                if (response.state) {
                    renderState(response.state);
                }

                showMessage(response.message || successMessage);
            } catch (error) {
                showMessage(error.message, true);
            }
        }

        document.getElementById("shutdownBtn").addEventListener("click", () => {
            runAction("/api/action/shutdown-toggle", {}, "Shutdown toggled.");
        });

        document.getElementById("blackoutBtn").addEventListener("click", () => {
            runAction("/api/action/blackout", {}, "Blackout triggered.");
        });

        document.getElementById("lockDesktopBtn").addEventListener("click", () => {
            runAction("/api/action/lock-desktop", {}, "Desktop locked.");
        });

        document.getElementById("addKillAppBtn").addEventListener("click", () => addSelectedApp("runningAppsKill", "killAppsList"));
        document.getElementById("addWatchAppBtn").addEventListener("click", () => addSelectedApp("runningAppsWatch", "watchAppsList"));
        document.getElementById("addPushoverAppBtn").addEventListener("click", () => addSelectedPushoverLifecycleApp());
        document.getElementById("addFolderWatchBtn").addEventListener("click", () => addFolderWatchEntry());

        applySectionDefaultsForViewport();
        attachAutoSaveHandlers();
        startLiveSync();

        loadState().catch((error) => showMessage(error.message, true));
    </script>
</body>
</html>
""";
        }
    }
}
