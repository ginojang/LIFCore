// ���� Ÿ��
public enum NeuronType : byte { Sensory, Inter, Motor }

// �� WebGL���� SoA(Struct-of-Arrays)�� ����
public sealed class LIFState
{
    public float[] potential;
    public float[] threshold;
    public float[] leak;
    public float[] refractory;
    public NeuronType[] type;

    // �ó����� pre ���� �������� ���� ��ġ
    public int[] synStartIndex;   // �� pre�� ���� �ε���
    public int[] synCount;        // �� pre�� ����
    public int[] synPost;         // synapses[k].post
    public float[] synWeight;     // synapses[k].weight

    public float[] externalInput; // ���� �Է�
    public float[] motorFiring;   // ��� ī��Ʈ(������ �ۿ��� ��t�� rateȭ)
}

// ����Ƽ�꿡�� Burst + Jobs, WebGL���� �� �Լ��� ��ü
public static class LIFStepCpu
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    static float Clamp01Minus(float x) => x < 0f ? 0f : (x > 1f ? 0f : (1f - x));

    public static void Step(LIFState s, int count, float dtMs, float refractoryMs)
    {
        // ��� ���� ĳ�� (JIT ������ IL2CPP������ �б�/�ε� ���ҿ� ����)
        var pot = s.potential; var thr = s.threshold; var leak = s.leak;
        var refc = s.refractory; var typ = s.type;
        var start = s.synStartIndex; var scount = s.synCount;
        var post = s.synPost; var w = s.synWeight;
        var ext = s.externalInput; var motor = s.motorFiring;

        // 1�� �н�: ���� ������Ʈ + ��ȭ üũ
        for (int i = 0; i < count; i++)
        {
            float p = pot[i];
            // ����
            p *= Clamp01Minus(leak[i]); // (1 - leak) ���� ����

            // �ܺ� �Է�(����)
            if (typ[i] == NeuronType.Sensory)
                p += ext[i];

            // ������
            float r = refc[i];
            if (r > 0f)
            {
                r -= dtMs;
                refc[i] = r > 0f ? r : 0f;
                pot[i] = p; // ���� ����
                continue;
            }

            // ��ȭ
            bool fired = (p >= thr[i]);
            if (fired)
            {
                p = 0f;
                refc[i] = refractoryMs;

                // �ó��� ��� ���� (MVP: ����ť ����)
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
