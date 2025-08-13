using UnityEngine;

public class LIFQuickDemo : MonoBehaviour
{
    void Start()
    {
        var m = LIFManager.Instance;

        // ���� 2���� ���Ҵ�: 0(Sensory) -> 1(Motor)
        m.Reallocate(2);
        m.SetNeuronType(0, NeuronType.Sensory);
        m.SetNeuronType(1, NeuronType.Motor);

        // �Ӱ谪/���� ���ݸ� ƪ
        m.State.threshold[0] = 1.0f;
        m.State.threshold[1] = 1.0f;
        m.State.leak[0] = 0.0f;  // ���� ����
        m.State.leak[1] = 0.0f;  // ���� ����

        // �ó���: 0 -> 1 (����ġ 1.2f)
        m.BuildSynapsesFromEdges(new (int pre, int post, float w)[] {
            (0, 1, 1.2f)
        });

        // �� ���ܿ� 1ms��, �� 5���� �������� ��������
        for (int step = 0; step < 5; step++)
        {
            // 0�� ������ �����Է� ���� (�Ӱ� 1.0 �ѱ�� 1.2)
            m.AddSensoryInput(0, 1.2f);
            m.StepOnce(1.0f); // dt=1ms �� ����

            float p0 = m.State.potential[0];
            float p1 = m.State.potential[1];
            float fire1 = m.ReadMotorFiring(1, true);
            Debug.Log($"[step {step}] p0={p0:F2}, p1={p1:F2}, motor(1) fired={fire1}");
        }
    }
}
