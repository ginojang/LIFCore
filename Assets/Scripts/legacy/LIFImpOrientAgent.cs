// LIFImpOrientAgent.cs
using UnityEngine;


namespace Legacy
{
    public class LIFImpOrientAgent : MonoBehaviour
    {
        [Header("Targets")]
        public Transform player;              // ArenaBuilder���� Player.transform �Ҵ����൵ �ǰ�,
                                              // �� ã���� Start���� �ڵ� �˻� �õ�.

        [Header("Turn Control")]
        public float turnSpeedDeg = 240f;   // ���� = 1.0�� ���� �ʴ� ȸ���ӵ�
        public float deadZoneDeg = 2f;      // �ʹ� ���� ������ ����
        public float angleNormDeg = 45f;    // |����|�� �� ���� �� �Է� 1.0�� ����ȭ
        public float inputGain = 1.2f;      // ���� ���� ����

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

            // �׷��� ������ ����(���� ���� ����)
            if (mgr.State == null || mgr.N < 4)
                ports = LIFGraphBuilder_Orient.Build(mgr);
            else
                ports = new LIFOrientPorts { S_Left = 0, S_Right = 1, M_TurnL = 2, M_TurnR = 3 }; // ���� ��ġ ����

            // �÷��̾� �ڵ� Ž��(������)
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

        // LIF ���� ������ ���� ����
        void PreStepInject(LIFState s, float dtMs)
        {
            if (player == null || s == null) return;

            // XZ ����� �����ִ� �������� (-180..+180)
            Vector3 a = transform.forward; a.y = 0f; a.Normalize();
            Vector3 b = (player.position - transform.position); b.y = 0f; b.Normalize();


            if (b.sqrMagnitude < 1e-6f) return;

            float ang = Vector3.SignedAngle(a, b, Vector3.up);   // +: ��ȸ�� �ʿ�, -: ��ȸ�� �ʿ�


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