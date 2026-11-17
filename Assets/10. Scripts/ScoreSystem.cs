using UnityEngine;

namespace TimeSlasher
{
    public class ScoreSystem : MonoBehaviour
    {
        public static ScoreSystem Instance;

        [Header("점수 규칙")]
        public int pointsPerFruit = 10;       // 과일 첫 절단 기본 점수
        public float comboKeepSeconds = 2f;   // 이 시간(과일 시간) 동안 못 베면 콤보 초기화

        public int Score { get; private set; }
        public int BestScore { get; private set; }
        public int Combo { get; private set; }

        private float comboTimer;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        // 이벤트 구독은 Start에서, 해제는 OnDestroy에서 (FPS HitEffectController와 같은 방식)
        private void Start()
        {
            SliceSystem.Instance.OnCut += HandleCut;
        }

        private void OnDestroy()
        {
            if (SliceSystem.Instance != null) SliceSystem.Instance.OnCut -= HandleCut;
        }

        private void Update()
        {
            if (GameManager.Instance.IsPlaying == false) return;

            // 콤보 타이머는 과일 시간 기준 (블레이드 타임 중엔 천천히 줄어 콤보가 안 끊김)
            comboTimer -= Time.deltaTime * BladeTimeController.Instance.Scale;
            if (comboTimer <= 0f) Combo = 0;
        }

        private void HandleCut(CutInfo info)
        {
            if (GameManager.Instance.IsPlaying == false) return;

            // 모든 절단은 콤보 +1
            Combo++;
            comboTimer = comboKeepSeconds;

            // 점수는 과일 첫 절단만 (조각 베기는 콤보만)
            if (info.IsFirstCut == false) return;

            int points = Mathf.RoundToInt(pointsPerFruit * ComboMultiplier());
            if (info.IsReserved) points *= 2;   // 블레이드 타임 중 벤 과일은 ×2
            Score += points;
        }

        public float ComboMultiplier()
        {
            if (Combo >= 20) return 3f;
            if (Combo >= 10) return 2f;
            if (Combo >= 5) return 1.5f;
            return 1f;
        }

        #region GameManager가 호출

        public void ResetScore()
        {
            Score = 0;
            Combo = 0;
            comboTimer = 0f;
        }

        public void ResetCombo()
        {
            Combo = 0;
        }

        public void SaveBest()
        {
            if (Score > BestScore) BestScore = Score;
        }

        #endregion
    }
}
