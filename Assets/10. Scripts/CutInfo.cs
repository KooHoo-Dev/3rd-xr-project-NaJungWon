using UnityEngine;

namespace TimeSlasher
{
    public class CutInfo
    {
        public Vector3 Position;      // 벤 위치
        public Vector3 Normal;        // 자른 면 방향
        public bool IsFirstCut;       // 과일을 처음 벤 것 (점수·게이지 대상)
        public bool IsReserved;       // 블레이드 타임 중이라 예약됨 (점수 ×2, 절단선 표시)
        public Fruit Fruit;           // 벤 과일. 평상시엔 이벤트 직후 사라지므로 저장해 두고 쓰면 안 됨
        public Blade Blade;           // 벤 칼 (진동할 손)
    }
}
