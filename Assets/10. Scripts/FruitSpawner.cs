using Study3D.FpsGame;
using UnityEngine;

namespace TimeSlasher
{
    // 과일을 어떻게 날려 보낼지
    public enum LaunchPattern
    {
        Rise,    // 가까이에서 위로 솟았다가 그대로 떨어짐
        Toss,    // 멀리서 솟아올라 플레이어 쪽으로 토스됨
        Mixed,   // Rise와 Toss 반반
    }

    // 패턴마다 따로 정하는 발사 값 (Rise와 Toss가 같은 모양으로 씀)
    // [System.Serializable]을 붙여야 인스펙터에 펼쳐서 보임
    [System.Serializable]
    public struct LaunchSettings
    {
        public float MinDistance;    // 플레이어와의 생성 거리 (최소)
        public float MaxDistance;    // 플레이어와의 생성 거리 (최대)
        public float MinApexY;       // 최고점 높이 (최소)
        public float MaxApexY;       // 최고점 높이 (최대)
        public float MinSideAngle;   // 정면 기준 좌우 각도 (최소, 왼쪽은 -)
        public float MaxSideAngle;   // 정면 기준 좌우 각도 (최대, 오른쪽은 +)
    }

    public class FruitSpawner : MonoBehaviour
    {
        [Header("과일")]
        public string[] fruitKeys = { "Apple" };
        public LaunchPattern pattern = LaunchPattern.Mixed;
        public Transform player;
        public float launchY = -0.5f;        // 바닥 아래 발사 높이 (큰 과일도 바닥 위로 안 삐져나오게)

        [Header("Rise: 가까이에서 수직으로 올라갔다 떨어짐")]
        public LaunchSettings rise = new LaunchSettings
        {
            MinDistance = 0.6f, MaxDistance = 1.0f,
            MinApexY = 1.3f, MaxApexY = 1.8f,          // 가슴 ~ 머리 위 살짝 (고개를 많이 안 들게)
            MinSideAngle = -35f, MaxSideAngle = 35f,
        };

        [Header("Toss: 멀리서 솟아올라 타격 존에서 최고점")]
        public LaunchSettings toss = new LaunchSettings
        {
            MinDistance = 2.0f, MaxDistance = 3.0f,
            MinApexY = 1.1f, MaxApexY = 1.7f,          // 타격 존 높이 (가슴 ~ 머리)
            MinSideAngle = -30f, MaxSideAngle = 30f,
        };

        [Header("타격 존 (Toss의 최고점이 생기는 곳, 칼이 닿는 곳)")]
        public float hitZoneMinDistance = 0.6f;   // 플레이어 앞 거리 (쌍검 0.55m도 닿게)
        public float hitZoneMaxDistance = 1.0f;
        public float hitZoneHalfWidth = 0.5f;     // 좌우 폭의 절반

        [Header("난이도")]
        public float startInterval = 2.0f;   // 처음 발사 간격
        public float endInterval = 0.7f;     // 최대 난이도 발사 간격
        public int maxCount = 4;             // 한 번에 최대 발사 개수
        public float rampSeconds = 90f;      // 최대 난이도까지 걸리는 시간
        public float firstDelay = 1f;        // 게임 시작 후 첫 발사까지 대기

        private float playTime;
        private float timer;

        private void Update()
        {
            // 게임 중이 아니면 다음 판을 위해 시간을 처음으로 되돌려 둠
            if (GameManager.Instance.IsPlaying == false)
            {
                playTime = 0f;
                timer = firstDelay;
                return;
            }

            playTime += Time.deltaTime;
            timer -= Time.deltaTime * BladeTimeController.Instance.Scale;   // 슬로우 중엔 발사도 느리게
            if (timer > 0f) return;

            float level = Mathf.Clamp01(playTime / rampSeconds);   // 0 → 1 난이도
            timer = Mathf.Lerp(startInterval, endInterval, level);
            int count = Random.Range(1, 1 + Mathf.RoundToInt(Mathf.Lerp(1, maxCount, level)));
            for (int i = 0; i < count; i++) SpawnOne();
        }

        private void SpawnOne()
        {
            // 1. 풀에서 랜덤 과일 하나 꺼내기
            string key = fruitKeys[Random.Range(0, fruitKeys.Length)];
            Fruit fruit = ObjectPoolManager.Instance.GetObjectOrNull(key) as Fruit;
            if (fruit == null) return;

            // 2. 이번 과일의 패턴 정하기 (Mixed면 Rise와 Toss 반반)
            LaunchPattern now = pattern;
            if (now == LaunchPattern.Mixed) now = Random.value < 0.5f ? LaunchPattern.Rise : LaunchPattern.Toss;

            // 3. 패턴마다 출발 위치와 최고점 정하기
            Vector3 pos;
            Vector3 apex;
            if (now == LaunchPattern.Rise)
            {
                // 가까이에서 수직으로: 최고점은 출발점 바로 위
                pos = GetLaunchPosition(rise);
                apex = new Vector3(pos.x, Random.Range(rise.MinApexY, rise.MaxApexY), pos.z);
            }
            else
            {
                // 멀리서 플레이어 쪽으로: 최고점은 타격 존 안
                pos = GetLaunchPosition(toss);
                apex = GetHitZonePosition(Random.Range(toss.MinApexY, toss.MaxApexY));
            }
            Vector3 velocity = GetVelocityToApex(pos, apex);

            // 4. 위치·회전 정하고 켠 뒤 상태 초기화
            fruit.transform.SetPositionAndRotation(pos, Random.rotation);
            fruit.gameObject.SetActive(true);
            fruit.Init();

            // 5. 발사 요청 (힘은 Fruit의 FixedUpdate에서 줌)
            fruit.Launch(velocity);
        }

        // 플레이어 정면 방향 (높이 성분은 뺌)
        private Vector3 Forward()
        {
            return Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        }

        // [수학] AI 작성
        // 플레이어 정면 기준 거리(Min~MaxDistance)·좌우 각도(Min~MaxSideAngle) 랜덤 위치. 높이는 바닥 아래(launchY)
        private Vector3 GetLaunchPosition(LaunchSettings settings)
        {
            float angle = Random.Range(settings.MinSideAngle, settings.MaxSideAngle);
            Vector3 dir = Quaternion.Euler(0f, angle, 0f) * Forward();
            Vector3 pos = player.position + dir * Random.Range(settings.MinDistance, settings.MaxDistance);
            pos.y = launchY;
            return pos;
        }

        // [수학] AI 작성
        // 타격 존 안의 랜덤한 점: 플레이어 앞 거리·좌우는 랜덤, 높이는 y
        private Vector3 GetHitZonePosition(float y)
        {
            Vector3 right = Vector3.Cross(Vector3.up, Forward());
            Vector3 pos = player.position
                          + Forward() * Random.Range(hitZoneMinDistance, hitZoneMaxDistance)
                          + right * Random.Range(-hitZoneHalfWidth, hitZoneHalfWidth);
            pos.y = y;
            return pos;
        }

        // [수학] AI 작성
        // start에서 출발해 apex에서 최고점이 되는 초속도
        // 위쪽 속도 vy = √(2·g·높이차), 최고점까지 시간 t = vy / g, 수평 속도 = 수평 거리 / t
        // (apex가 start 바로 위면 수평 속도 0 → 수직으로 올라갔다 떨어짐)
        private static Vector3 GetVelocityToApex(Vector3 start, Vector3 apex)
        {
            float g = -Physics.gravity.y;
            float height = Mathf.Max(0.1f, apex.y - start.y);
            float vy = Mathf.Sqrt(2f * g * height);
            float t = vy / g;

            Vector3 flat = apex - start;
            flat.y = 0f;
            return flat / t + Vector3.up * vy;
        }
    }
}
