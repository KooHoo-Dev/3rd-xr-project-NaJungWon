using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeSlasher
{
    public class GameUI : MonoBehaviour
    {
        [Header("월드 UI - 시작 화면")]
        public GameObject lobbyPanel;
        public TMP_Text weaponText;        // 지금 든 무기

        [Header("월드 UI - 게임 오버 화면")]
        public GameObject gameOverPanel;
        public TMP_Text resultText;        // 점수, 최고 점수

        [Header("월드 UI - 버튼 (누르면 할 일은 Start에서 코드로 연결)")]
        public Button startButton;         // 시작 → 게임 시작
        public Button restartButton;       // 다시 시작 → 게임 시작
        public Button quitButton;          // 종료 → 게임 끄기

        [Header("HUD")]
        public GameObject hudPanel;        // 플레이 중에만 켬
        public GameObject[] lifeIcons;     // 목숨 벚꽃 3개. 잃을 때마다 하나씩 꺼짐
        public TMP_Text scoreText;
        public TMP_Text comboText;

        [Header("HUD - 목숨 잃을 때 꽃잎")]
        public Sprite petalSprite;         // UI_SakuraPetal
        public int petalCount = 6;         // 벚꽃 하나에서 흩어지는 꽃잎 수
        public float petalSeconds = 1.2f;  // 꽃잎이 떨어지며 사라지는 시간

        [Header("HUD - 블레이드 타임 게이지 (카타나 날이 아래에서 위로 참)")]
        public Image gaugeFillImage;       // UI_KatanaGaugeFill (Image Type = Filled, Vertical, Bottom)
        public float gaugeFillMin = 0.21f; // 그림에서 칼날이 시작하는 높이 (손잡이·코등이 위)
        public float gaugeFillMax = 0.98f; // 그림에서 칼끝 높이
        public GameObject gaugeHint;       // "A/X 누르기" 이름표 (쓸 수 있을 때만 켬)
        public Color gaugeChargingColor = new Color(0.55f, 0.68f, 0.82f);
        public Color gaugeReadyColor = new Color(0.66f, 0.83f, 1f);

        [Header("HUD - 일괄 절단 연출")]
        public Image flashImage;           // 흰 이미지 (처음엔 투명)
        public GameObject slashEffect;     // 먹물 튀김 + "N 斬" 묶음
        public TMP_Text slashText;         // "N 斬"
        public float flashFadeSpeed = 3f;
        public float slashTextSeconds = 1f;

        private float flashAlpha;
        private float slashTextTime;
        private int shownLives = -1;       // 화면에 보이고 있는 목숨 수 (줄어든 순간을 알기 위해)

        // 떨어지는 꽃잎 (같은 번호끼리 한 세트)
        private List<RectTransform> petals = new List<RectTransform>();
        private List<Vector2> petalVelocities = new List<Vector2>();
        private List<float> petalSpins = new List<float>();
        private List<float> petalTimes = new List<float>();

        private void Start()
        {
            // 버튼을 누르면 할 일 (인스펙터의 OnClick 대신 코드로 연결)
            startButton.onClick.AddListener(GameManager.Instance.StartGame);
            restartButton.onClick.AddListener(GameManager.Instance.StartGame);
            quitButton.onClick.AddListener(GameManager.Instance.QuitGame);

            // 시작·게임 오버 화면의 버튼을 누르면 전부 칼 베는 소리 (버튼마다 따로 연결할 필요 없음)
            AddClickSound(lobbyPanel);
            AddClickSound(gameOverPanel);
        }

        private void AddClickSound(GameObject panel)
        {
            if (panel == null) return;
            foreach (Button button in panel.GetComponentsInChildren<Button>(true))
            {
                button.onClick.AddListener(SoundManager.Instance.PlayUiClick);
            }
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            ScoreSystem score = ScoreSystem.Instance;
            BladeTimeController blade = BladeTimeController.Instance;

            // 상태에 맞는 화면만 켬 (설정창이 열려 있으면 로비 화면·HUD는 숨김. HUD는 맨 앞에 그려져 설정창을 가리므로)
            bool settingsOpen = SettingsUI.Instance != null && SettingsUI.Instance.IsOpen;
            lobbyPanel.SetActive(gm.State == GameState.Lobby && settingsOpen == false);
            gameOverPanel.SetActive(gm.State == GameState.GameOver);
            hudPanel.SetActive(gm.State == GameState.Playing && settingsOpen == false);

            // 월드 UI
            weaponText.text = gm.Weapon == WeaponType.LongSword ? "선택한 무기 : 긴 칼 한 자루" : "선택한 무기 : 짧은 칼 두 자루";
            resultText.text = $"점수  {score.Score}\n최고 점수  {score.BestScore}";

            UpdateLives(gm);

            scoreText.text = $"{score.Score}";
            comboText.text = score.Combo >= 2 ? $"{score.Combo} 連   ×{score.ComboMultiplier()}" : "";

            // 게이지: 카타나 날이 아래에서 위로 차오르고, 쓸 수 있으면 밝아지며 안내가 뜸
            gaugeFillImage.fillAmount = Mathf.Lerp(gaugeFillMin, gaugeFillMax, blade.Gauge);
            gaugeFillImage.color = blade.CanStart ? gaugeReadyColor : gaugeChargingColor;
            gaugeHint.SetActive(blade.CanStart && blade.IsSlow == false);

            UpdateFlash();
            UpdatePetals();
        }

        // 목숨: 남은 개수만큼 벚꽃을 켜고, 줄어든 순간 꺼지는 벚꽃에서 꽃잎을 흩뿌림
        private void UpdateLives(GameManager gm)
        {
            if (gm.IsPlaying && shownLives > gm.Lives)
            {
                for (int i = gm.Lives; i < shownLives && i < lifeIcons.Length; i++)
                {
                    SpawnPetals(lifeIcons[i]);
                }
            }
            shownLives = gm.Lives;

            for (int i = 0; i < lifeIcons.Length; i++)
            {
                lifeIcons[i].SetActive(i < gm.Lives);
            }
        }

        // 일괄 절단 순간 SliceFeedback이 호출
        public void Flash(int count)
        {
            flashAlpha = 1f;
            slashTextTime = slashTextSeconds;
            slashText.text = $"{count} 斬";
        }

        private void UpdateFlash()
        {
            // 흰 플래시는 점점 투명해짐
            flashAlpha = Mathf.Max(0f, flashAlpha - Time.deltaTime * flashFadeSpeed);
            Color color = flashImage.color;
            color.a = flashAlpha * 0.6f;
            flashImage.color = color;

            // 먹물 + "N 斬"은 잠깐 보였다 사라짐
            slashTextTime -= Time.deltaTime;
            slashEffect.SetActive(slashTextTime > 0f);
        }

        #region 꽃잎

        // 벚꽃 자리에 꽃잎 이미지를 여러 장 만들어 사방으로 흩뿌림
        private void SpawnPetals(GameObject icon)
        {
            if (petalSprite == null) return;
            Image iconImage = icon.GetComponent<Image>();

            for (int i = 0; i < petalCount; i++)
            {
                GameObject go = new GameObject("Petal", typeof(RectTransform));
                RectTransform rect = (RectTransform)go.transform;
                rect.SetParent(hudPanel.transform, false);
                rect.position = icon.transform.position;           // 꺼지는 벚꽃 자리에서 시작
                rect.sizeDelta = new Vector2(48f, 48f);
                rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

                Image image = go.AddComponent<Image>();
                image.sprite = petalSprite;
                image.raycastTarget = false;
                if (iconImage != null) image.material = iconImage.material;   // HUD처럼 항상 맨 앞에 그림

                petals.Add(rect);
                petalVelocities.Add(new Vector2(Random.Range(-220f, 220f), Random.Range(60f, 260f)));
                petalSpins.Add(Random.Range(-360f, 360f));
                petalTimes.Add(0f);
            }
        }

        // [수학] AI 작성
        // 꽃잎: 위로 살짝 튀었다가 중력(px/s²)으로 떨어지며 돌고, 시간이 지나면 투명해짐
        private void UpdatePetals()
        {
            for (int i = petals.Count - 1; i >= 0; i--)
            {
                petalTimes[i] += Time.deltaTime;
                Vector2 v = petalVelocities[i] + Vector2.down * 600f * Time.deltaTime;
                v.x *= 1f - 1.5f * Time.deltaTime;               // 옆으로는 공기 저항
                petalVelocities[i] = v;

                RectTransform rect = petals[i];
                rect.localPosition += (Vector3)(v * Time.deltaTime);
                rect.localRotation *= Quaternion.Euler(0f, 0f, petalSpins[i] * Time.deltaTime);

                float t = petalTimes[i] / petalSeconds;
                Image image = rect.GetComponent<Image>();
                Color c = image.color;
                c.a = 1f - t * t;
                image.color = c;

                if (t >= 1f)
                {
                    Destroy(rect.gameObject);
                    petals.RemoveAt(i);
                    petalVelocities.RemoveAt(i);
                    petalSpins.RemoveAt(i);
                    petalTimes.RemoveAt(i);
                }
            }
        }

        #endregion
    }
}
