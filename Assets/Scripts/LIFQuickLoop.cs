// LIFQuickLoop.cs (�����)
using UnityEngine;

public class LIFQuickLoop : MonoBehaviour
{
    public LIFState state;
    public LIFMiniHud hud;
    public float dtMs = 10f;
    public float refractoryMs = 5f;

    private LIFTickStats stats;
    private bool[] spiked;


    void Start()
    {
        int n = state?.potential?.Length ?? 0;
        spiked = new bool[n];
        hud.spikedThisTick = spiked; // HUD�� ����
        hud.state = state;
    }

    void Update()
    {
        // ����: ���� �Է� �޽�(����)
        // state.externalInput[0] = 1.2f; // �ʿ� ��

        LIFStepCpu.StepWithFlags(state, state.potential.Length, dtMs, refractoryMs, ref stats, spiked);

        // �ܺ� ��å: �޽����̸� ���� �Է��� ���⼭ Ŭ����
        // System.Array.Clear(state.externalInput, 0, state.externalInput.Length);

        // HUD�� ��� ����
        hud.stats = stats;

        // motorFiring ����(������ ����): �����Ӵ� ��¦ �ٿ� ����ġ ��� ������ũ�� �ٻ�
        for (int i = 0; i < state.motorFiring.Length; i++)
            state.motorFiring[i] = Mathf.Max(0f, state.motorFiring[i] - 0.05f);
    }
}
