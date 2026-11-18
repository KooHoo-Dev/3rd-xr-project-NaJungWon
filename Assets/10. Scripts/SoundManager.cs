using UnityEngine;

namespace TimeSlasher
{
    // 소리 담당: BGM, 블레이드 타임 소리 변화, 효과음
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance;

        [Header("BGM")]
        public AudioClip bgmClip;                  // BGM_Default
        public AudioSource bgmSource;              // BGM 전용 (Loop 켬, AudioLowPassFilter 같이 붙임)
        public AudioLowPassFilter bgmLowPass;      // 블레이드 타임 중 고음을 깎아 먹먹하게
        [Range(0f, 1f)] public float bgmVolume = 0.5f;

        [Header("블레이드 타임 중 BGM")]
        public float focusBgmPitch = 0.8f;         // 느려진 느낌
        public float focusCutoff = 900f;           // 이 주파수 위는 깎음 (평소 22000 = 안 깎음)
        public float focusChangeSpeed = 5f;        // 바뀌는 속도 (1초에 바뀌는 양)
        public AudioClip focusLoopClip;            // SFX_FocusHeartbeat_Loop (타이코 심장박동)
        public AudioSource focusLoopSource;        // 심장박동 전용 (Loop 켬)
        [Range(0f, 1f)] public float focusLoopVolume = 0.7f;

        [Header("효과음 (2D: 화면·몸 쪽 소리)")]
        public AudioSource sfxSource;              // 효과음 전용 (2D. 비워두면 자식 "SFX"로 자동 생성)
        [Range(0f, 1f)] public float sfxVolume = 1f;

        [Header("효과음 - 베기 (3D: 벤 위치에서 들림)")]
        public AudioClip[] cutClips;               // SFX_Cut_01~03 (랜덤으로 하나)
        public AudioClip slashMarkClip;            // 블레이드 타임 중 빗금
        public AudioClip bigSlashClip;             // 일괄 절단
        public float cutPitchRandom = 0.08f;       // 같은 소리 반복이 덜 느껴지게 피치를 살짝 흔듦

        [Header("효과음 - 블레이드 타임")]
        public AudioClip focusInClip;              // 시작
        public AudioClip focusOutClip;             // 끝 (시시오도시)
        public AudioClip gaugeReadyClip;           // 게이지가 쓸 수 있을 만큼 찼을 때 (풍경)

        [Header("효과음 - 게임 진행·UI")]
        public AudioClip uiClickClip;              // 버튼 클릭
        public AudioClip weaponSwapClip;           // 무기 교체
        public AudioClip missClip;                 // 목숨 감소
        public AudioClip gameStartClip;            // 게임 시작
        public AudioClip gameOverClip;             // 게임 오버

        private float focus;          // 지금 블레이드 타임 정도 (0 = 평소, 1 = 블레이드 타임)
        private bool isFocus;         // 목표

        // 설정창 슬라이더로 정하는 전체 소리 크기 (0 = 무음, 1 = 최대). PlayerPrefs에 저장되어 다음 실행에도 유지
        // 실제 소리 = 인스펙터 기본 볼륨(bgmVolume, sfxVolume) × 이 값
        public float BgmLevel { get; private set; } = 1f;
        public float SfxLevel { get; private set; } = 1f;
        private const string BgmLevelKey = "SlashTime.BgmLevel";
        private const string SfxLevelKey = "SlashTime.SfxLevel";

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // 인스펙터에서 비워두면 자식 오브젝트로 자동으로 만듦 (직접 만들 필요 없음)
            if (sfxSource == null) sfxSource = CreateChildSource("SFX");
            if (bgmSource == null) bgmSource = CreateChildSource("BGM");
            if (bgmLowPass == null)
            {
                bgmLowPass = bgmSource.GetComponent<AudioLowPassFilter>();
                if (bgmLowPass == null) bgmLowPass = bgmSource.gameObject.AddComponent<AudioLowPassFilter>();
            }
            bgmLowPass.cutoffFrequency = 22000f;   // 평소엔 안 깎음
            if (focusLoopSource == null) focusLoopSource = CreateChildSource("FocusLoop");

            // 저장된 소리 크기 불러오기 (처음이면 1 = 최대)
            SetBgmLevel(PlayerPrefs.GetFloat(BgmLevelKey, 1f));
            SetSfxLevel(PlayerPrefs.GetFloat(SfxLevelKey, 1f));
        }

        // 2D 소리용 AudioSource를 가진 자식 오브젝트를 만듦
        private AudioSource CreateChildSource(string childName)
        {
            GameObject child = new GameObject(childName);
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 2D (어디서나 같은 크기)
            return source;
        }

        private void Start()
        {
            // BGM은 로비부터 계속 흐름
            if (bgmSource != null && bgmClip != null)
            {
                bgmSource.clip = bgmClip;
                bgmSource.loop = true;
                bgmSource.volume = bgmVolume * BgmLevel;
                bgmSource.Play();
            }

            // 심장박동은 볼륨 0으로 미리 틀어두고, 블레이드 타임에 볼륨만 올림 (박자가 끊기지 않게)
            if (focusLoopSource != null && focusLoopClip != null)
            {
                focusLoopSource.clip = focusLoopClip;
                focusLoopSource.loop = true;
                focusLoopSource.volume = 0f;
                focusLoopSource.Play();
            }
        }

        private void Update()
        {
            // 블레이드 타임 정도를 서서히 바꿔 BGM에 적용
            float target = isFocus ? 1f : 0f;
            if (focus == target) return;
            focus = Mathf.MoveTowards(focus, target, focusChangeSpeed * Time.deltaTime);

            if (bgmSource != null) bgmSource.pitch = Mathf.Lerp(1f, focusBgmPitch, focus);
            if (bgmLowPass != null) bgmLowPass.cutoffFrequency = Mathf.Lerp(22000f, focusCutoff, focus);
            UpdateFocusLoopVolume();
        }

        // 심장박동 크기 = 기본 볼륨 × 블레이드 타임 정도 × 효과음 크기
        private void UpdateFocusLoopVolume()
        {
            if (focusLoopSource != null) focusLoopSource.volume = focusLoopVolume * focus * SfxLevel;
        }

        #region 설정 (SettingsUI가 호출)

        // BGM 전체 크기 (0 ~ 1). 슬라이더를 움직이는 동안 바로 들림
        public void SetBgmLevel(float level)
        {
            BgmLevel = Mathf.Clamp01(level);
            bgmSource.volume = bgmVolume * BgmLevel;
            PlayerPrefs.SetFloat(BgmLevelKey, BgmLevel);
        }

        // 효과음 전체 크기 (0 ~ 1). 심장박동 소리도 효과음으로 침
        public void SetSfxLevel(float level)
        {
            SfxLevel = Mathf.Clamp01(level);
            UpdateFocusLoopVolume();
            PlayerPrefs.SetFloat(SfxLevelKey, SfxLevel);
        }

        #endregion

        #region 블레이드 타임 (BladeTimeController가 호출)

        public void StartFocus()
        {
            isFocus = true;
            PlaySfx(focusInClip);
        }

        public void EndFocus()
        {
            isFocus = false;
            PlaySfx(focusOutClip);
        }

        public void PlayGaugeReady()
        {
            PlaySfx(gaugeReadyClip);
        }

        #endregion

        #region 베기 (SliceFeedback이 호출)

        // 평상시 베기: 3개 중 랜덤, 피치 살짝 랜덤, 벤 위치에서
        public void PlayCut(Vector3 position)
        {
            if (cutClips == null || cutClips.Length == 0) return;
            AudioClip clip = cutClips[Random.Range(0, cutClips.Length)];
            PlaySfxAt(clip, position, 1f + Random.Range(-cutPitchRandom, cutPitchRandom));
        }

        // 블레이드 타임 중 빗금
        public void PlaySlashMark(Vector3 position)
        {
            PlaySfxAt(slashMarkClip, position, 1f + Random.Range(-cutPitchRandom, cutPitchRandom));
        }

        // 일괄 절단 (한 번만, 몸 쪽에서 크게)
        public void PlayBigSlash()
        {
            PlaySfx(bigSlashClip);
        }

        #endregion

        #region 게임 진행·UI (GameManager, GameUI가 호출)

        public void PlayUiClick() { PlaySfx(uiClickClip); }
        public void PlayWeaponSwap() { PlaySfx(weaponSwapClip); }
        public void PlayMiss() { PlaySfx(missClip); }
        public void PlayGameStart() { PlaySfx(gameStartClip); }
        public void PlayGameOver() { PlaySfx(gameOverClip); }

        #endregion

        // 2D 효과음 (겹쳐서 재생 가능)
        private void PlaySfx(AudioClip clip)
        {
            if (clip == null || sfxSource == null) return;
            sfxSource.PlayOneShot(clip, sfxVolume * SfxLevel);
        }

        // 3D 효과음: 위치에 잠깐 소리 오브젝트를 만들어 재생하고, 끝나면 지움
        private void PlaySfxAt(AudioClip clip, Vector3 position, float pitch)
        {
            if (clip == null || SfxLevel <= 0f) return;   // 무음이면 소리 오브젝트도 안 만듦

            GameObject go = new GameObject("SFX_" + clip.name);
            go.transform.position = position;
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.volume = sfxVolume * SfxLevel;
            source.pitch = pitch;
            source.spatialBlend = 1f;          // 3D
            source.minDistance = 1f;           // 1m 안에서는 같은 크기
            source.maxDistance = 15f;
            source.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }
    }
}
