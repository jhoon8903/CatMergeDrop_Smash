using NUnit.Framework;
using Projects.Scripts.GamePlay;
using System.Reflection;
using UnityEngine;

namespace Projects.Editor
{
    public sealed class DropCameraControllerTests
    {
        [Test]
        public void BeginDrop_LaunchesCatsBeforeMovingTheCamera()
        {
            GameObject cameraObject = new GameObject("DropCameraTest");
            GameObject rigObject = new GameObject("DropCameraRigTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                cameraObject.transform.position = new Vector3(0f, 0f, -10f);
                DropCameraController rig = rigObject.AddComponent<DropCameraController>();
                rig.Initialize(camera, WorldPlayfieldLayout.BoardCenterY,
                    WorldPlayfieldLayout.BoardHalfWidth);

                bool launched = false;
                rig.BeginDrop(() => launched = true);

                Assert.That(launched, Is.True);
                Assert.That(camera.transform.position.y, Is.EqualTo(
                    WorldPlayfieldLayout.BoardCenterY));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void NotifyBrickImpact_ContinuesTrackingDownwardTowardGoal()
        {
            GameObject cameraObject = new GameObject("DropCameraTest");
            GameObject rigObject = new GameObject("DropCameraRigTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                DropCameraController rig = rigObject.AddComponent<DropCameraController>();
                rig.Initialize(camera, WorldPlayfieldLayout.BoardCenterY,
                    WorldPlayfieldLayout.BoardHalfWidth);
                rig.BeginDrop(null);

                rig.TrackFallingCat(0, -8f);
                rig.NotifyBrickImpact(-4f);
                rig.TrackFallingCat(0, -12f);
                rig.FocusBrokenTile(-6f);
                float focusAfterFall = GetPrivate<float>(rig, "_dropFocusY");
                rig.TrackFallingCat(0, -9f);

                Assert.That(GetPrivate<bool>(rig, "_hasHitBrick"), Is.True);
                Assert.That(focusAfterFall, Is.LessThan(-10f));
                Assert.That(GetPrivate<float>(rig, "_dropFocusY"), Is.EqualTo(focusAfterFall));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void LostLeadingCat_SwitchesFocusToSurvivingCatAbove()
        {
            GameObject cameraObject = new GameObject("DropCameraTest");
            GameObject rigObject = new GameObject("DropCameraRigTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                DropCameraController rig = rigObject.AddComponent<DropCameraController>();
                rig.Initialize(camera, WorldPlayfieldLayout.BoardCenterY,
                    WorldPlayfieldLayout.BoardHalfWidth);
                rig.BeginDrop(null);

                rig.TrackFallingCat(0, -12f);
                rig.NotifyBrickImpact(-12f);
                float oldFocus = GetPrivate<float>(rig, "_dropFocusY");
                rig.TrackFallingCat(1, -5f);
                float switchedFocus = GetPrivate<float>(rig, "_dropFocusY");
                rig.FocusBrokenTile(-12f);

                Assert.That(rig.TrackedCatSlot, Is.EqualTo(1));
                Assert.That(switchedFocus, Is.GreaterThan(oldFocus));
                Assert.That(GetPrivate<float>(rig, "_dropFocusY"), Is.EqualTo(switchedFocus),
                    "The destroyed cat's impact must not pull the camera back down.");

                rig.ReturnToMerge();
                rig.BeginDrop(null);
                rig.TrackFallingCat(0, -12f);
                rig.TrackFallingCat(1, -5f);
                float firstImpactSwitchFocus = GetPrivate<float>(rig, "_dropFocusY");
                rig.NotifyBrickImpact(-12f);
                Assert.That(GetPrivate<float>(rig, "_dropFocusY"), Is.EqualTo(firstImpactSwitchFocus));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void SwitchingCats_DampsCameraMovementTowardNewTarget()
        {
            GameObject cameraObject = new GameObject("DropCameraTest");
            GameObject rigObject = new GameObject("DropCameraRigTest");
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                DropCameraController rig = rigObject.AddComponent<DropCameraController>();
                rig.Initialize(camera, WorldPlayfieldLayout.BoardCenterY,
                    WorldPlayfieldLayout.BoardHalfWidth);
                rig.BeginDrop(null);
                rig.TrackFallingCat(0, -12f);
                for (int frame = 0; frame < 120; frame++) AdvanceCamera(rig);

                float beforeSwitch = camera.transform.position.y;
                rig.TrackFallingCat(1, -5f);
                float newFocus = GetPrivate<float>(rig, "_dropFocusY");
                Assert.That(camera.transform.position.y, Is.EqualTo(beforeSwitch),
                    "Changing the target must not teleport the camera.");

                AdvanceCamera(rig);
                Assert.That(camera.transform.position.y, Is.GreaterThan(beforeSwitch));
                Assert.That(camera.transform.position.y, Is.LessThan(newFocus));

                for (int frame = 0; frame < 180; frame++) AdvanceCamera(rig);
                Assert.That(camera.transform.position.y, Is.EqualTo(newFocus).Within(.05f));
            }
            finally
            {
                Object.DestroyImmediate(rigObject);
                Object.DestroyImmediate(cameraObject);
            }
        }

        private static void AdvanceCamera(DropCameraController rig)
        {
            MethodInfo method = typeof(DropCameraController).GetMethod("AdvanceCamera",
                BindingFlags.Instance | BindingFlags.NonPublic);
            method.Invoke(rig, new object[] { 1f / 60f });
        }

        private static T GetPrivate<T>(DropCameraController rig, string fieldName)
        {
            FieldInfo field = typeof(DropCameraController).GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (T)field.GetValue(rig);
        }
    }
}
