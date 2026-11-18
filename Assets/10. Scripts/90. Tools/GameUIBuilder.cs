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
    // SlashTime 화면을 코드만으로 조립하는 에디터 도구 (수업 UIBuilderPrompt 구조)
    // - 빈 GameObject에 붙이고 컴포넌트 우클릭 → "1. Build UI" / "2. Clear UI"
    // - 만든 뒤 씬의 GameUI 칸들을 자동으로 연결합니다 (버튼이 할 일은 GameUI가 코드로 연결)
    // - 이미 만든 UI를 다시 연결만 하려면 "5. Connect GameUI"
    // - 설정창만 새로 만들려면 "6. Build Settings" (로비에 "설정" 버튼이 없으면 같이 추가)
    //
    // VR이라 수업 프롬프트(Screen Space Overlay)와 다른 점
    // 1) 시작·게임 오버 화면 = World Space 캔버스 + TrackedDeviceGraphicRaycaster (레이로 버튼 클릭)
    // 2) HUD = Screen Space Overlay는 헤드셋에 안 보이므로, 카메라 자식으로 붙인 World Space 판으로 만듦
    //    Raycaster를 안 붙여서 플레이어가 누를 수 없고, 고개를 돌려도 항상 시야에 보임
    //
    // 디자인: 낮의 신사 마당에 맞춘 동양풍 (한지 족자, 붓 터치 버튼, 벚꽃 목숨, 카타나 게이지, 먹물)
    // 이미지는 Assets/00. SlashTime/Texture/UI/ 의 UI_*.png 를 이름으로 불러옴
    //
    // 만드는 구조
    // World UI (World Space, 플레이어 정면)
    //  ├ LobbyPanel     (족자) TitleText, TitleSeal, TitleLine, SubtitleText, WeaponText, GuideText, StartButton
    //  └ GameOverPanel  (족자) GameOverTitleText, TitleSeal, TitleLine, ResultText, HintText, RestartButton, QuitButton (처음엔 꺼짐)
    //    (LobbyPanel에는 SettingsButton도 있음)
    // Settings UI (World Space, 열 때마다 SettingsUI가 머리 앞으로 옮김, 처음엔 꺼짐)
    //  └ SettingsPanel  (족자) SettingsTitleText, TitleSeal, TitleLine, BgmRow, SfxRow, Buttons(CloseButton, LobbyButton)
    //     └ BgmRow / SfxRow = Label, ValueText(%), Slider(Background 먹선, Fill 주홍 먹선, Handle 音 도장)
    // HUD UI (World Space, Main Camera 자식, 1m 앞)
    //  ├ HudPanel       (처음엔 꺼짐)
    //  │  ├ LifeIcons   LifeIcon_1 ~ 3 (아래 가운데, 벚꽃)
    //  │  ├ ScoreTag    한지 이름표 안에 ScoreText, ComboText (오른쪽 위)
    //  │  └ Gauge       GaugeLabel(이름표), GaugeBack(카타나), GaugeFill(칼날, Filled), GaugeHint(이름표) (오른쪽 가운데)
    //  ├ FlashImage     (흰색, 투명으로 시작)
    //  └ SlashEffect    InkSplash(먹물) + SlashText("N 斬") (처음엔 꺼짐)
    // HUD는 항상 맨 앞에 그림 (칼이 1m보다 가까이 와도 안 가려짐)
    //  - 이미지: M_HudImage (TimeSlasher/UIOverlay), 글자: M_HudText (TextMeshPro/Mobile/Distance Field Overlay)
    //  - 이미 만든 HUD에는 "4. Apply HUD Overlay"로 적용 (GameUI 연결은 그대로 유지됨)
    public class GameUIBuilder : MonoBehaviour
    {
        public const string WorldRootName = "World UI";
        public const string HudRootName = "HUD UI";
        public const string SettingsRootName = "Settings UI";

        // 빌드에도 같은 필드가 있어야 직렬화가 어긋나지 않으므로 필드는 #if 밖에 둡니다
        [SerializeField] private TMP_FontAsset font;          // 한글 폰트 (NotoSansKR-Regular SDF)
        [SerializeField] private Transform headCamera;        // HUD를 붙일 카메라 (비우면 Camera.main)

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

        // UI 이미지 폴더 (UI_*.png)
        private const string UiTexturePath = "Assets/00. SlashTime/Texture/UI/";

        // 색 (먹·한지·주홍·은청색)
        private static readonly Color Ink = new Color(0.12f, 0.12f, 0.13f);
        private static readonly Color InkSoft = new Color(0.32f, 0.3f, 0.28f);
        private static readonly Color Washi = new Color(0.96f, 0.93f, 0.87f);
        private static readonly Color Vermilion = new Color(0.78f, 0.06f, 0.18f);
        private static readonly Color ButtonHover = new Color(0.7f, 0.08f, 0.16f);
        private static readonly Color QuitInk = new Color(0.42f, 0.4f, 0.38f);
        private static readonly Color BladeBlue = new Color(0.55f, 0.68f, 0.82f);

        [ContextMenu("1. Build UI")]
        public void BuildUI()
        {
            // 이미 만든 화면이 있으면 겹쳐 만들지 않음
            if (GameObject.Find(WorldRootName) != null || GameObject.Find(HudRootName) != null || FindRoot(SettingsRootName) != null)
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

            Transform settings = CreateSettingsCanvas();

            // 처음에 숨겨 둘 패널
            world.Find("GameOverPanel").gameObject.SetActive(false);
            hudPanel.gameObject.SetActive(false);

            ConnectGameUI(world, hud);
            ConnectSettingsUI(world, settings);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] UI를 만들고 GameUI에 연결했습니다.");
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

            GameObject world = GameObject.Find(WorldRootName);
            ConnectGameUI(world != null ? world.transform : null, hud);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] HUD를 다시 만들고 GameUI에 연결했습니다.");
        }

        [ContextMenu("2. Clear UI")]
        public void ClearUI()
        {
            foreach (string rootName in new[] { WorldRootName, HudRootName, SettingsRootName })
            {
                GameObject root = FindRoot(rootName);   // 꺼져 있는 설정창도 찾아야 해서
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

        // 이미 만든 UI를 씬의 GameUI에 다시 연결만 함
        [ContextMenu("5. Connect GameUI")]
        public void ConnectGameUIMenu()
        {
            GameObject world = GameObject.Find(WorldRootName);
            GameObject hud = GameObject.Find(HudRootName);
            ConnectGameUI(world != null ? world.transform : null, hud != null ? hud.transform : null);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        // 설정창만 새로 만들고 연결 (로비에 "설정" 버튼이 없으면 같이 만듦)
        [ContextMenu("6. Build Settings")]
        public void BuildSettingsMenu()
        {
            GameObject world = GameObject.Find(WorldRootName);
            if (world == null)
            {
                Debug.LogWarning($"[GameUIBuilder] '{WorldRootName}'이 없습니다. 먼저 1. Build UI를 하세요.");
                return;
            }

            // 이미 있으면 지우고 다시 만듦
            GameObject old = FindRoot(SettingsRootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            // 예전에 만든 로비에는 "설정" 버튼이 없으므로 추가
            Transform lobby = world.transform.Find("LobbyPanel");
            if (lobby != null && lobby.Find("SettingsButton") == null) CreateSettingsButton(lobby);

            Transform settings = CreateSettingsCanvas();
            ConnectSettingsUI(world.transform, settings);

            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[GameUIBuilder] 설정창을 만들고 SettingsUI에 연결했습니다.");
        }

        // 꺼져 있는 최상위 오브젝트도 찾음 (GameObject.Find는 켜진 것만 찾음)
        private GameObject FindRoot(string rootName)
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                if (root.name == rootName) return root;
            }
            return null;
        }

        #region SettingsUI 자동 연결

        // SettingsUI가 씬에 없으면 GameUI 오브젝트에 붙이고, 만든 오브젝트를 칸에 넣음
        private void ConnectSettingsUI(Transform world, Transform settings)
        {
            SettingsUI ui = FindAnyObjectByType<SettingsUI>(FindObjectsInactive.Include);
            if (ui == null)
            {
                GameUI gameUI = FindAnyObjectByType<GameUI>(FindObjectsInactive.Include);
                if (gameUI == null)
                {
                    Debug.LogWarning("[GameUIBuilder] 씬에 GameUI가 없어서 SettingsUI를 붙이지 못했습니다.");
                    return;
                }
                ui = Undo.AddComponent<SettingsUI>(gameUI.gameObject);   // GameUI와 같은 오브젝트에 붙임
            }

            Undo.RecordObject(ui, "Connect SettingsUI");
            ui.settingsRoot = settings.gameObject;
            ui.openButton = Find(world, "LobbyPanel/SettingsButton").GetComponent<Button>();
            ui.closeButton = Find(settings, "SettingsPanel/Buttons/CloseButton").GetComponent<Button>();
            ui.closeLabel = Find(settings, "SettingsPanel/Buttons/CloseButton/LabelText").GetComponent<TMP_Text>();
            ui.lobbyButton = Find(settings, "SettingsPanel/Buttons/LobbyButton").GetComponent<Button>();
            ui.bgmSlider = Find(settings, "SettingsPanel/BgmRow/Slider").GetComponent<Slider>();
            ui.bgmValueText = Find(settings, "SettingsPanel/BgmRow/ValueText").GetComponent<TMP_Text>();
            ui.sfxSlider = Find(settings, "SettingsPanel/SfxRow/Slider").GetComponent<Slider>();
            ui.sfxValueText = Find(settings, "SettingsPanel/SfxRow/ValueText").GetComponent<TMP_Text>();
            EditorUtility.SetDirty(ui);
            Debug.Log("[GameUIBuilder] SettingsUI 칸을 연결했습니다.");
        }

        #endregion

        #region GameUI 자동 연결

        // 만든 오브젝트를 이름(경로)으로 찾아서 씬의 GameUI 칸에 넣음
        // world나 hud가 null이면 그쪽 칸은 건드리지 않음
        private void ConnectGameUI(Transform world, Transform hud)
        {
            GameUI ui = FindAnyObjectByType<GameUI>(FindObjectsInactive.Include);
            if (ui == null)
            {
                Debug.LogWarning("[GameUIBuilder] 씬에 GameUI가 없어서 연결하지 못했습니다.");
                return;
            }

            Undo.RecordObject(ui, "Connect GameUI");

            if (world != null)
            {
                ui.lobbyPanel = Find(world, "LobbyPanel").gameObject;
                ui.weaponText = Find(world, "LobbyPanel/WeaponText").GetComponent<TMP_Text>();
                ui.startButton = Find(world, "LobbyPanel/StartButton").GetComponent<Button>();
                ui.gameOverPanel = Find(world, "GameOverPanel").gameObject;
                ui.resultText = Find(world, "GameOverPanel/ResultText").GetComponent<TMP_Text>();
                ui.restartButton = Find(world, "GameOverPanel/RestartButton").GetComponent<Button>();
                ui.quitButton = Find(world, "GameOverPanel/QuitButton").GetComponent<Button>();
            }

            if (hud != null)
            {
                ui.hudPanel = Find(hud, "HudPanel").gameObject;
                ui.lifeIcons = new GameObject[3];
                for (int i = 0; i < 3; i++)
                {
                    ui.lifeIcons[i] = Find(hud, $"HudPanel/LifeIcons/LifeIcon_{i + 1}").gameObject;
                }
                ui.scoreText = Find(hud, "HudPanel/ScoreTag/ScoreText").GetComponent<TMP_Text>();
                ui.comboText = Find(hud, "HudPanel/ScoreTag/ComboText").GetComponent<TMP_Text>();
                ui.petalSprite = LoadSprite("UI_SakuraPetal");
                ui.gaugeFillImage = Find(hud, "HudPanel/Gauge/GaugeBack/GaugeFill").GetComponent<Image>();
                ui.gaugeHint = Find(hud, "HudPanel/Gauge/GaugeHint").gameObject;
                ui.flashImage = Find(hud, "FlashImage").GetComponent<Image>();
                ui.slashEffect = Find(hud, "SlashEffect").gameObject;
                ui.slashText = Find(hud, "SlashEffect/SlashText").GetComponent<TMP_Text>();
            }

            EditorUtility.SetDirty(ui);
            Debug.Log("[GameUIBuilder] GameUI 칸을 연결했습니다.");
        }

        // 경로로 자식을 찾음 (꺼져 있어도 찾아짐). 없으면 어떤 경로가 없는지 에러로 알려줌
        private static Transform Find(Transform root, string path)
        {
            Transform t = root.Find(path);
            if (t == null) Debug.LogError($"[GameUIBuilder] '{root.name}/{path}'를 찾지 못했습니다. 2. Clear UI 후 1. Build UI를 다시 하세요.");
            return t;
        }

        #endregion

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

        // 한지 족자 바탕 (9-slice로 늘려도 막대·테두리가 안 늘어남)
        private GameObject NewScrollPanel(string name, Transform canvas)
        {
            GameObject panel = NewPanel(name, canvas, Vector2.zero);
            Image image = panel.GetComponent<Image>();
            image.sprite = LoadSprite("UI_ScrollPanel");
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            Stretch((RectTransform)panel.transform, 0f);
            return panel;
        }

        // 제목 + 오른쪽 낙관 + 아래 붓 띠
        private void CreateTitle(Transform panel, string name, string text, float y)
        {
            TMP_Text title = NewText(name, panel, text, 104, TextAlignmentOptions.Center);
            title.color = Ink;
            title.fontStyle = FontStyles.Bold;
            Place(title.rectTransform, Top, new Vector2(0f, y), new Vector2(760f, 130f));

            GameObject seal = NewImage("TitleSeal", panel, "UI_Seal_Zan", Color.white);
            Place((RectTransform)seal.transform, Top, new Vector2(330f, y - 22f), new Vector2(84f, 84f));

            GameObject line = NewImage("TitleLine", panel, "UI_BrushBand", Ink);
            Place((RectTransform)line.transform, Top, new Vector2(0f, y - 128f), new Vector2(560f, 40f));
        }

        private void CreateLobbyPanel(Transform canvas)
        {
            GameObject panel = NewScrollPanel("LobbyPanel", canvas);

            CreateTitle(panel.transform, "TitleText", "SlashTime", -70f);

            TMP_Text subtitle = NewText("SubtitleText", panel.transform, "斬時  ·  제작 NaJungWon", 30, TextAlignmentOptions.Center);
            subtitle.color = InkSoft;
            Place(subtitle.rectTransform, Top, new Vector2(0f, -240f), new Vector2(800f, 44f));

            TMP_Text weapon = NewText("WeaponText", panel.transform, "선택한 무기 : 긴 칼 한 자루", 38, TextAlignmentOptions.Center);
            weapon.color = Vermilion;
            Place(weapon.rectTransform, Top, new Vector2(0f, -300f), new Vector2(800f, 56f));

            TMP_Text guide = NewText("GuideText", panel.transform,
                "오른쪽 거치대의 칼을 레이로 가리키고 그립 버튼으로 무기를 고르세요\n" +
                "A / X 버튼을 누르고 있으면 블레이드 타임 · 주변에 부딪힐 물건이 없는지 확인하세요",
                24, TextAlignmentOptions.Center);
            guide.color = InkSoft;
            Place(guide.rectTransform, Top, new Vector2(0f, -368f), new Vector2(840f, 90f));

            Button start = NewButton("StartButton", panel.transform, "시작", Ink, out _);
            Place((RectTransform)start.transform, Bottom, new Vector2(0f, 70f), new Vector2(420f, 120f));

            CreateSettingsButton(panel.transform);
        }

        // 로비 오른쪽 아래의 작은 "설정" 버튼 (누르는 소리는 GameUI가 로비 버튼 전체에 붙임)
        private void CreateSettingsButton(Transform lobby)
        {
            Button settings = NewButton("SettingsButton", lobby, "설정", QuitInk, out TMP_Text label);
            label.fontSize = 36;
            Place((RectTransform)settings.transform, Bottom, new Vector2(330f, 80f), new Vector2(220f, 90f));
        }

        private void CreateGameOverPanel(Transform canvas)
        {
            GameObject panel = NewScrollPanel("GameOverPanel", canvas);

            CreateTitle(panel.transform, "GameOverTitleText", "게임 오버", -70f);

            TMP_Text result = NewText("ResultText", panel.transform, "점수  0\n최고 점수  0", 48, TextAlignmentOptions.Center);
            result.color = Ink;
            Place(result.rectTransform, Center, new Vector2(0f, 20f), new Vector2(800f, 150f));

            TMP_Text hint = NewText("HintText", panel.transform, "거치대에서 무기를 바꾼 뒤 다시 시작할 수 있어요", 26,
                TextAlignmentOptions.Center);
            hint.color = InkSoft;
            Place(hint.rectTransform, Center, new Vector2(0f, -85f), new Vector2(800f, 44f));

            Button restart = NewButton("RestartButton", panel.transform, "다시 시작", Ink, out _);
            Place((RectTransform)restart.transform, Bottom, new Vector2(-200f, 70f), new Vector2(380f, 110f));

            Button quit = NewButton("QuitButton", panel.transform, "종료", QuitInk, out _);
            Place((RectTransform)quit.transform, Bottom, new Vector2(200f, 70f), new Vector2(380f, 110f));
        }

        #endregion

        #region 설정창 (열 때 머리 앞으로 옮겨짐)

        // 설정창: 700 x 620 px, 1px = 1mm → 0.7m x 0.62m 판 (1.2m 앞에 뜸)
        private static readonly Vector2 SettingsSize = new Vector2(700f, 620f);

        private Transform CreateSettingsCanvas()
        {
            GameObject root = NewUI(SettingsRootName, null);
            Undo.RegisterCreatedObjectUndo(root, "Build Settings");
            root.transform.position = worldUIPosition + new Vector3(0f, 0f, -0.8f);   // 에디터에서 볼 자리 (실행 중엔 SettingsUI가 옮김)
            root.transform.rotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * WorldScale;
            ((RectTransform)root.transform).sizeDelta = SettingsSize;

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            root.AddComponent<TrackedDeviceGraphicRaycaster>();   // 레이로 누를 수 있게

            GameObject panel = NewScrollPanel("SettingsPanel", root.transform);
            CreateTitle(panel.transform, "SettingsTitleText", "설정", -60f);
            // 제목이 짧아서 낙관과 붓 띠를 제목 크기에 맞춤
            Place((RectTransform)panel.transform.Find("TitleSeal"), Top, new Vector2(140f, -82f), new Vector2(72f, 72f));
            Place((RectTransform)panel.transform.Find("TitleLine"), Top, new Vector2(0f, -188f), new Vector2(420f, 36f));

            CreateSoundRow(panel.transform, "BgmRow", "배경음악", 40f);
            CreateSoundRow(panel.transform, "SfxRow", "효과음", -95f);

            // 아래 버튼 묶음: 가로로 가운데 정렬 (로비에서 LobbyButton을 숨기면 CloseButton이 가운데로)
            GameObject buttons = NewUI("Buttons", panel.transform);
            Place((RectTransform)buttons.transform, Bottom, new Vector2(0f, 50f), new Vector2(620f, 100f));
            HorizontalLayoutGroup layout = buttons.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 30f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            Button close = NewButton("CloseButton", buttons.transform, "계속하기", Ink, out TMP_Text closeLabel);
            ((RectTransform)close.transform).sizeDelta = new Vector2(280f, 100f);
            closeLabel.fontSize = 42;

            Button lobby = NewButton("LobbyButton", buttons.transform, "로비로", QuitInk, out TMP_Text lobbyLabel);
            ((RectTransform)lobby.transform).sizeDelta = new Vector2(280f, 100f);
            lobbyLabel.fontSize = 42;

            root.SetActive(false);   // 처음엔 숨김
            return root.transform;
        }

        // 소리 한 줄: 위에 [이름 ........ 70%], 아래에 먹선 슬라이더 (音 도장 손잡이)
        // 트리거를 누른 채 좌우로 끌면 왼쪽 끝 0 ~ 오른쪽 끝 1. 선을 바로 눌러도 그 자리로 이동
        private void CreateSoundRow(Transform panel, string name, string label, float y)
        {
            GameObject row = NewUI(name, panel);
            Place((RectTransform)row.transform, Center, new Vector2(0f, y), new Vector2(560f, 120f));

            TMP_Text labelText = NewText("Label", row.transform, label, 40, TextAlignmentOptions.Left);
            labelText.color = Ink;
            Place(labelText.rectTransform, Center, new Vector2(-130f, 30f), new Vector2(300f, 56f));

            TMP_Text value = NewText("ValueText", row.transform, "100%", 34, TextAlignmentOptions.Right);
            value.color = Vermilion;
            Place(value.rectTransform, Center, new Vector2(210f, 30f), new Vector2(140f, 56f));

            // 슬라이더 본체
            GameObject sliderGo = NewUI("Slider", row.transform);
            Place((RectTransform)sliderGo.transform, Center, new Vector2(0f, -25f), new Vector2(520f, 60f));

            // 바탕: 옅은 먹선 (누르는 범위이기도 함)
            GameObject back = NewImage("Background", sliderGo.transform, "UI_BrushBand", new Color(Ink.r, Ink.g, Ink.b, 0.3f));
            RectTransform backRect = (RectTransform)back.transform;
            Stretch(backRect, 0f);
            backRect.offsetMin = new Vector2(0f, 12f);
            backRect.offsetMax = new Vector2(0f, -12f);
            back.GetComponent<Image>().raycastTarget = true;

            // 채움: 주홍 먹선 (왼쪽부터 값만큼. 너비는 Slider가 앵커로 바꿈)
            GameObject fillArea = NewUI("Fill Area", sliderGo.transform);
            Stretch((RectTransform)fillArea.transform, 0f);
            ((RectTransform)fillArea.transform).offsetMin = new Vector2(0f, 12f);
            ((RectTransform)fillArea.transform).offsetMax = new Vector2(0f, -12f);
            GameObject fill = NewImage("Fill", fillArea.transform, "UI_BrushBand", Vermilion);
            Stretch((RectTransform)fill.transform, 0f);

            // 손잡이: 音 도장 (끝까지 가도 반쯤 밖으로 안 나가게 양옆 여백)
            GameObject handleArea = NewUI("Handle Slide Area", sliderGo.transform);
            Stretch((RectTransform)handleArea.transform, 0f);
            ((RectTransform)handleArea.transform).offsetMin = new Vector2(30f, 0f);
            ((RectTransform)handleArea.transform).offsetMax = new Vector2(-30f, 0f);
            GameObject handle = NewImage("Handle", handleArea.transform, "UI_SoundSeal", Color.white);
            RectTransform handleRect = (RectTransform)handle.transform;
            handleRect.anchorMin = new Vector2(0f, 0.5f);
            handleRect.anchorMax = new Vector2(0f, 0.5f);
            handleRect.sizeDelta = new Vector2(76f, 76f);
            handle.GetComponent<Image>().raycastTarget = true;

            Slider slider = sliderGo.AddComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = handleRect;
            slider.targetGraphic = handle.GetComponent<Image>();     // 가리키면 도장 색이 살짝 바뀜
            slider.direction = Slider.Direction.LeftToRight;           // 왼쪽 0 → 오른쪽 1
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.value = 1f;

            ColorBlock colors = slider.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.82f, 0.82f);
            colors.selectedColor = Color.white;
            colors.pressedColor = new Color(0.8f, 0.6f, 0.6f);
            colors.colorMultiplier = 1f;
            slider.colors = colors;
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

        // 아래 가운데: 벚꽃 3송이 (가로로 나란히)
        private void CreateLifeIcons(Transform hud)
        {
            GameObject icons = NewUI("LifeIcons", hud);
            Place((RectTransform)icons.transform, Bottom, new Vector2(0f, 24f), new Vector2(420f, 120f));

            HorizontalLayoutGroup layout = icons.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 24f;
            layout.childAlignment = TextAnchor.MiddleCenter;   // 벚꽃이 꺼져도 남은 벚꽃은 가운데로
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            for (int i = 1; i <= 3; i++)
            {
                GameObject icon = NewImage($"LifeIcon_{i}", icons.transform, "UI_Sakura", Color.white);
                ((RectTransform)icon.transform).sizeDelta = new Vector2(120f, 120f);
                icon.GetComponent<Image>().preserveAspect = true;
            }
        }

        // 오른쪽 위: 한지 이름표 안에 점수, 그 아래 콤보 (낮 배경에서도 잘 보이게 먹색 글씨)
        private void CreateScore(Transform hud)
        {
            GameObject tag = NewImage("ScoreTag", hud, "UI_WashiTag", Color.white);
            tag.GetComponent<Image>().type = Image.Type.Sliced;
            Place((RectTransform)tag.transform, TopRight, new Vector2(-30f, -24f), new Vector2(380f, 170f));

            TMP_Text score = NewText("ScoreText", tag.transform, "0", 66, TextAlignmentOptions.Right);
            score.color = Ink;
            score.fontStyle = FontStyles.Bold;
            Place(score.rectTransform, TopRight, new Vector2(-36f, -18f), new Vector2(320f, 84f));

            TMP_Text combo = NewText("ComboText", tag.transform, "", 36, TextAlignmentOptions.Right);
            combo.color = Vermilion;
            Place(combo.rectTransform, TopRight, new Vector2(-36f, -102f), new Vector2(320f, 50f));
        }

        // 오른쪽 가운데: 블레이드 타임 게이지 (세로 카타나, 칼날이 아래에서 위로 참)
        // 위에서부터: 이름표 → 카타나 → 안내 이름표
        private void CreateGauge(Transform hud)
        {
            GameObject gauge = NewUI("Gauge", hud);
            Place((RectTransform)gauge.transform, Right, new Vector2(-50f, -100f), new Vector2(170f, 474f));

            GameObject label = NewTag("GaugeLabel", gauge.transform, "블레이드 타임", 22, Ink);
            Place((RectTransform)label.transform, Top, Vector2.zero, new Vector2(170f, 56f));

            // 카타나 바탕 (빈 칼날 + 코등이 + 손잡이)
            GameObject back = NewImage("GaugeBack", gauge.transform, "UI_KatanaGaugeBack", Color.white);
            Place((RectTransform)back.transform, Top, new Vector2(0f, -62f), new Vector2(60f, 360f));

            // 칼날 채움: Filled(세로, 아래부터). 채우는 양은 GameUI가 fillAmount로 바꿈
            GameObject fill = NewImage("GaugeFill", back.transform, "UI_KatanaGaugeFill", BladeBlue);
            Stretch((RectTransform)fill.transform, 0f);
            Image fillImage = fill.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Vertical;
            fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            fillImage.fillAmount = 0f;

            // 쓸 수 있을 때만 켜지는 안내 (GameUI가 켜고 끔)
            GameObject hint = NewTag("GaugeHint", gauge.transform, "A / X 누르기", 22, Vermilion);
            Place((RectTransform)hint.transform, Bottom, Vector2.zero, new Vector2(170f, 50f));
        }

        // 일괄 절단 연출: 흰 플래시 + 먹물 튀김 위 "N 斬"
        private void CreateSlashEffect(Transform hudCanvas)
        {
            GameObject flash = NewPanel("FlashImage", hudCanvas, Vector2.zero);
            Image flashImage = flash.GetComponent<Image>();
            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;
            Stretch((RectTransform)flash.transform, 0f);

            GameObject effect = NewUI("SlashEffect", hudCanvas);
            Place((RectTransform)effect.transform, Center, new Vector2(0f, 40f), new Vector2(520f, 520f));

            GameObject splash = NewImage("InkSplash", effect.transform, "UI_InkSplash", new Color(0.08f, 0.08f, 0.1f, 0.92f));
            Stretch((RectTransform)splash.transform, 0f);

            TMP_Text slash = NewText("SlashText", effect.transform, "3 斬", 130, TextAlignmentOptions.Center);
            slash.color = Washi;
            slash.fontStyle = FontStyles.Bold;
            Stretch(slash.rectTransform, 0f);

            effect.SetActive(false);
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

        // UI 이미지 폴더에서 이름으로 스프라이트를 불러옴 (없으면 경고 후 네모로 나옴)
        private static Sprite LoadSprite(string fileName)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiTexturePath + fileName + ".png");
            if (sprite == null) Debug.LogWarning($"[GameUIBuilder] {fileName}.png를 스프라이트로 못 찾았습니다. Texture Type이 Sprite인지 확인하세요.");
            return sprite;
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

        // 스프라이트 이미지 (색을 곱해서 씀. 흰색 그림이면 그 색이 됨)
        private GameObject NewImage(string name, Transform parent, string spriteName, Color color)
        {
            GameObject go = NewPanel(name, parent, Vector2.zero);
            Image image = go.GetComponent<Image>();
            image.sprite = LoadSprite(spriteName);
            image.color = color;
            image.raycastTarget = false;
            return go;
        }

        // 한지 이름표 + 가운데 글씨 (HUD에서 낮 배경 위 글씨가 잘 보이게)
        private GameObject NewTag(string name, Transform parent, string text, float size, Color textColor)
        {
            GameObject tag = NewImage(name, parent, "UI_WashiTag", Color.white);
            tag.GetComponent<Image>().type = Image.Type.Sliced;

            TMP_Text label = NewText(name + "Text", tag.transform, text, size, TextAlignmentOptions.Center);
            label.color = textColor;
            Stretch(label.rectTransform, 6f);
            return tag;
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

        // 붓 터치 버튼: 흰 붓 그림에 버튼 색(평소 먹색, 가리키면 주홍)을 입힘
        private Button NewButton(string name, Transform parent, string label, Color normal, out TMP_Text labelText)
        {
            GameObject go = NewPanel(name, parent, new Vector2(380f, 110f));
            Image image = go.GetComponent<Image>();
            image.sprite = LoadSprite("UI_BrushStroke");
            image.color = Color.white;
            Button button = go.AddComponent<Button>();

            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = ButtonHover;
            colors.selectedColor = normal;
            colors.pressedColor = Vermilion * 0.7f;
            colors.colorMultiplier = 1f;
            button.colors = colors;

            labelText = NewText("LabelText", go.transform, label, 48, TextAlignmentOptions.Center);
            labelText.color = Washi;
            labelText.fontStyle = FontStyles.Bold;
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
