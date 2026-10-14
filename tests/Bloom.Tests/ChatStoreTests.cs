using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

internal static class ChatStoreTests
{
    private sealed class Model
    {
        public string Value { get; set; } = "";
        public string? Optional { get; set; }
    }

    private sealed class Cycle
    {
        public Cycle? Next { get; set; }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "bloom-chat-store-test-" + Guid.NewGuid().ToString("N"));
        public readonly ChatStore Store;
        public string File => Path.Combine(Root, "chat.json");

        public Fixture(Action<string, byte[]>? write = null, Action<string>? delete = null)
        {
            Directory.CreateDirectory(Root);
            Store = new ChatStore(Dispatcher.CurrentDispatcher, write, delete);
        }

        public void Dispose()
        {
            Store.CloseAsync().GetAwaiter().GetResult();
            Directory.Delete(Root, true);
        }
    }

    private static bool Completed(Task<bool> task)
    {
        Check.True(task.Wait(TimeSpan.FromSeconds(5)), "disk-only completion does not require UI pumping");
        return task.Result;
    }

    private static string Value(byte[] bytes)
    {
        using var json = JsonDocument.Parse(bytes);
        return json.RootElement.GetProperty("value").GetString()!;
    }

    public static void BlockedWriteAllowsInputAndCapturesImmutableLatestSnapshot()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new List<string>();
        int ownerThread = Environment.CurrentManagedThreadId, writerThread = ownerThread;
        using var f = new Fixture((file, bytes) =>
        {
            writerThread = Environment.CurrentManagedThreadId;
            if (values.Count == 0) { entered.Set(); release.Wait(); }
            values.Add(Value(bytes));
            AtomicFile.Write(file, stream => stream.Write(bytes));
        });
        try
        {
            var model = new Model { Value = "첫 스냅샷" };
            f.Store.Save(f.File, model);
            Check.True(entered.Wait(TimeSpan.FromSeconds(5)), "writer is blocked off UI");
            model.Value = "not saved";
            bool inputRanWhileBlocked = false;
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            _ = dispatcher.BeginInvoke(() =>
            {
                inputRanWhileBlocked = !release.IsSet;
                model.Value = "pending";
                f.Store.Save(f.File, model);
                model.Value = "최신 스냅샷";
                f.Store.Save(f.File, model);
                model.Value = "changed after capture";
                frame.Continue = false;
            }, DispatcherPriority.Input);
            Dispatcher.PushFrame(frame);
            var flushed = f.Store.FlushAsync();
            release.Set();
            Check.True(Completed(flushed), "accepted snapshots commit successfully");
            Check.True(inputRanWhileBlocked, "actual Input callback runs while IO is blocked");
            Check.True(writerThread != ownerThread, "file writing uses a worker");
            Check.True(values.SequenceEqual(new[] { "첫 스냅샷", "최신 스냅샷" }), "only adjacent pending saves coalesce, with immutable captured values");
            byte[] persisted = File.ReadAllBytes(f.File);
            Check.Eq(Value(persisted), "최신 스냅샷", "latest captured state is durable");
            Check.True(!(persisted.Length >= 3 && persisted[0] == 0xef && persisted[1] == 0xbb && persisted[2] == 0xbf), "UTF-8 has no BOM");
            Check.True(!Encoding.UTF8.GetString(persisted).Contains("optional", StringComparison.Ordinal), "existing null-omitting JSON schema is preserved");
        }
        finally { release.Set(); }
    }

    public static void DeleteOrdersOldWritesBeforeSameIdReplacement()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var operations = new List<string>();
        using var f = new Fixture((file, bytes) =>
        {
            if (operations.Count == 0) { entered.Set(); release.Wait(); }
            operations.Add(Value(bytes));
            AtomicFile.Write(file, stream => stream.Write(bytes));
        }, file => { operations.Add("delete"); File.Delete(file); });
        try
        {
            f.Store.Save(f.File, new Model { Value = "old active" });
            Check.True(entered.Wait(TimeSpan.FromSeconds(5)), "first old write is active");
            f.Store.Save(f.File, new Model { Value = "old pending" });
            var deleted = f.Store.Delete(f.File);
            f.Store.Save(f.File, new Model { Value = "replacement" });
            var flushed = f.Store.FlushAsync();
            release.Set();
            Check.True(Completed(deleted), "JSON deletion completes");
            Check.True(Completed(flushed), "replacement follows successful deletion");
            Check.True(operations.SequenceEqual(new[] { "old active", "old pending", "delete", "replacement" }), "deletion cannot overtake old snapshots or erase the replacement");
            Check.Eq(Value(File.ReadAllBytes(f.File)), "replacement", "same-ID new chat remains on disk");
        }
        finally { release.Set(); }
    }

    public static void OtherFileAndFlushAreCoalescingBoundaries()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var values = new List<string>();
        using var f = new Fixture((_, bytes) =>
        {
            if (values.Count == 0) { entered.Set(); release.Wait(); }
            values.Add(Value(bytes));
        });
        try
        {
            f.Store.Save(f.File, new Model { Value = "active" });
            Check.True(entered.Wait(TimeSpan.FromSeconds(5)), "writer blocked for deterministic queue order");
            f.Store.Save(f.File, new Model { Value = "before groups" });
            f.Store.Save(Path.Combine(f.Root, "groups.json"), new Model { Value = "groups" });
            f.Store.Save(f.File, new Model { Value = "before flush" });
            var first = f.Store.FlushAsync();
            f.Store.Save(f.File, new Model { Value = "after flush" });
            var last = f.Store.FlushAsync();
            release.Set();
            Check.True(Completed(first) && Completed(last), "both flush barriers finish");
            Check.True(values.SequenceEqual(new[] { "active", "before groups", "groups", "before flush", "after flush" }), "coalescing cannot cross another file or flush boundary");
        }
        finally { release.Set(); }
    }

    public static void FailedAtomicWritePreservesDestinationAndLaterWriteRecovers()
    {
        bool fail = true;
        using var f = new Fixture((file, bytes) => AtomicFile.Write(file, stream =>
        {
            stream.Write(bytes);
            if (fail) { fail = false; throw new IOException("synthetic staging failure"); }
        }));
        const string original = "{\"value\":\"previous durable generation\"}";
        File.WriteAllText(f.File, original, new UTF8Encoding(false));
        f.Store.Save(f.File, new Model { Value = "failed generation" });
        Check.True(!Completed(f.Store.FlushAsync()), "failed IO is reported by the next flush");
        Check.Eq(File.ReadAllText(f.File), original, "failed staging does not corrupt the old destination");
        Check.Eq(Directory.GetFiles(f.Root, "*.tmp").Length, 0, "owned failed staging is cleaned");
        f.Store.Save(f.File, new Model { Value = "recovered generation" });
        Check.True(Completed(f.Store.FlushAsync()), "a later write recovers instead of poisoning the worker");
        Check.Eq(Value(File.ReadAllBytes(f.File)), "recovered generation", "recovery replaces with complete valid JSON");
    }

    public static void FailedDeletionReportsFailureAndDoesNotStrandLaterWork()
    {
        using var f = new Fixture(delete: _ => throw new IOException("synthetic deletion failure"));
        Check.True(!Completed(f.Store.Delete(f.File)), "deletion completion reports its own failure");
        f.Store.Save(f.File, new Model { Value = "following write" });
        Check.True(!Completed(f.Store.FlushAsync()), "flush includes the preceding deletion failure");
        Check.Eq(Value(File.ReadAllBytes(f.File)), "following write", "worker continues after failed deletion");
        Check.True(Completed(f.Store.FlushAsync()), "failure accounting resets at the barrier");
    }

    public static void SerializationFailureIsOrderedAndLaterSaveRecovers()
    {
        using var f = new Fixture();
        var cycle = new Cycle();
        cycle.Next = cycle;
        f.Store.Save(f.File, cycle);
        Check.True(!Completed(f.Store.FlushAsync()), "owner-side serialization failure is included in FIFO flush");
        Check.True(!File.Exists(f.File), "invalid serialization never starts a file write");
        f.Store.Save(f.File, new Model { Value = "valid" });
        Check.True(Completed(f.Store.FlushAsync()), "valid serialization after an error still commits");
        Check.Eq(Value(File.ReadAllBytes(f.File)), "valid", "recovered JSON is complete");
    }

    public static void CloseDrainsAcceptedWorkWithoutUiCallbacksAndRejectsLateMutation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var f = new Fixture((file, bytes) =>
        {
            entered.Set();
            release.Wait();
            AtomicFile.Write(file, stream => stream.Write(bytes));
        });
        try
        {
            f.Store.Save(f.File, new Model { Value = "accepted before close" });
            Check.True(entered.Wait(TimeSpan.FromSeconds(5)), "accepted disk work has started");
            var close = f.Store.CloseAsync();
            Check.True(!close.IsCompleted, "close waits for accepted disk durability");
            f.Store.Save(f.File, new Model { Value = "late rejected write" });
            Check.True(!Completed(f.Store.Delete(f.File)), "closed queue rejects deletion too");
            Check.True(ReferenceEquals(close, f.Store.CloseAsync()) && ReferenceEquals(close, f.Store.FlushAsync()), "repeated close/flush share final completion");
            release.Set();
            Check.True(Completed(close), "explicit shutdown can drain with the owner dispatcher blocked");
            Check.Eq(Value(File.ReadAllBytes(f.File)), "accepted before close", "late mutation cannot resurrect or erase durable state");
        }
        finally { release.Set(); }
    }
}
