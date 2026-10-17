namespace GazeShift
{
    public sealed class CameraFrameGate
    {
        // Provisional freshness guard carried from the phone prototype; tune from headset evidence.
        public const double MaximumAgeSeconds = 0.250;
        private double lastUpdate = double.NaN;

        public void ObserveUpdate(double now)
        {
            if (!double.IsNaN(now) && !double.IsInfinity(now) && now >= 0)
                lastUpdate = now;
        }

        public void Clear() => lastUpdate = double.NaN;

        public bool IsFresh(double now) => !double.IsNaN(now) && !double.IsInfinity(now)
            && !double.IsNaN(lastUpdate) && now >= lastUpdate && now - lastUpdate <= MaximumAgeSeconds;
    }
}
