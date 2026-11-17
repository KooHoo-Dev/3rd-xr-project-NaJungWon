using UnityEngine;
using UnityEngine.SceneManagement;

namespace TimeSlasher
{
    // 과일의 물리 세계를 따로 작성. 그 세계의 시간만 직접 흘려준다는데 겁나신기하네
    public class FruitWorld : MonoBehaviour
    {
        public static FruitWorld Instance;

        [Header("과일 세계로 옮길 부모 (과일 풀, 조각, 바닥 콜라이더가 들어감)")]
        public Transform root;

        public Transform Root => root;

        // 칼 판정(Raycast, OverlapCapsule)은 이 물리 씬으로 해야 과일이 찾아집니다
        public PhysicsScene FruitPhysics { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else { Destroy(gameObject); return; }

            // 1. 물리 연산을 따로 하는 씬을 하나 만듭니다
            Scene fruitScene = SceneManager.CreateScene("FruitPhysics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            FruitPhysics = fruitScene.GetPhysicsScene();

            // 2. 과일 부모(FruitRoot)를 그 씬으로 옮깁니다
            //    (FruitRoot는 씬 최상위 오브젝트여야 옮길 수 있습니다)
            //    이후 이 아래에 생기는 건 전부 과일 세계 소속이 됩니다
            SceneManager.MoveGameObjectToScene(root.gameObject, fruitScene);
        }

        private void FixedUpdate()
        {
            // 3. 과일 세계를 "실제 시간 × 배율"만큼만 진행합니다 (평소 1배, 슬로우 0.25배)
            //    이 씬은 Unity가 자동으로 진행시키지 않아서, 여기서 불러야만 시간이 흐릅니다
            FruitPhysics.Simulate(Time.fixedDeltaTime * BladeTimeController.Instance.Scale);
        }
    }
}
