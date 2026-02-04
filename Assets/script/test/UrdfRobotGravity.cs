using UnityEngine;

namespace Unity.Robotics.UrdfImporter
{
    public class UrdfRobotGravity : MonoBehaviour
    {
        [Header("Gravity")]
        [SerializeField] private bool gravityEnabled = true;

        [Tooltip("Include inactive children when applying gravity.")]
        [SerializeField] private bool includeInactive = true;

        [Tooltip("Also apply gravity to Rigidbody (not only ArticulationBody).")]
        [SerializeField] private bool affectRigidbodies = true;

        private ArticulationBody[] cachedArticulationBodies;
        private Rigidbody[] cachedRigidbodies;

        private void Awake()
        {
            CacheBodies();
        }

        private void OnEnable()
        {
            // 確保啟用物件時狀態一致（避免看起來沒生效）
            ApplyGravity(gravityEnabled);
        }

        private void Start()
        {
            // 有些 URDF importer 會在 Start 後才補齊某些 component
            CacheBodies();
            ApplyGravity(gravityEnabled);
        }

        private void OnTransformChildrenChanged()
        {
            // 若 runtime 會新增/啟用 link（例如換工具、載入模組），可自動更新
            CacheBodies();
            ApplyGravity(gravityEnabled);
        }

        private void CacheBodies()
        {
            cachedArticulationBodies = GetComponentsInChildren<ArticulationBody>(includeInactive);
            if (affectRigidbodies)
                cachedRigidbodies = GetComponentsInChildren<Rigidbody>(includeInactive);
        }

        /// <summary>在 Inspector Button 或 UI Button 綁這個</summary>
        public void ToggleGravity()
        {
            gravityEnabled = !gravityEnabled;
            ApplyGravity(gravityEnabled);
        }

        /// <summary>強制設定重力狀態（推薦給程式呼叫）</summary>
        public void SetGravity(bool enabled)
        {
            gravityEnabled = enabled;
            ApplyGravity(gravityEnabled);
        }

        private void ApplyGravity(bool enabled)
        {
            if (cachedArticulationBodies == null || cachedArticulationBodies.Length == 0)
                cachedArticulationBodies = GetComponentsInChildren<ArticulationBody>(includeInactive);

            foreach (var ab in cachedArticulationBodies)
            {
                if (ab == null) continue;
                ab.useGravity = enabled;
                ab.WakeUp(); // 避免切換後看起來沒變
            }

            if (!affectRigidbodies) return;

            if (cachedRigidbodies == null || cachedRigidbodies.Length == 0)
                cachedRigidbodies = GetComponentsInChildren<Rigidbody>(includeInactive);

            foreach (var rb in cachedRigidbodies)
            {
                if (rb == null) continue;
                rb.useGravity = enabled;
                rb.WakeUp();
            }
        }
    }
}
