using System.Collections.Generic;

namespace Bloom.Ai;

/// One earlier question and its answer, copied from the saved chat on the UI thread (strings only: the chat's messages
/// change there) so an API session can build them into its history on a worker thread (ApiSession.HistoryOf): the
/// message as the model got it (ChatMessage.Sent), its Lens picture, its attached files (read again then), the answer.
internal sealed record ApiPastTurn(string Asked, string? Image, IReadOnlyList<string> Files, string Answer);
