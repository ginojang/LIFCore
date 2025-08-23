// File: LIF.cs
// C 포팅 용이성 유지(SoA, 순수 for-loop)
// .NET 7+ 권장

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Neuro.LIF
{
    // ------------------------------------------------------------
    // Core Types
    // ------------------------------------------------------------
    public enum NeuronType : byte { Sensory, Inter, Motor }

    /// <summary>
    /// 불변(또는 준불변) 네트워크 메타/정적 정보(그래프 포함).
    /// - 나중에 C로 포팅하기 쉽게 SoA 배열 유지
    /// - 런타임에 자주 안 바뀌는 것들: type, threshold, leak, 시냅스 그래프
    /// </summary>
    public sealed class LIFNetwork
    {
        public int Count { get; }

        // Neuron meta (size: N)
        public NeuronType[] Type { get; }
        public float[] Threshold { get; }
        public float[] Leak { get; }               // [1/s]

        // Graph (CSR-like)
        public int[] SynapseStartIndex { get; }
        public int[] SynapseCount { get; }
        public int[] SynapsePost { get; }              // size: totalSyn
        public float[] SynapseWeight { get; }          // size: totalSyn

        public LIFNetwork(
            NeuronType[] type,
            float[] threshold,
            float[] leak,
            int[] synapseStartIndex,
            int[] synapseCount,
            int[] synapsePost,
            float[] synapseWeight
        )
        {
            // 기본 유효성
            if (type is null || threshold is null || leak is null)
                throw new ArgumentNullException(nameof(type));
            if (type.Length != threshold.Length || type.Length != leak.Length)
                throw new ArgumentException("type/threshold/leak length mismatch");

            if (synapseStartIndex is null || synapseCount is null || synapsePost is null || synapseWeight is null)
                throw new ArgumentNullException(nameof(synapseStartIndex));
            if (synapseStartIndex.Length != synapseCount.Length)
                throw new ArgumentException("synStartIndex/synCount length mismatch");
            if (synapsePost.Length != synapseWeight.Length)
                throw new ArgumentException("synPost/synWeight length mismatch");

            Count = type.Length;
            Type = type;
            Threshold = threshold;
            Leak = leak;

            SynapseStartIndex = synapseStartIndex;
            SynapseCount = synapseCount;
            SynapsePost = synapsePost;
            SynapseWeight = synapseWeight;

            // 그래프 범위 확인(디버그 모드에서만 비용 지불)
#if DEBUG
            for (int i = 0; i < Count; i++)
            {
                int st = SynapseStartIndex[i];
                int c = SynapseCount[i];
                if (st < 0 || c < 0 || st + c > SynapsePost.Length)
                    throw new ArgumentOutOfRangeException($"Invalid CSR range at {i}: [{st}, {st + c})");
            }
#endif
        }
    }

    /// <summary>
    /// 가변 상태(매 tick 업데이트): potential, refractory, externalInput, motorFiring
    /// </summary>
    public sealed class LIFState
    {
        public float[] Potential { get; }
        public float[] RefractoryMs { get; }   // 남은 불응기(ms)

        // I/O (size: N)
        public float[] ExternalInput { get; }  // 보통 Sensory만 사용(펄스면 호출부에서 0 클리어)
        public float[] MotorFiring { get; }    // 모터 발화 카운트(accumulate)

        public LIFState(int count)
        {
            Potential = new float[count];
            RefractoryMs = new float[count];
            ExternalInput = new float[count];
            MotorFiring = new float[count];
        }

        public void ClearAll()
        {
            Array.Clear(Potential, 0, Potential.Length);
            Array.Clear(RefractoryMs, 0, RefractoryMs.Length);
            Array.Clear(ExternalInput, 0, ExternalInput.Length);
            Array.Clear(MotorFiring, 0, MotorFiring.Length);
        }
    }

    /// <summary>
    /// 가벼운 tick 통계(옵션)
    /// </summary>
    public struct LIFTickStats
    {
        public int Spikes;
        public int RefractorySkips;
        public int SynUpdates;
        public bool HadNaNOrInf;
        public float PotMin;
        public float PotMax;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Begin()
        {
            Spikes = 0;
            RefractorySkips = 0;
            SynUpdates = 0;
            HadNaNOrInf = false;
            PotMin = float.PositiveInfinity;
            PotMax = float.NegativeInfinity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Observe(float v)
        {
            if (v < PotMin) PotMin = v;
            if (v > PotMax) PotMax = v;
        }
    }

    // ------------------------------------------------------------
    // Stepper
    // ------------------------------------------------------------
    public static class LIFStepper
    {
        /// <summary>
        /// 기본 1패스 Step (안정형, 동순서 불변)
        /// - 누수(Euler): V += (-λ·V) * dt
        /// - 발화시: V=0, 불응기 세팅, MotorFiring++, 시냅스는 버퍼(accum)에 누적
        /// - 동일 tick 내 재판정 금지: 후반부에 일괄 누적
        /// </summary>
        public static void Step(
            LIFNetwork net, LIFState st, float dtMs, float refractoryMs,
            ref LIFTickStats stats)
        {
            StepWithFlags(net, st, dtMs, refractoryMs, ref stats, null);
        }

        /// <summary>
        /// 디버그/원샷 2패스:
        /// 1) 패스1: 외부입력 포함 적분/발화/전도 누적
        /// 2) 외부입력 클리어(옵션)
        /// 3) 패스2: 누적 전도 반영된 전위로 최종 판정
        /// </summary>
        public static void StepTwoPassOneShot(
            LIFNetwork net, LIFState st, float dtMs, float refractoryMs,
            ref LIFTickStats stats,
            bool clearExternalAfterFirst = true,
            bool[]? spikedFirst = null,
            bool[]? spikedSecond = null)
        {
            StepWithFlags(net, st, dtMs, refractoryMs, ref stats, spikedFirst);

            if (clearExternalAfterFirst)
                Array.Clear(st.ExternalInput, 0, Math.Min(net.Count, st.ExternalInput.Length));

            StepWithFlags(net, st, dtMs, refractoryMs, ref stats, spikedSecond);
        }

        /// <summary>
        /// 내부 구현: 필요 시 스파이크 플래그 기록
        /// </summary>
        public static void StepWithFlags(
            LIFNetwork net, LIFState st, float dtMs, float refractoryMs,
            ref LIFTickStats stats, bool[]? spikedThisTick)
        {
            int n = net.Count;
            if (spikedThisTick is { Length: > 0 })
                Array.Clear(spikedThisTick, 0, Math.Min(spikedThisTick.Length, n));

            stats.Begin();
            float dt = dtMs * 0.001f;

            // 아래 값들은 배열 참조 이기 때문에. ref 참조임.
            var V = st.Potential;
            var Rm = st.RefractoryMs;
            var X = st.ExternalInput;
            var MF = st.MotorFiring;

            var type = net.Type;
            var th = net.Threshold;
            var leak = net.Leak;

            var start = net.SynapseStartIndex;
            var cnt = net.SynapseCount;
            var post = net.SynapsePost;
            var w = net.SynapseWeight;

            // 누적 버퍼 풀에서 가져오기(할당↓)
            float[] accumArr = ArrayPool<float>.Shared.Rent(n);
            var accum = accumArr.AsSpan(0, n);
            accum.Clear();

            try
            {
                // 1차 루프: 적분/발화 판정, 발화 시 시냅스 가중 누적(accum)
                for (int i = 0; i < n; i++)
                {
                    float refr = Rm[i];
                    if (refr > 0f)
                    {
                        Rm[i] = MathF.Max(0f, refr - dtMs);
                        stats.RefractorySkips++;
                        continue;
                    }

                    float v = V[i];

                    // leak
                    float lambda = leak[i];
                    if (lambda < 0f) lambda = 0f;
                    v += (-lambda * v) * dt;

                    // external input (보통 Sensory일 때만)
                    if (type[i] == NeuronType.Sensory)
                        v += X[i];

                    // spike?
                    if (v >= th[i])
                    {
                        V[i] = 0f;
                        Rm[i] = refractoryMs;
                        stats.Spikes++;

                        if (spikedThisTick is { Length: > 0 } && i < spikedThisTick.Length)
                            spikedThisTick[i] = true;

                        if (type[i] == NeuronType.Motor)
                            MF[i] += 1f;

                        int s0 = start[i];
                        int sN = s0 + cnt[i];
                        for (int k = s0; k < sN; k++)
                        {
                            accum[post[k]] += w[k];
                            stats.SynUpdates++;
                        }
                    }
                    else
                    {
                        // NaN/Inf 가드
                        if (float.IsNaN(v) || float.IsInfinity(v))
                        {
                            v = 0f;
                            stats.HadNaNOrInf = true;
                        }
                        V[i] = v;
                        stats.Observe(v);
                    }
                }

                // 2차 루프: 누적 전도 일괄 반영(동일 tick 재판정 금지)
                for (int j = 0; j < n; j++)
                {
                    float aj = accum[j];
                    if (aj == 0f) continue;

                    float v = V[j] + aj;
                    if (float.IsNaN(v) || float.IsInfinity(v))
                    {
                        v = 0f;
                        stats.HadNaNOrInf = true;
                    }
                    V[j] = v;
                    stats.Observe(v);
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(accumArr, clearArray: true);
            }
        }
    }
}
