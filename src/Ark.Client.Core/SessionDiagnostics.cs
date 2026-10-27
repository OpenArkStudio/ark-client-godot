using System.Diagnostics;

namespace Ark.Client.Core;

public sealed record SessionMetrics(double? SnapshotAgeMilliseconds, double? ApplicationRttMilliseconds,
    uint? PendingPingNonce, uint? MatchedPongNonce);

/// <summary>Owner-thread observations use a monotonic clock independently of frame timing.</summary>
public sealed class SessionDiagnostics
{
    private readonly Func<double> _milliseconds;
    private double? _snapshotAt;
    private double _pingAt;
    private uint? _pendingNonce;
    private uint? _matchedNonce;
    private double? _rtt;

    public SessionDiagnostics(Func<double>? milliseconds = null)
    {
        _milliseconds = milliseconds ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    public void SnapshotApplied() => _snapshotAt = _milliseconds();

    public void BeginPing(uint nonce)
    {
        _pendingNonce = nonce;
        _pingAt = _milliseconds();
    }

    public bool MatchPong(uint nonce)
    {
        if (_pendingNonce != nonce)
        {
            return false;
        }
        _rtt = Math.Max(0, _milliseconds() - _pingAt);
        _matchedNonce = nonce;
        _pendingNonce = null;
        return true;
    }

    public SessionMetrics Capture() => new(
        _snapshotAt is double at ? Math.Max(0, _milliseconds() - at) : null,
        _rtt, _pendingNonce, _matchedNonce);

    public void Reset()
    {
        _snapshotAt = null;
        _pingAt = 0;
        _pendingNonce = null;
        _matchedNonce = null;
        _rtt = null;
    }
}
