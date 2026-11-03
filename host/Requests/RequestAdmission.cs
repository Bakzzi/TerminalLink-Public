namespace TerminalLink.Requests;

// Admission and update pause share one lock: no submission can slip between the
// idle check and normal connector shutdown.
internal sealed class RequestAdmission {
    readonly object gate = new(); int active; bool paused;
    internal bool Busy { get { lock (gate) return active != 0; } }
    internal IDisposable? Enter() { lock (gate) { if (paused) return null; active++; return new Lease(this); } }
    internal bool Pause() { lock (gate) { if (active != 0) return false; paused = true; return true; } }
    sealed class Lease(RequestAdmission owner) : IDisposable {
        RequestAdmission? held = owner;
        public void Dispose() { var release = Interlocked.Exchange(ref held, null); if (release != null) lock (release.gate) release.active--; }
    }
}
