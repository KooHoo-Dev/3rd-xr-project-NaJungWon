using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;

namespace TimeSlasher
{
    // 칼 판정 (칼 1자루당 1개, 쌍검이면 2개)
    // 칼날에 닿은 과일과 자를 방향을 찾아서 SliceSystem에 넘기기만 합니다
    // (진동·소리는 SliceFeedback이 정해서 Vibrate를 부름)
    public class Blade : MonoBehaviour
    {
        [Header("칼날 설정")]
        public Transform bladeBase;          // 칼날 시작점 (빈 오브젝트)
        public Transform bladeTip;           // 칼날 끝점 (빈 오브젝트)
        public float bladeRadius = 0.02f;    // 칼날 두께 (반지름)
        public LayerMask fruitMask;          // 판정할 레이어 (Fruit)

        [Header("진동")]
        public HapticImpulsePlayer haptic;   // 이 칼을 든 손 컨트롤러의 HapticImpulsePlayer

        [Header("디버그")]
        public bool drawDebug = false;       // 켜면 Scene 뷰에 자르는 방향(법선)을 그림
        private const int RayCount = 5;      // 칼날 위에서 검사할 점 개수
        private const float MinMove = 0.02f;   // 한 프레임에 이보다 덜 움직이면 "가만히 있음"

        private Vector3 prevBase;
        private Vector3 prevTip;
        private Collider[] touching = new Collider[16];   // OverlapCapsule 결과를 담을 배열 (미리 만들어 둠)

        private void OnEnable()
        {
            SavePrevious();
        }

        // XR 컨트롤러가 손 위치로 옮겨진 뒤에 검사하려고 LateUpdate를 써야한다고 한다.
        private void LateUpdate()
        {
            PhysicsScene fruitPhysics = FruitWorld.Instance.FruitPhysics;
            
            // sweep방식이라고함
            // 1. 휘두를 때: 칼날 위 5개 점이 지난 위치 → 지금 위치로 선을 쏴서 과일이 걸리는지
            //    빠르게 휘둘러도 과일을 지나치지 않습니다
            for (int i = 0; i < RayCount; i++)
            {
                float t = i / (RayCount - 1f);
                Vector3 from = Vector3.Lerp(prevBase, prevTip, t);
                Vector3 to = Vector3.Lerp(bladeBase.position, bladeTip.position, t);
                Vector3 dir = to - from;
                float dist = dir.magnitude;
                if (dist < 0.01f) continue;   // 안 움직였으면 건너뜀

                if (fruitPhysics.Raycast(from, dir / dist, out RaycastHit hit, dist, fruitMask,
                        QueryTriggerInteraction.Ignore))
                {
                    TryCut(hit.collider, hit.point);
                }
            }

            // 2. 가만히 대고 있을 때: 칼날 캡슐 안에 들어온 과일
            int count = fruitPhysics.OverlapCapsule(bladeBase.position, bladeTip.position, bladeRadius,
                touching, fruitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                TryCut(touching[i], ClosestPointOnBlade(touching[i].transform.position));
            }

            SavePrevious();
        }

        private void TryCut(Collider col, Vector3 point)
        {
            Fruit fruit = col.GetComponent<Fruit>();
            if (fruit == null) return;

            Vector3 normal = GetCutNormal();
            if (drawDebug) Debug.DrawRay(point, normal * 0.3f, Color.cyan, 1f);

            SliceSystem.Instance.Cut(fruit, point, normal, this); // 자르기는 SliceSystem
        }

        // 이 칼을 든 손 진동 (SliceFeedback이 벨 때 부름)
        public void Vibrate(float amplitude, float duration)
        {
            if (haptic != null) haptic.SendHapticImpulse(amplitude, duration);
        }

        private void SavePrevious()
        {
            prevBase = bladeBase.position;
            prevTip = bladeTip.position;
        }

        // [수학] AI 작성
        // 칼날 선분(bladeBase ~ bladeTip) 위에서 center와 가장 가까운 점
        private Vector3 ClosestPointOnBlade(Vector3 center)
        {
            Vector3 a = bladeBase.position;
            Vector3 ab = bladeTip.position - a;
            float t = Mathf.Clamp01(Vector3.Dot(center - a, ab) / ab.sqrMagnitude);
            return a + ab * t;
        }

        // [수학] AI 작성
        // 자르는 면의 법선 = 칼날 방향 × 칼이 움직인 방향
        // 칼이 거의 안 움직였거나, 찌르기처럼 칼날 방향으로 움직였으면 칼날 면 방향(로컬 X축)
        private Vector3 GetCutNormal()
        {
            Vector3 bladeDir = (bladeTip.position - bladeBase.position).normalized;
            Vector3 nowCenter = (bladeBase.position + bladeTip.position) * 0.5f;
            Vector3 prevCenter = (prevBase + prevTip) * 0.5f;
            Vector3 move = nowCenter - prevCenter;

            if (move.magnitude < MinMove) return transform.right;

            Vector3 moveDir = move.normalized;
            if (Mathf.Abs(Vector3.Dot(bladeDir, moveDir)) > 0.9f) return transform.right;

            return Vector3.Cross(bladeDir, moveDir).normalized;
        }
    }
}
