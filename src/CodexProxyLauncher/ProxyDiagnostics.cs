using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace CodexProxyLauncher;

internal enum StatusKind
{
    Neutral,
    Working,
    Success,
    Warning,
    Error,
}

internal sealed record ProxyCheckResult(
    string TcpStatus,
    string HttpStatus,
    StatusKind TcpKind = StatusKind.Neutral,
    StatusKind HttpKind = StatusKind.Neutral);

internal static class ProxyDiagnostics
{
    private static readonly Uri TestEndpoint = new("https://api.openai.com/v1/models");

    public static async Task<ProxyCheckResult> CheckAsync(Uri proxy, CancellationToken cancellation)
    {
        using (var client = new TcpClient())
        using (var tcpDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
        {
            tcpDeadline.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await ConnectAsync(client, proxy, tcpDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                return new("连接超时", "等待端口连通", StatusKind.Error);
            }
            catch (SocketException ex)
            {
                string reason = ex.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "连接被拒绝",
                    SocketError.HostNotFound or SocketError.NoData => "无法解析代理主机",
                    _ => $"连接失败（{ex.SocketErrorCode}）"
                };
                return new(reason, "等待端口连通", StatusKind.Error);
            }
        }

        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy(proxy),
            AllowAutoRedirect = false,
            UseCookies = false,
        };
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var httpDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        httpDeadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using HttpResponseMessage response = await http.GetAsync(
                TestEndpoint, HttpCompletionOption.ResponseHeadersRead, httpDeadline.Token).ConfigureAwait(false);
            // An unauthenticated response from the HTTPS test endpoint still proves connectivity.
            bool passed = response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized;
            string status = passed ? "通过（测试地址已响应）" : $"失败（HTTP {(int)response.StatusCode}）";
            return new("可达", status, StatusKind.Success, passed ? StatusKind.Success : StatusKind.Error);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return new("可达", "请求超时", StatusKind.Success, StatusKind.Error);
        }
        catch (HttpRequestException ex)
        {
            cancellation.ThrowIfCancellationRequested();
            if (httpDeadline.IsCancellationRequested)
                return new("可达", "请求超时", StatusKind.Success, StatusKind.Error);

            string reason = GetHttpFailure(ex);
            return new("可达", $"失败（{reason}）", StatusKind.Success, StatusKind.Error);
        }
    }

    private static async Task ConnectAsync(TcpClient client, Uri proxy, CancellationToken cancellation)
    {
#if NETFRAMEWORK
        cancellation.ThrowIfCancellationRequested();
        Task connection = client.ConnectAsync(proxy.IdnHost, proxy.Port);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Register(() => canceled.TrySetResult(true));
        if (await Task.WhenAny(connection, canceled.Task).ConfigureAwait(false) != connection)
        {
            // The caller disposes the socket. Observe any fault from the pending connection.
            _ = connection.ContinueWith(static task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            cancellation.ThrowIfCancellationRequested();
        }
        await connection.ConfigureAwait(false);
#else
        await client.ConnectAsync(proxy.IdnHost, proxy.Port, cancellation).ConfigureAwait(false);
#endif
    }

    private static string GetHttpFailure(HttpRequestException error)
    {
#if NETFRAMEWORK
        if (error.InnerException is WebException { Response: HttpWebResponse response })
        {
            using (response)
                return response.StatusCode == HttpStatusCode.ProxyAuthenticationRequired
                    ? "代理要求身份验证"
                    : "代理拒绝建立 HTTPS 隧道";
        }
        return (error.InnerException as WebException)?.Status switch
        {
            WebExceptionStatus.ProtocolError => "代理拒绝建立 HTTPS 隧道",
            WebExceptionStatus.SecureChannelFailure or WebExceptionStatus.TrustFailure => "TLS 握手失败",
            _ => "无法通过代理访问测试地址",
        };
#else
        if (error.StatusCode == HttpStatusCode.ProxyAuthenticationRequired)
            return "代理要求身份验证";

        return error.HttpRequestError switch
        {
            HttpRequestError.ProxyTunnelError => "代理拒绝建立 HTTPS 隧道",
            HttpRequestError.SecureConnectionError => "TLS 握手失败",
            _ => "无法通过代理访问测试地址",
        };
#endif
    }
}
