using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
public class LIFManager : MonoBehaviour
{
    // ---- Singleton ----
    public static LIFManager Instance { get; private set; }

    [Header("Lifecycle")]
    [Tooltip("씬 전환 시 파괴되지 않도록 유지")]
    [SerializeField] private bool dontDestroyOnLoad = true;

    [Header("Neuron Settings")]
    [Min(1)] public int neuronCount = 302;
    [Tooltip("기본 임계값 (각 뉴런 threshold 초기값)")]
    public float defaultThreshold = 1.0f;
    [Tooltip("기본 누수(0~1), 실제 계산은 (1 - leak)로 적용됨")]
    [Range(0f, 1f)] public float defaultLeak = 0.02f;
    [Tooltip("기본 뉴런 타입(초기화시 전체 적용 후 필요시 바꿔도 됨)")]
    public NeuronType defaultType = NeuronType.Inter;

    [Header("Step Settings (ms)")]
    [Tooltip("스텝 간격 힌트(ms). Fixed의 실제 dt와 일치시키는 것을 권장")]
    public float stepDtMs = 1.0f;
    [Tooltip("발화 후 휴지기(ms)")]
    public float refractoryMs = 2.0f;
    [Tooltip("Time.timeScale 무시하고 고정틱 사용")]
    public bool useUnscaledTime = false;

    [Header("Execution")]
    [Tooltip("시뮬레이션 실행 여부")]
    public bool runSimulation = true;

    // ---- Public State ----
    public LIFState State { get; private set; }
    public int SimTick { get; private set; } // 유일한 시간 축(int tick)

    // ---- Events (관측용 훅) ----
    public event System.Action BeforeStep;   // 스텝 직전
    public event System.Action AfterStep;    // 스텝 직후

    // ---- Input / Weight Queues (틱 경계 스케줄) ----
    public struct ScheduledInput { public int tick; public int neuron; public float amp; }
    public struct ScheduledWeight { public int tick; public int edgeIndex; public float value; public bool additive; }

    public class LIFInputQueue
    {
        readonly List<ScheduledInput> _buf = new();
        public void Enqueue(ScheduledInput e) => _buf.Add(e);
        public void DrainForTick(int tick, float[] pending)
        {
            for (int i = _buf.Count - 1; i >= 0; --i)
            {
                var e = _buf[i];
                if (e.tick != tick) continue;
                if ((uint)e.neuron < (uint)pending.Length)
                    pending[e.neuron] += e.amp;
                _buf.RemoveAt(i);
            }
        }
    }
    public class LIFWeightQueue
    {
        readonly List<ScheduledWeight> _buf = new();
        public float clampMin = -10f, clampMax = 10f;
        public void Enqueue(ScheduledWeight w) => _buf.Add(w);
        public void DrainForTick(int tick, float[] synWeight)
        {
            for (int i = _buf.Count - 1; i >= 0; --i)
            {
                var e = _buf[i];
                if (e.tick != tick) continue;
                if ((uint)e.edgeIndex < (uint)synWeight.Length)
                {
                    float w = e.additive ? (synWeight[e.edgeIndex] + e.value) : e.value;
                    synWeight[e.edgeIndex] = Mathf.Clamp(w, clampMin, clampMax);
                }
                _buf.RemoveAt(i);
            }
        }
    }

    public LIFInputQueue InputQ { get; } = new LIFInputQueue();
    public LIFWeightQueue WeightQ { get; } = new LIFWeightQueue();

    // ---- Internals ----
    // 다음 틱에 1회 소비되는 감각 입력(래칭 버퍼)
    public float[] pendingExternalInput;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        AllocateStateIfNeeded(neuronCount);
    }

    private void Start()
    {
        ResetStateValues();
        SimTick = 0;
    }

    // ⛔ Update는 시뮬 금지. 화면/HUD/로그만 다른 컴포넌트에서 처리.

    private void FixedUpdate()
    {
        if (!runSimulation) return;
        SimTick++;

        // ---- 스텝 직전 훅/적용 ----
        BeforeStep?.Invoke();

        // (1) 입력 스케줄 소비 → 이번 틱에만 쓸 externalInput으로 복사
        InputQ.DrainForTick(SimTick, pendingExternalInput);
        System.Array.Copy(pendingExternalInput, State.externalInput, neuronCount);
        System.Array.Clear(pendingExternalInput, 0, neuronCount);

        // (2) 가중치 스케줄 적용 (정책 A: 이번 틱에 즉시 반영)
        WeightQ.DrainForTick(SimTick, State.synWeight);

        // ---- 1틱 실행 ----
        float dtMs = (useUnscaledTime ? Time.fixedUnscaledDeltaTime : Time.fixedDeltaTime) * 1000f;
        if (dtMs <= 0f) dtMs = stepDtMs; // 안전 장치

        //LIFStepCpu.Step(State, neuronCount, dtMs, refractoryMs);

        // 감각 입력은 1틱 소비 후 클리어
        System.Array.Clear(State.externalInput, 0, neuronCount);

        // ---- 스텝 직후 훅 ----
        AfterStep?.Invoke();
    }

    // ---- Public Scheduling API (외부에는 Enqueue만 공개) ----

    /// <summary>다음 틱에 1회 펄스</summary>
    public void EnqueuePulseNextTick(int neuron, float amp)
    {
        InputQ.Enqueue(new ScheduledInput { tick = SimTick + 1, neuron = neuron, amp = amp });
    }

    /// <summary>폭(ms)만큼 연속 펄스 (틱 단위로 변환)</summary>
    public void EnqueuePulseSpanMs(int neuron, float amp, float widthMs)
    {
        int durTicks = Mathf.Max(1, Mathf.CeilToInt(widthMs / Mathf.Max(0.0001f, stepDtMs)));
        int start = SimTick + 1;
        for (int t = 0; t < durTicks; t++)
            InputQ.Enqueue(new ScheduledInput { tick = start + t, neuron = neuron, amp = amp });
    }

    /// <summary>절대 틱 지정 펄스</summary>
    public void EnqueueAtTick(int absTick, int neuron, float amp)
    {
        InputQ.Enqueue(new ScheduledInput { tick = Mathf.Max(1, absTick), neuron = neuron, amp = amp });
    }

    /// <summary>가중치 변경 스케줄(기본은 절대값 설정). additive=true면 누적</summary>
    public void EnqueueWeightAtTick(int edgeIndex, float value, bool additive = false, int? tick = null)
    {
        WeightQ.Enqueue(new ScheduledWeight
        {
            tick = tick ?? (SimTick + 1), // 기본: 다음 틱부터 반영(안정)
            edgeIndex = edgeIndex,
            value = value,
            additive = additive
        });
    }

    // ---- 기존 편의 API (읽기/설정/할당) ----

    public float ReadMotorFiring(int neuronIndex, bool clearAfterRead = true)
    {
        if ((uint)neuronIndex >= (uint)neuronCount) return 0f;
        float v = State.motorFiring[neuronIndex];
        if (clearAfterRead) State.motorFiring[neuronIndex] = 0f;
        return v;
    }

    public void SetNeuronType(int index, NeuronType t)
    {
        if ((uint)index >= (uint)neuronCount) return;
        State.type[index] = t;
    }
    public void SetAllNeuronType(NeuronType t)
    {
        for (int i = 0; i < neuronCount; i++) State.type[i] = t;
    }

    public void Reallocate(int newNeuronCount)
    {
        if (newNeuronCount <= 0) newNeuronCount = 1;
        neuronCount = newNeuronCount;
        AllocateStateIfNeeded(neuronCount);
        ResetStateValues();
        SimTick = 0;
    }

    public void SetSynapsesCompact(int[] synStartIndex, int[] synCount, int[] synPost, float[] synWeight)
    {
        if (synStartIndex == null || synCount == null || synPost == null || synWeight == null) { Debug.LogError("[LIF] SetSynapsesCompact: null 배열"); return; }
        if (synStartIndex.Length != neuronCount || synCount.Length != neuronCount) { Debug.LogError("[LIF] SetSynapsesCompact: 인덱스 길이 불일치"); return; }
        if (synPost.Length != synWeight.Length) { Debug.LogError("[LIF] SetSynapsesCompact: post/weight 길이 불일치"); return; }

        State.synStartIndex = synStartIndex;
        State.synCount = synCount;
        State.synPost = synPost;
        State.synWeight = synWeight;
    }

    public void BuildSynapsesFromEdges((int pre, int post, float w)[] edges)
    {
        if (edges == null) { Debug.LogError("[LIF] BuildSynapsesFromEdges: edges == null"); return; }

        var counts = new int[neuronCount];
        foreach (var e in edges)
        {
            if ((uint)e.pre >= (uint)neuronCount || (uint)e.post >= (uint)neuronCount) continue;
            counts[e.pre]++;
        }

        var start = new int[neuronCount];
        var synCount = new int[neuronCount];
        int total = 0;
        for (int i = 0; i < neuronCount; i++)
        {
            start[i] = total;
            synCount[i] = counts[i];
            total += counts[i];
        }

        var post = new int[total];
        var weight = new float[total];
        var writeIndex = new int[neuronCount];
        System.Array.Copy(start, writeIndex, neuronCount);

        foreach (var e in edges)
        {
            if ((uint)e.pre >= (uint)neuronCount || (uint)e.post >= (uint)neuronCount) continue;
            int idx = writeIndex[e.pre]++;
            post[idx] = e.post;
            weight[idx] = e.w;
        }

        SetSynapsesCompact(start, synCount, post, weight);
    }

    public void ResetStateValues()
    {
        var s = State;
        int n = neuronCount;

        for (int i = 0; i < n; i++)
        {
            s.potential[i] = 0f;
            s.threshold[i] = defaultThreshold;
            s.leak[i] = defaultLeak;
            s.refractory[i] = 0f;
            s.type[i] = defaultType;
            s.externalInput[i] = 0f;
            s.motorFiring[i] = 0f;
        }

        // 큐/버퍼 초기화
        System.Array.Clear(pendingExternalInput, 0, n);
    }

    private void AllocateStateIfNeeded(int n)
    {
        if (State == null) State = new LIFState();

        State.potential = new float[n];
        State.threshold = new float[n];
        State.leak = new float[n];
        State.refractory = new float[n];
        State.type = new NeuronType[n];

        State.synStartIndex = new int[n];
        State.synCount = new int[n];
        State.synPost = System.Array.Empty<int>();
        State.synWeight = System.Array.Empty<float>();

        State.externalInput = new float[n];
        State.motorFiring = new float[n];

        pendingExternalInput = new float[n];
    }
}
