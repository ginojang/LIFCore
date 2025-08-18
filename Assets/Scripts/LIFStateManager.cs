using UnityEngine;
using System;
using System.Collections;
using System.Diagnostics; // Stopwatch

public sealed class LIFStateManager : MonoBehaviour
{
    public static LIFStateManager I { get; private set; }

    public enum SimDriver { FixedUpdate, Coroutine }

    [Header("Driver")]
    public SimDriver driver = SimDriver.Coroutine;
    public bool useUnscaledTimeInCoroutine = true;
    public int maxCatchUpSteps = 8; // ������ �� ���� ���� ����

    [Header("Neuron Counts")]
    public int sensory = 1;
    public int inter = 0;
    public int motor = 1;

    [Header("Defaults")]
    public float defaultThreshold = 1.0f;
    public float defaultLeak = 0.0f;    // [1/s]
    public float defaultSynWeight = 1.2f;

    [Header("Simulation")]
    [Tooltip("�ùķ��̼� ���� ƽ (ms)")]
    public float dtMs = 10f;
    [Tooltip("������(ms)")]
    public float refractoryMs = 5f;
    [Tooltip("���ܸ��� ����Ǵ� ���� ��ȭ��/�� (EMA��)")]
    public float motorDecayPerSecond = 2.0f;

    [Header("Policy")]
    public bool clearExternalAfterStep = true;
    public bool logInit = false;

    // ---- Exposed state ----
    public LIFState State { get; private set; }
    public bool[] SpikedThisTick { get; private set; }
    public LIFTickStats Stats;// { get; private set; }

    public int N => (State?.potential?.Length) ?? 0;

    // ---- Driver internals ----
    private float _accumSec;              // FixedUpdate�� ������
    private Coroutine _simCo;             // �ڷ�ƾ �ڵ�
    private Stopwatch _sw;                // �ڷ�ƾ�� Ÿ�̸�
    private double _nextSec;              // ���� ���� ���� �ð�(��)

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        AllocateAndBuild();
    }

    void OnEnable()
    {
        StartDriver();
    }

    void OnDisable()
    {
        StopDriver();
    }

    void StartDriver()
    {
        StopDriver();
        if (driver == SimDriver.Coroutine)
        {
            _sw = new Stopwatch();
            _sw.Start();
            _nextSec = _sw.Elapsed.TotalSeconds + DtSec;
            _simCo = StartCoroutine(SimLoop());
        }
        else
        {
            _accumSec = 0f;
        }
    }

    void StopDriver()
    {
        if (_simCo != null) { StopCoroutine(_simCo); _simCo = null; }
        if (_sw != null) { _sw.Stop(); _sw = null; }
    }

    void FixedUpdate()
    {
        if (driver != SimDriver.FixedUpdate || State == null) return;

        // ���� ����ƽ ��� ���� ��������
        float d = Time.fixedDeltaTime;
        _accumSec += d;

        int steps = 0;
        while (_accumSec >= DtSec && steps < maxCatchUpSteps)
        {
            StepOnce();         // dt = DtSec
            _accumSec -= DtSec;
            steps++;
        }
        if (steps == maxCatchUpSteps && _accumSec >= DtSec)
        {
            // ���� catch-up ����: ���� ����(�帮��Ʈ �ּ�ȭ)
            _accumSec = 0f;
        }
    }

    IEnumerator SimLoop()
    {
        // ���� ƽ �����ٸ� (Stopwatch ���)
        while (enabled)
        {
            if (State == null) { yield return null; continue; }

            double now = _sw.Elapsed.TotalSeconds;
            // catch-up
            int steps = 0;
            while (now + 1e-6 >= _nextSec && steps < maxCatchUpSteps)
            {
                StepOnce(); // dt = DtSec
                _nextSec += DtSec;
                steps++;
                now = _sw.Elapsed.TotalSeconds;
            }
            if (steps == maxCatchUpSteps && now >= _nextSec)
            {
                // ���� ������ �� �帮��Ʈ ����: ���� �ð����� ������
                _nextSec = now + DtSec;
            }

            double remain = _nextSec - now;
            if (remain > 0)
            {
                if (useUnscaledTimeInCoroutine)
                    yield return new WaitForSecondsRealtime((float)remain);
                else
                    yield return new WaitForSeconds((float)remain);
            }
            else
            {
                // �̹� ���� �� ���� �����ӱ��� �纸
                yield return null;
            }
        }
    }

    public float DtSec => Mathf.Max(0.0001f, dtMs * 0.001f);

    // ================== Public API ==================

    public void StepOnce()
    {
        if (State == null) return;

        if (SpikedThisTick == null || SpikedThisTick.Length != N)
            SpikedThisTick = new bool[N];

        LIFStepCpu.StepWithFlags(State, N, dtMs, refractoryMs, ref Stats, SpikedThisTick);

        if (clearExternalAfterStep && State.externalInput != null)
            Array.Clear(State.externalInput, 0, State.externalInput.Length);

        // ���� ����: �ʴ� �������� ����ƽ�� ���� ȯ��
        if (State.motorFiring != null && motorDecayPerSecond > 0f)
        {
            float dec = motorDecayPerSecond * DtSec; // ���ܴ� ���跮
            for (int i = 0; i < State.motorFiring.Length; i++)
                State.motorFiring[i] = Mathf.Max(0f, State.motorFiring[i] - dec);
        }
    }

    public void PulseExternal(int neuronIndex, float amplitude)
    {
        if (State == null || State.externalInput == null) return;
        if ((uint)neuronIndex >= (uint)N) return;
        State.externalInput[neuronIndex] += amplitude;
    }

    public void ResetAllPotentials(float value = 0f)
    {
        if (State?.potential == null) return;
        Array.Fill(State.potential, value);
        if (State.refractory != null) Array.Fill(State.refractory, 0f);
        if (State.motorFiring != null) Array.Fill(State.motorFiring, 0f);
    }

    // ================== Init / Build ==================

    void AllocateAndBuild()
    {
        int nSens = Mathf.Max(0, sensory);
        int nInter = Mathf.Max(0, inter);
        int nMot = Mathf.Max(0, motor);
        int n = nSens + nInter + nMot;

        if (n <= 0) { UnityEngine.Debug.LogWarning("[LIF] N=0"); return; }

        var s = new LIFState
        {
            potential = new float[n],
            threshold = new float[n],
            leak = new float[n],
            refractory = new float[n],
            type = new NeuronType[n],

            synStartIndex = new int[n],
            synCount = new int[n],
            synPost = Array.Empty<int>(),
            synWeight = Array.Empty<float>(),

            externalInput = new float[n],
            motorFiring = new float[n],
        };

        for (int i = 0; i < n; i++)
        {
            s.threshold[i] = defaultThreshold;
            s.leak[i] = defaultLeak;
            s.refractory[i] = 0f;
        }

        int idx = 0;
        for (int i = 0; i < nSens; i++) s.type[idx++] = NeuronType.Sensory;
        for (int i = 0; i < nInter; i++) s.type[idx++] = NeuronType.Inter;
        for (int i = 0; i < nMot; i++) s.type[idx++] = NeuronType.Motor;

        BuildFullyConnectedSensoryToMotor(s, nSens, nInter, nMot, defaultSynWeight);

        State = s;
        SpikedThisTick = new bool[n];
        Stats = new LIFTickStats();

        if (logInit)
            UnityEngine.Debug.Log($"[LIF] Allocated N={n} (S={nSens}, I={nInter}, M={nMot}), synapses={s.synPost.Length}");
    }

    void BuildFullyConnectedSensoryToMotor(LIFState s, int nSens, int nInter, int nMot, float w)
    {
        int n = nSens + nInter + nMot;
        int firstMotor = nSens + nInter;

        int synTotal = nSens * nMot;
        s.synPost = new int[synTotal];
        s.synWeight = new float[synTotal];

        int cursor = 0;
        for (int pre = 0; pre < n; pre++)
        {
            s.synStartIndex[pre] = cursor;

            if (s.type[pre] == NeuronType.Sensory && nMot > 0)
            {
                for (int m = 0; m < nMot; m++)
                {
                    s.synPost[cursor] = firstMotor + m;
                    s.synWeight[cursor] = w;
                    cursor++;
                }
                s.synCount[pre] = nMot;
            }
            else
            {
                s.synCount[pre] = 0;
            }
        }
    }
}
