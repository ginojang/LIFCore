// LIFImpOrientAgent.cs
using UnityEngine;


namespace Legacy
{
    public class LIFImpOrientAgent : MonoBehaviour
    {
        [Header("Targets")]
        public Transform player;              // ArenaBuilder에서 Player.transform 할당해줘도 되고,
                                              // 못 찾으면 Start에서 자동 검색 시도.

        [Header("Turn Control")]
        public float turnSpeedDeg = 240f;   // 모터 = 1.0일 때의 초당 회전속도
        public float deadZoneDeg = 2f;      // 너무 작은 오차는 무시
        public float angleNormDeg = 45f;    // |각도|가 이 값일 때 입력 1.0로 정규화
        public float inputGain = 1.2f;      // 센서 주입 게인

        private LIFOrientPorts ports;
        private LIFStateManager mgr;

        void Awake()
        {
            mgr = LIFStateManager.I;
            if (mgr == null)
            {
                Debug.LogError("[ImpOrient] LIFStateManager not found.");
                enabled = false; return;
            }

            // 그래프 없으면 빌드(단일 임프 가정)
            if (mgr.State == null || mgr.N < 4)
                ports = LIFGraphBuilder_Orient.Build(mgr);
            else
                ports = new LIFOrientPorts { S_Left = 0, S_Right = 1, M_TurnL = 2, M_TurnR = 3 }; // 동일 배치 가정

            // 플레이어 자동 탐색(없으면)
            if (player == null)
            {
                var p = GameObject.Find("Player");
                if (p) player = p.transform;
            }
        }

        void OnEnable()
        {
            if (mgr != null) mgr.OnBeforeStep += PreStepInject;
        }
        void OnDisable()
        {
            if (mgr != null) mgr.OnBeforeStep -= PreStepInject;
        }

        // LIF 스텝 직전에 센서 주입
        void PreStepInject(LIFState s, float dtMs)
        {
            if (player == null || s == null) return;

            // XZ 평면의 서명있는 각도오차 (-180..+180)
            Vector3 a = transform.forward; a.y = 0f; a.Normalize();
            Vector3 b = (player.position - transform.position); b.y = 0f; b.Normalize();


            if (b.sqrMagnitude < 1e-6f) return;

            float ang = Vector3.SignedAngle(a, b, Vector3.up);   // +: 우회전 필요, -: 좌회전 필요


            if (Mathf.Abs(ang) < deadZoneDeg) return;

            float mag = Mathf.Clamp01(Mathf.Abs(ang) / Mathf.Max(1e-3f, angleNormDeg)); // 0..1

            if (ang > 0f) s.externalInput[ports.S_Right] += mag * inputGain;
            else s.externalInput[ports.S_Left] += mag * inputGain;
        }

        void Update()
        {
            if (mgr == null || mgr.State == null) return;
            var mf = mgr.State.motorFiring;
            if (mf == null || mf.Length <= ports.M_TurnR) return;

            float turn = Mathf.Clamp(mf[ports.M_TurnR] - mf[ports.M_TurnL], -1f, 1f);
            if (Mathf.Abs(turn) > 1e-4f)
                transform.Rotate(0f, turn * turnSpeedDeg * Time.deltaTime, 0f, Space.World);
        }
    }


}