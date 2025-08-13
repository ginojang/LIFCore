// 공통 타입
public enum NeuronType : byte { Sensory, Inter, Motor }

// ★ WebGL에선 SoA(Struct-of-Arrays)가 유리
public sealed class LIFState
{
    public float[] potential;
    public float[] threshold;
    public float[] leak;
    public float[] refractory;
    public NeuronType[] type;

    // 시냅스는 pre 뉴런 기준으로 연속 배치
    public int[] synStartIndex;   // 각 pre의 시작 인덱스
    public int[] synCount;        // 각 pre의 개수
    public int[] synPost;         // synapses[k].post
    public float[] synWeight;     // synapses[k].weight

    public float[] externalInput; // 감각 입력
    public float[] motorFiring;   // 출력 카운트(윈도우 밖에서 Δt로 rate화)
}

// 네이티브에선 Burst + Jobs, WebGL에선 이 함수로 대체
public static class LIFStepCpu
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static float Clamp01Minus(float x) => x < 0f ? 0f : (x > 1f ? 0f : (1f - x));

    public static void Step(LIFState s, int count, float dtMs, float refractoryMs)
    {
        // 경계 길이 캐싱 (JIT 없지만 IL2CPP에서도 분기/로드 감소에 유리)
        var pot = s.potential; var thr = s.threshold; var leak = s.leak;
        var refc = s.refractory; var typ = s.type;
        var start = s.synStartIndex; var scount = s.synCount;
        var post = s.synPost; var w = s.synWeight;
        var ext = s.externalInput; var motor = s.motorFiring;

        // 1차 패스: 상태 업데이트 + 발화 체크
        for (int i = 0; i < count; i++)
        {
            float p = pot[i];
            // 누수
            p *= Clamp01Minus(leak[i]); // (1 - leak) 범위 고정

            // 외부 입력(감각)
            if (typ[i] == NeuronType.Sensory)
                p += ext[i];

            // 휴지기
            float r = refc[i];
            if (r > 0f)
            {
                r -= dtMs;
                refc[i] = r > 0f ? r : 0f;
                pot[i] = p; // 조기 종료
                continue;
            }

            // 발화
            bool fired = (p >= thr[i]);
            if (fired)
            {
                p = 0f;
                refc[i] = refractoryMs;

                // 시냅스 즉시 전파 (MVP: 지연큐 없음)
                int st = start[i];
                int ct = scount[i];
                int end = st + ct;
                for (int k = st; k < end; k++)
                {
                    int j = post[k];
                    pot[j] = pot[j] + w[k];
                }

                if (typ[i] == NeuronType.Motor)
                    motor[i] = motor[i] + 1f;
            }

            pot[i] = p;
        }
    }
}
