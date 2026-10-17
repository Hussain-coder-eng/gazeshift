namespace GazeShift
{
    internal sealed class InteractionFeedback
    {
        private const float RejectionInterval = 0.7f;
        private float nextRejectionTime = float.NegativeInfinity;
        private float pulseUntil = float.NegativeInfinity;

        public bool TryReject(float time)
        {
            if (!RegionGeometry.IsFinite(time) || time < nextRejectionTime) return false;
            nextRejectionTime = time + RejectionInterval;
            return true;
        }
        public void BeginPulse(float time, float duration)
        {
            if (RegionGeometry.IsFinite(time) && RegionGeometry.IsFinite(duration) && duration > 0) pulseUntil = time + duration;
        }
        public bool IsPulsing(float time) => RegionGeometry.IsFinite(time) && time < pulseUntil;
        public void Stop() => pulseUntil = float.NegativeInfinity;
    }
}
