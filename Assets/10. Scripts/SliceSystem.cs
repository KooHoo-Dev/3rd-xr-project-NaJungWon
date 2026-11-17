using System;
using System.Collections.Generic;
using EzySlice;
using UnityEngine;

namespace TimeSlasher
{
    public class SliceSystem : MonoBehaviour
    {
        public static SliceSystem Instance;

        [Header("베기 설정")]
        public Fruit piecePrefab;               // 잘랐을 때 만들 조각 프리팹
        public int maxCutCount = 3;             // 한 과일 계열 최대 절단 횟수
        public float cutDelay = 0.5f;           // 베인 직후·생긴 직후 다시 안 베이는 시간
        public float pieceSpreadSpeed = 1.5f;   // 갈라진 조각이 양쪽으로 벌어지는 속도

        #region 베기 이벤트
        public event Action<CutInfo> OnCut;              // 벨 때마다
        public event Action<Vector3, Color> OnPiecesReleased;   // 예약된 과일이 실제로 갈라질 때 (과일마다, 위치·과즙 색)
        public event Action<int> OnBatchRelease;         // 블레이드 타임이 끝나 한꺼번에 갈라질 때 (한 번)
        #endregion

        // 블레이드 타임 중 베여서 일괄 절단을 기다리는 과일
        private List<Fruit> reserved = new List<Fruit>();

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        #region 베기 (Blade가 호출)

        public bool Cut(Fruit fruit, Vector3 point, Vector3 normal, Blade blade)
        {
            // 1. 벨 수 있는 상태인지 (방금 베였거나, 너무 많이 잘렸거나, 너무 작으면 안 베임)
            if (fruit.CanBeCut() == false) return false;
            if (fruit.CutCount >= maxCutCount) return false;

            bool isSlow = BladeTimeController.Instance.IsSlow;

            // 2. 실제로 자르기 (평면이 안 지나가면 실패 → 아무 일도 없음)
            List<Fruit> pieces = null;
            if (isSlow)
            {
                // 블레이드 타임 중: 잘라서 숨겨둠. 과일은 그대로 날아감
                if (CutHidden(fruit, point, normal) == false) return false;
            }
            else
            {
                // 평상시: 바로 잘라서 조각 2개를 만듦
                pieces = SliceObject(fruit, point, normal);
                if (pieces == null) return false;
            }

            // 3. 과일 상태 갱신
            bool firstCut = (fruit.isPiece == false && fruit.IsScored == false);
            fruit.MarkCut(cutDelay);
            if (isSlow && fruit.IsReserved == false)
            {
                fruit.SetReserved(true);
                reserved.Add(fruit);
            }

            // 4. 베었다고 알림 (과일이 사라지기 전에)
            CutInfo info = new CutInfo();
            info.Position = point;
            info.Normal = normal;
            info.IsFirstCut = firstCut;
            info.IsReserved = isSlow;
            info.Fruit = fruit;
            info.Blade = blade;
            OnCut?.Invoke(info);

            // 5. 평상시면 바로 조각을 보여주고 원래 과일은 없앰
            if (isSlow == false)
            {
                ShowPieces(fruit, pieces);
                fruit.Remove();   // 과일이면 풀로, 조각이면 삭제
            }

            return true;
        }

        // 블레이드 타임 중 미리 자르기
        // - 처음이면 과일 자체를 잘라 조각 2개를 숨겨둠
        // - 이미 숨겨둔 조각이 있으면 그 조각들을 하나씩 다시 자름
        private bool CutHidden(Fruit fruit, Vector3 point, Vector3 normal)
        {
            if (fruit.HiddenPieces.Count == 0)
            {
                List<Fruit> firstPieces = SliceObject(fruit, point, normal);
                if (firstPieces == null) return false;
                fruit.HidePieces(firstPieces);
                return true;
            }

            bool anyCut = false;
            List<Fruit> nextHidden = new List<Fruit>();
            foreach (Fruit piece in fruit.HiddenPieces)
            {
                // 너무 작은 조각이나 평면이 안 지나가는 조각은 그대로 둠
                List<Fruit> newPieces = piece.IsTooSmall() ? null : SliceObject(piece, point, normal);
                if (newPieces == null)
                {
                    nextHidden.Add(piece);
                    continue;
                }

                nextHidden.AddRange(newPieces);
                piece.Remove();   // 다시 잘린 조각은 삭제
                anyCut = true;
            }

            fruit.HiddenPieces.Clear();
            fruit.HidePieces(nextHidden);
            return anyCut;
        }

        #endregion

        #region 일괄 절단 (블레이드 타임 끝)

        // BladeTimeController가 블레이드 타임이 끝나는 순간 호출: 같은 프레임에 전부 갈라짐
        public void ReleaseAll()
        {
            // Release 도중 목록이 바뀔 수 있으므로 복사본으로 돌림
            List<Fruit> list = new List<Fruit>(reserved);
            reserved.Clear();

            int count = 0;
            foreach (Fruit fruit in list)
            {
                // 그 사이 사라졌거나 풀에서 다른 과일로 재사용됐으면 건너뜀
                if (fruit == null || fruit.IsReserved == false) continue;
                Release(fruit);
                count++;
            }

            if (count > 0) OnBatchRelease?.Invoke(count);
        }

        // Fruit가 호출: 예약된 과일이 바닥에 닿으면 그 과일만 먼저 갈라짐
        public void ReleaseOne(Fruit fruit)
        {
            reserved.Remove(fruit);
            Release(fruit);
        }

        // 과일이 예약된 채로 사라질 때 Fruit가 호출 (안 빼면 재사용된 과일이 엉뚱하게 갈라짐)
        public void Unreserve(Fruit fruit)
        {
            reserved.Remove(fruit);
        }

        // 게임 시작·게임 오버 때 GameManager가 호출 (일괄 절단 연출 없이 목록만 비움)
        public void ClearReserved()
        {
            reserved.Clear();
        }

        private void Release(Fruit fruit)
        {
            Vector3 position = fruit.Center;
            Color juiceColor = fruit.juiceColor;   // 과일이 풀로 돌아가기 전에 색을 기억
            ShowPieces(fruit, fruit.HiddenPieces);
            fruit.HiddenPieces.Clear();   // 보여준 조각은 이제 따로 움직이므로 목록에서 뺌
            fruit.SetReserved(false);
            fruit.Remove();
            OnPiecesReleased?.Invoke(position, juiceColor);
        }

        #endregion

        #region 조각 보이기

        private void ShowPieces(Fruit fruit, List<Fruit> pieces)
        {
            foreach (Fruit piece in pieces)
            {
                piece.transform.SetParent(FruitWorld.Instance.Root, true);   // 과일 세계 안에 그대로 둠
                piece.gameObject.SetActive(true);

                // 원래 과일이 날아가던 속도를 이어받고,
                // 과일 중심에서 조각 쪽으로 밀어서 양쪽으로 벌어지게 함
                Vector3 away = (piece.Center - fruit.Center).normalized;
                piece.Body.linearVelocity = fruit.Body.linearVelocity;
                piece.Body.AddForce(away * pieceSpreadSpeed, ForceMode.VelocityChange);
            }
        }

        #endregion

        #region 메시 절단 (EzySlice)

        // [수학] AI 작성
        // EzySlice로 target의 메시를 평면으로 잘라 조각 2개를 만들어 돌려줌 (꺼진 상태)
        // 평면이 메시를 안 지나가면 null
        private List<Fruit> SliceObject(Fruit target, Vector3 point, Vector3 normal)
        {
            SlicedHull hull = target.gameObject.Slice(point, normal, target.crossSectionMat);
            if (hull == null || hull.upperHull == null || hull.lowerHull == null) return null;

            // 원래 머티리얼 + 단면 머티리얼 (EzySlice가 단면을 마지막 서브메시로 붙여줌)
            // 절단선 머티리얼은 빼고 넘김 (머티리얼 개수 = 서브메시 개수여야 하고, 조각엔 절단선이 필요 없음)
            Material[] materials = GetPieceMaterials(target.BaseMaterials, target.crossSectionMat);

            List<Fruit> pieces = new List<Fruit>();
            pieces.Add(CreatePiece(target, hull.upperHull, materials));
            pieces.Add(CreatePiece(target, hull.lowerHull, materials));
            return pieces;
        }

        private Fruit CreatePiece(Fruit target, Mesh mesh, Material[] materials)
        {
            // 원래 물체와 같은 자리·회전·크기로 조각을 만들고, 일단 꺼둠
            Fruit piece = Instantiate(piecePrefab, target.transform.position, target.transform.rotation,
                FruitWorld.Instance.Root);
            piece.gameObject.SetActive(false);
            piece.transform.localScale = target.transform.lossyScale;

            // 메시·머티리얼·콜라이더 넣기
            piece.SetMesh(mesh, materials);

            // 상태 초기화 후, 조각도 다시 벨 수 있도록 원래 과일 정보를 물려줌
            piece.Init();
            piece.SetupAsPiece(target, cutDelay);
            return piece;
        }

        // [수학] AI 작성
        // 단면 머티리얼이 아직 없으면 맨 뒤에 붙인 배열을 돌려줌
        private static Material[] GetPieceMaterials(Material[] original, Material crossSectionMat)
        {
            foreach (Material mat in original)
            {
                if (mat == crossSectionMat) return original;
            }

            Material[] result = new Material[original.Length + 1];
            original.CopyTo(result, 0);
            result[original.Length] = crossSectionMat;
            return result;
        }

        #endregion
    }
}
