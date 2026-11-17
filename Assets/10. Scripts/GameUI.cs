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

        [Header("HUD")]
        public GameObject hudPanel;        // 플레이 중에만 켬
        public GameObject[] lifeIcons;     // 목숨 아이콘 3개. 잃을 때마다 하나씩 꺼짐
        public TMP_Text scoreText;
        public TMP_Text comboText;

        [Header("HUD - 블레이드 타임 게이지")]
        public RectTransform gaugeFill;    // 세로 크기(localScale.y)를 게이지만큼 줄임 (아래에서 위로 참)
        public Image gaugeFillImage;
        public TMP_Text gaugeHintText;     // "A/X 누르고 있기"
        public Color gaugeChargingColor = new Color(0.2f, 0.4f, 1f);
        public Color gaugeReadyColor = new Color(0.4f, 0.9f, 1f);

        [Header("HUD - 일괄 절단 연출")]
        public Image flashImage;           // 흰 이미지 (처음엔 투명)
        public TMP_Text slashText;         // "x N SLASH"
        public float flashFadeSpeed = 3f;
        public float slashTextSeconds = 1f;

        private float flashAlpha;
        private float slashTextTime;

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            ScoreSystem score = ScoreSystem.Instance;
            BladeTimeController blade = BladeTimeController.Instance;

            // 상태에 맞는 화면만 켬
            lobbyPanel.SetActive(gm.State == GameState.Lobby);
            gameOverPanel.SetActive(gm.State == GameState.GameOver);
            hudPanel.SetActive(gm.State == GameState.Playing);

            // 월드 UI
            weaponText.text = gm.Weapon == WeaponType.LongSword ? "선택한 무기: 긴 한손검" : "선택한 무기: 짧은 쌍검";
            resultText.text = $"점수 {score.Score}\n최고 점수 {score.BestScore}";

            // 목숨: 남은 개수만큼만 아이콘을 켬
            for (int i = 0; i < lifeIcons.Length; i++)
            {
                lifeIcons[i].SetActive(i < gm.Lives);
            }

            scoreText.text = $"{score.Score}";
            comboText.text = score.Combo >= 2 ? $"{score.Combo} 콤보  x{score.ComboMultiplier()}" : "";

            // 게이지: 세로로 차오르고, 25% 이상이면 밝아지며 안내 문구가 뜸
            gaugeFill.localScale = new Vector3(1f, blade.Gauge, 1f);
            gaugeFillImage.color = blade.CanStart ? gaugeReadyColor : gaugeChargingColor;
            gaugeHintText.text = (blade.CanStart && blade.IsSlow == false) ? "A/X\n누르고 있기" : "";

            UpdateFlash();
        }

        // 일괄 절단 순간 SliceFeedback이 호출
        public void Flash(int count)
        {
            flashAlpha = 1f;
            slashTextTime = slashTextSeconds;
            slashText.text = $"x{count} SLASH";
        }

        private void UpdateFlash()
        {
            // 흰 플래시는 점점 투명해짐
            flashAlpha = Mathf.Max(0f, flashAlpha - Time.deltaTime * flashFadeSpeed);
            Color color = flashImage.color;
            color.a = flashAlpha * 0.6f;
            flashImage.color = color;

            // "x N SLASH"는 잠깐 보였다 사라짐
            slashTextTime -= Time.deltaTime;
            slashText.gameObject.SetActive(slashTextTime > 0f);
        }
    }
}
