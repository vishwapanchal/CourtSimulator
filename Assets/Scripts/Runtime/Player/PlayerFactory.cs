using CourtSim.Runtime.Common;
using CourtSim.Runtime.Interaction;
using UnityEngine;

namespace CourtSim.Runtime.Player
{
    /// <summary>Builds the desktop player rig: capsule, camera at eye height, interaction probe.</summary>
    public static class PlayerFactory
    {
        public const float EyeHeight = 1.62f;
        public const float BodyHeight = 1.75f;
        public const float BodyRadius = 0.3f;

        /// <summary>Creates the rig with its feet at <paramref name="feetPosition"/>.</summary>
        public static FirstPersonController Create(Vector3 feetPosition, float yawDegrees, Transform parent = null)
        {
            var player = new GameObject("Player");
            player.transform.SetParent(parent, false);
            // Lift slightly so the capsule never starts intersecting the floor.
            player.transform.SetPositionAndRotation(feetPosition + new Vector3(0f, 0.05f, 0f), Quaternion.Euler(0f, yawDegrees, 0f));

            var body = player.AddComponent<CharacterController>();
            body.height = BodyHeight;
            body.radius = BodyRadius;
            body.center = new Vector3(0f, BodyHeight * 0.5f, 0f);
            body.stepOffset = 0.3f;
            body.skinWidth = 0.03f;
            body.slopeLimit = 45f;
            // The default (0.001 m) silently drops per-frame moves at high frame rates: at 2,400 fps a
            // 1 m/s walk is 0.0004 m per frame, so the player would never move.
            body.minMoveDistance = 0f;

            // The camera must exist before the controller so its Awake can find the pivot.
            Transform pivot = UnityObjectUtil.CreateChild("CameraPivot", player.transform);
            pivot.localPosition = new Vector3(0f, EyeHeight, 0f);
            pivot.gameObject.tag = "MainCamera";

            var camera = pivot.gameObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 60f;
            camera.fieldOfView = 70f;
            pivot.gameObject.AddComponent<AudioListener>();
            pivot.gameObject.AddComponent<InteractionProbe>();

            var controller = player.AddComponent<FirstPersonController>();
            controller.AssignCameraPivot(pivot);
            return controller;
        }
    }
}
