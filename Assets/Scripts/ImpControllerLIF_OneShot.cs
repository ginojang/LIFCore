using UnityEngine;
using Neuro.LIF;


public class ImpControllerLIF_OneShot : MonoBehaviour
{
    [Header("Target")]
    public Transform player;                // ����θ� "Player" �ڵ� �˻�

    public float deadZoneDeg = 5f;        // |e|<deadzone �̸� �Է� 0



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

        //
        // �ʱ�ȭ
        var net = new LIFNetwork(
            type: new[] { NeuronType.Sensory, NeuronType.Sensory, NeuronType.Motor, NeuronType.Motor },
            threshold: new[] { 0.40f, 0.40f, 0.65f, 0.65f },
            leak: new[] { 2.0f, 2.0f, 6.0f, 6.0f },
            synapseStartIndex: new[] { 0, 1, 2, 4 },
            synapseCount: new[] { 1, 1, 2, 2 },
            synapsePost: new[] { 2, 3, 3, 2, 2, 3 },
            synapseWeight: new[] { +0.9f, +0.9f, -0.8f, -0.3f, -0.8f, -0.3f }
        );
        var st = new LIFState(net.Count);
        var stats = new LIFTickStats();

        // �Է� ����
        st.ExternalInput[0] = L; // left
        st.ExternalInput[1] = R; // right

        // ���� 2�н�
        LIFStepper.StepTwoPassOneShot(net, st, dtMs: 10f, refractoryMs: 0f, ref stats);

        // ��� �б�
        float mL = st.MotorFiring[2];
        float mR = st.MotorFiring[3];


        Debug.Log($"Result One shot >>  mL:{mL} mR{mR}");


    }
}
