using UnityEngine;

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
    [Tooltip("기본 스텝 간격(ms) — FixedUpdate에서 전달하지 않으면 사용")]
    public float stepDtMs = 1.0f;
    [Tooltip("발화 후 휴지기(ms)")]
    public float refractoryMs = 2.0f;

    [Header("Execution")]
    [Tooltip("시뮬레이션 실행 여부")]
    public bool runSimulation = true;

    // 내부 상태
    public LIFState State { get; private set; }

    // ---- Unity Lifecycle ----
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        if (dontDestroyOnLoad) DontDestroyOnLoad(gameObject);

        AllocateStateIfNeeded(neuronCount);
    }

    private void Start()
    {
        ResetStateValues();
    }

    // ⛔️ Update 제거: 이 컴포넌트는 화면/로그 관여 안 함

    private void FixedUpdate()
    {
        if (!runSimulation) return;

        // 물리틱마다 1스텝: dt는 고정 틱을 그대로 사용
        float dtMs = Time.fixedDeltaTime * 1000f;
        StepOnce(dtMs); // 내부에서 externalInput을 클리어함
    }

    // ---- Public API ----

    /// 외부 감각 입력 누적 (센서리 뉴런만 의미 있음)
    public void AddSensoryInput(int neuronIndex, float value)
    {
        if ((uint)neuronIndex >= (uint)neuronCount) return;
        State.externalInput[neuronIndex] += value;
    }

    /// 모터 뉴런 발화 카운트 읽기(옵션으로 초기화)
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

    /// 뉴런 수 변경 시 SoA 재할당
    public void Reallocate(int newNeuronCount)
    {
        if (newNeuronCount <= 0) newNeuronCount = 1;
        neuronCount = newNeuronCount;
        AllocateStateIfNeeded(neuronCount);
        ResetStateValues();
    }

    /// CSR 형태로 시냅스 설정
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

    /// 에지 리스트에서 시냅스 압축 구성
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

    /// 상태 초기화
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
    }

    /// 외부에서도 강제 스텝 가능 (테스트/배치용)
    public void StepOnce(float? customDtMs = null)
    {
        float dt = customDtMs ?? stepDtMs;
        LIFStepCpu.Step(State, neuronCount, dt, refractoryMs);

        // 감각 입력은 한 스텝 후 소모
        System.Array.Clear(State.externalInput, 0, neuronCount);
    }

    // ---- Internals ----
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
    }
}
