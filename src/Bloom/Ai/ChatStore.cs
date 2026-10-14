using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Core;

namespace Bloom.Ai;

/// Capture chat/group JSON on its UI owner; commit snapshots and deletions in one ordered worker.
/// Flush/close never require a UI callback, so explicit quit can wait for disk durability safely.
internal sealed class ChatStore
{
    private enum Kind { Save, Delete, Barrier, Failure }
    private sealed class Work(Kind kind, string file = "", byte[]? bytes = null,
        TaskCompletionSource<bool>? done = null, Exception? error = null)
    {
        public readonly Kind Kind = kind;
        public readonly string File = file;
        public byte[]? Bytes = bytes;
        public readonly TaskCompletionSource<bool>? Done = done;
        public readonly Exception? Error = error;
    }

    private readonly Dispatcher owner;
    private readonly Action<string, byte[]> write;
    private readonly Action<string> delete;
    private readonly object gate = new();
    private readonly Queue<Work> queue = new();
    private Work? tail;
    private bool running, closed;
    private bool failedSinceBarrier; // only the serialized worker reads/writes this
    private Task<bool>? closing;

    public ChatStore(Dispatcher owner, Action<string, byte[]>? write = null, Action<string>? delete = null)
    {
        this.owner = owner;
        this.write = write ?? ((file, bytes) => AtomicFile.Write(file, stream => stream.Write(bytes)));
        this.delete = delete ?? File.Delete;
    }

    /// Serialization uses the owner now. Neither the live model nor a caller-owned byte buffer reaches the worker.
    public void Save<T>(string file, T value)
    {
        owner.VerifyAccess();
        lock (gate) if (closed) return;
        byte[] bytes;
        try { bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json.Options); }
        catch (Exception e) { Enqueue(new Work(Kind.Failure, file, error: e)); return; }
        lock (gate)
        {
            if (closed) return;
            // Only adjacent pending saves may collapse. Deletion, other files and flush are ordering boundaries.
            if (tail is { Kind: Kind.Save } last && string.Equals(last.File, file, StringComparison.OrdinalIgnoreCase))
                last.Bytes = bytes;
            else EnqueueLocked(new Work(Kind.Save, file, bytes));
        }
    }

    public Task<bool> Delete(string file)
    {
        owner.VerifyAccess();
        var done = Completion();
        lock (gate)
        {
            if (closed) return Task.FromResult(false);
            EnqueueLocked(new Work(Kind.Delete, file, done: done));
        }
        return done.Task;
    }

    /// Includes every operation accepted before this call; reports failures since the preceding barrier.
    public Task<bool> FlushAsync()
    {
        owner.VerifyAccess();
        lock (gate)
        {
            if (closing is not null) return closing;
            var done = Completion();
            EnqueueLocked(new Work(Kind.Barrier, done: done));
            return done.Task;
        }
    }

    /// Capture final models first, then close. Accepted work drains; late saves/deletes cannot resurrect a chat.
    public Task<bool> CloseAsync()
    {
        owner.VerifyAccess();
        lock (gate)
        {
            if (closing is not null) return closing;
            closed = true;
            var done = Completion();
            closing = done.Task;
            EnqueueLocked(new Work(Kind.Barrier, done: done));
            return closing;
        }
    }

    private static TaskCompletionSource<bool> Completion() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void Enqueue(Work work)
    {
        lock (gate) if (!closed) EnqueueLocked(work);
    }

    private void EnqueueLocked(Work work)
    {
        queue.Enqueue(work);
        tail = work;
        if (running) return;
        running = true;
        _ = Task.Run(Run);
    }

    private void Run()
    {
        while (true)
        {
            Work work;
            lock (gate)
            {
                if (queue.Count == 0) { running = false; return; }
                work = queue.Dequeue();
                if (queue.Count == 0) tail = null;
            }
            if (work.Kind == Kind.Barrier)
            {
                bool success = !failedSinceBarrier;
                failedSinceBarrier = false;
                work.Done!.TrySetResult(success);
                continue;
            }
            bool ok = true;
            try
            {
                if (work.Kind == Kind.Save) write(work.File, work.Bytes!);
                else if (work.Kind == Kind.Delete) delete(work.File);
                else throw work.Error!;
            }
            catch (Exception e)
            {
                ok = false;
                failedSinceBarrier = true;
                Log.Error("chat store " + Path.GetFileName(work.File), e);
            }
            work.Done?.TrySetResult(ok);
        }
    }
}
