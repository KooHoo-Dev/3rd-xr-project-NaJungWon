using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace TimeSlasher
{
    // 낮의 신사 마당을 코드로 배치하는 에디터 도구 (GameUIBuilder와 같은 방식)
    // - 빈 GameObject에 붙이고 컴포넌트 우클릭 → "1. Build Shrine Yard" / "2. Clear Shrine Yard" / "3. Apply Sky"
    // - 플레이어는 원점에 서서 움직이지 않으므로, 지금 Floor(30m x 30m) 안에만 조금 놓고
    //   바깥은 돌담 + 나무로 둘러 가려서 안 보이게 함
    //
    // 에셋을 그대로 쓰면 생기는 문제와 여기서 한 처리
    // 1) AnimeSuburbCity 데모 씬은 오브젝트가 약 1만 개 → 데모를 복사하지 않고 필요한 프리팹만 몇 개 놓음
    // 2) 연못: 데모는 땅을 파서 물을 0.55m 아래에 두고 바위를 반쯤 묻은 구조라 평평한 Floor에 그대로 못 씀
    //    → 바위로 테두리를 두른 "정원 연못"으로 만듦 (납작한 원판 물 + 반쯤 묻힌 바위 + 물결)
    //    → 다리는 원래 크기 그대로 두고, 연못 크기를 다리 길이에 맞춰 양끝이 땅에 닿게 함
    //    → 물(WaterShader)은 Scene Depth를 써서 Quest에서 무거움 → URP Lit 물 머티리얼
    // 2-1) 바닥: Suburb 바닥 셰이더는 타일링이 없어 30m Floor에 늘어남
    //    → 흙 텍스처로 URP Lit 머티리얼을 만들어 반복(타일링)시키고, 참배길은 SM_Pavement 타일로 깜
    // 3) 장식에 붙은 콜라이더는 필요 없음 (칼 판정은 과일 물리 씬에서 함) → 지워서 레이 판정·물리 비용을 줄임
    // 4) 배경을 Stages 아래에 만들어서 블레이드 타임 중 같이 어두워지게 함
    //    (Suburb 셰이더의 _Tint·_TopColor·_BotColor는 BladeTimeController가 처리)
    // 5) 사과(빨강)와 색이 겹치지 않게: 정면에는 회색 돌 토리이, 붉은 토리이는 등 뒤 입구, 가을 주황 나무는 안 씀
    //
    // 만드는 구조 (플레이어 원점, +Z가 정면)
    // Floor     머티리얼만 흙 바닥(M_ShrineGround)으로 바꿈 (Clear하면 원래대로)
    // Shrine Yard
    //  ├ Ground  참배길: 가운데 광장(3x3 타일) + 앞(본전까지)·뒤(입구까지) 길
    //  ├ Front   돌 토리이(9m), 본전(13m), 석등 2개
    //  ├ Left    바위 테두리 연못(왼쪽 앞) + 붉은 다리 + 풀
    //  ├ Right   벚나무 2그루, 덤불
    //  ├ Back    붉은 토리이 (등 뒤 6m, 입구)
    //  └ Ring    둘레 돌담(반지름 14m) + 큰 나무 → 바깥을 가림
    public class ShrineYardBuilder : MonoBehaviour
    {
        public const string RootName = "Shrine Yard";

        [Header("부모 (비우면 씬의 'Stages')")]
        [SerializeField] private Transform stageRoot;      // BladeTimeController.dimRoots에 들어 있는 Stages

        [Header("배치")]
        [SerializeField] private float ringRadius = 14f;      // 둘레 돌담 반지름 (Floor 30m의 절반보다 조금 작게)
        [SerializeField] private float wallHeight = 2.5f;     // 돌담 높이 (눈높이 1.6m보다 높아야 바깥 바닥이 안 보임)
        [SerializeField] private int ringTreeCount = 10;      // 돌담 안쪽에 둘러 심는 큰 나무 수
        [SerializeField] private bool removeColliders = true; // 장식 콜라이더 지우기

        [Header("바닥")]
        [SerializeField] private Renderer floorRenderer;          // 지금 Floor (비우면 Stages 아래 'Floor')
        [SerializeField] private float groundTileMeters = 2.5f;   // 흙 텍스처 한 장이 덮는 크기 (작을수록 촘촘)
        [SerializeField, HideInInspector] private Material originalFloorMat;   // Clear 때 되돌릴 원래 머티리얼

        [Header("연못 (왼쪽 앞)")]
        [SerializeField] private Vector3 pondCenter = new Vector3(-7f, 0f, 4.5f);
        [SerializeField] private float pondLengthRatio = 0.48f;   // 다리 방향 지름 = 다리 길이 × 이 값 (1보다 작아야 다리 양끝이 땅 위)
        [SerializeField] private float pondWidthRatio = 0.5f;     // 다리와 직각 방향 지름 = 다리 길이 × 이 값

#if UNITY_EDITOR
        // 쓰는 프리팹 (경로가 바뀌면 여기만 고치면 됨)
        private const string StoneToriiPath = "Assets/Anime Tokyo/Prefabs/Buildings/SM_toorigate_stone.prefab";
        private const string ShrineHallPath = "Assets/Anime Tokyo/Prefabs/Buildings/SM_building_traditional_01.prefab";
        private const string StoneLanternPath = "Assets/Anime Tokyo/Prefabs/Props Modular/SM_lantern_traditional_01.prefab";
        private const string WallPath = "Assets/Anime Tokyo/Prefabs/Props Modular/SM_wall_stoneblocks_01.prefab";
        private const string BigTreePath = "Assets/Anime Tokyo/Prefabs/Nature/SM_tree_large.prefab";
        private const string RedToriiPath = "Assets/AnimeSuburbCity/Prefabs/Park/SM_ToriiGate.prefab";
        private const string BridgePath = "Assets/AnimeSuburbCity/Prefabs/Park/SM_ParkBridge.prefab";
        private const string RipplePath = "Assets/AnimeSuburbCity/Prefabs/SM_WaterRipples.prefab";
        private const string PavementPath = "Assets/AnimeSuburbCity/Prefabs/Park/SM_Pavement.prefab";
        private const string GrassPath = "Assets/AnimeSuburbCity/Prefabs/Vegetation/SM_Grass.prefab";
        private const string GroundTexPath = "Assets/AnimeSuburbCity/Textures/T_Dirt_A.TGA";
        private const string BenchPath = "Assets/AnimeSuburbCity/Prefabs/Shop/SM_Bench.prefab";
        private const string FlowerBedPath = "Assets/AnimeSuburbCity/Prefabs/Props/SM_FlowerBed.prefab";
        private const string PetalsPath1 = "Assets/Anime Tokyo/Prefabs/Nature/SM_leaves_ground_01.prefab";   // 분홍 꽃잎 더미 (M_leaf)
        private const string PetalsPath2 = "Assets/Anime Tokyo/Prefabs/Nature/SM_leaves_ground_02.prefab";
        private const string PetalsPath3 = "Assets/Anime Tokyo/Prefabs/Nature/SM_leaves_ground_03.prefab";
        private const string Stone1Path = "Assets/AnimeSuburbCity/Prefabs/Park/SM_Stone_1.prefab";
        private const string Stone2Path = "Assets/AnimeSuburbCity/Prefabs/Park/SM_Stone_2.prefab";
        private const string SakuraPath = "Assets/AnimeSuburbCity/Prefabs/Vegetation/SM_tree02.prefab";   // 잎이 TreeLeaves02(분홍)
        private const string BushPath = "Assets/AnimeSuburbCity/Prefabs/Vegetation/SM_Bush01.prefab";
        private const string SkyPath = "Assets/Anime Tokyo/Materials/Skybox Materials/Skybox Daylight.mat";

        // 연못용 단순한 물 머티리얼 (없으면 처음 한 번 만들어 저장)
        private const string PondMatPath = "Assets/00. SlashTime/Material/M_ShrinePond.mat";
        private static readonly Color PondColor = new Color(0.12f, 0.32f, 0.36f);   // 깊어 보이게 약간 어둡게

        // 흙 바닥 머티리얼 (T_Dirt_A를 반복해서 깖)
        private const string GroundMatPath = "Assets/00. SlashTime/Material/M_ShrineGround.mat";
        private static readonly Color GroundTint = new Color(0.78f, 0.74f, 0.68f);   // 살짝 어둡게 → 과즙·조각이 잘 보임

        [ContextMenu("1. Build Shrine Yard")]
        public void BuildYard()
        {
            Transform parent = GetStageRoot();
            if (parent == null)
            {
                Debug.LogWarning("[ShrineYardBuilder] 'Stages'를 찾지 못했습니다. stageRoot를 연결하세요.");
                return;
            }
            if (parent.Find(RootName) != null)
            {
                Debug.LogWarning($"[ShrineYardBuilder] '{RootName}'이 이미 있습니다. 먼저 2. Clear Shrine Yard를 하세요.");
                return;
            }

            Random.InitState(7);   // 바위·나무의 랜덤 크기를 매번 같게 (다시 만들어도 같은 모습)

            ApplyFloorMaterial();

            Transform root = CreateGroup(RootName, parent);
            BuildGround(CreateGroup("Ground", root));
            BuildFront(CreateGroup("Front", root));
            BuildLeft(CreateGroup("Left", root));
            BuildRight(CreateGroup("Right", root));
            BuildBack(CreateGroup("Back", root));
            BuildRing(CreateGroup("Ring", root));

            // 움직이지 않는 배경이므로 Static (조명 굽기·묶어 그리기)
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.SetStaticEditorFlags(t.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            }

            if (removeColliders) RemoveColliders(root);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log($"[ShrineYardBuilder] 신사 마당을 만들었습니다. 오브젝트 {root.GetComponentsInChildren<Renderer>(true).Length}개 (Renderer 기준)");
        }

        [ContextMenu("2. Clear Shrine Yard")]
        public void ClearYard()
        {
            Transform parent = GetStageRoot();
            Transform old = parent != null ? parent.Find(RootName) : null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            // Floor 머티리얼을 원래대로
            Renderer floor = GetFloorRenderer();
            if (floor != null && originalFloorMat != null)
            {
                Undo.RecordObject(floor, "Restore Floor");
                floor.sharedMaterial = originalFloorMat;
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        // 하늘을 낮 하늘로 바꿈 (_Exposure가 있어서 블레이드 타임 중 하늘도 어두워짐)
        [ContextMenu("3. Apply Sky")]
        public void ApplySky()
        {
            Material sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            if (sky == null)
            {
                Debug.LogWarning($"[ShrineYardBuilder] 하늘 머티리얼이 없습니다: {SkyPath}");
                return;
            }
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[ShrineYardBuilder] 하늘을 Skybox Daylight로 바꿨습니다. Lighting 창에서 조명을 다시 구우세요.");
        }

        #region 바닥

        // Floor에 흙 바닥 머티리얼을 씌움 (원래 머티리얼은 기억해 둠)
        private void ApplyFloorMaterial()
        {
            Renderer floor = GetFloorRenderer();
            Material ground = GetGroundMaterial(floor);
            if (floor == null || ground == null)
            {
                Debug.LogWarning("[ShrineYardBuilder] Floor 또는 흙 텍스처를 찾지 못해 바닥은 그대로 둡니다.");
                return;
            }

            if (floor.sharedMaterial != ground)
            {
                Undo.RecordObject(this, "Remember Floor");
                originalFloorMat = floor.sharedMaterial;
                Undo.RecordObject(floor, "Shrine Ground");
                floor.sharedMaterial = ground;
            }
        }

        // 참배길: 가운데 광장 3x3 타일 + 앞으로 본전까지, 뒤로 입구 토리이까지 한 줄
        private void BuildGround(Transform group)
        {
            GameObject tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PavementPath);
            if (tilePrefab == null)
            {
                Debug.LogWarning($"[ShrineYardBuilder] 프리팹이 없습니다: {PavementPath}");
                return;
            }

            float tile = MeasureTileSize(tilePrefab);   // 타일 한 장 크기 (데모 기준 약 3m)

            // 가운데 광장 (플레이어가 서는 곳)
            for (int x = -1; x <= 1; x++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    PlaceTile(group, new Vector3(x * tile, 0f, z * tile));
                }
            }

            // 앞 길: 광장 앞에서 본전 바로 앞(약 11m)까지
            for (float z = tile * 2f; z < 11.5f; z += tile) PlaceTile(group, new Vector3(0f, 0f, z));

            // 뒤 길: 광장 뒤에서 입구 토리이(-6.5m) 너머까지
            for (float z = -tile * 2f; z > -9f; z -= tile) PlaceTile(group, new Vector3(0f, 0f, z));
        }

        // 타일은 원래 크기 그대로, 윗면이 바닥보다 1cm 위에 오게 (Floor와 겹쳐 깜빡이지 않게)
        private static void PlaceTile(Transform group, Vector3 position)
        {
            GameObject go = Place(PavementPath, group, position, 0f, 0f, 0f);
            if (go == null) return;
            // 피벗이 가운데가 아닐 수 있으므로 상자 가운데를 원하는 위치에 맞춤
            Bounds b = GetBounds(go);
            Vector3 move = new Vector3(position.x - b.center.x, 0.01f - b.max.y, position.z - b.center.z);
            go.transform.position += move;
        }

        #endregion

        #region 구역별 배치

        // 정면: 회색 돌 토리이 → 그 뒤 본전, 양옆 석등
        private void BuildFront(Transform group)
        {
            Place(StoneToriiPath, group, new Vector3(0f, 0f, 9f), 0f, 4.5f, 0f);
            Place(ShrineHallPath, group, new Vector3(0f, 0f, 12.5f), 180f, 0f, 9f);    // 정면이 플레이어를 보게
            Place(StoneLanternPath, group, new Vector3(-2.6f, 0f, 8.5f), 0f, 1.8f, 0f);
            Place(StoneLanternPath, group, new Vector3(2.6f, 0f, 8.5f), 0f, 1.8f, 0f);
        }

        // 왼쪽 앞: 바위로 테두리를 두른 정원 연못 + 그 위를 건너는 붉은 다리
        // 1) 다리를 원래 크기로 놓고 길이를 잼 (길이 방향을 앞뒤(Z)로 → 플레이어가 옆에서 아치 모양을 봄)
        // 2) 연못 = 다리 길이보다 조금 짧은 타원 → 다리 양끝이 연못 밖 땅에 닿음
        // 3) 테두리 = 바위를 타원 둘레에 반쯤 묻어서 촘촘히 (다리가 놓이는 두 곳은 비움)
        private void BuildLeft(Transform group)
        {
            Vector3 c = pondCenter;

            // 1. 다리 (크기는 그대로, 방향만 맞춤)
            GameObject bridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BridgePath);
            float bridgeLength = 5f;
            float bridgeYaw = 0f;
            if (bridgePrefab != null)
            {
                Vector3 size = MeasureSize(bridgePrefab);
                bridgeYaw = size.x > size.z ? 90f : 0f;   // 긴 쪽이 X면 90도 돌려서 Z(앞뒤)로
                bridgeLength = Mathf.Max(size.x, size.z);
                // 다리 피벗 = 양끝 붉은 발판 높이 (데모에서도 다리 피벗과 길 타일 높이가 같음)
                // → 바닥에 붙이기(PutOnGround)를 하면 물속 기둥이 바닥에 닿아 다리가 떠 버리므로 하지 않음
                GameObject bridge = Place(BridgePath, group, c, bridgeYaw, 0f, 0f, false);
                if (bridge != null) CenterOn(bridge, c);
            }

            // 2. 물: 납작한 원기둥(원판)을 타원으로 눌러서 바닥 바로 위에 깜
            float halfLong = bridgeLength * pondLengthRatio * 0.5f;   // 다리 방향(Z) 반지름 → 나머지는 양끝 땅 위
            float halfWide = bridgeLength * pondWidthRatio * 0.5f;
            CreateWater(group, c, halfWide, halfLong);

            // 물결 2개
            Place(RipplePath, group, c + new Vector3(-halfWide * 0.4f, 0.07f, halfLong * 0.3f), 0f, 0f, halfWide * 0.8f);
            Place(RipplePath, group, c + new Vector3(halfWide * 0.35f, 0.07f, -halfLong * 0.4f), 0f, 0f, halfWide * 0.6f);

            // 3. 바위 테두리
            BuildStoneRim(group, c, halfWide, halfLong);

            // 4. 둘레 풀 몇 포기
            for (int i = 0; i < 6; i++)
            {
                float angle = 30f + 60f * i + Random.Range(-10f, 10f);
                Vector3 pos = c + PointOnEllipse(angle, halfWide + 0.9f, halfLong + 0.9f);
                Place(GrassPath, group, pos, Random.Range(0f, 360f), Random.Range(0.4f, 0.7f), 0f);
            }
        }

        // 물 원판 (Unity 기본 Cylinder를 아주 납작하게). 콜라이더는 지움
        private static void CreateWater(Transform group, Vector3 center, float halfX, float halfZ)
        {
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Undo.RegisterCreatedObjectUndo(water, "Build Shrine Yard");
            water.name = "PondWater";
            water.transform.SetParent(group, false);
            water.transform.localPosition = center + new Vector3(0f, 0.05f, 0f);   // 바닥보다 5cm 위 (옆면은 바위가 가림)
            water.transform.localScale = new Vector3(halfX * 2f, 0.025f, halfZ * 2f);   // Cylinder 높이가 2라서 0.025 → 5cm
            Object.DestroyImmediate(water.GetComponent<Collider>());

            Material pond = GetPondMaterial();
            if (pond != null) water.GetComponent<Renderer>().sharedMaterial = pond;
        }

        // 연못 둘레에 바위를 반쯤 묻어 촘촘히 놓음 (데모처럼 크기·회전을 섞음)
        private static void BuildStoneRim(Transform group, Vector3 center, float halfX, float halfZ)
        {
            float perimeter = EllipsePerimeter(halfX, halfZ);
            int count = Mathf.CeilToInt(perimeter / 0.55f);   // 바위 간격 약 55cm

            for (int i = 0; i < count; i++)
            {
                float angle = 360f * i / count;

                // 다리가 놓이는 앞(0도)·뒤(180도)는 비워 둠
                if (Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 14f || Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 14f) continue;

                Vector3 pos = center + PointOnEllipse(angle, halfX, halfZ);
                string path = Random.value < 0.6f ? Stone1Path : Stone2Path;
                float width = Random.Range(0.55f, 1.0f);
                GameObject stone = Place(path, group, pos, Random.Range(0f, 360f), 0f, width);
                if (stone == null) continue;

                // 키의 40% 정도를 땅에 묻음 → 바닥에서 솟은 자연석처럼
                Bounds b = GetBounds(stone);
                stone.transform.position += Vector3.down * (b.size.y * 0.4f);
            }
        }

        // 오른쪽 뒤: 벚나무 2그루 + 덤불 (무기 선택대가 오른쪽 0.85m에 있으므로 멀리)
        // 오른쪽: 벚나무 정원 (광장 오른쪽 끝 약 5.5m부터 돌담 앞까지)
        // 벚나무 3그루 + 떨어진 꽃잎 + 석등 + 벤치 + 꽃밭 + 정원석·덤불·풀
        private void BuildRight(Transform group)
        {
            // 벚나무 (앞쪽 큰 것, 옆쪽, 뒤쪽 작은 것)
            Place(SakuraPath, group, new Vector3(7f, 0f, 5.5f), 40f, 6f, 0f);
            Place(SakuraPath, group, new Vector3(9.5f, 0f, 0.5f), 160f, 5.5f, 0f);
            Place(SakuraPath, group, new Vector3(7.5f, 0f, -4.5f), 250f, 4.5f, 0f);

            // 나무 밑에 떨어진 꽃잎 (Anime Tokyo의 분홍 잎 더미)
            string[] petals = { PetalsPath1, PetalsPath2, PetalsPath3 };
            Vector3[] petalSpots =
            {
                new Vector3(7f, 0f, 5.5f), new Vector3(9.5f, 0f, 0.5f), new Vector3(7.5f, 0f, -4.5f),
                new Vector3(6.2f, 0f, 3f), new Vector3(8.3f, 0f, -1.8f), new Vector3(8.6f, 0f, 3.4f),
            };
            for (int i = 0; i < petalSpots.Length; i++)
            {
                // 바닥보다 1.5cm 위 (바닥과 겹쳐 깜빡이지 않게)
                Vector3 pos = petalSpots[i] + Vector3.up * 0.015f;
                Place(petals[i % petals.Length], group, pos, Random.Range(0f, 360f), 0f, Random.Range(1.8f, 2.8f));
            }

            // 벤치: 벚나무 사이에서 플레이어(원점) 쪽을 바라봄
            Vector3 benchPos = new Vector3(7.8f, 0f, 2f);
            Place(BenchPath, group, benchPos, LookYaw(benchPos), 0f, 1.6f);

            // 석등 (정면 석등과 같은 모델)
            Place(StoneLanternPath, group, new Vector3(6f, 0f, 7.5f), 0f, 1.6f, 0f);
            Place(StoneLanternPath, group, new Vector3(6.2f, 0f, -2.5f), 0f, 1.4f, 0f);

            // 꽃밭 (돌담 앞)
            Place(FlowerBedPath, group, new Vector3(11f, 0f, 4f), 90f, 0f, 2.2f);
            Place(FlowerBedPath, group, new Vector3(10.5f, 0f, -3.5f), 70f, 0f, 2f);

            // 정원석
            Place(Stone2Path, group, new Vector3(5.8f, 0f, 1f), 40f, 0f, 0.9f);
            Place(Stone1Path, group, new Vector3(6.4f, 0f, 0.4f), 200f, 0f, 0.5f);
            Place(Stone1Path, group, new Vector3(9f, 0f, 6.8f), 120f, 0f, 0.7f);

            // 덤불·풀
            Place(BushPath, group, new Vector3(5.8f, 0f, 4.2f), 0f, 0.9f, 0f);
            Place(BushPath, group, new Vector3(10f, 0f, 7.5f), 90f, 1.2f, 0f);
            Place(BushPath, group, new Vector3(10.5f, 0f, -1f), 200f, 1f, 0f);
            for (int i = 0; i < 8; i++)
            {
                Vector3 pos = new Vector3(Random.Range(5.8f, 10.5f), 0f, Random.Range(-5f, 7.5f));
                Place(GrassPath, group, pos, Random.Range(0f, 360f), Random.Range(0.35f, 0.6f), 0f);
            }
        }

        // 등 뒤: 붉은 토리이 = 마당 입구 (정면 과일 배경과 겹치지 않게 뒤에 둠)
        private void BuildBack(Transform group)
        {
            Place(RedToriiPath, group, new Vector3(0f, 0f, -6.5f), 0f, 4.5f, 0f);
        }

        // 둘레: 돌담을 원으로 이어 붙이고, 안쪽에 큰 나무 → 바깥은 안 보임
        private void BuildRing(Transform group)
        {
            GameObject wallPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPath);
            if (wallPrefab == null)
            {
                Debug.LogWarning($"[ShrineYardBuilder] 프리팹이 없습니다: {WallPath}");
            }
            else
            {
                // 돌담 한 조각의 길이를 재서 원 둘레를 채울 개수를 정함
                float length = MeasureWallLength(wallPrefab, wallHeight, out float yawFix);
                int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * ringRadius / length));
                for (int i = 0; i < count; i++)
                {
                    float angle = 360f * i / count;
                    Vector3 pos = PointOnCircle(angle, ringRadius);
                    // 안쪽(원점)을 바라보게 + 돌담 길이 방향을 원의 접선에 맞춤
                    Place(WallPath, group, pos, angle + 180f + yawFix, wallHeight, 0f);
                }
            }

            // 큰 나무: 정면(과일이 날아오는 쪽 ±25도)은 본전이 가리므로 비워 둠
            for (int i = 0; i < ringTreeCount; i++)
            {
                float angle = 25f + (360f - 50f) * i / Mathf.Max(1, ringTreeCount - 1);
                Vector3 pos = PointOnCircle(angle, ringRadius - 1.8f);
                Place(BigTreePath, group, pos, Random.Range(0f, 360f), Random.Range(7f, 9f), 0f);
            }
        }

        #endregion

        #region 공통 함수

        private Renderer GetFloorRenderer()
        {
            if (floorRenderer != null) return floorRenderer;
            Transform stage = GetStageRoot();
            Transform floor = stage != null ? stage.Find("Floor") : null;
            return floor != null ? floor.GetComponent<Renderer>() : null;
        }

        // 흙 바닥 URP Lit 머티리얼 (없으면 만들어 저장). Floor 크기에 맞춰 텍스처를 반복
        private Material GetGroundMaterial(Renderer floor)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(GroundMatPath);
            if (mat == null)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexPath);
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (tex == null || lit == null) return null;

                mat = new Material(lit);
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", GroundTint);
                mat.SetFloat("_Smoothness", 0.05f);
                AssetDatabase.CreateAsset(mat, GroundMatPath);
            }

            // 반복 횟수 = Floor 가로 길이 / 텍스처 한 장 크기
            if (floor != null)
            {
                float floorSize = floor.bounds.size.x;
                float repeat = Mathf.Max(1f, floorSize / groundTileMeters);
                mat.SetTextureScale("_BaseMap", new Vector2(repeat, repeat));
                EditorUtility.SetDirty(mat);
            }
            AssetDatabase.SaveAssets();
            return mat;
        }

        private Transform GetStageRoot()
        {
            if (stageRoot != null) return stageRoot;
            GameObject found = GameObject.Find("Stages");
            return found != null ? found.transform : null;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Build Shrine Yard");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // 프리팹을 놓고, 높이나 폭을 원하는 크기로 맞춘 뒤 바닥(y = 0)에 붙임
        // height, width 중 0이 아닌 쪽으로 크기를 맞춤 (둘 다 0이면 원래 크기)
        // snapToGround = false면 피벗을 position 높이에 그대로 둠 (다리처럼 피벗이 발판 높이인 모델)
        private static GameObject Place(string path, Transform parent, Vector3 position, float yaw, float height, float width, bool snapToGround = true)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[ShrineYardBuilder] 프리팹이 없습니다: {path}");
                return null;
            }

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            Undo.RegisterCreatedObjectUndo(go, "Build Shrine Yard");
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);

            FitSize(go, height, width);
            if (snapToGround) PutOnGround(go, position.y);
            return go;
        }

        // 장식에 붙은 콜라이더를 전부 지움 (프리팹 원본은 그대로)
        private static void RemoveColliders(Transform root)
        {
            foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            {
                Undo.DestroyObjectImmediate(col);
            }
        }

        // 연못용 URP Lit 머티리얼: 파란 청록색, 매끈하게 (반사 느낌)
        private static Material GetPondMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(PondMatPath);
            if (mat != null) return mat;

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return null;

            mat = new Material(lit);
            mat.SetColor("_BaseColor", PondColor);
            mat.SetFloat("_Smoothness", 0.92f);
            mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, PondMatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }

        #endregion

        #region 계산 함수

        // [수학] AI 작성
        // 오브젝트 전체(자식 Renderer 포함)를 감싸는 월드 기준 상자
        private static Bounds GetBounds(GameObject go)
        {
            Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        // [수학] AI 작성
        // 높이(또는 가로·세로 중 긴 쪽 폭)가 원하는 값이 되도록 균일하게 크기 조절
        private static void FitSize(GameObject go, float height, float width)
        {
            Bounds b = GetBounds(go);
            float scale = 1f;
            if (height > 0f && b.size.y > 0.01f) scale = height / b.size.y;
            else if (width > 0f && Mathf.Max(b.size.x, b.size.z) > 0.01f) scale = width / Mathf.Max(b.size.x, b.size.z);
            go.transform.localScale *= scale;
        }

        // [수학] AI 작성
        // 오브젝트 맨 아래가 groundY(바닥 위 높이)에 오도록 위아래로 옮김
        private static void PutOnGround(GameObject go, float groundY)
        {
            Bounds b = GetBounds(go);
            go.transform.position += Vector3.up * (groundY - b.min.y);
        }

        // [수학] AI 작성
        // 원점 중심, 정면(+Z)에서 시계 방향 angle도 위치의 점
        private static Vector3 PointOnCircle(float angle, float radius)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * radius;
        }

        // [수학] AI 작성
        // 원점 중심 타원 위의 점 (angle 0도 = +Z, 시계 방향). halfX·halfZ = 가로·세로 반지름
        private static Vector3 PointOnEllipse(float angle, float halfX, float halfZ)
        {
            float rad = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad) * halfX, 0f, Mathf.Cos(rad) * halfZ);
        }

        // [수학] AI 작성
        // position에서 원점(플레이어)을 바라보는 Y 회전값
        private static float LookYaw(Vector3 position)
        {
            return Mathf.Atan2(-position.x, -position.z) * Mathf.Rad2Deg;
        }

        // [수학] AI 작성
        // 타원 둘레 길이 (라마누잔 근사식)
        private static float EllipsePerimeter(float a, float b)
        {
            return Mathf.PI * (3f * (a + b) - Mathf.Sqrt((3f * a + b) * (a + 3f * b)));
        }

        // [수학] AI 작성
        // 프리팹을 원래 크기·회전 0으로 잠깐 놓아서 상자 크기를 잼
        private static Vector3 MeasureSize(GameObject prefab)
        {
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Vector3 size = GetBounds(temp).size;
            DestroyImmediate(temp);
            return size;
        }

        // [수학] AI 작성
        // 바닥 타일 한 장의 크기 (가로·세로 중 큰 쪽). 너무 작게 재지면 3m로
        private static float MeasureTileSize(GameObject prefab)
        {
            Vector3 size = MeasureSize(prefab);
            float tile = Mathf.Max(size.x, size.z);
            return tile > 0.5f ? tile : 3f;
        }

        // [수학] AI 작성
        // 상자 가운데(XZ)가 target에 오도록 옆으로만 옮김 (피벗이 가운데가 아닌 모델용)
        private static void CenterOn(GameObject go, Vector3 target)
        {
            Bounds b = GetBounds(go);
            go.transform.position += new Vector3(target.x - b.center.x, 0f, target.z - b.center.z);
        }

        // [수학] AI 작성
        // 돌담 한 조각을 원하는 높이로 맞췄을 때의 길이를 잼
        // 길이 방향이 Z축이면 yawFix = 90 (접선 방향으로 눕히기 위해)
        private static float MeasureWallLength(GameObject prefab, float height, out float yawFix)
        {
            GameObject temp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            FitSize(temp, height, 0f);
            Bounds b = GetBounds(temp);
            DestroyImmediate(temp);

            // 원점(=앞)을 바라보게 돌리면 로컬 X축이 접선 방향. 길이가 Z축이면 90도 더 돌림
            yawFix = b.size.z > b.size.x ? 90f : 0f;
            return Mathf.Max(0.5f, Mathf.Max(b.size.x, b.size.z) * 0.95f);   // 살짝 겹치게 해서 틈이 안 보이게
        }

        #endregion
#endif
    }
}
