// ==========================================================
// LIF Core Types
// ==========================================================

/// <summary>
/// 뉴런의 역할 타입(메모리 절약을 위해 byte).
/// </summary>
public enum NeuronType : byte
{
    /// <summary>외부 자극을 받아들이는 감각 뉴런</summary>
    Sensory,
    /// <summary>중계/가공을 담당하는 인터뉴런</summary>
    Inter,
    /// <summary>행동/출력을 담당하는 운동 뉴런</summary>
    Motor,
}

// ==========================================================
// LIFState (SoA: Struct-of-Arrays)
//  - WebGL/IL2CPP 친화적 레이아웃
//  - 실행 로직은 별도 시뮬레이터(LIFStepCpu/LIFSim 등)가 담당
//
// Invariants:
//  - 모든 1D 배열 길이는 N(뉴런 수)와 동일해야 함(단, synPost/synWeight는 시냅스 총수).
//  - synStartIndex.Length == synCount.Length == N
//  - 각 i에 대해 0 <= synStartIndex[i] <= synPost.Length 이며,
//    synStartIndex[i] + synCount[i] <= synPost.Length.
//  - type[i]에 따라 의미가 달라질 수 있음(예: externalInput은 보통 Sensory에서만 사용).
//  - 권장 단위: 시간(ms), leak은 [1/s] 또는 tick 감쇠계수, potential/threshold는 동일 스케일.
// ==========================================================

/// <summary>
/// Leaky Integrate-and-Fire 네트워크의 "현재 상태" 컨테이너.
/// SoA 레이아웃으로 캐시 효율을 극대화했다.
/// </summary>
public sealed class LIFState
{
    #region Neuron State (size: N)

    /// <summary>
    /// 막전위 V(t).
    /// 외부 입력/시냅스 가중치/누수/불응기에 의해 매 tick 갱신된다.
    /// </summary>
    public float[] potential;

    /// <summary>
    /// 발화 임계치 θ.
    /// potential[i] ≥ threshold[i]이면 spike 발생.
    /// </summary>
    public float[] threshold;

    /// <summary>
    /// 누수 계수.
    /// (연속형 모델이라면 dv/dt = -leak*v + I 에서의 leak에 해당)
    /// </summary>
    public float[] leak;

    /// <summary>
    /// 남은 불응기(단위: ms).
    /// 0보다 크면 발화/적분을 건너뛰고 카운트다운한다.
    /// </summary>
    public float[] refractory;

    /// <summary>
    /// 뉴런 역할 타입(Sensory/Inter/Motor).
    /// 업데이트 시 타입별로 규칙을 달리 적용할 수 있다.
    /// </summary>
    public NeuronType[] type;

    #endregion

    // ---------------------------------------------------------------------

    #region Synapse Graph (Compressed Adjacency / CSR-like)

    /// <summary>
    /// pre 뉴런 i의 시냅스 리스트가 synPost/synWeight에서 시작하는 오프셋.
    /// </summary>
    public int[] synStartIndex;

    /// <summary>
    /// pre 뉴런 i가 갖는 시냅스 개수.
    /// 유효 범위: [synStartIndex[i], synStartIndex[i] + synCount[i]).
    /// synStartIndex/synCount/synPost/synWeight 조합은 희소 그래프(CSR) 표현이다.
    /// </summary>
    public int[] synCount;

    /// <summary>
    /// k번째 시냅스의 post(도착) 뉴런 인덱스. 길이는 시냅스 총수.
    /// </summary>
    public int[] synPost;

    /// <summary>
    /// k번째 시냅스의 가중치(양수: 흥분, 음수: 억제). synPost와 동일 길이.
    /// </summary>
    public float[] synWeight;

    #endregion

    // ---------------------------------------------------------------------

    #region I/O Buffers (size: N)

    /// <summary>
    /// 외부 자극(감각 입력) 버퍼.
    /// 보통 Sensory 뉴런에서만 사용하며, 펄스형 입력이면 tick 후 0으로 초기화하는 패턴을 권장.
    /// </summary>
    public float[] externalInput;

    /// <summary>
    /// Motor 뉴런의 스파이크 카운터.
    /// 시뮬레이터 바깥에서 윈도우/EMA로 발화율(예: Hz)로 변환해 사용한다.
    /// </summary>
    public float[] motorFiring;

    #endregion
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
