using System.Collections.Generic;
using Study3D.FpsGame;
using UnityEngine;

namespace TimeSlasher
{
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class Fruit : MonoBehaviour, IObjectPoolItem
    {
        public string Key { get; set; }
        public GameObject FruitObject => gameObject;

        [Header("과일 / 조각 설정")]
        public bool isPiece = false;            // 조각 체크
        public Material crossSectionMat;        // 단면(과육) 머티리얼. 조각은 자를 때 원래 과일 것을 물려받음
        public Color juiceColor = new Color(1f, 0.3f, 0.2f);   // 과즙 파티클 색 (과즙 파티클은 하나, 색만 바꿈)
        public LayerMask risingLayer;           // 발사 직후 레이어 (아무것과도 안 부딪힘)
        public LayerMask fruitLayer;            // 바닥 위로 올라온 뒤 레이어 (바닥과 부딪힘)

        [Header("바닥")]
        public LayerMask floorMask;             // 바닥 레이어 (Ground)
        public float floorY = 0f;               // 바닥 윗면 높이
        public float killY = -1f;               // 이 높이 아래로 떨어지면 즉시 제거 (발사 높이보다 낮게)
        public float lifeAfterFloor = 2f;       // 바닥에 닿고 이 시간 뒤에 사라짐

        [Header("베기")]
        public float minPieceSize = 0.03f;      // 이거보다 작은(반지름) 조각은 벨 수 없음

        // 다른 스크립트에서 읽기만 하는 값
        public Rigidbody Body => body;
        public MeshRenderer MeshRenderer => meshRenderer;
        public Vector3 Center => transform.TransformPoint(meshFilter.sharedMesh.bounds.center);   // 메시 중심 (월드)
        public float Radius => AverageSize(meshFilter.sharedMesh) * MaxScale(transform.lossyScale);   // 메시 대략 반지름 (월드)
        public int CutCount => cutCount;
        public bool IsScored => scored;
        public bool IsReserved => reserved;
        public List<Fruit> HiddenPieces => hiddenPieces;
        public Material[] BaseMaterials => baseMaterials;   // 절단선 머티리얼을 뺀 원래 머티리얼 (자를 때 SliceSystem이 씀)

        private Rigidbody body;
        private Collider col;            // 붙어 있는 콜라이더 (Sphere 또는 Mesh)
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        private int risingLayerIndex;    // LayerMask를 gameObject.layer에 넣을 수 있는 번호로 바꾼 값
        private int fruitLayerIndex;

        private int cutCount;            // 이 과일 계열이 잘린 횟수
        private float noCutTime;         // 0보다 크면 베이지 않음
        private bool scored;             // 이미 베였는지 (처음 베였을 때만 점수)
        private bool reserved;           // 블레이드 타임 중 베여서 일괄 절단을 기다리는 중
        private bool touchedFloor;       // 바닥에 닿았는지
        private bool removed;            // 이미 없앴는지 (풀에 두 번 들어가는 것 방지)
        private float lifeTime;

        private Vector3 launchVelocity;
        private bool launchRequested;

        // 블레이드 타임 중 미리 잘라 숨겨둔 조각 (과일의 자식)
        private List<Fruit> hiddenPieces = new List<Fruit>();
        // 블레이드 타임 중 그어진 절단선 (셰이더에 넘길 자른 면 정보)
        private const int MaxSlashLines = 3;     // SlashLine 셰이더의 MAX_SLASH와 같게
        private static readonly int SlashPlanesId = Shader.PropertyToID("_SlashPlanes");
        private static readonly int SlashCountId = Shader.PropertyToID("_SlashCount");
        private Vector4[] slashPlanes = new Vector4[MaxSlashLines];
        private int slashCount;
        private Material[] baseMaterials;        // 절단선 머티리얼을 붙이기 전 원래 머티리얼
        private MaterialPropertyBlock slashBlock; // 과일마다 다른 값을 머티리얼 복사 없이 넣는 상자

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            col = GetComponent<Collider>();
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            baseMaterials = meshRenderer.sharedMaterials;
            slashBlock = new MaterialPropertyBlock();

            risingLayerIndex = ToLayerIndex(risingLayer);
            fruitLayerIndex = ToLayerIndex(fruitLayer);
        }

        // 풀에서 꺼낼 때 / 조각을 만들 때 호출. 재사용되는 개체라 이전 상태를 전부 지워줍니다
        public void Init()
        {
            cutCount = 0;
            noCutTime = 0f;
            scored = false;
            reserved = false;
            touchedFloor = false;
            removed = false;
            launchRequested = false;
            lifeTime = lifeAfterFloor;
            hiddenPieces.Clear();
            ClearSlashLines();

            // 남은 운동량 초기화
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            // 과일은 바닥 아래에서 출발하므로 아무것과도 안 부딪히는 레이어로 시작
            gameObject.layer = isPiece ? fruitLayerIndex : risingLayerIndex;
        }

        #region 발사

        // FruitSpawner가 꺼낸 직후 호출. 바로 힘을 주지 않고 요청만 저장
        public void Launch(Vector3 velocity)
        {
            launchVelocity = velocity;
            launchRequested = true;
        }

        private void FixedUpdate()
        {
            if (launchRequested)
            {
                launchRequested = false;
                body.AddForce(launchVelocity, ForceMode.VelocityChange);
                // 살짝 회전도 줘서 날아가는 느낌을 냄
                body.AddTorque(Random.insideUnitSphere * 3f, ForceMode.VelocityChange);
            }
        }

        #endregion

        private void Update()
        {
            noCutTime -= Time.deltaTime;

            // 과일 아래쪽이 바닥 위로 올라오면 바닥과 부딪히는 레이어로 바꿈
            if (gameObject.layer == risingLayerIndex && col.bounds.min.y > floorY + 0.02f)
                gameObject.layer = fruitLayerIndex;

            if (touchedFloor) // 바닥에 닿은 뒤부터 일정 시간 지나면 없애기
            {
                lifeTime -= Time.deltaTime;
                if (lifeTime <= 0f)
                {
                    Remove();
                    return;
                }
            }

            // 안전장치: 일정 높이 아래로 떨어지면 바로 없앰
            if (transform.position.y < killY) Remove();
        }

        #region 베기 상태 (SliceSystem이 호출)

        // 지금 벨 수 있는지 (방금 베였거나 너무 작으면 안 됨)
        public bool CanBeCut()
        {
            return noCutTime <= 0f && IsTooSmall() == false;
        }

        public bool IsTooSmall()
        {
            return Radius < minPieceSize;
        }

        // 베였을 때: 잠깐 다시 안 베이게 하고, 절단 횟수를 올리고, 베였다고 표시
        public void MarkCut(float delay)
        {
            noCutTime = delay;
            cutCount++;
            scored = true;
        }

        public void SetReserved(bool value)
        {
            reserved = value;
        }

        // 조각을 과일의 자식으로 숨겨둠. 자식이라 과일과 함께 움직임
        public void HidePieces(List<Fruit> pieces)
        {
            foreach (Fruit piece in pieces)
            {
                piece.transform.SetParent(transform, true);
                piece.gameObject.SetActive(false);
                if (hiddenPieces.Contains(piece) == false) hiddenPieces.Add(piece);
            }
        }

        // 조각을 만들 때 잘린 메시·머티리얼을 넣고 콜라이더를 메시에 맞춤
        public void SetMesh(Mesh mesh, Material[] materials)
        {
            meshFilter.sharedMesh = mesh;
            meshRenderer.sharedMaterials = materials;
            baseMaterials = materials;

            MeshCollider meshCol = col as MeshCollider;      // MeshCollider면
            if (meshCol != null)
            {
                meshCol.convex = true;                       // Rigidbody로 움직이려면 Convex 필수
                meshCol.sharedMesh = mesh;                   // 잘린 메시 모양 그대로 (여기서 쿠킹 비용 발생)
            }

            SphereCollider sphere = col as SphereCollider;   // SphereCollider면
            if (sphere != null) FitSphereToMesh(sphere, mesh);   // 메시 크기에 맞춘 구
        }

        // 조각을 만들 때 원래 과일 정보를 물려받음
        public void SetupAsPiece(Fruit from, float delay)
        {
            cutCount = from.cutCount + 1;
            crossSectionMat = from.crossSectionMat;
            juiceColor = from.juiceColor;   // 조각을 다시 베도 같은 색 과즙
            noCutTime = delay;   // 생긴 직후 잠깐은 다시 안 베임
        }

        #endregion

        #region 절단선 (SliceFeedback이 긋고, 과일이 지움)

        // 블레이드 타임 중 베였을 때 SliceFeedback이 호출
        // 오브젝트를 만들지 않고, 절단선 머티리얼을 두 번째 머티리얼로 붙인 뒤 자른 면 정보만 셰이더에 넘김
        public void AddSlashLine(Material lineMat, Vector3 point, Vector3 normal)
        {
            if (lineMat == null || slashCount >= MaxSlashLines) return;

            // 처음 그을 때만 머티리얼 추가 (원래 머티리얼 + 절단선 머티리얼)
            if (slashCount == 0)
            {
                Material[] withLine = new Material[baseMaterials.Length + 1];
                baseMaterials.CopyTo(withLine, 0);
                withLine[baseMaterials.Length] = lineMat;
                meshRenderer.sharedMaterials = withLine;
            }

            slashPlanes[slashCount] = ToLocalPlane(point, normal);
            slashCount++;

            // 절단선 머티리얼 칸(맨 뒤)에만 값을 넣음 → 원래 사과 머티리얼은 영향 없음
            slashBlock.SetVectorArray(SlashPlanesId, slashPlanes);
            slashBlock.SetFloat(SlashCountId, slashCount);
            meshRenderer.SetPropertyBlock(slashBlock, baseMaterials.Length);
        }

        // 과일이 사라지거나 풀에서 다시 꺼낼 때: 원래 머티리얼로 되돌림
        private void ClearSlashLines()
        {
            if (slashCount == 0) return;
            slashCount = 0;
            meshRenderer.sharedMaterials = baseMaterials;
        }

        #endregion

        #region 바닥 충돌 (LayerMask)

        private void OnCollisionEnter(Collision collision)
        {
            if (floorMask.Contains(collision.gameObject) == false) return;

            // 예약된 과일은 바닥에 닿는 순간 갈라짐 → 자르는 건 SliceSystem 담당
            if (reserved)
            {
                SliceSystem.Instance.ReleaseOne(this);
                return;
            }
            
            if (touchedFloor) return; // 처음 바닥에 닿은 순간만
            touchedFloor = true;   // 이때부터 Update에서 시간을 세고 Remove

            // 안 벤 과일이면 놓침 → 목숨은 GameManager 담당 (조각은 그냥 튕기고 구르다 사라짐)
            if (isPiece == false && scored == false)
            {
                GameManager.Instance.OnMiss();
            }
        }

        #endregion

        #region 없애기

        public void Remove()
        {
            if (removed) return;
            removed = true;

            // 예약 목록에 남아 있으면 빼줌 (안 빼면 재사용된 과일이 엉뚱하게 갈라짐)
            if (reserved) SliceSystem.Instance.Unreserve(this);
            reserved = false;
            foreach (Fruit piece in hiddenPieces) piece.Remove();
            hiddenPieces.Clear();
            ClearSlashLines();

            if (isPiece)
            {
                // 메시는 자를 때 새로 만든 것이라 직접 지워야 메모리가 안 센다고함
                Destroy(meshFilter.sharedMesh);
                Destroy(gameObject); // 조각은 재사용 x
            }
            else
            {
                ReturnToPool(); // 과일은 재사용
            }
        }

        public void ReturnToPool()
        {
            ObjectPoolManager.Instance.ReturnToPool(this);
        }

        #endregion

        #region 계산 함수

        // [수학] AI 작성
        // LayerMask(비트 묶음)에서 켜져 있는 첫 번째 레이어 번호를 구함 (gameObject.layer에는 번호를 넣어야 함)
        private static int ToLayerIndex(LayerMask mask)
        {
            int bits = mask.value;
            for (int i = 0; i < 32; i++)
            {
                if ((bits & (1 << i)) != 0) return i;
            }
            return 0;
        }

        // [수학] AI 작성
        // 월드 기준 자른 면(지나는 점 + 법선)을 과일 기준 좌표의 평면 (법선 xyz, 거리 w)으로 바꿈
        // 과일 기준으로 저장해야 과일이 돌며 날아가도 선이 표면에 붙어 다님 (과일 스케일은 세 축이 같다고 가정)
        private Vector4 ToLocalPlane(Vector3 point, Vector3 normal)
        {
            Vector3 localPoint = transform.InverseTransformPoint(point);
            Vector3 localNormal = transform.InverseTransformDirection(normal).normalized;
            return new Vector4(localNormal.x, localNormal.y, localNormal.z, Vector3.Dot(localNormal, localPoint));
        }

        // [수학] AI 작성
        // SphereCollider의 중심·반지름을 메시 크기에 맞춤
        private static void FitSphereToMesh(SphereCollider target, Mesh mesh)
        {
            target.center = mesh.bounds.center;
            target.radius = AverageSize(mesh);
        }

        // [수학] AI 작성
        // 메시의 대략적인 반지름 = 로컬 bounds 세 축 반지름의 평균
        private static float AverageSize(Mesh mesh)
        {
            Vector3 e = mesh.bounds.extents;
            return (e.x + e.y + e.z) / 3f;
        }

        // [수학] AI 작성
        // 세 축 크기 중 가장 큰 값
        private static float MaxScale(Vector3 scale)
        {
            return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        }

        #endregion
    }
}
