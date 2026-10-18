using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OrbitConvert.Shell;

/// <summary>A request forwarded from a second OrbitConvert process to the running one.</summary>
public sealed record InstanceMessage(string Command, IReadOnlyList<string> Files);

/// <summary>
/// Keeps one OrbitConvert per user session. Later launches (for example one per file from the
/// Explorer context menu) forward their arguments over a named pipe that only the current user
/// can open, then exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public SingleInstance(ILogger logger, string id = "OrbitConvert")
    {
        _logger = logger;
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        _mutex = new Mutex(initiallyOwned: true, $@"Local\{id}.{sid}", out var created);
        IsPrimary = created;
        _pipeName = $"{id}.{sid}";
    }

    public bool IsPrimary { get; }

    public event EventHandler<InstanceMessage>? MessageReceived;

    public void StartListening()
    {
        if (!IsPrimary)
        {
            throw new InvalidOperationException("Only the primary instance listens.");
        }
        _ = Task.Run(() => ListenAsync(_cts.Token));
    }

    /// <summary>Sends a message to the primary instance. Returns false if it could not be reached.</summary>
    public bool TrySend(InstanceMessage message, TimeSpan timeout)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            // This process usually holds foreground rights (it was just launched by Explorer); pass them
            // on so the running instance may bring its radial menu or window to the front.
            if (GetNamedPipeServerProcessId(client.SafePipeHandle.DangerousGetHandle(), out var serverPid))
            {
                AllowSetForegroundWindow(serverPid);
            }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            client.Write(bytes);
            client.Flush();
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not reach the running instance");
            return false;
        }
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[4096];
                int read;
                while ((read = await server.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
                {
                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > 4 * 1024 * 1024)
                    {
                        throw new InvalidDataException("Message too large.");
                    }
                }
                var message = JsonSerializer.Deserialize<InstanceMessage>(Encoding.UTF8.GetString(buffer.ToArray()));
                if (message is not null)
                {
                    MessageReceived?.Invoke(this, message);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ignoring malformed instance message");
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(nint pipe, out uint serverProcessId);

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    public void Dispose()
    {
        _cts.Cancel();
        if (IsPrimary)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
        }
        _mutex.Dispose();
    }
}
