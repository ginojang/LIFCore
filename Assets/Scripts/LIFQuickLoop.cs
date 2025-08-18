// LIFQuickLoop.cs (데모용)
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
        hud.spikedThisTick = spiked; // HUD에 공유
        hud.state = state;
    }

    void Update()
    {
        // 예시: 감각 입력 펄스(임의)
        // state.externalInput[0] = 1.2f; // 필요 시

        LIFStepCpu.StepWithFlags(state, state.potential.Length, dtMs, refractoryMs, ref stats, spiked);

        // 외부 정책: 펄스형이면 감각 입력을 여기서 클리어
        // System.Array.Clear(state.externalInput, 0, state.externalInput.Length);

        // HUD로 통계 전달
        hud.stats = stats;

        // motorFiring 감쇠(윈도우 느낌): 프레임당 살짝 줄여 지속치 대신 스파이크율 근사
        for (int i = 0; i < state.motorFiring.Length; i++)
            state.motorFiring[i] = Mathf.Max(0f, state.motorFiring[i] - 0.05f);
    }
}
