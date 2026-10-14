using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bloom.Core;

namespace Bloom.Ai;

/// One answer of an API chat: model call after model call while the model calls tools (each run through the
/// ApiToolbox, its result sent back), until it answers without calling one — or MaxSteps calls went by, or the turn is
/// stopped. Every step is kept (ApiStep) for the chat's file.
internal static class ApiTurn
{
    /// Model calls one answer may take (a sane bound for a model that keeps calling tools).
    public const int MaxSteps = 25;

    /// → the answer (its text across the steps; failed: the error) and the steps it took. `status`: the chat's line
    /// while a tool runs (null: cleared).
    public static async Task<(ApiReply Reply, List<ApiStep> Steps)> RunAsync(ApiRequest request, ApiToolbox? tools, Action<string> delta, Action<string?> status,
        CancellationToken stop, int maxSteps = MaxSteps)
    {
        var messages = request.Messages.ToList();
        var steps = new List<ApiStep>();
        var text = new StringBuilder();
        if (tools is not null) request = request with { Tools = tools.Tools };
        for (int step = 0; step < maxSteps; step++)
        {
            var reply = await ApiClient.StreamAsync(request with { Messages = messages }, delta, stop).ConfigureAwait(false);
            if (reply.Text.Length > 0) text.Append(text.Length > 0 ? "\n\n" : "").Append(reply.Text);
            if (!reply.Ok || reply.Calls.Count == 0 || tools is null)
            {
                if (reply.Text.Length > 0) steps.Add(ApiStep.Of(reply, [], request.Source));
                return (reply with { Text = text.ToString(), Calls = [] }, steps);
            }
            var records = new List<ApiCallRecord>();
            try
            {
                foreach (var call in reply.Calls)
                {
                    status(ApiToolbox.Doing(call));
                    var (result, error) = await tools.RunAsync(call, stop).ConfigureAwait(false);
                    records.Add(ApiCallRecord.Of(call, result, error));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // every call keeps a result, so the conversation stays one a provider takes when it goes on
                foreach (var call in reply.Calls.Skip(records.Count)) records.Add(ApiCallRecord.Of(call, "Stopped by the user.", true));
                steps.Add(ApiStep.Of(reply, records, request.Source));
                return (new ApiReply(false, text.ToString(), AiErrors.Stopped), steps);
            }
            status(null);
            var done = ApiStep.Of(reply, records, request.Source);
            steps.Add(done);
            messages.AddRange(done.Messages());
        }
        Log.Write($"api {request.Provider.Id}: stopped after {maxSteps} tool steps");
        return (new ApiReply(false, text.ToString(), AiErrors.ApiTooManySteps(maxSteps)), steps);
    }
}
