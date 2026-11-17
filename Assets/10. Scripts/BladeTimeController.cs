using System.Collections.Generic;
using StudyXR.Common;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TimeSlasher
{
    // 블레이드 타임(타임 슬로우)
    public class BladeTimeController : MonoBehaviour
    {
        public static BladeTimeController Instance;

        [Header("감속 설정")]
        public float slowScale = 0.25f;        // 슬로우 중 과일 속도 배율
        public float maxSlowSeconds = 3f;      // 가득 찬 게이지로 버티는 시간 (실제 시간)
        public float minGaugeToStart = 0.25f;  // 이 이상이어야 발동
        public float gaugePerFruit = 0.1f;     // 과일 첫 절단마다 게이지 충전량

        [Header("연출 설정")]
        public AudioSource bgm;                // 배경음 (슬로우 중 낮게 끌어내림)
        public float slowBgmPitch = 0.6f;

        [Header("배경 어둡게 (후처리 없이 머티리얼 색만 낮춤)")]
        public Transform[] dimRoots;           // 이 오브젝트들(Stage, Props)의 자식 Renderer는 전부 어두워짐. 하늘도 같이 어두워짐
        public float dimBrightness = 0.2f;    // 블레이드 타임 중 밝기 (1 = 그대로, 0 = 새까맣게)
        public float dimSkyExposure = 0.2f;    // 블레이드 타임 중 하늘 밝기 (Skybox의 Exposure)
        public float dimFadeSpeed = 6f;        // 어두워지고 밝아지는 속도 (1초에 바뀌는 양)

        public float Scale { get; private set; } = 1f;   // 과일 세계가 흐르는 속도 (FruitWorld와 플레이어 World 분리)
        public bool IsSlow { get; private set; } = false;
        public float Gauge { get; private set; } = 0f;   // 0 ~ 1

        // 게이지가 발동 가능한 만큼 찼는지 (GameUI용)
        public bool CanStart => Gauge >= minGaugeToStart;

        private InputAction slowAction;

        private List<Material> dimMaterials = new List<Material>();   // 어둡게 할 머티리얼 (복사본)
        private List<Color> dimColors = new List<Color>();            // 원래 색
        private Material skyMaterial;     // 하늘 머티리얼 (복사본)
        private float skyExposure;        // 원래 하늘 밝기
        private float dim;                // 지금 어두운 정도 (0 = 평소, 1 = 블레이드 타임)

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        
        private void OnEnable() //테스트 용
        {
            // A 버튼으로 액션을 만들고, X 버튼을 추가로 묶어줍니다 (키보드 테스트는 F4)
            slowAction = QuestButtons.Create(QuestButton.A, "BladeTime", "<Keyboard>/f4");
            slowAction.AddBinding(QuestButtons.Path(QuestButton.X));
            slowAction.Enable();
        }

        private void OnDisable()
        {
            slowAction.Disable();
            slowAction.Dispose();
        }

        // 게이지 충전은 베기 이벤트를 직접 받아서 함
        // 구독은 Start에서, 해제는 OnDestroy에서 (FPS HitEffectController와 같은 방식)
        private void Start()
        {
            SliceSystem.Instance.OnCut += HandleCut;
            SetupDim();
        }

        private void OnDestroy()
        {
            if (SliceSystem.Instance != null) SliceSystem.Instance.OnCut -= HandleCut;
        }

        // 평상시 과일 첫 절단만 충전 (조각 베기, 블레이드 타임 중 베기는 충전 안 함)
        private void HandleCut(CutInfo info)
        {
            if (info.IsFirstCut == false || IsSlow) return;
            Gauge = Mathf.Min(1f, Gauge + gaugePerFruit);
        }

        private void Update()
        {
            UpdateDim();   // 게임 중이 아니어도 원래 밝기로 돌아가야 하므로 맨 위에서

            if (GameManager.Instance.IsPlaying == false) return;

            bool pressed = slowAction.IsPressed();

            // 슬로우가 아니고, 키를 눌렀고, 게이지가 충분하면 -> 슬로우 시작
            if (IsSlow == false && pressed && CanStart) StartSlow();
            
            if (IsSlow)
            {
                Gauge -= Time.deltaTime / maxSlowSeconds;
                if (pressed == false || Gauge <= 0f) EndSlow();
            }
        }

        private void StartSlow()
        {
            IsSlow = true;
            Scale = slowScale;
            if (bgm != null) bgm.pitch = slowBgmPitch;
        }

        private void EndSlow()
        {
            IsSlow = false;
            Scale = 1f;
            if (bgm != null) bgm.pitch = 1f;
            Gauge = Mathf.Max(0f, Gauge);

            // 슬로우 동안 벤 과일을 한 번에 갈라지게 함 → 자르는 건 SliceSystem 담당
            SliceSystem.Instance.ReleaseAll();
        }

        // 게이지 리셋
        public void ResetGauge()
        {
            Gauge = 0f;
        }

        // 게임 오버
        public void ForceEnd()
        {
            if (IsSlow) EndSlow();
        }

        #region 배경 어둡게

        // 어둡게 할 머티리얼과 원래 색을 기억해 둠
        private void SetupDim()
        {
            // Stage, Props 아래 자식 전부 (꺼져 있는 자식도 포함)
            foreach (Transform root in dimRoots)
            {
                if (root == null) continue;
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) AddDimRenderer(r);
            }

            // 하늘도 복사본으로 바꿔서 씀 (원본 Skybox 머티리얼은 안 바뀜)
            if (RenderSettings.skybox != null && RenderSettings.skybox.HasProperty("_Exposure"))
            {
                skyMaterial = new Material(RenderSettings.skybox);
                RenderSettings.skybox = skyMaterial;
                skyExposure = skyMaterial.GetFloat("_Exposure");
            }
        }

        private void AddDimRenderer(Renderer r)
        {
            if (r == null) return;
            // renderer.materials는 이 물체 전용 복사본을 만들어 줌 → 원본 머티리얼 파일은 안 바뀜
            foreach (Material m in r.materials)
            {
                dimMaterials.Add(m);
                dimColors.Add(m.color);   // URP Lit의 Base Color
            }
        }

        // 블레이드 타임이면 0 → 1, 아니면 1 → 0 으로 서서히 바꾸고 색에 적용
        private void UpdateDim()
        {
            float target = IsSlow ? 1f : 0f;
            if (dim == target) return;   // 다 바뀌었으면 매 프레임 색을 다시 넣지 않음

            dim = Mathf.MoveTowards(dim, target, dimFadeSpeed * Time.deltaTime);

            float brightness = Mathf.Lerp(1f, dimBrightness, dim);   // 1 → dimBrightness
            for (int i = 0; i < dimMaterials.Count; i++)
            {
                Color c = dimColors[i] * brightness;
                c.a = dimColors[i].a;   // 투명도는 그대로
                dimMaterials[i].color = c;
            }

            if (skyMaterial != null)
                skyMaterial.SetFloat("_Exposure", Mathf.Lerp(skyExposure, dimSkyExposure, dim));
        }

        #endregion
    }
}
