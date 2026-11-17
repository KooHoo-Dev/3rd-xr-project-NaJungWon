using UnityEngine;

namespace TimeSlasher
{
    public enum GameState { Lobby, Playing, GameOver }

    public enum WeaponType { LongSword, DualSwords }

    // 게임 흐름 담당: 대기/플레이/종료 전환, 목숨, 무기 선택, 레이 켜고 끄기
    // 점수는 ScoreSystem, 베기는 SliceSystem, 연출은 SliceFeedback이 합니다
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance;

        [Header("규칙")]
        public int maxLives = 3;

        [Header("무기 (컨트롤러 자식으로 붙여둔 칼)")]
        public GameObject[] longSwordObjects;    // 한손검을 골랐을 때 켤 것 (오른손 긴 칼)
        public GameObject[] dualSwordObjects;    // 쌍검을 골랐을 때 켤 것 (왼손·오른손 짧은 칼)

        [Header("레이 (메뉴에서만 보임)")]
        public GameObject[] rayObjects;          // 양손 Near-Far Interactor 등

        public GameState State { get; private set; } = GameState.Lobby;
        public bool IsPlaying => State == GameState.Playing;
        public WeaponType Weapon { get; private set; } = WeaponType.LongSword;
        public int Lives { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            Lives = maxLives;
        }

        private void Start()
        {
            // 처음엔 한손검을 든 상태로 시작
            SelectLongSword();
            SetRays(true);
        }

        #region 무기 선택 (전시대의 XRSimpleInteractable → Select Entered 이벤트에 연결)

        public void SelectLongSword()
        {
            SelectWeapon(WeaponType.LongSword);
        }

        public void SelectDualSwords()
        {
            SelectWeapon(WeaponType.DualSwords);
        }

        private void SelectWeapon(WeaponType weapon)
        {
            // 게임 중에는 무기를 바꿀 수 없음
            if (IsPlaying) return;

            Weapon = weapon;
            SetObjects(longSwordObjects, weapon == WeaponType.LongSword);
            SetObjects(dualSwordObjects, weapon == WeaponType.DualSwords);
        }

        #endregion

        #region 게임 흐름 (UI 버튼 OnClick에 연결)

        // 시작 화면의 시작 버튼, 게임 오버 화면의 재시작 버튼
        // 이때 무기교체 가능
        public void StartGame()
        {
            if (IsPlaying) return;

            Lives = maxLives;
            ScoreSystem.Instance.ResetScore();
            SliceSystem.Instance.ClearReserved();
            BladeTimeController.Instance.ResetGauge();

            State = GameState.Playing;
            SetRays(false);   // 플레이 중엔 레이 숨김
        }

        // 안 벤 과일이 바닥에 닿으면 Fruit가 호출
        public void OnMiss()
        {
            if (IsPlaying == false) return;

            Lives--;
            ScoreSystem.Instance.ResetCombo();
            if (Lives <= 0) GameOver();
        }

        private void GameOver()
        {
            State = GameState.GameOver;

            // 예약 목록을 먼저 비우고 블레이드 타임을 끝냄 (일괄 절단 연출이 안 나오게)
            SliceSystem.Instance.ClearReserved();
            BladeTimeController.Instance.ForceEnd();

            // 화면에 남은 과일·조각을 전부 없앰 (켜져 있는 것만 찾아짐. 숨겨둔 조각은 과일이 같이 지움)
            foreach (Fruit fruit in FindObjectsByType<Fruit>(FindObjectsSortMode.None))
            {
                fruit.Remove();
            }

            ScoreSystem.Instance.SaveBest();
            SetRays(true);
        }

        // 게임 종료
        public void QuitGame()
        {
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            #else
            Application.Quit();
            #endif
        }

        #endregion

        private void SetRays(bool on)
        {
            SetObjects(rayObjects, on);
        }

        // 배열에 든 오브젝트를 모두 켜거나 끔
        private void SetObjects(GameObject[] objects, bool on)
        {
            if (objects == null) return;
            for (int i = 0; i < objects.Length; ++i)
            {
                if (objects[i] != null) objects[i].SetActive(on);
            }
        }
    }
}
