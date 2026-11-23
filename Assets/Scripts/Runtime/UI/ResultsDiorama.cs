using System.Collections.Generic;
using CourtSim.Core.Cases;
using CourtSim.Core.Decisions;
using CourtSim.Core.Logging;
using CourtSim.Core.Sessions;
using CourtSim.Runtime.Common;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace CourtSim.Runtime.UI
{
    /// <summary>
    /// The colourful 3D picture at the top of the final report: one podium pillar per phase (tall and green with a
    /// gold star when correct, medium amber when acceptable, short coral with a cross when wrong), golden scales of
    /// justice that balance when most rulings were sound, a little robot whose visor glows green when every piece of
    /// wrong AI advice was caught, and confetti for a good score.
    /// </summary>
    /// <remarks>
    /// How a 3D scene ends up inside a flat UI card:
    /// <list type="number">
    /// <item>The scene is built from Unity's built-in primitives (cube, sphere, cylinder) far below the courtroom
    /// (500 m down), so nobody walking around the courtroom could ever see it.</item>
    /// <item>Every object in it sits on its own layer (31). Its private camera only draws that layer, and every
    /// other camera and light is told to ignore that layer, so the two worlds never mix.</item>
    /// <item>The private camera does not draw to the screen. It draws into a <see cref="RenderTexture"/>, which is
    /// just an image in graphics memory, like a photo that is re-taken every frame.</item>
    /// <item>A <see cref="RawImage"/> in the report shows that texture, exactly as it would show any picture. The
    /// pillars, scales and confetti are animated in code, so the "photo" moves.</item>
    /// </list>
    /// The component lives on the RawImage. When the report is hidden it switches the scene and camera off, and when
    /// the UI is destroyed it destroys the scene, the texture and the materials it made.
    /// </remarks>
    [RequireComponent(typeof(RawImage))]
    public sealed class ResultsDiorama : MonoBehaviour
    {
        /// <summary>The layer only the diorama uses. Unnamed layers work by number, so no project settings change.</summary>
        public const int Layer = 31;

        /// <summary>Texture size: a little sharper than the 1184 × 296 image it fills, and the same 4:1 shape.</summary>
        public const int TextureWidth = 1600;
        public const int TextureHeight = 400;

        // The palette for the 3D scene and the report cards that match it.
        public static readonly Color Emerald = new Color(0.10f, 0.80f, 0.48f);
        public static readonly Color Amber = new Color(1.00f, 0.62f, 0.12f);
        public static readonly Color Coral = new Color(1.00f, 0.40f, 0.38f);
        public static readonly Color RoyalBlue = new Color(0.22f, 0.42f, 0.96f);
        public static readonly Color Gold = new Color(1.00f, 0.82f, 0.28f);

        /// <summary>The deep navy behind the scene; also used for the frame around the picture.</summary>
        public static readonly Color Backdrop = new Color(0.035f, 0.05f, 0.12f);

        private const int LayerMaskBit = 1 << Layer;
        private const int MaxPillars = 3;
        private const int ConfettiCount = 70;
        private const float StageTop = 0.34f;
        private const float PlinthHeight = 0.12f;
        private const float PillarWidth = 0.62f;
        private const float PillarSpacing = 1.35f;
        private const float RiseStart = 0.25f;
        private const float RiseStagger = 0.35f;
        private const float RiseDuration = 0.9f;
        private const float TippedAngle = 16f;

        // Far below the courtroom, well past any courtroom camera's far clipping plane.
        private static readonly Vector3 Origin = new Vector3(0f, -500f, 0f);

        private static Mesh _sphere;
        private static Mesh _cylinder;

        private readonly List<Material> _materials = new List<Material>();
        private readonly List<Pillar> _pillars = new List<Pillar>();

        private RawImage _image;
        private GameObject _world;
        private Camera _camera;
        private RenderTexture _texture;
        private Material _template;

        private Material _emerald;
        private Material _amber;
        private Material _coral;
        private Material _coralGlow;
        private Material _gold;
        private Material _goldGlow;
        private Material _icon;
        private Material _lens;
        private Material _plinth;
        private Material _visorGreen;
        private Material _visorAmber;
        private Material _eye;

        private Transform _turntable;
        private Transform _beam;
        private Transform _leftHanger;
        private Transform _rightHanger;
        private Transform _robotHead;
        private Transform _antennaTip;
        private MeshRenderer _visor;
        private MeshRenderer _antennaTipRenderer;

        private Transform _confettiRoot;
        private Transform[] _confetti;
        private Vector3[] _confettiVelocity;
        private Vector3[] _confettiSpin;

        private bool _shown;
        private CaseSession _session;
        private int _shownRulings;
        private int _pillarCount;
        private float _startTime;
        private float _scalesTarget;
        private bool _celebrate;
        private bool _confettiLaunched;

        /// <summary>True while the diorama is meant to be on screen.</summary>
        public bool IsShowing => _shown;

        /// <summary>The colour for a ruling of this quality: emerald, amber or coral.</summary>
        public static Color ResultColour(OptionQuality quality)
        {
            switch (quality)
            {
                case OptionQuality.Sound:
                    return Emerald;
                case OptionQuality.Defensible:
                    return Amber;
                default:
                    return Coral;
            }
        }

        /// <summary>Builds the scene the first time, then sets it up for this session and starts the animation.</summary>
        public void Show(CaseSession session)
        {
            if (session == null)
            {
                Hide();
                return;
            }

            // The screen can refresh while the report is open; don't restart the animation for the same result.
            SessionSummary summary = session.Summary;
            if (_shown && ReferenceEquals(session, _session) && summary.Rulings == _shownRulings)
            {
                return;
            }

            _session = session;
            _shownRulings = summary.Rulings;
            EnsureBuilt();
            IReadOnlyList<DebriefItem> items = Debrief.Build(session);

            _pillarCount = Mathf.Min(items.Count, MaxPillars);
            for (int i = 0; i < _pillars.Count; i++)
            {
                bool used = i < _pillarCount;
                _pillars[i].Root.gameObject.SetActive(used);
                if (used)
                {
                    float x = (i - ((_pillarCount - 1) * 0.5f)) * PillarSpacing;
                    _pillars[i].Root.localPosition = new Vector3(x, StageTop, -0.2f);
                    _pillars[i].Set(items[i].Quality, session.Case.DecisionPoints[i].Definition.Kind, this);
                }
            }

            // "Most rulings sound" keeps the scales level; otherwise the left pan sinks.
            bool balanced = summary.Rulings > 0 && summary.SoundRulings * 2 > summary.Rulings;
            _scalesTarget = balanced ? 0f : TippedAngle;

            // Green visor when every planted wrong suggestion was caught (also when there was none to catch).
            bool caughtAll = summary.FlawsCaught == summary.FlawedAdviceSeen;
            Material visor = caughtAll ? _visorGreen : _visorAmber;
            _visor.sharedMaterial = visor;
            _antennaTipRenderer.sharedMaterial = visor;

            // Confetti for 2 of 3 or better.
            _celebrate = summary.Rulings > 0 && summary.SoundRulings * 3 >= summary.Rulings * 2 && _confetti != null;
            _confettiLaunched = false;
            if (_confettiRoot != null)
            {
                _confettiRoot.gameObject.SetActive(false);
            }

            _startTime = Time.unscaledTime;
            _shown = true;
            StopOtherCamerasAndLightsSeeingUs();
            Animate(0f, 0f);
            ApplyVisibility();
        }

        /// <summary>Stops drawing the scene. It is kept, so showing it again is cheap.</summary>
        public void Hide()
        {
            _shown = false;
            ApplyVisibility();
        }

        private void OnEnable()
        {
            ApplyVisibility();
        }

        private void OnDisable()
        {
            // The report screen fading out deactivates this object: switch the scene and its camera off so it costs nothing.
            if (_world != null)
            {
                _world.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_image != null)
            {
                _image.texture = null;
            }

            UnityObjectUtil.SafeDestroy(_world);
            if (_texture != null)
            {
                _texture.Release();
                UnityObjectUtil.SafeDestroy(_texture);
            }

            foreach (Material material in _materials)
            {
                UnityObjectUtil.SafeDestroy(material);
            }

            _materials.Clear();
            UnityObjectUtil.SafeDestroy(_template);
        }

        private void Update()
        {
            if (!_shown || _world == null)
            {
                return;
            }

            // Unscaled time, so the report keeps animating even if gameplay is paused.
            Animate(Time.unscaledTime - _startTime, Mathf.Min(Time.unscaledDeltaTime, 0.05f));
        }

        private void ApplyVisibility()
        {
            if (_world != null)
            {
                _world.SetActive(_shown && isActiveAndEnabled);
            }
        }

        // ---------------------------------------------------------------- Animation

        private void Animate(float t, float deltaTime)
        {
            // The stage swings gently left and right like a slow turntable, so the pillars catch the light.
            _turntable.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 0.35f) * 10f, 0f);

            for (int i = 0; i < _pillarCount; i++)
            {
                float start = RiseStart + (i * RiseStagger);
                float rise = EaseOutCubic(Mathf.Clamp01((t - start) / RiseDuration));
                float pop = EaseOutBack(Mathf.Clamp01((t - start - (RiseDuration * 0.8f)) / 0.45f));
                _pillars[i].Animate(rise, pop, t, i);
            }

            // Scales: start level, settle to the target angle with a little wobble, then keep breathing slightly.
            float settle = EaseOutCubic(Mathf.Clamp01((t - 0.6f) / 1.6f));
            float wobble = Mathf.Sin(t * 3.2f) * 5f * Mathf.Exp(-t * 0.9f);
            float angle = (_scalesTarget * settle) + wobble + (Mathf.Sin(t * 0.6f) * 1.2f);
            _beam.localRotation = Quaternion.Euler(0f, 0f, angle);

            // The pans hang from the beam ends; turning them back by the same angle keeps them hanging straight down.
            Quaternion upright = Quaternion.Euler(0f, 0f, -angle);
            _leftHanger.localRotation = upright;
            _rightHanger.localRotation = upright;

            // The robot looks over towards the pillars and nods a little; its antenna light pulses.
            _robotHead.localRotation = Quaternion.Euler(Mathf.Sin(t * 1.3f) * 4f, 20f + (Mathf.Sin(t * 0.9f) * 12f), Mathf.Sin(t * 0.7f) * 5f);
            _antennaTip.localScale = Vector3.one * (0.15f + (Mathf.Sin(t * 4f) * 0.03f));

            AnimateConfetti(t, deltaTime);
        }

        private void AnimateConfetti(float t, float deltaTime)
        {
            if (!_celebrate)
            {
                return;
            }

            // The burst goes off once the last pillar has finished rising.
            float burstTime = RiseStart + (_pillarCount * RiseStagger) + RiseDuration;
            if (!_confettiLaunched)
            {
                if (t < burstTime)
                {
                    return;
                }

                LaunchConfetti();
            }

            const float Gravity = 4.5f;
            float drag = 1f - (1.6f * deltaTime);
            for (int i = 0; i < _confetti.Length; i++)
            {
                Vector3 velocity = _confettiVelocity[i];
                velocity.y -= Gravity * deltaTime;
                velocity *= drag;

                // Paper flutters: it never falls faster than a slow drift.
                if (velocity.y < -1.1f)
                {
                    velocity.y = -1.1f;
                }

                Transform piece = _confetti[i];
                Vector3 position = piece.localPosition + (velocity * deltaTime);
                if (position.y < 0.05f)
                {
                    // Landed: drop it in again from above, so a gentle shower keeps going.
                    position = new Vector3(Random.Range(-5f, 4f), Random.Range(3.8f, 4.6f), Random.Range(-1.6f, 1.2f));
                    velocity = new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(-0.8f, -0.3f), 0f);
                }

                _confettiVelocity[i] = velocity;
                piece.localPosition = position;
                piece.Rotate(_confettiSpin[i] * deltaTime, Space.Self);
            }
        }

        private void LaunchConfetti()
        {
            _confettiLaunched = true;
            _confettiRoot.gameObject.SetActive(true);
            for (int i = 0; i < _confetti.Length; i++)
            {
                // Shoot up and outwards from just above the middle pillar.
                Vector2 outwards = Random.insideUnitCircle;
                _confetti[i].localPosition = new Vector3(outwards.x * 0.4f, 2.4f, -0.2f + (outwards.y * 0.3f));
                _confettiVelocity[i] = new Vector3(outwards.x * 4.5f, Random.Range(3.5f, 6.5f), outwards.y * 1.5f);
                _confettiSpin[i] = new Vector3(Random.Range(-360f, 360f), Random.Range(-360f, 360f), Random.Range(-360f, 360f));
            }
        }

        private static float EaseOutCubic(float x)
        {
            float inverse = 1f - x;
            return 1f - (inverse * inverse * inverse);
        }

        /// <summary>Overshoots a little before settling, so things "pop" into place.</summary>
        private static float EaseOutBack(float x)
        {
            const float C1 = 1.70158f;
            const float C3 = C1 + 1f;
            float shifted = x - 1f;
            return 1f + (C3 * shifted * shifted * shifted) + (C1 * shifted * shifted);
        }

        // ---------------------------------------------------------------- Building

        private void EnsureBuilt()
        {
            if (_world != null)
            {
                return;
            }

            _image = GetComponent<RawImage>();

            // Without a graphics device (tests run with -nographics) there is nothing to draw, so skip the camera,
            // the texture and the confetti, but still build the rest so the code paths are the same.
            bool canRender = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            bool lightweight = Application.isBatchMode || !canRender;

            _world = new GameObject("ResultsDiorama") { layer = Layer };
            _world.SetActive(false);
            _world.transform.position = Origin;
            Transform root = _world.transform;

            CreateMaterials();
            BuildRoom(root);
            BuildLights(root);
            BuildStage(root);
            BuildScales(root);
            BuildRobot(root);
            if (!lightweight)
            {
                BuildConfetti(root);
            }

            if (canRender)
            {
                BuildCamera(root, lightweight);
                _image.texture = _texture;
                _image.color = Color.white;
            }
            else
            {
                _image.texture = null;
                _image.color = Backdrop;
            }
        }

        private void BuildCamera(Transform root, bool lightweight)
        {
            _texture = new RenderTexture(TextureWidth, TextureHeight, 24, RenderTextureFormat.ARGB32)
            {
                name = "ResultsDioramaTexture",
                antiAliasing = lightweight ? 1 : 4, // smooths the edges of the pillars
                hideFlags = HideFlags.DontSave,
            };
            _texture.Create();

            Transform eye = Group("Camera", root, new Vector3(-3.6f, 3.1f, -10.5f));

            // Look a little to the left of the stage, so the stage sits on the right and the score text has room.
            eye.LookAt(root.TransformPoint(new Vector3(-3.6f, 1.45f, 0f)));
            _camera = eye.gameObject.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Backdrop;
            _camera.cullingMask = LayerMaskBit;
            _camera.fieldOfView = 24f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 40f;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            _camera.useOcclusionCulling = false;
            _camera.depth = -50f;
            _camera.targetTexture = _texture;
        }

        private void BuildRoom(Transform root)
        {
            Material floor = CreateMaterial(new Color(0.07f, 0.09f, 0.2f), 0.45f, 0f, 0f);
            Material wall = CreateMaterial(new Color(0.06f, 0.08f, 0.18f), 0.15f, 0f, 0f);
            Part("Floor", root, CylinderMesh, new Vector3(0f, -0.05f, 0f), new Vector3(30f, 0.05f, 30f), floor);
            Part("BackWall", root, BuiltinAssets.CubeMesh, new Vector3(0f, 4f, 6f), new Vector3(44f, 12f, 0.2f), wall);
        }

        private void BuildLights(Transform root)
        {
            // Warm key light from the front left casts soft shadows; a cool rim light from behind outlines the shapes.
            AddLight(root, "KeyLight", LightType.Directional, new Vector3(42f, 28f, 0f), Vector3.zero, new Color(1f, 0.9f, 0.76f), 1.15f, 0f, true);
            AddLight(root, "RimLight", LightType.Directional, new Vector3(28f, 205f, 0f), Vector3.zero, new Color(0.5f, 0.68f, 1f), 0.85f, 0f, false);

            // Coloured glows on the back wall: gold behind the stage, blue behind the scales, coral behind the robot.
            AddLight(root, "GoldGlow", LightType.Point, Vector3.zero, new Vector3(0f, 2.4f, 3.6f), Gold, 1.8f, 6.5f, false);
            AddLight(root, "BlueGlow", LightType.Point, Vector3.zero, new Vector3(-4.5f, 2f, 4f), RoyalBlue, 1.5f, 6f, false);
            AddLight(root, "CoralGlow", LightType.Point, Vector3.zero, new Vector3(4.5f, 2f, 4f), new Color(1f, 0.45f, 0.65f), 1.2f, 6f, false);
        }

        private void BuildStage(Transform root)
        {
            Material stage = CreateMaterial(RoyalBlue, 0.55f, 0f, 0.05f);
            Part("StageRim", root, CylinderMesh, new Vector3(0f, 0.08f, 0f), new Vector3(4.9f, 0.08f, 4.9f), _gold);

            _turntable = Group("Turntable", root, Vector3.zero);
            Part("Stage", _turntable, CylinderMesh, new Vector3(0f, 0.2f, 0f), new Vector3(4.6f, 0.14f, 4.6f), stage);

            // A ring of little gold studs around the stage edge.
            const int Studs = 18;
            for (int i = 0; i < Studs; i++)
            {
                float angle = i * Mathf.PI * 2f / Studs;
                Vector3 position = new Vector3(Mathf.Cos(angle) * 2.15f, StageTop, Mathf.Sin(angle) * 2.15f);
                Part("Stud", _turntable, SphereMesh, position, Vector3.one * 0.1f, _goldGlow, Vector3.zero, false);
            }

            for (int i = 0; i < MaxPillars; i++)
            {
                _pillars.Add(new Pillar(this, _turntable, i));
            }
        }

        private void BuildScales(Transform root)
        {
            // Stands to the left of the stage, so it never hides the pillars.
            Transform scales = Group("ScalesOfJustice", root, new Vector3(-3.9f, 0f, 0.6f));
            Part("Base", scales, CylinderMesh, new Vector3(0f, 0.08f, 0f), new Vector3(1.2f, 0.08f, 1.2f), _gold);
            Part("Foot", scales, CylinderMesh, new Vector3(0f, 0.22f, 0f), new Vector3(0.6f, 0.06f, 0.6f), _gold);
            Part("Post", scales, CylinderMesh, new Vector3(0f, 1.55f, 0f), new Vector3(0.13f, 1.3f, 0.13f), _gold);
            Part("Finial", scales, SphereMesh, new Vector3(0f, 3f, 0f), Vector3.one * 0.26f, _goldGlow);

            _beam = Group("Beam", scales, new Vector3(0f, 2.78f, 0f));
            Part("Bar", _beam, BuiltinAssets.CubeMesh, Vector3.zero, new Vector3(2.3f, 0.08f, 0.08f), _gold);
            Part("Pivot", _beam, SphereMesh, Vector3.zero, Vector3.one * 0.16f, _gold);
            _leftHanger = BuildPan(_beam, "LeftPan", -1.1f);
            _rightHanger = BuildPan(_beam, "RightPan", 1.1f);
        }

        private Transform BuildPan(Transform beam, string name, float x)
        {
            Transform hanger = Group(name, beam, new Vector3(x, 0f, 0f));
            Part("End", hanger, SphereMesh, Vector3.zero, Vector3.one * 0.12f, _goldGlow);

            // Two chains in a narrow "V" down to the pan.
            Part("ChainL", hanger, BuiltinAssets.CubeMesh, new Vector3(-0.15f, -0.42f, 0f), new Vector3(0.025f, 0.86f, 0.025f), _gold, new Vector3(0f, 0f, -20f));
            Part("ChainR", hanger, BuiltinAssets.CubeMesh, new Vector3(0.15f, -0.42f, 0f), new Vector3(0.025f, 0.86f, 0.025f), _gold, new Vector3(0f, 0f, 20f));
            Part("Pan", hanger, CylinderMesh, new Vector3(0f, -0.84f, 0f), new Vector3(0.8f, 0.035f, 0.8f), _gold);
            Part("PanBowl", hanger, SphereMesh, new Vector3(0f, -0.88f, 0f), new Vector3(0.6f, 0.12f, 0.6f), _gold);
            return hanger;
        }

        private void BuildRobot(Transform root)
        {
            Material body = CreateMaterial(new Color(0.16f, 0.24f, 0.55f), 0.5f, 0f, 0f);
            Material silver = CreateMaterial(new Color(0.8f, 0.84f, 0.92f), 0.75f, 0.2f, 0f);
            Material dark = CreateMaterial(Backdrop, 0.3f, 0f, 0f);

            Transform robot = Group("AiRobot", root, new Vector3(3.3f, 0f, -0.4f));
            Part("Pedestal", robot, CylinderMesh, new Vector3(0f, 0.3f, 0f), new Vector3(1f, 0.3f, 1f), body);
            Part("PedestalTrim", robot, CylinderMesh, new Vector3(0f, 0.61f, 0f), new Vector3(1.05f, 0.02f, 1.05f), _goldGlow);
            Part("Neck", robot, CylinderMesh, new Vector3(0f, 0.74f, 0f), new Vector3(0.24f, 0.12f, 0.24f), silver);

            _robotHead = Group("Head", robot, new Vector3(0f, 1.28f, 0f));
            Part("Skull", _robotHead, SphereMesh, Vector3.zero, Vector3.one * 0.95f, silver);
            _visor = Part("Visor", _robotHead, BuiltinAssets.CubeMesh, new Vector3(0f, 0.06f, -0.33f), new Vector3(0.66f, 0.22f, 0.3f), _visorGreen)
                .GetComponent<MeshRenderer>();
            Part("EyeL", _robotHead, SphereMesh, new Vector3(-0.14f, 0.06f, -0.48f), Vector3.one * 0.08f, _eye, Vector3.zero, false);
            Part("EyeR", _robotHead, SphereMesh, new Vector3(0.14f, 0.06f, -0.48f), Vector3.one * 0.08f, _eye, Vector3.zero, false);
            Part("Mouth", _robotHead, BuiltinAssets.CubeMesh, new Vector3(0f, -0.2f, -0.43f), new Vector3(0.22f, 0.035f, 0.04f), dark);
            Part("EarL", _robotHead, CylinderMesh, new Vector3(-0.48f, 0f, 0f), new Vector3(0.2f, 0.06f, 0.2f), _coral, new Vector3(0f, 0f, 90f));
            Part("EarR", _robotHead, CylinderMesh, new Vector3(0.48f, 0f, 0f), new Vector3(0.2f, 0.06f, 0.2f), _coral, new Vector3(0f, 0f, 90f));
            Part("Antenna", _robotHead, CylinderMesh, new Vector3(0f, 0.6f, 0f), new Vector3(0.035f, 0.16f, 0.035f), silver);
            _antennaTip = Part("AntennaTip", _robotHead, SphereMesh, new Vector3(0f, 0.8f, 0f), Vector3.one * 0.15f, _visorGreen);
            _antennaTipRenderer = _antennaTip.GetComponent<MeshRenderer>();
        }

        private void BuildConfetti(Transform root)
        {
            Color[] colours = { Emerald, Amber, Coral, RoyalBlue, Gold, new Color(0.85f, 0.45f, 1f) };
            var materials = new Material[colours.Length];
            for (int i = 0; i < colours.Length; i++)
            {
                materials[i] = CreateMaterial(colours[i], 0.4f, 0f, 0.35f);
            }

            _confettiRoot = Group("Confetti", root, Vector3.zero);
            _confettiRoot.gameObject.SetActive(false);
            _confetti = new Transform[ConfettiCount];
            _confettiVelocity = new Vector3[ConfettiCount];
            _confettiSpin = new Vector3[ConfettiCount];
            for (int i = 0; i < ConfettiCount; i++)
            {
                // Each piece is a tiny flat cube: a paper square.
                _confetti[i] = Part("Piece", _confettiRoot, BuiltinAssets.CubeMesh, Vector3.zero, new Vector3(0.09f, 0.09f, 0.012f), materials[i % materials.Length], Vector3.zero, false);
            }
        }

        private void CreateMaterials()
        {
            _emerald = CreateMaterial(Emerald, 0.55f, 0f, 0.08f);
            _amber = CreateMaterial(Amber, 0.55f, 0f, 0.08f);
            _coral = CreateMaterial(Coral, 0.55f, 0f, 0.08f);
            _coralGlow = CreateMaterial(Coral, 0.6f, 0f, 0.7f);
            _gold = CreateMaterial(Gold, 0.7f, 0.35f, 0.12f);
            _goldGlow = CreateMaterial(Gold, 0.6f, 0f, 0.8f);
            _icon = CreateMaterial(new Color(0.93f, 0.95f, 1f), 0.5f, 0f, 0.25f);
            _lens = CreateMaterial(new Color(0.45f, 0.9f, 1f), 0.9f, 0f, 0.6f);
            _plinth = CreateMaterial(new Color(0.12f, 0.16f, 0.36f), 0.4f, 0f, 0f);
            _visorGreen = CreateMaterial(Emerald, 0.8f, 0f, 1.1f);
            _visorAmber = CreateMaterial(Amber, 0.8f, 0f, 1.1f);
            _eye = CreateMaterial(Color.white, 0.8f, 0f, 1f);
        }

        // ---------------------------------------------------------------- Small helpers

        /// <summary>A plain Standard-shader material: colour, shininess, metal, and how much it glows by itself.</summary>
        private Material CreateMaterial(Color albedo, float smoothness, float metallic, float glow)
        {
            if (_template == null)
            {
                _template = CreateTemplate();
            }

            var material = new Material(_template) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", albedo);
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            if (glow > 0f && material.HasProperty("_EmissionColor"))
            {
                // Emission makes a surface look lit from inside, which is how the star and visor "glow".
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", albedo * glow);
            }

            _materials.Add(material);
            return material;
        }

        private static Material CreateTemplate()
        {
            Shader shader = Shader.Find("Standard");
            if (shader != null)
            {
                return new Material(shader) { hideFlags = HideFlags.DontSave };
            }

            // Shader.Find can fail in a player build that stripped the shader; a primitive's material always exists.
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var fallback = new Material(probe.GetComponent<Renderer>().sharedMaterial) { hideFlags = HideFlags.DontSave };
            UnityObjectUtil.SafeDestroy(probe);
            return fallback;
        }

        private static Transform Group(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name) { layer = Layer };
            Transform transform = go.transform;
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            return transform;
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale, Material material)
        {
            return Part(name, parent, mesh, localPosition, scale, material, Vector3.zero, true);
        }

        private static Transform Part(string name, Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale, Material material, Vector3 euler)
        {
            return Part(name, parent, mesh, localPosition, scale, material, euler, true);
        }

        /// <summary>One coloured primitive. No collider: nothing ever touches the diorama.</summary>
        private static Transform Part(string name, Transform parent, Mesh mesh, Vector3 localPosition, Vector3 scale, Material material, Vector3 euler, bool castShadows)
        {
            Transform transform = Group(name, parent, localPosition);
            transform.localRotation = Quaternion.Euler(euler);
            transform.localScale = scale;
            transform.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = transform.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return transform;
        }

        /// <summary>A ring drawn as short bars around a circle, facing the camera. Used for the key and the magnifier.</summary>
        private static void AddRing(Transform parent, Vector3 centre, float radius, float thickness, int segments, Material material)
        {
            float segmentLength = 2f * Mathf.PI * radius / segments * 1.15f; // a little overlap hides the joins
            for (int i = 0; i < segments; i++)
            {
                float degrees = i * 360f / segments;
                float radians = degrees * Mathf.Deg2Rad;
                Vector3 position = centre + (new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * radius);

                // Turning the bar by (angle + 90°) lays it along the circle instead of across it.
                Part("Ring", parent, BuiltinAssets.CubeMesh, position, new Vector3(segmentLength, thickness, thickness), material, new Vector3(0f, 0f, degrees + 90f));
            }
        }

        private void AddLight(Transform root, string name, LightType type, Vector3 euler, Vector3 position, Color colour, float intensity, float range, bool shadows)
        {
            Transform transform = Group(name, root, position);
            transform.localRotation = Quaternion.Euler(euler);
            var light = transform.gameObject.AddComponent<Light>();
            light.type = type;
            light.color = colour;
            light.intensity = intensity;
            if (type == LightType.Point)
            {
                light.range = range;
            }

            // Only light the diorama layer, so these lights never brighten the courtroom.
            light.cullingMask = LayerMaskBit;

            // "Important" lights are always drawn per pixel, so the few lights here look smooth on the big flat walls.
            light.renderMode = LightRenderMode.ForcePixel;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = 0.55f;
        }

        /// <summary>
        /// Removes the diorama layer from every other camera and light. The scene is also far away, so this is a
        /// second safety net; it runs each time the report opens because cameras can be created later (e.g. the
        /// judge's seat camera).
        /// </summary>
        private static void StopOtherCamerasAndLightsSeeingUs()
        {
            foreach (Camera other in FindObjectsOfType<Camera>(true))
            {
                if (other.gameObject.layer != Layer)
                {
                    other.cullingMask &= ~LayerMaskBit;
                }
            }

            foreach (Light other in FindObjectsOfType<Light>(true))
            {
                if (other.gameObject.layer != Layer)
                {
                    other.cullingMask &= ~LayerMaskBit;
                }
            }
        }

        private static Mesh SphereMesh
        {
            get
            {
                if (_sphere == null)
                {
                    _sphere = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                }

                return _sphere;
            }
        }

        private static Mesh CylinderMesh
        {
            get
            {
                if (_cylinder == null)
                {
                    _cylinder = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
                }

                return _cylinder;
            }
        }

        /// <summary>
        /// One podium: a plinth, a pillar whose height shows the result, a marker on top (star, medal or cross)
        /// and the phase's icon floating above (key for bail, magnifying glass for evidence, gavel for sentencing).
        /// </summary>
        private sealed class Pillar
        {
            private readonly Transform _body;
            private readonly Transform _cap;
            private readonly MeshRenderer _bodyRenderer;
            private readonly Transform _star;
            private readonly Transform _medal;
            private readonly Transform _cross;
            private readonly Transform _key;
            private readonly Transform _magnifier;
            private readonly Transform _gavel;
            private readonly Transform _iconHolder;
            private Transform _marker;
            private float _height;
            private bool _spin;

            public Pillar(ResultsDiorama diorama, Transform parent, int index)
            {
                Root = Group($"Pillar_{index + 1}", parent, Vector3.zero);
                Part("Plinth", Root, BuiltinAssets.CubeMesh, new Vector3(0f, PlinthHeight * 0.5f, 0f), new Vector3(0.95f, PlinthHeight, 0.95f), diorama._plinth);
                _body = Part("Body", Root, CylinderMesh, Vector3.zero, Vector3.one, diorama._emerald);
                _bodyRenderer = _body.GetComponent<MeshRenderer>();
                _cap = Part("Cap", Root, CylinderMesh, Vector3.zero, new Vector3(PillarWidth + 0.1f, 0.03f, PillarWidth + 0.1f), diorama._gold);

                _star = BuildStar(diorama, Root);
                _medal = BuildMedal(diorama, Root);
                _cross = BuildCross(diorama, Root);

                _iconHolder = Group("Icon", Root, Vector3.zero);
                _key = BuildKey(diorama, _iconHolder);
                _magnifier = BuildMagnifier(diorama, _iconHolder);
                _gavel = BuildGavel(diorama, _iconHolder);
            }

            public Transform Root { get; }

            public void Set(OptionQuality quality, DecisionPointKind kind, ResultsDiorama diorama)
            {
                switch (quality)
                {
                    case OptionQuality.Sound:
                        _height = 1.55f;
                        _bodyRenderer.sharedMaterial = diorama._emerald;
                        _marker = _star;
                        _spin = true;
                        break;
                    case OptionQuality.Defensible:
                        _height = 1.0f;
                        _bodyRenderer.sharedMaterial = diorama._amber;
                        _marker = _medal;
                        _spin = true;
                        break;
                    default:
                        _height = 0.45f;
                        _bodyRenderer.sharedMaterial = diorama._coral;
                        _marker = _cross;
                        _spin = false;
                        break;
                }

                _star.gameObject.SetActive(_marker == _star);
                _medal.gameObject.SetActive(_marker == _medal);
                _cross.gameObject.SetActive(_marker == _cross);
                _key.gameObject.SetActive(kind == DecisionPointKind.Bail);
                _magnifier.gameObject.SetActive(kind == DecisionPointKind.EvidenceAdmissibility);
                _gavel.gameObject.SetActive(kind == DecisionPointKind.Sentencing);
            }

            /// <param name="rise">0 → 1 as the pillar grows.</param>
            /// <param name="pop">0 → 1 (with a small overshoot) as the marker and icon appear.</param>
            public void Animate(float rise, float pop, float t, int index)
            {
                float height = Mathf.Lerp(0.02f, _height, rise);

                // Unity's cylinder is 2 units tall, so a y-scale of half the height gives the right size.
                _body.localScale = new Vector3(PillarWidth, height * 0.5f, PillarWidth);
                _body.localPosition = new Vector3(0f, PlinthHeight + (height * 0.5f), 0f);
                float top = PlinthHeight + height;
                _cap.localPosition = new Vector3(0f, top, 0f);

                float markerYaw = _spin ? t * 90f : Mathf.Sin((t * 2f) + index) * 20f;
                _marker.localPosition = new Vector3(0f, top + 0.32f, 0f);
                _marker.localRotation = Quaternion.Euler(0f, markerYaw, 0f);
                _marker.localScale = Vector3.one * pop;

                // The icon floats and bobs gently above the marker.
                _iconHolder.localPosition = new Vector3(0f, top + 0.98f + (Mathf.Sin((t * 2f) + index) * 0.06f), 0f);
                _iconHolder.localRotation = Quaternion.Euler(0f, Mathf.Sin((t * 0.8f) + index) * 25f, 0f);
                _iconHolder.localScale = Vector3.one * (0.75f * pop);
            }

            /// <summary>A five-pointed star: five bars pointing outwards from a round centre.</summary>
            private static Transform BuildStar(ResultsDiorama diorama, Transform parent)
            {
                Transform star = Group("Star", parent, Vector3.zero);
                Part("Centre", star, SphereMesh, Vector3.zero, new Vector3(0.22f, 0.22f, 0.12f), diorama._goldGlow);
                for (int i = 0; i < 5; i++)
                {
                    float degrees = i * 72f;
                    float radians = degrees * Mathf.Deg2Rad;
                    Vector3 direction = new Vector3(Mathf.Sin(radians), Mathf.Cos(radians), 0f);

                    // A bar's length runs along its y axis; turning it by -angle points it along "direction".
                    Part("Point", star, BuiltinAssets.CubeMesh, direction * 0.17f, new Vector3(0.11f, 0.3f, 0.07f), diorama._goldGlow, new Vector3(0f, 0f, -degrees));
                }

                return star;
            }

            /// <summary>An amber medal for an acceptable ruling: a disc with a gold rim.</summary>
            private static Transform BuildMedal(ResultsDiorama diorama, Transform parent)
            {
                Transform medal = Group("Medal", parent, Vector3.zero);
                Part("Disc", medal, CylinderMesh, Vector3.zero, new Vector3(0.36f, 0.035f, 0.36f), diorama._amber, new Vector3(90f, 0f, 0f));
                AddRing(medal, Vector3.zero, 0.2f, 0.045f, 14, diorama._goldGlow);
                return medal;
            }

            /// <summary>A coral cross for a wrong ruling: two crossed bars.</summary>
            private static Transform BuildCross(ResultsDiorama diorama, Transform parent)
            {
                Transform cross = Group("Cross", parent, Vector3.zero);
                Part("BarA", cross, BuiltinAssets.CubeMesh, Vector3.zero, new Vector3(0.09f, 0.42f, 0.09f), diorama._coralGlow, new Vector3(0f, 0f, 45f));
                Part("BarB", cross, BuiltinAssets.CubeMesh, Vector3.zero, new Vector3(0.09f, 0.42f, 0.09f), diorama._coralGlow, new Vector3(0f, 0f, -45f));
                return cross;
            }

            /// <summary>Bail: a key, because bail unlocks the cell door.</summary>
            private static Transform BuildKey(ResultsDiorama diorama, Transform parent)
            {
                Transform key = Group("Key", parent, Vector3.zero);
                AddRing(key, new Vector3(0f, 0.2f, 0f), 0.16f, 0.07f, 10, diorama._goldGlow);
                Part("Shaft", key, BuiltinAssets.CubeMesh, new Vector3(0f, -0.14f, 0f), new Vector3(0.07f, 0.52f, 0.07f), diorama._goldGlow);
                Part("ToothA", key, BuiltinAssets.CubeMesh, new Vector3(0.07f, -0.36f, 0f), new Vector3(0.13f, 0.06f, 0.07f), diorama._goldGlow);
                Part("ToothB", key, BuiltinAssets.CubeMesh, new Vector3(0.055f, -0.24f, 0f), new Vector3(0.1f, 0.05f, 0.07f), diorama._goldGlow);
                return key;
            }

            /// <summary>Evidence: a magnifying glass with a glowing blue lens.</summary>
            private static Transform BuildMagnifier(ResultsDiorama diorama, Transform parent)
            {
                Transform glass = Group("Magnifier", parent, Vector3.zero);
                Vector3 centre = new Vector3(-0.06f, 0.08f, 0f);
                AddRing(glass, centre, 0.2f, 0.06f, 14, diorama._icon);
                Part("Lens", glass, CylinderMesh, centre, new Vector3(0.36f, 0.015f, 0.36f), diorama._lens, new Vector3(90f, 0f, 0f));

                // The handle points down and to the right from the rim.
                Vector3 direction = new Vector3(0.6f, -0.8f, 0f);
                float angle = Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg;
                Part("Handle", glass, CylinderMesh, centre + (direction * 0.38f), new Vector3(0.08f, 0.16f, 0.08f), diorama._icon, new Vector3(0f, 0f, angle));
                return glass;
            }

            /// <summary>Sentencing: a judge's gavel, tilted as if mid-strike.</summary>
            private static Transform BuildGavel(ResultsDiorama diorama, Transform parent)
            {
                Transform gavel = Group("Gavel", parent, Vector3.zero);
                gavel.localRotation = Quaternion.Euler(0f, 0f, 25f);
                Part("Head", gavel, CylinderMesh, new Vector3(0f, 0.16f, 0f), new Vector3(0.22f, 0.22f, 0.22f), diorama._icon, new Vector3(0f, 0f, 90f));
                Part("BandL", gavel, CylinderMesh, new Vector3(-0.15f, 0.16f, 0f), new Vector3(0.245f, 0.025f, 0.245f), diorama._goldGlow, new Vector3(0f, 0f, 90f));
                Part("BandR", gavel, CylinderMesh, new Vector3(0.15f, 0.16f, 0f), new Vector3(0.245f, 0.025f, 0.245f), diorama._goldGlow, new Vector3(0f, 0f, 90f));
                Part("Handle", gavel, CylinderMesh, new Vector3(0f, -0.14f, 0f), new Vector3(0.065f, 0.26f, 0.065f), diorama._icon);
                return gavel;
            }
        }
    }
}
