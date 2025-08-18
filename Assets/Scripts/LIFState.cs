using System;

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
//  - 실행 로직은 아래 LIFStepCpu.Step 이 담당
//
// Invariants:
//  - 모든 1D 배열 길이는 N(뉴런 수)와 동일해야 함(단, synPost/synWeight는 시냅스 총수).
//  - synStartIndex.Length == synCount.Length == N
//  - 각 i에 대해 0 <= synStartIndex[i] <= synPost.Length 이며,
//    synStartIndex[i] + synCount[i] <= synPost.Length.
//  - type[i]에 따라 의미가 달라질 수 있음(예: externalInput은 보통 Sensory에서만 사용).
//  - 권장 단위: 시간(ms), leak은 [1/s], potential/threshold는 동일 스케일.
// ==========================================================

/// <summary>
/// Leaky Integrate-and-Fire 네트워크의 "현재 상태" 컨테이너.
/// SoA 레이아웃으로 캐시 효율을 극대화했다.
/// </summary>
public sealed class LIFState
{
    #region Neuron State (size: N)

    /// <summary>막전위 V(t). 외부 입력/시냅스/누수/불응기에 의해 매 tick 갱신된다.</summary>
    public float[] potential;

    /// <summary>발화 임계치 θ. potential[i] ≥ threshold[i]이면 spike 발생.</summary>
    public float[] threshold;

    /// <summary>누수율 λ [1/s]. (연속형: dv/dt = -λ·v + I)</summary>
    public float[] leak;

    /// <summary>남은 불응기(단위: ms). 0보다 크면 발화/적분을 건너뛰고 카운트다운.</summary>
    public float[] refractory;

    /// <summary>뉴런 역할 타입(Sensory/Inter/Motor). 타입별 규칙 분기 등에 사용.</summary>
    public NeuronType[] type;

    #endregion

    // ---------------------------------------------------------------------

    #region Synapse Graph (Compressed Adjacency / CSR-like)

    /// <summary>pre 뉴런 i의 시냅스 리스트가 synPost/synWeight에서 시작하는 오프셋.</summary>
    public int[] synStartIndex;

    /// <summary>pre 뉴런 i가 갖는 시냅스 개수. 유효 범위: [start, start+count).</summary>
    public int[] synCount;

    /// <summary>k번째 시냅스의 post(도착) 뉴런 인덱스. 길이는 시냅스 총수.</summary>
    public int[] synPost;

    /// <summary>k번째 시냅스의 가중치(양수: 흥분, 음수: 억제). synPost와 동일 길이.</summary>
    public float[] synWeight;

    #endregion

    // ---------------------------------------------------------------------

    #region I/O Buffers (size: N)

    /// <summary>
    /// 외부 자극(감각 입력) 버퍼.
    /// [정책] 펄스형이면 Step 후 호출자에서 0으로 초기화하는 패턴 권장.
    /// </summary>
    public float[] externalInput;

    /// <summary>
    /// Motor 뉴런의 스파이크 카운터.
    /// [정책] 바깥에서 윈도우/EMA로 발화율(Hz) 변환.
    /// </summary>
    public float[] motorFiring;

    #endregion
}

// ==========================================================
// Light Monitoring (가벼운 계측만)
// ==========================================================

/// <summary>
/// 프레임(틱) 단위 계측 지표(라이트).
/// HUD/로그/대시보드로 바로 보이게 최소 필드만 둔다.
/// </summary>
public struct LIFTickStats
{
    public int Spikes;           // 이번 tick 총 스파이크 수
    public int RefractorySkips;  // 불응기 때문에 건너뛴 뉴런 수
    public int SynUpdates;       // 전도 누적 횟수(accum += w)
    public bool HadNaNOrInf;     // NaN/Inf 발생 여부
    public float PotMin;         // 관측 전위 최소
    public float PotMax;         // 관측 전위 최대

    public void Begin()
    {
        Spikes = 0;
        RefractorySkips = 0;
        SynUpdates = 0;
        HadNaNOrInf = false;
        PotMin = float.PositiveInfinity;
        PotMax = float.NegativeInfinity;
    }
}

// ==========================================================
// LIFStepCpu - 안정형(순서 비의존) 전도만 유지
//  - 누수: 연속형(Euler 근사) p += (-λ·p)*dt   ※ dt는 [s]
//  - 전도: 동일 tick 내 재판정 금지(버퍼링 후 일괄 가산)
//  - 외부 입력: 펄스 정책이면 호출자에서 clear
//  - 안전장치: 음수 λ 가드, NaN/Inf 방지
// ==========================================================

public static class LIFStepCpu
{
    /// <summary>
    /// 안정형 Step (라이트 버전, 주석 풍부).
    /// </summary>
    /// <param name="s">상태</param>
    /// <param name="count">뉴런 수(N)</param>
    /// <param name="dtMs">시간 간격(ms)</param>
    /// <param name="refractoryMs">불응기(ms)</param>
    /// <param name="stats">선택: 라이트 계측(ref). 필요 없으면 new 후 무시 가능.</param>
    /// 
    // LIFStepCpu.cs 내에 추가 (기존 Step는 그대로 두고 오버로드만 추가)
    public static void StepWithFlags(
        LIFState s, int count, float dtMs, float refractoryMs,
        ref LIFTickStats stats, bool[] spikedThisTick  // size=N
    )
    {
        if (spikedThisTick != null && spikedThisTick.Length >= count)
            Array.Clear(spikedThisTick, 0, count);

        // 원본 Step 코드와 동일…
        var potential = s.potential;
        var threshold = s.threshold;
        var leak = s.leak;
        var refractory = s.refractory;
        var type = s.type;

        var synStartIndex = s.synStartIndex;
        var synCount = s.synCount;
        var synPost = s.synPost;
        var synWeight = s.synWeight;

        var externalInput = s.externalInput;
        var motorFiring = s.motorFiring;

        stats.Begin();
        float dt = dtMs * 0.001f;

        Span<float> accum = stackalloc float[count];
        accum.Clear();

        for (int i = 0; i < count; i++)
        {
            float curRefractory = refractory[i];
            if (curRefractory > 0f)
            {
                curRefractory -= dtMs;
                refractory[i] = curRefractory > 0f ? curRefractory : 0f;
                stats.RefractorySkips++;
                continue;
            }

            float curPotential = potential[i];
            float lambda = leak[i];
            if (lambda < 0f) lambda = 0f;
            curPotential += (-lambda * curPotential) * dt;

            if (type[i] == NeuronType.Sensory)
                curPotential += externalInput[i];

            if (curPotential >= threshold[i])
            {
                potential[i] = 0f;
                refractory[i] = refractoryMs;
                stats.Spikes++;

                if (spikedThisTick != null && i < spikedThisTick.Length)
                    spikedThisTick[i] = true;

                if (type[i] == NeuronType.Motor)
                    motorFiring[i] += 1f;

                int st = synStartIndex[i], end = st + synCount[i];
                for (int k = st; k < end; k++)
                {
                    accum[synPost[k]] += synWeight[k];
                    stats.SynUpdates++;
                }
            }
            else
            {
                if (float.IsNaN(curPotential) || float.IsInfinity(curPotential))
                {
                    curPotential = 0f;
                    stats.HadNaNOrInf = true;
                }
                potential[i] = curPotential;

                if (curPotential < stats.PotMin) stats.PotMin = curPotential;
                if (curPotential > stats.PotMax) stats.PotMax = curPotential;
            }
        }

        for (int j = 0; j < count; j++)
        {
            float aj = accum[j];
            if (aj != 0f)
            {
                float v = potential[j] + aj;
                if (float.IsNaN(v) || float.IsInfinity(v))
                {
                    v = 0f;
                    stats.HadNaNOrInf = true;
                }
                potential[j] = v;
                if (v < stats.PotMin) stats.PotMin = v;
                if (v > stats.PotMax) stats.PotMax = v;
            }
        }
    }


}



/*
 
    Span<T>는 연속된 메모리 구간을 가리키는 가벼운 “뷰(view)” 타입. 
    배열, stackalloc으로 할당한 스택 메모리, 고정된 포인터 메모리 등 여러 소스 위에 0-할당으로 슬라이싱할 수 있음. 
    C#의 ref struct라서 힙에 못 올라가고 메서드 스코프 안에서만 살아.

    왜 쓰나?
    할당 없이 슬라이스: 배열 조각을 잘라 써도 새 배열을 만들지 않음.
    안전한 저수준 접근: 포인터처럼 빠르지만 범위 체크가 붙어 더 안전.
    데이터 복사 최소화: 파싱, 누적 버퍼, 임시 작업에 좋음.


    Span<int> s1 = new int[10];          // 배열 위의 뷰
    Span<int> s2 = s1.Slice(2, 4);       // [2..5) 구간 (할당 없음)
    Span<int> s3 = stackalloc int[128];  // 스택 버퍼 (매우 빠름, 수명 짧음)
    ReadOnlySpan<byte> ro = "abc".AsSpan(); // 읽기 전용


    수명/제한(중요)
    ref struct라서:

    필드로 보관 불가(클래스 멤버 X), 박싱/캡처 불가(람다, async/iterator 안됨),

    메서드 밖으로 반환 불가(스택/외부 메모리를 가리킬 수 있으니 위험).
    큰 사이즈를 stackalloc 하면 스택 오버플로 위험 → 큽니다 싶으면 ArrayPool<T>.Shared.Rent(...) 같은 풀링을 써.

    Span<T> vs 친구들

    ReadOnlySpan<T>: 읽기 전용 뷰.
    Memory<T>: 힙에 저장·보관 가능(필드 OK), await/비동기와 궁합 좋음. 필요 시 Span로 꺼낼 땐 .Span.
    ArraySegment<T>: 배열 한 조각을 나타내지만, 배열에만 쓸 수 있고 API가 제한적.
    Unity의 NativeArray<T>: 잡스/Burst와 궁합 최고(버스트 최적화), 대신 Unity 컬렉션 생태계.


    //자주 쓰는 패턴 모음
    using System;                      // Span, ReadOnlySpan, MemoryExtensions
    using System.Runtime.InteropServices; // MemoryMarshal (옵션)


    * 복사 (할당 없음)
    ReadOnlySpan<float> src = a;
    Span<float> dst = b;
    src.CopyTo(dst);

    * 완전 동일 비교 (원소 1:1)
    bool same = a.AsSpan().SequenceEqual(b);

    * 사전식 비교(lexicographic)
    int cmp = a.AsSpan().SequenceCompareTo(b);

    * 초기화 / 채우기
    var s = a.AsSpan();
    s.Clear();        // 0으로
    s.Fill(1.0f);     // 특정 값

    * 바이트 뷰로 캐스팅(복사 없이)
    ReadOnlySpan<float> f = a;
    ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(f);

    * 타입 재해석 캐스팅(복사 없이)
    Span<float> f = a;
    Span<int> asInt = MemoryMarshal.Cast<float,int>(f);


    * 참고

    float 비교는 SequenceEqual이 “정확히 같은 비트값” 기준이야. 오차 허용이 필요하면 간단 루프가 안전해.
    bool AlmostEqual(ReadOnlySpan<float> x, ReadOnlySpan<float> y, float eps = 1e-5f) {
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++)
            if (MathF.Abs(x[i] - y[i]) > eps) return false;
        return true;
    }

    * 배열 ↔ 배열 복사만 한다면 Array.Copy도 충분히 빠름.
    원시형(byte 등) 대량 복사면 Buffer.BlockCopy가 유리할 때도 있지만, Span.CopyTo로 통일해도 무난해.

    요컨대: 슬라이스/비교/복사 = Span 한 방이면 끝! 필요하면 버퍼 풀링(ArrayPool<T>)만 얹어 쓰면 돼.



 */