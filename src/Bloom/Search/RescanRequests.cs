namespace Bloom.Search;

/// One active scanner, with a coalesced follow-up when requests arrive during its pass.
internal sealed class RescanRequests
{
    private readonly object gate = new();
    private bool running, pending;

    public bool Request()
    {
        lock (gate)
        {
            if (running)
            {
                pending = true;
                return false;
            }
            running = true;
            return true;
        }
    }

    public bool NextPass()
    {
        lock (gate)
        {
            if (pending)
            {
                pending = false;
                return true;
            }
            running = false;
            return false;
        }
    }
}
