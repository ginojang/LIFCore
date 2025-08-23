// File: LIF.cs
// C ���� ���̼� ����(SoA, ���� for-loop)
// .NET 7+ ����

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
    /// �Һ�(�Ǵ� �غҺ�) ��Ʈ��ũ ��Ÿ/���� ����(�׷��� ����).
    /// - ���߿� C�� �����ϱ� ���� SoA �迭 ����
    /// - ��Ÿ�ӿ� ���� �� �ٲ�� �͵�: type, threshold, leak, �ó��� �׷���
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
            // �⺻ ��ȿ��
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

            // �׷��� ���� Ȯ��(����� ��忡���� ��� ����)
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
    /// ���� ����(�� tick ������Ʈ): potential, refractory, externalInput, motorFiring
    /// </summary>
    public sealed class LIFState
    {
        public float[] Potential { get; }
        public float[] RefractoryMs { get; }   // ���� ������(ms)

        // I/O (size: N)
        public float[] ExternalInput { get; }  // ���� Sensory�� ���(�޽��� ȣ��ο��� 0 Ŭ����)
        public float[] MotorFiring { get; }    // ���� ��ȭ ī��Ʈ(accumulate)

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
    /// ������ tick ���(�ɼ�)
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
        /// �⺻ 1�н� Step (������, ������ �Һ�)
        /// - ����(Euler): V += (-�롤V) * dt
        /// - ��ȭ��: V=0, ������ ����, MotorFiring++, �ó����� ����(accum)�� ����
        /// - ���� tick �� ������ ����: �Ĺݺο� �ϰ� ����
        /// </summary>
        public static void Step(
            LIFNetwork net, LIFState st, float dtMs, float refractoryMs,
            ref LIFTickStats stats)
        {
            StepWithFlags(net, st, dtMs, refractoryMs, ref stats, null);
        }

        /// <summary>
        /// �����/���� 2�н�:
        /// 1) �н�1: �ܺ��Է� ���� ����/��ȭ/���� ����
        /// 2) �ܺ��Է� Ŭ����(�ɼ�)
        /// 3) �н�2: ���� ���� �ݿ��� ������ ���� ����
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
        /// ���� ����: �ʿ� �� ������ũ �÷��� ���
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

            // �Ʒ� ������ �迭 ���� �̱� ������. ref ������.
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

            // ���� ���� Ǯ���� ��������(�Ҵ��)
            float[] accumArr = ArrayPool<float>.Shared.Rent(n);
            var accum = accumArr.AsSpan(0, n);
            accum.Clear();

            try
            {
                // 1�� ����: ����/��ȭ ����, ��ȭ �� �ó��� ���� ����(accum)
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

                    // external input (���� Sensory�� ����)
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
                        // NaN/Inf ����
                        if (float.IsNaN(v) || float.IsInfinity(v))
                        {
                            v = 0f;
                            stats.HadNaNOrInf = true;
                        }
                        V[i] = v;
                        stats.Observe(v);
                    }
                }

                // 2�� ����: ���� ���� �ϰ� �ݿ�(���� tick ������ ����)
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
