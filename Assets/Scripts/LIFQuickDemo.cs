using UnityEngine;

public class LIFQuickDemo : MonoBehaviour
{
    void Start()
    {
        var m = LIFManager.Instance;

        // 뉴런 2개로 재할당: 0(Sensory) -> 1(Motor)
        m.Reallocate(2);
        m.SetNeuronType(0, NeuronType.Sensory);
        m.SetNeuronType(1, NeuronType.Motor);

        // 임계값/누수 조금만 튠
        m.State.threshold[0] = 1.0f;
        m.State.threshold[1] = 1.0f;
        m.State.leak[0] = 0.0f;  // 누수 없음
        m.State.leak[1] = 0.0f;  // 누수 없음

        // 시냅스: 0 -> 1 (가중치 1.2f)
        m.BuildSynapsesFromEdges(new (int pre, int post, float w)[] {
            (0, 1, 1.2f)
        });

        // 한 스텝에 1ms씩, 총 5스텝 수동으로 돌려보기
        for (int step = 0; step < 5; step++)
        {
            // 0번 뉴런에 감각입력 주입 (임계 1.0 넘기게 1.2)
            m.AddSensoryInput(0, 1.2f);
            m.StepOnce(1.0f); // dt=1ms 한 스텝

            float p0 = m.State.potential[0];
            float p1 = m.State.potential[1];
            float fire1 = m.ReadMotorFiring(1, true);
            Debug.Log($"[step {step}] p0={p0:F2}, p1={p1:F2}, motor(1) fired={fire1}");
        }
    }
}
