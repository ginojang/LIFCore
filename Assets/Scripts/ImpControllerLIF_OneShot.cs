using UnityEngine;


namespace Legacy
{
    /// <summary>
    /// ����׿�: LIF�� Start()���� "�� �� ��"�� ������
    /// �÷��̾ �������� ��/�� �����ϰ�(2S->2M),
    /// �α� ��� + (�ɼ�) �� ���� ȸ�� ����.
    /// </summary>
    public class ImpControllerLIF_OneShot : MonoBehaviour
    {
        [Header("Target")]
        public Transform player;                // ����θ� "Player" �ڵ� �˻�

        [Header("Angle �� Input (once)")]
        public float deadZoneDeg = 5f;        // |e|<deadzone �̸� �Է� 0
        public float angleNormDeg = 60f;       // |e|=angleNorm�� �� �Է� 1.0
        public float inputGain = 0.8f;      // ���� �Է� ����

        [Header("One-shot rotate (optional)")]
        public bool applyOneShotRotation = true;
        public float rotateClampDeg = 30f; // �� ���� ���� �ִ� ����

        [Header("LIF step params")]
        public float dtMs = 10f;
        public float refractoryMs = 0f;

        // ���� LIF
        LIFState s;
        LIFTickStats stats;

        void Start()
        {
            // 0) Ÿ�� ã��
            if (player == null)
            {
                var p = GameObject.Find("Player");
                if (p != null) player = p.transform;
            }
            if (player == null)
            {
                Debug.LogWarning("[OneShotLIF] Player not found.");
                enabled = false; return;
            }

            // 1) ���� ���� ��� (XZ ���)
            Vector3 a = transform.forward; a.y = 0f; a.Normalize();
            Vector3 b = (player.position - transform.position); b.y = 0f; b.Normalize();
            if (b.sqrMagnitude < 1e-6f) { Debug.Log("[OneShotLIF] Player too close."); enabled = false; return; }

            float e = Vector3.SignedAngle(a, b, Vector3.up); // +: ����(CCW), -: ������(CW)

            // 2) e �� (Left, Right) �Է� ����
            float L = 0f, R = 0f;
            if (Mathf.Abs(e) >= deadZoneDeg)
            {
                if (e < 0)
                    L = 1.0f;
                else
                    R = 1.0f;
            }
            else
            {
                return;
            }

            // 3) �ʼ��� LIF �׷���(2S��2M) ����
            //    index: 0=S_Left, 1=S_Right, 2=M_TurnL, 3=M_TurnR
            s = new LIFState
            {
                potential = new float[4],
                threshold = new float[4],
                leak = new float[4],
                refractory = new float[4],
                type = new NeuronType[4],
                synStartIndex = new int[4],
                synCount = new int[4],
                synPost = new int[6],   // S->M 2, M<->M ���� 2, M �ڱ���� 2
                synWeight = new float[6],
                externalInput = new float[4],
                motorFiring = new float[4],
            };

            s.type[0] = NeuronType.Sensory; s.type[1] = NeuronType.Sensory;
            s.type[2] = NeuronType.Motor; s.type[3] = NeuronType.Motor;

            // �� �����ϰ� + ���� ����
            s.threshold[0] = 0.40f; s.threshold[1] = 0.40f; // S
            s.threshold[2] = 0.65f; s.threshold[3] = 0.65f; // M
            s.leak[0] = 2.0f; s.leak[1] = 2.0f;             // ���� �ϸ�
            s.leak[2] = 6.0f; s.leak[3] = 6.0f;             // ���� ���� ����

            int c = 0;
            // S_Left -> M_TurnL
            s.synStartIndex[0] = c;
            s.synPost[c] = 2; s.synWeight[c] = +0.9f; c++; s.synCount[0] = 1;
            // S_Right -> M_TurnR
            s.synStartIndex[1] = c;
            s.synPost[c] = 3; s.synWeight[c] = +0.9f; c++; s.synCount[1] = 1;
            // M_TurnL -> [M_TurnR inhibit, self-inhibit]
            s.synStartIndex[2] = c;
            s.synPost[c] = 3; s.synWeight[c] = -0.8f; c++;
            s.synPost[c] = 2; s.synWeight[c] = -0.3f; c++;
            s.synCount[2] = 2;
            // M_TurnR -> [M_TurnL inhibit, self-inhibit]
            s.synStartIndex[3] = c;
            s.synPost[c] = 2; s.synWeight[c] = -0.8f; c++;
            s.synPost[c] = 3; s.synWeight[c] = -0.3f; c++;
            s.synCount[3] = 2;

            // 4) "�� ����" ���� + ���� 2ȸ(����������)
            s.externalInput[0] = L;
            s.externalInput[1] = R;

            // s.externalInput[0] = L;  s.externalInput[1] = R; // (����)
            var stats = new LIFTickStats();

            LIFStepCpu.StepTwoPassOneShot(s, 4, dtMs, refractoryMs, ref stats,
                clearExternalAfterFirst: true,
                spikedFirst: null,
                spikedSecond: null);

            // ���⼭ s.motorFiring[2], s.motorFiring[3] ������ ���� ��/�� ���� �Ϸ�


            // ��� �б�
            float mL = s.motorFiring[2];
            float mR = s.motorFiring[3];
            float potL = s.potential[2];
            float potR = s.potential[3];

            Debug.Log($"[OneShotLIF] e={e:F1}��, L_in={L:F2}, R_in={R:F2} | " +
                      $"M_L fire={mL}, M_R fire={mR} | potL={potL:F2}, potR={potR:F2}");

            // 5) (�ɼ�) �� ���� ȸ�� ����

            if (applyOneShotRotation)
            {
                float turnSign = Mathf.Sign((mL - mR) != 0 ? (mL - mR) : (potL - potR));
                float angle = Mathf.Clamp(e, -rotateClampDeg, rotateClampDeg); // �������� �ʰ�
                if (turnSign != 0f)
                    transform.Rotate(0f, angle * turnSign, 0f, Space.World);
            }

            // e�� SignedAngle(forward, toPlayer, up)
            //transform.Rotate(0f, e, 0f, Space.World);   // => ��ٷ� �÷��̾� ����

            // ���� ���� ����
            enabled = false;
        }
    }

}