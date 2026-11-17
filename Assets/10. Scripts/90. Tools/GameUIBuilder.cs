using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
#endif

namespace TimeSlasher
{
    // 타임 슬래셔 화면을 코드만으로 조립하는 에디터 도구 (수업 UIBuilderPrompt 구조)
    // - 빈 GameObject에 붙이고 컴포넌트 우클릭 → "1. Build UI" / "2. Clear UI"
    // - 화면만 만듭니다. GameUI 필드 연결과 버튼 OnClick은 인스펙터에서 합니다
    //
    // VR이라 수업 프롬프트(Screen Space Overlay)와 다른 점
    // 1) 시작·게임 오버 화면 = World Space 캔버스 + TrackedDeviceGraphicRaycaster (레이 + 트리거로 버튼 클릭)
    // 2) HUD = Screen Space Overlay는 헤드셋에 안 보이므로, 카메라 자식으로 붙인 World Space 판으로 만듦
    //    Raycaster를 안 붙여서 플레이어가 누를 수 없고, 고개를 돌려도 항상 시야에 보임
    //
    // 만드는 구조
    // World UI (World Space, 플레이어 정면)
    //  ├ LobbyPanel     TitleText, SubtitleText, WeaponText, GuideText, StartButton
    //  └ GameOverPanel  GameOverTitleText, ResultText, HintText, RestartButton, QuitButton   (처음엔 꺼짐)
    // HUD UI (World Space, Main Camera 자식, 1m 앞)
    //  ├ HudPanel       (처음엔 꺼짐)
    //  │  ├ LifeIcons   LifeIcon_1 ~ 3 (아래 가운데, 큰 빨간 하트)
    //  │  ├ ScoreText, ComboText (오른쪽 위)
    //  │  └ Gauge       GaugeLabelText, GaugeBack, GaugeFill, GaugeHintText (오른쪽 가운데, 세로 막대)
    //  ├ FlashImage     (흰색, 투명으로 시작)
    //  └ SlashText
    // HUD는 항상 맨 앞에 그림 (칼이 1m보다 가까이 와도 안 가려짐)
    //  - 이미지: M_HudImage (TimeSlasher/UIOverlay), 글자: M_HudText (TextMeshPro/Mobile/Distance Field Overlay)
    //  - 이미 만든 HUD에는 "4. Apply HUD Overlay"로 적용 (GameUI 연결은 그대로 유지됨)
    public class GameUIBuilder : MonoBehaviour
    {
        public const string WorldRootName = "World UI";
        public const string HudRootName = "HUD UI";

        // 빌드에도 같은 필드가 있어야 직렬화가 어긋나지 않으므로 필드는 #if 밖에 둡니다
        [SerializeField] private TMP_FontAsset font;          // 한글 폰트 (NotoSansKR-Regular SDF)
        [SerializeField] private Transform headCamera;        // HUD를 붙일 카메라 (비우면 Camera.main)
        [SerializeField] private Sprite heartSprite;          // 목숨 하트 (T_Heart). 비우면 네모로 나옴

        [Header("배치")]
        [SerializeField] private Vector3 worldUIPosition = new Vector3(0f, 1.5f, 2f);   // 플레이어 정면 2m
        [SerializeField] private float hudDistance = 1f;                                  // 눈 앞 1m

#if UNITY_EDITOR
        // 월드 UI: 1000 x 700 px, 1px = 1mm → 1m x 0.7m 판
        private static readonly Vector2 WorldSize = new Vector2(1000f, 700f);
        private const float WorldScale = 0.001f;

        // HUD: 1200 x 700 px, 1px = 0.6mm → 1m 앞 0.72m x 0.42m 판
        private static readonly Vector2 HudSize = new Vector2(1200f, 700f);
        private const float HudScale = 0.0006f;

        // HUD를 맨 앞에 그리는 머티리얼 (없으면 처음 한 번 만들어 저장)
        private const string HudImageMatPath = "Assets/00. SlashTime/Material/M_HudImage.mat";
        private const string HudTextMatPath = "Assets/00. SlashTime/Material/M_HudText.mat";
        private const int HudSortingOrder = 10;   // 칼 잔상·불꽃 같은 투명 효과보다 나중에 그리게

        private static readonly Color PanelColor = new Color(0.05f, 0.05f, 0.1f, 0.85f);
        private static readonly Color GameOverColor = new Color(0.25f, 0.03f, 0.05f, 0.85f);
        private static readonly Color ButtonColor = new Color(0.15f, 0.45f, 0.9f);
        private static readonly Color QuitButtonColor = new Color(0.35f, 0.35f, 0.4f);
        private static readonly Color LifeColor = new Color(0.9f, 0.15f, 0.2f);
        private static readonly Color GaugeColor = new Color(0.2f, 0.4f, 1f);

        [ContextMenu("1. Build UI")]
        public void BuildUI()
        {
            // 이미 만든 화면이 있으면 겹쳐 만들지 않음
            if (GameObject.Find(WorldRootName) != null || GameObject.Find(HudRootName) != null)
            {
                Debug.LogWarning($"[GameUIBuilder] '{WorldRootName}' 또는 '{HudRootName}'이 이미 있습니다. 먼저 2. Clear UI를 하세요.");
                return;
            }

            if (FindAnyObjectByType<EventSystem>() == null)
                Debug.LogWarning("[GameUIBuilder] 씬에 EventSystem이 없습니다. 레이로 누르려면 EventSystem + XRUIInputModule이 필요합니다.");

            Transform world = CreateWorldCanvas();
            CreateLobbyPanel(world);
            CreateGameOverPanel(world);

            Transform hud = CreateHudCanvas();
            Transform hudPanel = CreateHudPanel(hud);
            CreateLifeIcons(hudPanel);
            CreateScore(hudPanel);
            CreateGauge(hudPanel);
            CreateSlashEffect(hud);
            ApplyHudOverlay(hud);

            // 처음에 숨겨 둘 패널
            world.Find("GameOverPanel").gameObject.SetActive(false);
            hudPanel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] UI를 만들었습니다.");
        }

        // HUD만 다시 만들기 (월드 UI의 버튼 OnClick 연결을 유지하고 싶을 때)
        // 다시 만든 뒤 GameUI의 HUD 칸들은 새로 연결해야 함
        [ContextMenu("3. Rebuild HUD Only")]
        public void RebuildHud()
        {
            GameObject old = GameObject.Find(HudRootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            Transform hud = CreateHudCanvas();
            Transform hudPanel = CreateHudPanel(hud);
            CreateLifeIcons(hudPanel);
            CreateScore(hudPanel);
            CreateGauge(hudPanel);
            CreateSlashEffect(hud);
            ApplyHudOverlay(hud);
            hudPanel.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] HUD를 다시 만들었습니다. GameUI의 HUD 칸들을 다시 연결하세요.");
        }

        [ContextMenu("2. Clear UI")]
        public void ClearUI()
        {
            foreach (string rootName in new[] { WorldRootName, HudRootName })
            {
                GameObject root = GameObject.Find(rootName);
                if (root == null) continue;
                Undo.DestroyObjectImmediate(root);
                Debug.Log($"[GameUIBuilder] '{rootName}'을 지웠습니다.");
            }
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        // 이미 만든 HUD를 항상 맨 앞에 그리게 바꿈 (다시 만들지 않으므로 GameUI 연결은 그대로)
        [ContextMenu("4. Apply HUD Overlay")]
        public void ApplyHudOverlayMenu()
        {
            GameObject hud = GameObject.Find(HudRootName);
            if (hud == null)
            {
                Debug.LogWarning($"[GameUIBuilder] '{HudRootName}'이 없습니다. 먼저 1. Build UI를 하세요.");
                return;
            }

            ApplyHudOverlay(hud.transform);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] HUD를 항상 맨 앞에 그리게 바꿨습니다.");
        }

        #region 월드 UI (시작, 게임 오버)

        private Transform CreateWorldCanvas()
        {
            GameObject root = NewUI(WorldRootName, null);
            Undo.RegisterCreatedObjectUndo(root, "Build UI");
            root.transform.position = worldUIPosition;
            root.transform.rotation = Quaternion.identity;   // 앞면이 플레이어(원점) 쪽
            root.transform.localScale = Vector3.one * WorldScale;
            ((RectTransform)root.transform).sizeDelta = WorldSize;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            root.AddComponent<TrackedDeviceGraphicRaycaster>();   // 레이로 누를 수 있게
            return root.transform;
        }

        private void CreateLobbyPanel(Transform canvas)
        {
            GameObject panel = NewPanel("LobbyPanel", canvas, Vector2.zero);
            panel.GetComponent<Image>().color = PanelColor;
            Stretch((RectTransform)panel.transform, 0f);

            TMP_Text title = NewText("TitleText", panel.transform, "타임 슬래셔", 96, TextAlignmentOptions.Center);
            Place(title.rectTransform, Top, new Vector2(0f, -50f), new Vector2(900f, 120f));

            TMP_Text subtitle = NewText("SubtitleText", panel.transform, "Time Slasher  ·  제작 NaJungWon", 34,
                TextAlignmentOptions.Center);
            Place(subtitle.rectTransform, Top, new Vector2(0f, -170f), new Vector2(900f, 50f));

            TMP_Text weapon = NewText("WeaponText", panel.transform, "선택한 무기: 긴 한손검", 40, TextAlignmentOptions.Center);
            weapon.color = new Color(1f, 0.9f, 0.4f);
            Place(weapon.rectTransform, Top, new Vector2(0f, -245f), new Vector2(900f, 60f));

            TMP_Text guide = NewText("GuideText", panel.transform,
                "오른쪽 전시대의 칼을 레이 + 트리거로 골라 무기를 바꾸세요\n앞쪽 긴 칼 = 한손검 · 뒤쪽 짧은 칼 2자루 = 쌍검\n주변에 부딪힐 물건이 없는지 확인하세요",
                28, TextAlignmentOptions.Center);
            Place(guide.rectTransform, Top, new Vector2(0f, -320f), new Vector2(900f, 140f));

            Button start = NewButton("StartButton", panel.transform, "시작", out _);
            Place((RectTransform)start.transform, Bottom, new Vector2(0f, 50f), new Vector2(360f, 100f));
        }

        private void CreateGameOverPanel(Transform canvas)
        {
            GameObject panel = NewPanel("GameOverPanel", canvas, Vector2.zero);
            panel.GetComponent<Image>().color = GameOverColor;
            Stretch((RectTransform)panel.transform, 0f);

            TMP_Text title = NewText("GameOverTitleText", panel.transform, "게임 오버", 90, TextAlignmentOptions.Center);
            Place(title.rectTransform, Top, new Vector2(0f, -60f), new Vector2(900f, 120f));

            TMP_Text result = NewText("ResultText", panel.transform, "점수 0\n최고 점수 0", 50, TextAlignmentOptions.Center);
            Place(result.rectTransform, Center, new Vector2(0f, 50f), new Vector2(900f, 160f));

            TMP_Text hint = NewText("HintText", panel.transform, "전시대에서 무기를 바꾼 뒤 재시작할 수 있어요", 28,
                TextAlignmentOptions.Center);
            Place(hint.rectTransform, Center, new Vector2(0f, -70f), new Vector2(900f, 50f));

            Button restart = NewButton("RestartButton", panel.transform, "재시작", out _);
            Place((RectTransform)restart.transform, Bottom, new Vector2(-200f, 50f), new Vector2(340f, 100f));

            Button quit = NewButton("QuitButton", panel.transform, "종료", out _);
            quit.GetComponent<Image>().color = QuitButtonColor;
            Place((RectTransform)quit.transform, Bottom, new Vector2(200f, 50f), new Vector2(340f, 100f));
        }

        #endregion

        #region HUD (머리에 붙음, 누를 수 없음)

        private Transform CreateHudCanvas()
        {
            Transform cam = headCamera != null ? headCamera : (Camera.main != null ? Camera.main.transform : null);
            if (cam == null) Debug.LogWarning("[GameUIBuilder] 카메라를 못 찾아 HUD를 씬 최상위에 만듭니다. Main Camera 자식으로 옮겨주세요.");

            GameObject root = NewUI(HudRootName, cam);
            Undo.RegisterCreatedObjectUndo(root, "Build UI");
            root.transform.localPosition = new Vector3(0f, 0f, hudDistance);
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * HudScale;
            ((RectTransform)root.transform).sizeDelta = HudSize;

            // Raycaster를 붙이지 않음 → 레이가 HUD를 누를 수 없음
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            return root.transform;
        }

        private Transform CreateHudPanel(Transform canvas)
        {
            GameObject panel = NewUI("HudPanel", canvas);
            Stretch((RectTransform)panel.transform, 0f);
            return panel.transform;
        }

        // 아래 가운데: 큰 하트 3개 (가로로 나란히)
        private void CreateLifeIcons(Transform hud)
        {
            GameObject icons = NewUI("LifeIcons", hud);
            Place((RectTransform)icons.transform, Bottom, new Vector2(0f, 30f), new Vector2(420f, 120f));

            HorizontalLayoutGroup layout = icons.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childAlignment = TextAnchor.MiddleCenter;   // 하트가 꺼져도 남은 하트는 가운데로
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 1; i <= 3; i++)
            {
                GameObject icon = NewPanel($"LifeIcon_{i}", icons.transform, new Vector2(120f, 120f));
                Image image = icon.GetComponent<Image>();
                image.sprite = heartSprite;      // 흰 하트 그림에
                image.color = LifeColor;         // 빨간색을 곱함
                image.preserveAspect = true;     // 하트 비율 유지
                image.raycastTarget = false;
            }
        }

        // 오른쪽 위: 점수, 그 아래 콤보
        private void CreateScore(Transform hud)
        {
            TMP_Text score = NewText("ScoreText", hud, "0", 64, TextAlignmentOptions.Right);
            Place(score.rectTransform, TopRight, new Vector2(-40f, -30f), new Vector2(420f, 80f));

            TMP_Text combo = NewText("ComboText", hud, "", 42, TextAlignmentOptions.Right);
            combo.color = new Color(1f, 0.85f, 0.3f);
            Place(combo.rectTransform, TopRight, new Vector2(-40f, -115f), new Vector2(420f, 60f));
        }

        // 오른쪽 가운데: 블레이드 타임 게이지 (세로 막대, 아래에서 위로 참)
        // 위에서부터: 이름 → 막대 → 안내 문구
        private void CreateGauge(Transform hud)
        {
            GameObject gauge = NewUI("Gauge", hud);
            Place((RectTransform)gauge.transform, Right, new Vector2(-40f, -40f), new Vector2(180f, 420f));

            TMP_Text label = NewText("GaugeLabelText", gauge.transform, "블레이드\n타임", 24, TextAlignmentOptions.Center);
            Place(label.rectTransform, Top, Vector2.zero, new Vector2(180f, 64f));

            GameObject back = NewPanel("GaugeBack", gauge.transform, Vector2.zero);
            back.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);
            Place((RectTransform)back.transform, Top, new Vector2(0f, -70f), new Vector2(50f, 290f));

            // 아래 끝을 기준(pivot)으로 두어, 세로 크기를 줄이면 위에서부터 줄어듦
            GameObject fill = NewPanel("GaugeFill", back.transform, Vector2.zero);
            fill.GetComponent<Image>().color = GaugeColor;
            RectTransform fillRect = (RectTransform)fill.transform;
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0.5f, 0f);
            fillRect.offsetMin = new Vector2(4f, 4f);     // 테두리 4px 안쪽으로
            fillRect.offsetMax = new Vector2(-4f, -4f);
            fillRect.localScale = new Vector3(1f, 0f, 1f);

            TMP_Text hint = NewText("GaugeHintText", gauge.transform, "", 22, TextAlignmentOptions.Center);
            hint.color = new Color(0.6f, 1f, 1f);
            Place(hint.rectTransform, Bottom, Vector2.zero, new Vector2(180f, 60f));
        }

        // 일괄 절단 연출: 흰 플래시 + "x N SLASH"
        private void CreateSlashEffect(Transform hudCanvas)
        {
            GameObject flash = NewPanel("FlashImage", hudCanvas, Vector2.zero);
            Image flashImage = flash.GetComponent<Image>();
            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            Stretch((RectTransform)flash.transform, 0f);

            TMP_Text slash = NewText("SlashText", hudCanvas, "", 120, TextAlignmentOptions.Center);
            slash.color = new Color(0.4f, 1f, 1f);
            Place(slash.rectTransform, Center, Vector2.zero, new Vector2(1100f, 180f));
        }

        #endregion

        #region HUD 맨 앞에 그리기

        // HUD 안의 모든 이미지·글자에 깊이를 무시하는 머티리얼을 넣고, 캔버스 순서를 올림
        private void ApplyHudOverlay(Transform hud)
        {
            Material imageMat = GetHudImageMaterial();
            Material textMat = GetHudTextMaterial();

            Canvas canvas = hud.GetComponent<Canvas>();
            Undo.RecordObject(canvas, "HUD Overlay");
            canvas.sortingOrder = HudSortingOrder;

            // true: 꺼져 있는 HudPanel 안쪽까지 전부
            foreach (Image image in hud.GetComponentsInChildren<Image>(true))
            {
                Undo.RecordObject(image, "HUD Overlay");
                image.material = imageMat;
            }

            if (textMat == null) return;
            foreach (TMP_Text text in hud.GetComponentsInChildren<TMP_Text>(true))
            {
                Undo.RecordObject(text, "HUD Overlay");
                text.fontSharedMaterial = textMat;
            }
        }

        // 이미지용: TimeSlasher/UIOverlay 셰이더 머티리얼
        private Material GetHudImageMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(HudImageMatPath);
            if (mat != null) return mat;

            mat = new Material(Shader.Find("TimeSlasher/UIOverlay"));
            AssetDatabase.CreateAsset(mat, HudImageMatPath);
            return mat;
        }

        // 글자용: 폰트 머티리얼을 복사해서 셰이더만 Overlay(깊이 무시)로 바꿈 (폰트 아틀라스는 그대로)
        private Material GetHudTextMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(HudTextMatPath);
            if (mat != null) return mat;

            if (font == null)
            {
                Debug.LogWarning("[GameUIBuilder] font가 비어 있어 HUD 글자는 바꾸지 못했습니다. font를 넣고 다시 하세요.");
                return null;
            }

            mat = new Material(font.material);
            mat.shader = Shader.Find("TextMeshPro/Mobile/Distance Field Overlay");
            AssetDatabase.CreateAsset(mat, HudTextMatPath);
            return mat;
        }

        #endregion

        #region 도우미

        // 앵커 프리셋: (anchorMin = anchorMax, pivot)
        private static readonly Vector2[] Top = { new Vector2(0.5f, 1f), new Vector2(0.5f, 1f) };
        private static readonly Vector2[] Bottom = { new Vector2(0.5f, 0f), new Vector2(0.5f, 0f) };
        private static readonly Vector2[] Center = { new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) };
        private static readonly Vector2[] TopLeft = { new Vector2(0f, 1f), new Vector2(0f, 1f) };
        private static readonly Vector2[] TopRight = { new Vector2(1f, 1f), new Vector2(1f, 1f) };
        private static readonly Vector2[] Right = { new Vector2(1f, 0.5f), new Vector2(1f, 0.5f) };

        // 앵커·피벗·위치·크기를 한 번에 명시적으로 설정
        private static void Place(RectTransform rect, Vector2[] anchorAndPivot, Vector2 position, Vector2 size)
        {
            rect.anchorMin = anchorAndPivot[0];
            rect.anchorMax = anchorAndPivot[0];
            rect.pivot = anchorAndPivot[1];
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private GameObject NewUI(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        private GameObject NewPanel(string name, Transform parent, Vector2 size)
        {
            GameObject go = NewUI(name, parent);
            RectTransform rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            go.AddComponent<Image>();
            return go;
        }

        private TMP_Text NewText(string name, Transform parent, string text, float size, TextAlignmentOptions align)
        {
            GameObject go = NewUI(name, parent);
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.alignment = align;
            tmp.color = Color.white;
            tmp.raycastTarget = false;   // 글자가 버튼 클릭을 막지 않게
            return tmp;
        }

        private Button NewButton(string name, Transform parent, string label, out TMP_Text labelText)
        {
            GameObject go = NewPanel(name, parent, new Vector2(300f, 100f));
            go.GetComponent<Image>().color = ButtonColor;
            Button button = go.AddComponent<Button>();

            labelText = NewText("LabelText", go.transform, label, 52, TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform, 0f);
            return button;
        }

        private void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        #endregion
#endif
    }
}
