using UnityEngine;

namespace Archer
{
    /// <summary>
    /// Entry point of the vertical slice. Builds the whole world at runtime so
    /// the scene stays trivial and everything lives in version-controlled code.
    /// Drop this on a single GameObject and press Play.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Arena")]
        public float ArenaWidth = 26f;
        public float ArenaLength = 140f;

        [Header("Camera (over the shoulder, tuned for 9:16)")]
        public Vector3 CameraOffset = new Vector3(0f, 2.6f, -4f);
        public Vector3 CameraLookAt = new Vector3(0f, 0.1f, 24f);
        public float CameraFov = 60f;

        [Header("Spawn lane (narrow so monsters stay in the portrait frame)")]
        public float SpawnHalfWidth = 4.8f;
        public float SpawnNearZ = 21f;
        public float SpawnFarZ = 30f;

        [Header("Bow")]
        public float MinLaunchSpeed = 16f;
        public float MaxLaunchSpeed = 30f;
        public float MinPitch = -20f;
        public float MaxPitch = 52f;
        public float MaxYaw = 34f;
        public float ArrowDamage = 34f;

        [Header("Archer")]
        public float ArcherHealth = 100f;
        public int MaxArrows = 6;

        [Header("Time")]
        public float StartTimeOfDay = 0.65f;

        Transform _world;
        Transform _arrowParent;
        Transform _monsterParent;
        GameObject _archerRoot;
        Camera _camera;
        Light _sun;
        ArcherController _archer;

        void Awake()
        {
            // Unity's mobile default is 30 fps. The display here is 60 Hz, and
            // uncapping removes the single largest source of "slowness".
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;

            _world = new GameObject("World").transform;

            BuildGround();
            ForestScene.Build(_world);
            BuildVillageEdge();
            BuildLighting();
            BuildCamera();
            BuildArcher();
            _world.gameObject.AddComponent<GameFeedback>();
            BuildManagers();
            BuildTimeOfDay();
            BuildHud();
            _world.gameObject.AddComponent<ForestAtmosphere>();
            gameObject.AddComponent<RuntimeMaterials>().Capture(_world);
            var effects = new GameObject("Combat effects");
            effects.transform.SetParent(_world, false);
            effects.AddComponent<CombatEffects>();

            Debug.Log("[Last Ranger] The forest watch is ready.");
        }

        void BuildGround()
        {
            var groundMat = Prim.Material(new Color(0.22f, 0.29f, 0.25f), 0.05f);
            var ground = Prim.Create(PrimitiveType.Plane, "Ground", _world,
                new Vector3(0f, 0f, ArenaLength * 0.5f - 15f),
                new Vector3(ArenaWidth / 10f, 1f, ArenaLength / 10f), groundMat);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            ForestPath.Build(_world, ArenaLength);
        }

        void BuildVillageEdge()
        {
            var gate = new GameObject("VillageEdge").transform;
            gate.SetParent(_world, false);

            var woodMat = Prim.Material(new Color(0.33f, 0.22f, 0.13f), 0.1f);
            var darkWoodMat = Prim.Material(new Color(0.24f, 0.16f, 0.09f), 0.1f);

            // Fence posts + rails flank the lane (center stays open).
            float side = 6.1f;
            for (int s = -1; s <= 1; s += 2)
            {
                for (int i = 0; i < 6; i++)
                {
                    float z = 4f + i * 3.2f;
                    Prim.Create(PrimitiveType.Cylinder, "FencePost", gate, new Vector3(s * side, 0.6f, z),
                        new Vector3(0.11f, 0.6f, 0.11f), woodMat, false);
                    Prim.Create(PrimitiveType.Cube, "RailA", gate, new Vector3(s * side, 0.95f, z + 1.6f),
                        new Vector3(0.09f, 0.09f, 3.2f), woodMat, false);
                    Prim.Create(PrimitiveType.Cube, "RailB", gate, new Vector3(s * side, 0.5f, z + 1.6f),
                        new Vector3(0.09f, 0.09f, 3.2f), woodMat, false);
                }

                // A lantern every other post for warm amber light.
                BuildLantern(gate, new Vector3(s * side, 2.0f, 4f));
                BuildLantern(gate, new Vector3(s * side, 2.0f, 13.6f));
            }

            BuildLantern(gate, new Vector3(-1.30f, 2.1f, 0.2f));
            BuildLantern(gate, new Vector3(1.30f, 2.1f, 0.2f));

            // A wooden gate arch at the near edge, framing the approach.
            float gw = 1.5f;
            for (int s = -1; s <= 1; s += 2)
                Prim.Create(PrimitiveType.Cylinder, "GatePost", gate, new Vector3(s * gw, 2.9f, 0f),
                    new Vector3(0.34f, 2.9f, 0.34f), woodMat, false);
            Prim.Create(PrimitiveType.Cube, "GateLintel", gate, new Vector3(0f, 5.8f, 0f),
                new Vector3(gw * 2f + 0.5f, 0.36f, 0.36f), darkWoodMat, false);

            // The fence, posts and lantern frames are static: batch them once.
            StaticBatchingUtility.Combine(gate.gameObject);
        }

        void BuildLantern(Transform parent, Vector3 localPos)
        {
            var iron = Prim.Material(new Color(0.12f,0.12f,0.10f),0.2f,0.4f);
            var glow = Prim.Emissive(new Color(1f,0.65f,0.20f),2.5f);
            Prim.Create(PrimitiveType.Cube,"Lantern flame",parent,localPos,new Vector3(0.26f,0.40f,0.26f),glow,false);
            for(int y=-1;y<=1;y+=2)
                Prim.Create(PrimitiveType.Cube,"Lantern cap",parent,localPos+Vector3.up*(y*0.23f),new Vector3(0.40f,0.07f,0.40f),iron,false);
            for(int x=-1;x<=1;x+=2)
                for(int z=-1;z<=1;z+=2)
                    Prim.Create(PrimitiveType.Cube,"Lantern frame",parent,localPos+new Vector3(x*0.16f,0,z*0.16f),new Vector3(0.035f,0.46f,0.035f),iron,false);
            var lightGo = new GameObject("Lantern light"); lightGo.transform.SetParent(parent,false); lightGo.transform.localPosition=localPos;
            var light = lightGo.AddComponent<Light>(); light.type=LightType.Point;
            light.range=13f; light.intensity=3f; light.color=new Color(1f,0.66f,0.28f);
        }

        void BuildLighting()
        {
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(_world, false);
            sunGo.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            _sun = sunGo.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.intensity = 1.2f;
            _sun.color = new Color(1f, 0.96f, 0.88f);
            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.5f;
            var fill = new GameObject("Moon rim");
            fill.transform.SetParent(_world, false);
            fill.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
            var rim = fill.AddComponent<Light>();
            rim.type = LightType.Directional; rim.intensity = 0.6f;
            rim.color = new Color(0.48f, 0.67f, 1f);
        }

        void BuildCamera()
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(_world, false);
            camGo.transform.position = CameraOffset;
            camGo.transform.rotation = Quaternion.LookRotation((CameraLookAt - CameraOffset).normalized, Vector3.up);

            _camera = camGo.AddComponent<Camera>();
            _camera.fieldOfView = CameraFov;
            _camera.nearClipPlane = 0.08f;
            _camera.farClipPlane = 1200f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.45f, 0.62f, 0.85f);
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<AspectFov>();
        }

        void BuildArcher()
        {
            _archerRoot = new GameObject("Archer");
            _archerRoot.transform.SetParent(_world, false);
            _archerRoot.transform.position = Vector3.zero;

            _arrowParent = new GameObject("Arrows").transform;
            _arrowParent.SetParent(_world, false);

            var controller = _archerRoot.AddComponent<ArcherController>();
            _archer = controller;
            controller.MinLaunchSpeed = MinLaunchSpeed;
            controller.MaxLaunchSpeed = MaxLaunchSpeed;
            controller.MinPitch = MinPitch;
            controller.MaxPitch = MaxPitch;
            controller.MaxYaw = MaxYaw;
            controller.ArrowDamage = ArrowDamage;
            controller.ShowAimGuide = PlayerPrefs.GetInt("LastRanger.Guide", 1) == 1;   // on by default for beginners
            controller.Initialize(_archerRoot.transform, _arrowParent);
        }

        void BuildManagers()
        {
            _monsterParent = new GameObject("Monsters").transform;
            _monsterParent.SetParent(_world, false);

            var managerGo = new GameObject("GameManager");
            managerGo.transform.SetParent(_world, false);
            var gm = managerGo.AddComponent<GameManager>();
            gm.MaxHealth = ArcherHealth;
            gm.Health = ArcherHealth;
            gm.MaxArrows = MaxArrows;
            gm.Arrows = MaxArrows;
            gm.MonsterParent = _monsterParent;
            gm.ArcherTarget = _archerRoot.transform;
            gm.SpawnHalfWidth = SpawnHalfWidth;
            gm.SpawnNearZ = SpawnNearZ;
            gm.SpawnFarZ = SpawnFarZ;
            gm.Archer = _archer;
        }

        void BuildTimeOfDay()
        {
            var tod = TimeOfDay.Create(_camera, _sun);
            tod.StartTime = StartTimeOfDay;
            tod.ResetToStart();
            tod.transform.SetParent(_world, false);
        }

        void BuildHud()
        {
            var hudGo = new GameObject("HUD");
            hudGo.transform.SetParent(_world, false);
            hudGo.AddComponent<Hud>();
        }
    }
}
