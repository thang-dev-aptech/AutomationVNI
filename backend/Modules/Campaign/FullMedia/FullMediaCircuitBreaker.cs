namespace Backend.Modules.Campaign.FullMedia;

/// <summary>
/// Trạng thái ngắt mạch dùng chung (singleton) — TimeProvider để test đóng/mở.
/// </summary>
public sealed class FullMediaCircuitBreaker(TimeProvider timeProvider)
{
    private int _consecutiveInfraFailures;
    private DateTimeOffset? _openUntil;
    private bool _notifiedThisOpen;

    public bool IsOpen
    {
        get
        {
            if (_openUntil is not { } until) return false;
            if (timeProvider.GetUtcNow() >= until)
            {
                _openUntil = null;
                _notifiedThisOpen = false;
                return false;
            }

            return true;
        }
    }

    public int ConsecutiveInfraFailures => _consecutiveInfraFailures;

    public void RecordSuccessOrTerminal()
    {
        _consecutiveInfraFailures = 0;
    }

    /// <summary>Ghi nhận lỗi hạ tầng. Trả true nếu vừa mở mạch.</summary>
    public bool RecordInfraFailure(int threshold, int cooldownSeconds, out bool shouldNotify)
    {
        shouldNotify = false;
        _consecutiveInfraFailures++;
        if (_consecutiveInfraFailures < Math.Max(1, threshold))
            return false;

        var cool = Math.Max(1, cooldownSeconds);
        _openUntil = timeProvider.GetUtcNow().AddSeconds(cool);
        _consecutiveInfraFailures = 0;
        if (!_notifiedThisOpen)
        {
            _notifiedThisOpen = true;
            shouldNotify = true;
        }

        return true;
    }

    /// <summary>Chỉ dùng trong test.</summary>
    internal void ResetForTests()
    {
        _consecutiveInfraFailures = 0;
        _openUntil = null;
        _notifiedThisOpen = false;
    }
}

/// <summary>Kết quả xử lý một ảnh — để cầu chì đếm đúng.</summary>
public enum ImageProcessOutcome
{
    /// <summary>Accepted / Rejected / Unusable — reset bộ đếm.</summary>
    TerminalOk = 1,
    /// <summary>Pending chờ điều kiện ngoài — không đếm, không reset.</summary>
    Waiting = 2,
    /// <summary>Lỗi hạ tầng — tăng bộ đếm cầu chì.</summary>
    InfraFailure = 3,
}
