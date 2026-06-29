using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Vortex.Shared;

namespace Vortex.Desktop.Services;

public interface IDesktopAuthenticationService
{
    Task<UserProfileDto?> SignInWithBrowserAsync(bool preferRegister, CancellationToken cancellationToken);
}

public sealed class DesktopAuthenticationService(BackendClient backendClient) : IDesktopAuthenticationService
{
    public async Task<UserProfileDto?> SignInWithBrowserAsync(
        bool preferRegister,
        CancellationToken cancellationToken)
    {
        var state = RandomUrlString(32);
        var verifier = RandomUrlString(64);
        var challenge = Sha256Url(verifier);
        var port = GetFreePort();

        var callbackUri = $"http://127.0.0.1:{port}/callback/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(callbackUri);
        listener.Start();
        DesktopLogService.Info("Desktop auth callback listener started on loopback.");

        try
        {
            var session = await backendClient.StartDesktopAuthAsync(
                new StartDesktopAuthRequest(
                    Sha256Url(state),
                    challenge,
                    callbackUri),
                cancellationToken);

            if (session is null)
            {
                throw new InvalidOperationException(
                    "Vortex Server giriş oturumu oluşturamadı.");
            }

            var authorizationUrl = AppendQuery(
                session.AuthorizationUrl,
                new Dictionary<string, string> { ["state"] = state });

            if (preferRegister)
            {
                var authorizePath = new Uri(authorizationUrl).PathAndQuery;
                authorizationUrl =
                    "http://127.0.0.1:5080/register" +
                    $"?returnUrl={Uri.EscapeDataString(authorizePath)}";
            }

            var callbackTask = listener.GetContextAsync();
            OpenBrowser(authorizationUrl);
            DesktopLogService.Info("Desktop auth browser opened.");

            var completed = await Task.WhenAny(
                callbackTask,
                Task.Delay(TimeSpan.FromMinutes(5), cancellationToken));

            if (completed != callbackTask)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    "Tarayıcı giriş işlemi zaman aşımına uğradı.");
            }

            var context = await callbackTask;
            DesktopLogService.Info("1. Callback alındı.");

            var callbackResponseWritten = false;

            try
            {
                var code = context.Request.QueryString["code"];
                var returnedState = context.Request.QueryString["state"];

                if (!string.Equals(
                        state,
                        returnedState,
                        StringComparison.Ordinal))
                {
                    DesktopLogService.Info("2. State doğrulaması başarısız.");
                    await WriteCallbackResponseAsync(
                        context.Response,
                        "Giriş doğrulanamadı. State değeri geçersiz.",
                        false,
                        CancellationToken.None);
                    callbackResponseWritten = true;

                    throw new InvalidOperationException(
                        "State doğrulaması başarısız.");
                }

                DesktopLogService.Info("2. State doğrulandı.");

                if (string.IsNullOrWhiteSpace(code))
                {
                    await WriteCallbackResponseAsync(
                        context.Response,
                        "Giriş tamamlanamadı. Authorization code alınamadı.",
                        false,
                        CancellationToken.None);
                    callbackResponseWritten = true;

                    throw new InvalidOperationException(
                        "Authorization code alınamadı.");
                }

                DesktopLogService.Info("3. Authorization code alındı.");
                DesktopLogService.Info("4. Code-token exchange çağrısı yapıldı.");

                var exchange = await backendClient.ExchangeDesktopCodeDetailedAsync(
                    new ExchangeDesktopCodeRequest(
                        session.SessionId,
                        code,
                        verifier,
                        state),
                    cancellationToken);

                DesktopLogService.Info(
                    $"5. Exchange HTTP durum kodu alındı: {(int)exchange.StatusCode}.");

                if (!exchange.StatusCode.IsSuccess())
                {
                    var message =
                        $"Giriş doğrulandı ancak token alınamadı. HTTP {(int)exchange.StatusCode}. {exchange.SafeBody}";

                    await WriteCallbackResponseAsync(
                        context.Response,
                        message,
                        false,
                        CancellationToken.None);
                    callbackResponseWritten = true;

                    throw new InvalidOperationException(message);
                }

                if (exchange.AuthResponse is null)
                {
                    await WriteCallbackResponseAsync(
                        context.Response,
                        "Token yanıtı okunamadı.",
                        false,
                        CancellationToken.None);
                    callbackResponseWritten = true;

                    throw new InvalidOperationException(
                        "AuthResponse parse edilemedi.");
                }

                DesktopLogService.Info("6. AuthResponse parse edildi.");

                await backendClient.SetTokenAsync(
                    exchange.AuthResponse.AccessToken,
                    cancellationToken);

                DesktopLogService.Info("7. Access token kaydedildi.");

                var user = await backendClient.GetMeAsync(cancellationToken);

                if (user is null)
                {
                    backendClient.Logout();

                    await WriteCallbackResponseAsync(
                        context.Response,
                        "/api/me çağrısı başarısız oldu. Lütfen tekrar giriş yapın.",
                        false,
                        CancellationToken.None);
                    callbackResponseWritten = true;

                    throw new InvalidOperationException(
                        "/api/me çağrısı başarısız oldu.");
                }

                DesktopLogService.Info("8. /api/me çağrısı başarılı oldu.");

                await WriteCallbackResponseAsync(
                    context.Response,
                    "Giriş başarılı. Artık işleminize Vortex uygulamasından devam edebilirsiniz.",
                    true,
                    CancellationToken.None);
                callbackResponseWritten = true;

                return user;
            }
            catch
            {
                if (!callbackResponseWritten)
                {
                    try
                    {
                        await WriteCallbackResponseAsync(
                            context.Response,
                            "Giriş işlemi tamamlanamadı.",
                            false,
                            CancellationToken.None);
                    }
                    catch
                    {
                        // Response might already be closed.
                    }
                }

                throw;
            }
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private static async Task WriteCallbackResponseAsync(
        HttpListenerResponse response,
        string message,
        bool success,
        CancellationToken cancellationToken)
    {
        var title = success
            ? "Vortex girişi başarılı"
            : "Vortex girişi başarısız";

        var html = $$"""
            <!doctype html>
            <html lang="tr">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{{WebUtility.HtmlEncode(title)}}</title>
            </head>
            <body style="
                margin:0;
                min-height:100vh;
                display:flex;
                align-items:center;
                justify-content:center;
                background:linear-gradient(135deg,#101213,#15172E);
                color:white;
                font-family:Segoe UI,Arial,sans-serif;">
                <main style="
                    max-width:650px;
                    padding:40px;
                    border-radius:20px;
                    background:rgba(255,255,255,0.06);
                    text-align:center;">
                    <h1>{{WebUtility.HtmlEncode(title)}}</h1>
                    <p>{{WebUtility.HtmlEncode(message)}}</p>
                    <p>Bu pencereyi kapatabilirsiniz.</p>
                </main>
            </body>
            </html>
            """;

        var bytes = Encoding.UTF8.GetBytes(html);

        response.StatusCode = success
            ? (int)HttpStatusCode.OK
            : (int)HttpStatusCode.BadRequest;

        response.ContentType = "text/html; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;

        await response.OutputStream.WriteAsync(
            bytes,
            cancellationToken);

        await response.OutputStream.FlushAsync(
            cancellationToken);

        response.Close();
    }

    private static void OpenBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string AppendQuery(string uri, Dictionary<string, string> values)
    {
        var separator = uri.Contains('?') ? "&" : "?";
        return uri + separator + string.Join(
            '&',
            values.Select(kv =>
                $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static string RandomUrlString(int bytes)
    {
        return TokenServiceCompat.Base64Url(
            RandomNumberGenerator.GetBytes(bytes));
    }

    private static string Sha256Url(string value)
    {
        return TokenServiceCompat.Base64Url(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(value)));
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(
            IPAddress.Loopback,
            0);

        listener.Start();

        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static class TokenServiceCompat
    {
        public static string Base64Url(byte[] bytes)
        {
            return Convert
                .ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}

internal static class HttpStatusCodeExtensions
{
    public static bool IsSuccess(this HttpStatusCode code)
    {
        var value = (int)code;
        return value >= 200 && value <= 299;
    }
}
