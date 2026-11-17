using Study3D.FpsGame;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace TimeSlasher
{
    public class SliceFeedback : MonoBehaviour
    {
        [Header("과즙")]
        public string juiceKey = "Juice";          // 과즙 파티클 풀 키

        [Header("베기 불꽃")]
        public string sparkKey = "Spark";          // 베는 순간 튀는 은청색 불꽃 파티클 풀 키

        [Header("절단선 (블레이드 타임 중)")]
        public Material slashLineMat;              // M_SlashOverlay (TimeSlasher/SlashLine 셰이더). 색·두께는 머티리얼에서 조절

        [Header("효과음")]
        public AudioSource sfx;
        public AudioClip cutClip;                  // 평상시 베기
        public AudioClip reserveClip;              // 블레이드 타임 중 베기 (절단선만)
        public AudioClip bigSlashClip;             // 일괄 절단

        [Header("진동")]
        public float cutAmplitude = 0.6f;          // 벨 때 벤 손 진동
        public float cutDuration = 0.05f;
        public HapticImpulsePlayer leftHaptic;     // 일괄 절단 때 양손 진동
        public HapticImpulsePlayer rightHaptic;

        [Header("화면")]
        public GameUI gameUI;                      // 플래시와 "x N SLASH"

        private void Start()
        {
            SliceSystem.Instance.OnCut += HandleCut;
            SliceSystem.Instance.OnPiecesReleased += HandlePiecesReleased;
            SliceSystem.Instance.OnBatchRelease += HandleBatchRelease;
        }

        private void OnDestroy()
        {
            if (SliceSystem.Instance == null) return;
            SliceSystem.Instance.OnCut -= HandleCut;
            SliceSystem.Instance.OnPiecesReleased -= HandlePiecesReleased;
            SliceSystem.Instance.OnBatchRelease -= HandleBatchRelease;
        }

        // 벨 때마다
        private void HandleCut(CutInfo info)
        {
            // 벤 손만 짧게 진동
            if (info.Blade != null) info.Blade.Vibrate(cutAmplitude, cutDuration);

            // 평상시·블레이드 타임 모두 베는 순간 불꽃
            PlayEffect(sparkKey, info.Position);

            if (info.IsReserved)
            {
                // 블레이드 타임 중: 갈라지지 않고 빛나는 절단선만
                DrawSlashLine(info);
                PlaySound(reserveClip);
            }
            else
            {
                // 평상시: 바로 갈라지므로 과즙
                PlayJuice(info.Position, info.Fruit.juiceColor);
                PlaySound(cutClip);
            }
        }

        // 예약된 과일이 실제로 갈라질 때 (과일마다)
        private void HandlePiecesReleased(Vector3 position, Color juiceColor)
        {
            PlayJuice(position, juiceColor);
        }

        // 블레이드 타임이 끝나 한꺼번에 갈라질 때 (한 번)
        // 큰 효과음 1회 + 양손 강한 진동 1회 + 흰 플래시 + "x N SLASH"
        private void HandleBatchRelease(int count)
        {
            PlaySound(bigSlashClip);
            if (leftHaptic != null) leftHaptic.SendHapticImpulse(1f, 0.25f);
            if (rightHaptic != null) rightHaptic.SendHapticImpulse(1f, 0.25f);
            if (gameUI != null) gameUI.Flash(count);
        }

        // 과즙 파티클 (FPS HitEffectController와 같은 방식)
        // 파티클은 하나만 쓰고, 켜기 전에 과일마다 색만 바꿔줌
        private void PlayJuice(Vector3 position, Color color)
        {
            IObjectPoolItem juice = ObjectPoolManager.Instance.GetObjectOrNull(juiceKey);
            if (juice == null) return;
            juice.FruitObject.transform.position = position;

            ParticleSystem particle = juice.FruitObject.GetComponent<ParticleSystem>();   // 과즙 파티클
            ParticleSystem.MainModule main = particle.main;                               // 시작 색은 main 모듈에 있음
            main.startColor = color;                                                      // 과일 색으로 바꿈

            juice.FruitObject.SetActive(true);   // 켜지면 Play On Awake로 재생
        }

        // 색을 안 바꾸는 이펙트를 풀에서 꺼내 위치에 재생
        private void PlayEffect(string key, Vector3 position)
        {
            IObjectPoolItem effect = ObjectPoolManager.Instance.GetObjectOrNull(key);
            if (effect == null) return;   // 풀에 등록 안 했으면 그냥 넘어감
            effect.FruitObject.transform.position = position;
            effect.FruitObject.SetActive(true);   // 켜지면 Play On Awake로 재생
        }

        private void PlaySound(AudioClip clip)
        {
            if (sfx != null && clip != null) sfx.PlayOneShot(clip);
        }

        // 과일 표면에 빛나는 절단선을 그음 (셰이더 방식)
        // 자른 위치·방향만 넘기면 과일이 머티리얼을 붙이고, 사라지거나 재사용될 때 알아서 지움
        private void DrawSlashLine(CutInfo info)
        {
            if (info.Fruit == null) return;
            info.Fruit.AddSlashLine(slashLineMat, info.Position, info.Normal);
        }
    }
}
