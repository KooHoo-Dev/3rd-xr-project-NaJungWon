using System.Collections.Generic;
using UnityEngine;

namespace TimeSlasher
{
    // 칼을 휘두를 때 남는 잔상 (칼 1자루당 1개, Blade와 같은 오브젝트에 붙임)
    // - 매 프레임 칼날 시작점·끝점 위치를 기록하고, 최근 trailTime초 동안의 기록을 띠 모양 메시로 이어 그림
    // - 빨리 휘두르면 기록 사이가 멀어져 잔상이 길어지고, 가만히 있으면 잔상이 안 보임
    // - TrailRenderer는 "선"이라 칼날 길이만큼의 "면"을 못 그려서 직접 메시를 만듦
    public class BladeTrail : MonoBehaviour
    {
        [Header("잔상 설정")]
        public Blade blade;                  // 칼날 시작점·끝점을 가져올 Blade
        public Material trailMaterial;       // M_BladeTrail (TimeSlasher/Glow, Round 끔)
        public float trailTime = 0.12f;      // 잔상이 남는 시간 (초)
        public Color headColor = new Color(0.85f, 0.93f, 1f, 1f);   // 칼 바로 뒤 색 (흰빛 도는 은청색)
        public Color tailColor = new Color(0.3f, 0.5f, 1f, 0f);     // 잔상 끝 색 (푸른색, 투명)

        // 기록 (같은 번호끼리 한 세트)
        private List<Vector3> basePoints = new List<Vector3>();
        private List<Vector3> tipPoints = new List<Vector3>();
        private List<float> times = new List<float>();

        // 메시 만들 때 쓰는 목록 (매 프레임 새로 만들지 않고 재사용)
        private List<Vector3> vertices = new List<Vector3>();
        private List<Color> colors = new List<Color>();
        private List<Vector2> uvs = new List<Vector2>();
        private List<int> triangles = new List<int>();

        private GameObject trailObject;   // 잔상을 그리는 오브젝트 (칼을 따라 움직이면 안 되므로 따로 만듦)
        private Mesh mesh;

        private void Awake()
        {
            // 월드 원점에 가만히 있는 오브젝트를 만들고, 정점은 월드 좌표로 넣음
            trailObject = new GameObject(name + "_Trail");
            mesh = new Mesh();
            mesh.MarkDynamic();   // 매 프레임 바뀌는 메시라고 알려줌

            trailObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer meshRenderer = trailObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = trailMaterial;
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 그림자 없음
            meshRenderer.receiveShadows = false;
        }

        // 무기를 바꿔서 칼이 꺼지면 잔상도 지움 (다시 켜질 때 엉뚱한 곳과 이어지지 않게)
        private void OnDisable()
        {
            ClearTrail();
        }

        private void OnDestroy()
        {
            Destroy(trailObject);
            Destroy(mesh);
        }

        // Blade와 마찬가지로 손 위치가 정해진 뒤에 기록
        private void LateUpdate()
        {
            // 1. 지금 칼날 위치 기록
            basePoints.Add(blade.bladeBase.position);
            tipPoints.Add(blade.bladeTip.position);
            times.Add(Time.unscaledTime);   // 일시정지(timeScale 0) 중에도 잔상이 사라지게 실제 시간 사용

            // 2. 오래된 기록 지우기 (맨 앞이 가장 오래됨)
            while (times.Count > 0 && Time.unscaledTime - times[0] > trailTime)
            {
                basePoints.RemoveAt(0);
                tipPoints.RemoveAt(0);
                times.RemoveAt(0);
            }

            // 3. 기록으로 메시 만들기
            BuildMesh();
        }

        private void ClearTrail()
        {
            basePoints.Clear();
            tipPoints.Clear();
            times.Clear();
            if (mesh != null) mesh.Clear();
        }

        // [수학] AI 작성
        // 기록 하나당 정점 2개(칼날 시작·끝)를 두고, 이웃한 기록끼리 사각형(삼각형 2개)으로 이음
        // 오래된 기록일수록 tailColor 쪽으로 (점점 투명하게)
        private void BuildMesh()
        {
            mesh.Clear();
            int count = times.Count;
            if (count < 2) return;

            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();

            for (int i = 0; i < count; i++)
            {
                float age = (Time.unscaledTime - times[i]) / trailTime;   // 0 = 방금, 1 = 곧 사라짐
                Color color = Color.Lerp(headColor, tailColor, age);

                vertices.Add(basePoints[i]);
                vertices.Add(tipPoints[i]);
                colors.Add(color);
                colors.Add(color);
                uvs.Add(new Vector2(age, 0f));   // y = 0: 칼날 시작
                uvs.Add(new Vector2(age, 1f));   // y = 1: 칼날 끝 (셰이더에서 더 밝게)
            }

            for (int i = 0; i < count - 1; i++)
            {
                int a = i * 2;       // 이번 기록의 시작점
                int b = a + 1;       // 이번 기록의 끝점
                int c = a + 2;       // 다음 기록의 시작점
                int d = a + 3;       // 다음 기록의 끝점
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(c); triangles.Add(b); triangles.Add(d);
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();   // 화면 밖이라고 잘못 판단해 안 그려지는 것 방지
        }
    }
}
