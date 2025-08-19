// LIFGraphBuilder_Orient.cs
using UnityEngine;

public struct LIFOrientPorts { public int S_Left, S_Right, M_TurnL, M_TurnR; }

public static class LIFGraphBuilder_Orient
{
    public static LIFOrientPorts Build(LIFStateManager mgr)
    {
        int nS = 2, nM = 2, N = nS + nM;
        var s = new LIFState
        {
            potential = new float[N],
            threshold = new float[N],
            leak = new float[N],
            refractory = new float[N],
            type = new NeuronType[N],

            synStartIndex = new int[N],
            synCount = new int[N],
            synPost = new int[4],   // S->M 2개 + M<->M 억제 2개 = 4
            synWeight = new float[4],

            externalInput = new float[N],
            motorFiring = new float[N],
        };

        // 타입

        s.type[0] = NeuronType.Sensory; // S_Left
        s.type[1] = NeuronType.Sensory; // S_Right
        s.type[2] = NeuronType.Motor;   // M_TurnL
        s.type[3] = NeuronType.Motor;   // M_TurnR

        // 임계/누수
        for (int i = 0; i < N; i++) { s.leak[i] = 0f; s.refractory[i] = 0f; s.threshold[i] = 1f; }
        s.threshold[0] = 0.3f; s.threshold[1] = 0.3f;  // S
        s.threshold[2] = 0.5f; s.threshold[3] = 0.5f;  // M

        // CSR 채우기
        int cur = 0;

        // pre 0: S_Left -> M_TurnL
        s.synStartIndex[0] = cur;
        s.synPost[cur] = 2; s.synWeight[cur] = +1.0f; cur++;
        s.synCount[0] = 1;

        // pre 1: S_Right -> M_TurnR
        s.synStartIndex[1] = cur;
        s.synPost[cur] = 3; s.synWeight[cur] = +1.0f; cur++;
        s.synCount[1] = 1;

        // pre 2: M_TurnL -> M_TurnR (억제)
        s.synStartIndex[2] = cur;
        s.synPost[cur] = 3; s.synWeight[cur] = -0.6f; cur++;
        s.synCount[2] = 1;

        // pre 3: M_TurnR -> M_TurnL (억제)
        s.synStartIndex[3] = cur;
        s.synPost[cur] = 2; s.synWeight[cur] = -0.6f; cur++;
        s.synCount[3] = 1;

        mgr.OverrideState(s);

        return new LIFOrientPorts { S_Left = 0, S_Right = 1, M_TurnL = 2, M_TurnR = 3 };
    }
}
