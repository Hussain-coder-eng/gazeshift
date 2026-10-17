using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GazeShift.Tests
{
    public sealed class QuestCameraSourceTests
    {
        private GameObject cameraObject;
        private QuestCameraSource source;
        private Texture2D texture;

        [SetUp]
        public void SetUp()
        {
            // Keep the SDK component inactive: these lifecycle checks must not open a native camera.
            cameraObject = new GameObject("Camera source test");
            cameraObject.SetActive(false);
            source = cameraObject.AddComponent<QuestCameraSource>();
            texture = new Texture2D(1, 1);
            SetField("frameTexture", texture);
            Freshness.ObserveUpdate(Time.realtimeSinceStartupAsDouble);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public void InactiveSourceDoesNotExposeCachedTextureOrProjection()
        {
            Assert.That(source.IsReady, Is.False);
            Assert.That(source.FrameTexture, Is.Null);
            Assert.That(source.TryWorldToTextureUv(Vector3.forward, out _), Is.False);
        }

        [Test]
        public void PauseAndResumeDiscardPriorCaptureUntilFreshUpdate()
        {
            Invoke("OnApplicationPause", true);
            Assert.That(GetField("frameTexture"), Is.Null);
            Assert.That(Freshness.IsFresh(Time.realtimeSinceStartupAsDouble), Is.False);
            Invoke("OnApplicationPause", false);
            Assert.That(Freshness.IsFresh(Time.realtimeSinceStartupAsDouble), Is.False);
            Assert.That(source.FrameTexture, Is.Null);
        }

        [Test]
        public void NativeFailureDiscardsCaptureAndReportsOnce()
        {
            LogAssert.Expect(LogType.Error, new Regex("GazeShift camera unavailable:.*camera failed"));
            Invoke("ReportCameraError", new InvalidOperationException("camera failed"));
            Invoke("ReportCameraError", new InvalidOperationException("camera still failed"));
            Assert.That(GetField("frameTexture"), Is.Null);
            Assert.That(Freshness.IsFresh(Time.realtimeSinceStartupAsDouble), Is.False);
            Assert.That(source.StatusInstruction, Is.EqualTo("Camera unavailable. Close and reopen GazeShift."));
        }

        private CameraFrameGate Freshness => (CameraFrameGate)GetField("freshness");
        private object GetField(string name) => typeof(QuestCameraSource)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(source);
        private void SetField(string name, object value) => typeof(QuestCameraSource)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(source, value);
        private void Invoke(string method, params object[] args) => typeof(QuestCameraSource)
            .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(source, args);
    }
}
