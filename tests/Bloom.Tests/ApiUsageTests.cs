using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bloom.Ai;
using Bloom.Core;

namespace Bloom.Tests;

/// What the API keys cost: OpenRouter's and DeepSeek's balance answers (and OpenRouter's way from /credits to /key),
/// and each wire's token counts, an answer's added up over its model calls.
internal static class ApiUsageTests
{
    public static void BalancesReadAsTheProvidersWriteThem()
    {
        Lang.Force(true);
        var credits = Check.NotNull(ApiBalances.OpenRouterCredits("{\"data\":{\"total_credits\":100.5,\"total_usage\":25.75}}"), "OpenRouter /credits");
        Check.Eq((credits.Label, credits.Amount), ("Balance", "$74.75"), "credits less usage");
        var limited = Check.NotNull(ApiBalances.OpenRouterKey("{\"data\":{\"label\":\"k\",\"limit\":20,\"limit_remaining\":12.345,\"usage\":7.655}}"), "OpenRouter /key with a limit");
        Check.Eq((limited.Label, limited.Amount), ("Balance", "$12.35"), "the key's limit left");
        var unlimited = Check.NotNull(ApiBalances.OpenRouterKey("{\"data\":{\"label\":\"k\",\"limit\":null,\"limit_remaining\":null,\"usage\":3.2}}"), "OpenRouter /key without one");
        Check.Eq((unlimited.Label, unlimited.Amount), ("Used", "$3.20"), "what it spent");
        var deepseek = Check.NotNull(ApiBalances.DeepSeek("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"110.00\",\"granted_balance\":\"10.00\",\"topped_up_balance\":\"100.00\"},{\"currency\":\"USD\",\"total_balance\":\"5.5\"}]}"), "DeepSeek");
        Check.Eq(deepseek.Amount, "¥110.00 · $5.50", "each currency's total");
        Check.True(ApiBalances.DeepSeek("{\"balance_infos\":[]}") is null && ApiBalances.OpenRouterKey("not json") is null && ApiBalances.OpenRouterCredits("{\"data\":{}}") is null, "nothing to tell: no card");
    }

    public static void OpenRouterFallsBackToTheKey()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var server = new FakeApiServer(r => r.Path.EndsWith("/credits", StringComparison.Ordinal)
            ? FakeApiServer.Status(403, "Forbidden", "", "{\"error\":{\"message\":\"management key required\"}}")
            : FakeApiServer.Status(200, "OK", "", "{\"data\":{\"limit_remaining\":4,\"usage\":1}}"));
        var p = new ApiProvider("fake-or-" + Guid.NewGuid().ToString("N")[..6], "Fake", server.BaseUrl, ApiWire.ChatCompletions, "") { Balance = ApiBalanceWay.OpenRouter };
        var first = ApiBalances.AskAsync(p, "sk-or", CancellationToken.None).GetAwaiter().GetResult();
        Check.Eq(first?.Amount, "$4.00", "an ordinary key's balance from /key");
        ApiBalances.AskAsync(p, "sk-or", CancellationToken.None).GetAwaiter().GetResult();
        var paths = server.Requests.Select(r => r.Path[(r.Path.LastIndexOf('/') + 1)..]).ToArray();
        Check.Eq(string.Join(",", paths), "credits,key,key", "/credits refused once is not asked again");
        Check.True(server.Requests.All(r => r.Headers.GetValueOrDefault("Authorization") == "Bearer sk-or"), "the key to its provider");
    }

    public static void EachWireCountsTokens()
    {
        var cc = ChatCompletionsWire.Instance.Read(new SseEvent(null, "{\"choices\":[],\"usage\":{\"prompt_tokens\":1200,\"completion_tokens\":300,\"total_tokens\":1500}}"));
        Check.Eq(cc.Tokens, new ApiTokens(1200, 300), "Chat Completions' last chunk");
        var start = MessagesWire.Instance.Read(new SseEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":90,\"output_tokens\":1}}}"));
        var end = MessagesWire.Instance.Read(new SseEvent("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"output_tokens\":42}}"));
        Check.Eq(end.Tokens!.Value.Over(start.Tokens!.Value), new ApiTokens(100, 42), "Messages: the input (cached too) at the start, the output at the end");
        var done = ResponsesWire.Instance.Read(new SseEvent("response.completed", "{\"type\":\"response.completed\",\"response\":{\"usage\":{\"input_tokens\":7,\"output_tokens\":8}}}"));
        Check.True(done.Done && done.Tokens == new ApiTokens(7, 8), "Responses' completed event");
        Check.Eq(ApiTokens.Short(1234) + " " + ApiTokens.Short(300) + " " + ApiTokens.Short(3_400_000), "1.2K 300 3.4M", "how the chat shows them");
    }

    public static void OnlyNumbersCountAndNullsKeepTheEarlierCounts()
    {
        var start = MessagesWire.Instance.Read(new SseEvent("message_start", "{\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":10,\"cache_read_input_tokens\":90,\"output_tokens\":1}}}"));
        var end = MessagesWire.Instance.Read(new SseEvent("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"},\"usage\":{\"input_tokens\":null,\"cache_read_input_tokens\":null,\"output_tokens\":42}}"));
        Check.Eq(end.Tokens!.Value.Over(start.Tokens!.Value), new ApiTokens(100, 42), "nulls in message_delta leave the earlier input count");
        var json = System.Text.Json.JsonDocument.Parse("{\"i\":\"12\",\"o\":true,\"n\":null,\"neg\":-5,\"ok\":7}").RootElement;
        Check.Eq(ApiTokens.Of(json, "i", "o"), null, "a string, a boolean and a null are not counts");
        Check.Eq(ApiTokens.Of(json, "neg", "ok"), new ApiTokens(0, 7), "nor is a negative number");
        Check.Eq(ApiTokens.Of(json, "n", "absent"), null, "null and absent: nothing");
        Check.Eq(ApiTokens.Of(System.Text.Json.JsonDocument.Parse("null").RootElement, "i", "o"), null, "a usage that is null");
    }

    public static void ABalanceForAKeyThatWasReplacedOrRemovedIsDropped()
    {
        var provider = ApiProviders.All.First(p => p.Balance != ApiBalanceWay.None);
        var balance = new UsageBalance("Balance", "$1.00", "");
        long at = ApiBalances.Begin(provider.Id);
        Check.True(ApiBalances.Keep(provider, at, balance) && ApiBalances.Cards.Any(c => c.Id == "api:" + provider.Id), "a look that began with the current key is kept");
        ApiBalances.Forget(provider.Id); // the key removed
        Check.True(!ApiBalances.Cards.Any(c => c.Id == "api:" + provider.Id), "its card goes with it");
        Check.True(!ApiBalances.Keep(provider, at, balance) && !ApiBalances.Cards.Any(c => c.Id == "api:" + provider.Id), "a look still under way for it does not bring the card back");
        long next = ApiBalances.Begin(provider.Id);
        Check.True(next != at && ApiBalances.Keep(provider, next, balance), "a look for the new key is kept");
        long old = next;
        ApiBalances.Forget(provider.Id); // the key replaced
        Check.True(!ApiBalances.Keep(provider, old, new UsageBalance("Balance", "$9.00", "")), "the old key's answer does not overwrite the replaced key's");
        ApiBalances.Forget(provider.Id);
    }

    public static void AnAnswerAddsUpItsCalls()
    {
        using var data = new SettingsTransferTests.DataFolder();
        using var f = new ApiToolTests.Folder();
        int n = -1;
        string[] steps =
        {
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"read_file\",\"arguments\":\"{\\\"path\\\":\\\"notes.txt\\\"}\"}}]},\"finish_reason\":\"tool_calls\"}]}",
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":20}}",
                "data: [DONE]"),
            FakeApiServer.Sse(
                "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"Read.\"},\"finish_reason\":\"stop\"}]}",
                "data: {\"choices\":[],\"usage\":{\"prompt_tokens\":150,\"completion_tokens\":5}}",
                "data: [DONE]"),
        };
        using var server = new FakeApiServer(_ => steps[Math.Min(Interlocked.Increment(ref n), steps.Length - 1)]);
        var request = new ApiRequest(new ApiProvider("fake", "Fake", server.BaseUrl, ApiWire.ChatCompletions, ""), "sk-t", "model-x", ApiWire.ChatCompletions, "",
            new[] { new ApiMessage(ApiMessage.User, "read it", Array.Empty<string>()) }, null, null);
        var (reply, _) = ApiTurn.RunAsync(request, ApiToolTests.Box(f, _ => true), _ => { }, _ => { }, CancellationToken.None).GetAwaiter().GetResult();
        Check.True(reply.Ok && reply.Tokens == new ApiTokens(250, 25), "both calls' counts in the answer's");
        server.Requests.TryDequeue(out var first);
        Check.True(first!.Body.Contains("\"include_usage\":true", StringComparison.Ordinal), "the counts asked for");
    }
}
