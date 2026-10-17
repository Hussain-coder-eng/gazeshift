using NUnit.Framework;

namespace GazeShift.Tests
{
    public sealed class CameraFrameGateTests
    {
        [Test]
        public void SourceNeedsFreshUpdateAndToleratesFasterDisplayFrames()
        {
            var gate = new CameraFrameGate();
            Assert.That(gate.IsFresh(10), Is.False);
            gate.ObserveUpdate(10);
            Assert.That(gate.IsFresh(10.011), Is.True);
            Assert.That(gate.IsFresh(10.251), Is.False);
        }

        [Test]
        public void PauseClearsFreshnessUntilNewFrame()
        {
            var gate = new CameraFrameGate();
            gate.ObserveUpdate(10);
            gate.Clear();
            Assert.That(gate.IsFresh(10.01), Is.False);
            gate.ObserveUpdate(11);
            Assert.That(gate.IsFresh(11.01), Is.True);
        }

        [Test]
        public void InvalidOrBackwardsClockCannotEnableStaleEffects()
        {
            var gate = new CameraFrameGate();
            gate.ObserveUpdate(double.NaN);
            Assert.That(gate.IsFresh(1), Is.False);
            gate.ObserveUpdate(10);
            Assert.That(gate.IsFresh(9), Is.False);
            Assert.That(gate.IsFresh(double.PositiveInfinity), Is.False);
        }
    }
}
