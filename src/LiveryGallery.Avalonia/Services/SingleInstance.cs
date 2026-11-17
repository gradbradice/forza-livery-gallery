using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LiveryGallery.Services;

/// <summary>
/// One gallery per user. The first copy holds a named mutex and answers on a named pipe (this user's only); a second
/// start asks it to show its window and ends. A user who saw nothing after a double-click and clicked again got two
/// galleries, both writing the same caches (a user's report, 1.7.1).
/// <para>
/// The talk on the pipe: the first copy sends its process id (4 bytes), so that the second, which Windows lets bring a
/// window forward (the user just started it), may hand that right on (AllowSetForegroundWindow); the second sends one
/// byte; the first answers 1 — its window comes forward (or is about to open), or 0 — it is closing. On 0, or with no
/// answer, the second waits for the first to end and starts in its place: a gallery started again just as it was closed
/// opens.
/// </para>
/// The mutex belongs to the thread that took it: <see cref="Acquire"/> and <see cref="Dispose"/> on the same thread
/// (Main's).
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _server;
    private Func<bool>? _activate;
    private bool _disposed;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        _server = Task.Run(() => ServeAsync(_stop.Token));
    }

    /// <summary>
    /// The name of a program's single copy for this user: the program's folder name and a hash of both (the pipe's
    /// namespace is shared by every user of the PC; only this user's own pipe is ever answered)
    /// </summary>
    public static string NameFor(string programFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(programFolder);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName + "\n" + programFolder));
        return programFolder + "-" + Convert.ToHexString(hash, 0, 6);
    }

    /// <summary>
    /// Becomes the program's only copy, or hands over to the one running
    /// </summary>
    /// <param name="name"><see cref="NameFor"/></param>
    /// <param name="waitForClosing">How long a copy that is closing is waited for</param>
    /// <param name="answerTimeout">How long the copy running is waited for to answer</param>
    /// <returns>This copy's hold (disposed at the end of Main), or null: another copy runs and has been shown</returns>
    public static SingleInstance? Acquire(string name, TimeSpan waitForClosing, TimeSpan? answerTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(false, MutexName(name));
        try
        {
            if (Take(mutex, TimeSpan.Zero)) return new SingleInstance(mutex, PipeName(name));
            if (AskToShow(PipeName(name), answerTimeout ?? TimeSpan.FromSeconds(5))) { mutex.Dispose(); return null; }
            // The copy running is closing (or does not answer): this one starts once it has ended
            if (Take(mutex, waitForClosing)) return new SingleInstance(mutex, PipeName(name));
            mutex.Dispose();
            return null;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
    }

    /// <summary>
    /// What a second start does with this copy: show its window. Called on the pipe's thread; returns false when this
    /// copy is closing (the second then waits and opens in its place). Until it is set, a second start is told the window
    /// is about to open
    /// </summary>
    public void OnSecondStart(Func<bool> activate) => Volatile.Write(ref _activate, activate);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _server.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _stop.Dispose();
    }

    private static bool Take(Mutex mutex, TimeSpan timeout)
    {
        try { return mutex.WaitOne(timeout); }
        // The copy before ended without letting go (a crash): the mutex is this one's now
        catch (AbandonedMutexException) { return true; }
    }

    // "Local\" — this Windows session's (Unix: the name as is, for the tests)
    private static string MutexName(string name) => (OperatingSystem.IsWindows() ? @"Local\" : "") + name + ".instance";

    private static string PipeName(string name) => name + ".show";

    private static bool AskToShow(string pipeName, TimeSpan timeout)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
            pipe.Connect((int)timeout.TotalMilliseconds);
            using var cancel = new CancellationTokenSource(timeout);
            var pid = new byte[4];
            pipe.ReadExactlyAsync(pid, cancel.Token).AsTask().GetAwaiter().GetResult();
            if (OperatingSystem.IsWindows()) _ = AllowSetForegroundWindow(BinaryPrimitives.ReadInt32LittleEndian(pid));
            pipe.Write([1]);
            pipe.Flush();
            var answer = new byte[1];
            pipe.ReadExactlyAsync(answer, cancel.Token).AsTask().GetAwaiter().GetResult();
            return answer[0] == 1;
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or EndOfStreamException)
        {
            return false;
        }
    }

    private async Task ServeAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stop);
                using var answerIn = CancellationTokenSource.CreateLinkedTokenSource(stop);
                answerIn.CancelAfter(TimeSpan.FromSeconds(5));
                var pid = new byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(pid, Environment.ProcessId);
                await pipe.WriteAsync(pid, answerIn.Token);
                await pipe.FlushAsync(answerIn.Token);
                var request = new byte[1];
                await pipe.ReadExactlyAsync(request, answerIn.Token);
                bool shown;
                try { shown = Volatile.Read(ref _activate)?.Invoke() ?? true; }
                catch (Exception) { shown = false; }
                await pipe.WriteAsync(new[] { shown ? (byte)1 : (byte)0 }, answerIn.Token);
                await pipe.FlushAsync(answerIn.Token);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
            catch (Exception e) when (e is IOException or OperationCanceledException or EndOfStreamException)
            {
                // A second start that went away midway: the next one is answered
            }
            catch (UnauthorizedAccessException)
            {
                // The name is taken by another user's pipe: no second start of this user can be answered; it then
                // waits for this copy to end, as for one that is closing
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { return; }
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
