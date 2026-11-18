using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace TimeSlasher
{
    // 설정창: 왼손 메뉴(≡) 버튼(키보드 Esc) 또는 로비의 "설정" 버튼으로 열고 닫음
    public class SettingsUI : MonoBehaviour
    {
        public static SettingsUI Instance;

        [Header("설정창 (World Space 캔버스 통째로 켜고 끔)")]
        public GameObject settingsRoot;    // "Settings UI" 캔버스
        public float distance = 1.2f;      // 머리 앞 거리 (m)

        [Header("버튼 (누르면 할 일은 Start에서 코드로 연결)")]
        public Button openButton;          // 로비 화면의 "설정"
        public Button closeButton;         // 계속하기(게임 중) / 닫기(로비)
        public TMP_Text closeLabel;        // closeButton 글씨
        public Button lobbyButton;         // 로비로 나가기 (게임 중에만 보임)

        [Header("소리 크기 슬라이더 (0 ~ 1, 슬라이더는 누르는 소리 없음)")]
        public Slider bgmSlider;
        public TMP_Text bgmValueText;      // "70%"
        public Slider sfxSlider;
        public TMP_Text sfxValueText;

        public bool IsOpen { get; private set; }

        private InputAction menuAction;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        private void OnEnable()
        {
            // 왼손 메뉴(≡) 버튼, 키보드 테스트는 Esc
            menuAction = new InputAction("Settings", InputActionType.Button, "<XRController>{LeftHand}/{MenuButton}");
            menuAction.AddBinding("<Keyboard>/escape");
            menuAction.Enable();
        }

        private void OnDisable()
        {
            menuAction.Disable();
            menuAction.Dispose();
        }

        private void Start()
        {
            // 버튼을 누르면 할 일 (인스펙터의 OnClick 대신 코드로 연결)
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            lobbyButton.onClick.AddListener(ReturnToLobby);

            // 설정창 안의 버튼은 칼 베는 소리 (Slider는 Button이 아니라서 소리가 안 붙음)
            // 로비의 "설정" 버튼은 GameUI가 로비 버튼 전체에 소리를 붙이므로 여기선 안 붙임
            foreach (Button button in settingsRoot.GetComponentsInChildren<Button>(true))
            {
                button.onClick.AddListener(SoundManager.Instance.PlayUiClick);
            }

            // 슬라이더를 저장된 값으로 맞춘 뒤 연결 (맞추는 동안 이벤트가 안 불리게 WithoutNotify)
            bgmSlider.minValue = 0f;
            bgmSlider.maxValue = 1f;
            sfxSlider.minValue = 0f;
            sfxSlider.maxValue = 1f;
            bgmSlider.SetValueWithoutNotify(SoundManager.Instance.BgmLevel);
            sfxSlider.SetValueWithoutNotify(SoundManager.Instance.SfxLevel);
            bgmSlider.onValueChanged.AddListener(OnBgmSlider);
            sfxSlider.onValueChanged.AddListener(OnSfxSlider);
            UpdateValueTexts();

            settingsRoot.SetActive(false);
        }

        private void Update()
        {
            if (menuAction.WasPressedThisFrame() == false) return;

            // 메뉴 버튼: 열려 있으면 닫고, 닫혀 있으면 엶 (게임 오버 화면에서는 안 열림)
            if (IsOpen) Close();
            else if (GameManager.Instance.State != GameState.GameOver) Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;

            GameManager.Instance.OpenSettings();
            PlaceInFrontOfHead();

            // 게임 중: 계속하기 + 로비로 / 로비: 닫기만
            bool playing = GameManager.Instance.IsPlaying;
            closeLabel.text = playing ? "계속하기" : "닫기";
            lobbyButton.gameObject.SetActive(playing);

            settingsRoot.SetActive(true);
        }

        public void Close()
        {
            if (IsOpen == false) return;
            IsOpen = false;

            settingsRoot.SetActive(false);
            GameManager.Instance.CloseSettings();
        }

        private void ReturnToLobby()
        {
            IsOpen = false;
            settingsRoot.SetActive(false);
            GameManager.Instance.ReturnToLobby();   // 이번 판 점수는 버림
        }

        // 머리 앞 distance(m), 눈높이에, 나를 바라보게 놓음 (고개를 숙이거나 들어도 수평으로)
        private void PlaceInFrontOfHead()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;   // 바로 위·아래를 보고 있을 때
            forward.Normalize();

            Transform root = settingsRoot.transform;
            root.position = cam.transform.position + forward * distance;
            root.rotation = Quaternion.LookRotation(forward);   // 캔버스 앞면이 나를 향함
        }

        // 슬라이더를 끄는 동안 계속 불림 → BGM은 바로 크기가 바뀌어 들림
        private void OnBgmSlider(float value)
        {
            SoundManager.Instance.SetBgmLevel(value);
            UpdateValueTexts();
        }

        private void OnSfxSlider(float value)
        {
            SoundManager.Instance.SetSfxLevel(value);
            UpdateValueTexts();
        }

        // 0 ~ 1 값을 0% ~ 100%로 표시
        private void UpdateValueTexts()
        {
            bgmValueText.text = Mathf.RoundToInt(bgmSlider.value * 100f) + "%";
            sfxValueText.text = Mathf.RoundToInt(sfxSlider.value * 100f) + "%";
        }
    }
}
