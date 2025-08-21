using UnityEngine;
using System.IO;
using System.Text;


namespace Legacy
{
    public class LIFQuickDemoPro : MonoBehaviour
    {
        [Header("Network")]
        public int sensory = 0;
        public int motor = 1;
        public float initialThreshold = 1.0f;
        public float initialLeak = 0.0f;
        public float synWeight = 1.2f; // sensory -> motor

        [Header("Pulse Control")]
        public KeyCode pulseKey = KeyCode.Space;
        public float pulseAmplitude = 1.2f;   // 1틱당 입력 크기
        public float pulseWidthMs = 1.0f;     // 몇 ms 동안 입력 유지할지
        public bool autoPulse = false;
        public float autoPulsePeriodMs = 80f; // 자동 펄스 주기(ms)

        [Header("Burst Mode")]
        public KeyCode burstKey = KeyCode.B;
        public int burstCount = 3;
        public float burstIntervalMs = 8f;

        [Header("HUD/Logging")]
        public bool showHUD = true;
        public int hudFontSize = 16;
        public bool logCSV = false;

        // --- internals ---
        float _periodAccumMs;        // 자동 펄스 타이머(ms)
        int _burstLeft;            // 남은 버스트 횟수
        float _burstAccumMs;         // 버스트 간격 타이머(ms)

        float _lastMotorSpikes;      // 이번 프레임 수거 스파이크
        int _totalSpikes;          // 누적 스파이크 수

        int _awaitStartTick = -1;  // 지연 측정: 입력 시작 틱
        float _lastLatencyMs;        // 최근 입력→스파이크 지연(ms)

        StringBuilder _csv;
        string _csvPath;

        void Start()
        {
            var m = LIFManager.Instance;

            // 네트워크 2뉴런 구성
            m.Reallocate(2);
            m.SetNeuronType(sensory, NeuronType.Sensory);
            m.SetNeuronType(motor, NeuronType.Motor);

            m.State.threshold[sensory] = initialThreshold;
            m.State.threshold[motor] = initialThreshold;
            m.State.leak[sensory] = initialLeak;
            m.State.leak[motor] = initialLeak;

            m.BuildSynapsesFromEdges(new (int, int, float)[]{
            (sensory, motor, synWeight)
        });

            if (logCSV)
            {
                _csv = new StringBuilder();
                _csv.AppendLine("frame,t_ms,auto,burst,p0,p1,spikes,latency_ms,thr,leak,w,refractory");
                _csvPath = Path.Combine(Application.persistentDataPath, "lif_switch_demo.csv");
            }
        }

        void Update()
        {
            var m = LIFManager.Instance;
            if (m == null) return;

            // === 런타임 파라미터 조정 (틱 경계 반영이 아님: 여기선 값만 세팅) ===
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus))
            {
                synWeight += 0.1f;
                // 단일 엣지 구조이므로 0->1 하나뿐: pre=sensory, post=motor
                RebuildEdge(m);
            }
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                synWeight -= 0.1f;
                RebuildEdge(m);
            }
            if (Input.GetKeyDown(KeyCode.LeftBracket)) m.State.threshold[motor] = Mathf.Max(0f, m.State.threshold[motor] - 0.1f);
            if (Input.GetKeyDown(KeyCode.RightBracket)) m.State.threshold[motor] += 0.1f;
            if (Input.GetKeyDown(KeyCode.Comma)) m.refractoryMs = Mathf.Max(0f, m.refractoryMs - 0.5f);
            if (Input.GetKeyDown(KeyCode.Period)) m.refractoryMs += 0.5f;

            // === 입력 트리거 (모두 큐로 스케줄) ===
            // 수동 펄스(폭 widthMs 만큼 틱으로 변환하여 한 번에 스케줄)
            if (Input.GetKeyDown(pulseKey))
                SchedulePulseSpan(m, pulseAmplitude, pulseWidthMs);

            // 버스트 시작
            if (Input.GetKeyDown(burstKey))
            {
                _burstLeft = Mathf.Max(1, burstCount);
                _burstAccumMs = 0f;
            }

            // 자동 펄스
            if (Input.GetKeyDown(KeyCode.T)) autoPulse = !autoPulse;
            if (autoPulse)
            {
                _periodAccumMs += Time.deltaTime * 1000f;
                while (_periodAccumMs >= autoPulsePeriodMs)
                {
                    _periodAccumMs -= autoPulsePeriodMs;
                    SchedulePulseSpan(m, pulseAmplitude, pulseWidthMs);
                }
            }

            // 버스트 진행(간격마다 1펄스 스케줄)
            if (_burstLeft > 0)
            {
                _burstAccumMs += Time.deltaTime * 1000f;
                if (_burstAccumMs >= burstIntervalMs)
                {
                    _burstAccumMs -= burstIntervalMs;
                    SchedulePulseSpan(m, pulseAmplitude, pulseWidthMs);
                    _burstLeft--;
                }
            }

            // === 출력 수거(읽기만, 시뮬은 Fixed에서만) ===
            _lastMotorSpikes = m.ReadMotorFiring(motor, clearAfterRead: true);
            if (_lastMotorSpikes >= 1f && _awaitStartTick >= 0)
            {
                int ticks = m.SimTick - _awaitStartTick + 1;
                if (ticks < 0) ticks = 0;
                _lastLatencyMs = ticks * Mathf.Max(m.stepDtMs, 0.0001f);
                _awaitStartTick = -1;
                _totalSpikes += (int)_lastMotorSpikes;
            }

            // === 로깅 ===
            if (logCSV)
            {
                float p0 = m.State.potential[sensory];
                float p1 = m.State.potential[motor];
                int autoFlag = autoPulse ? 1 : 0;
                int burstFlag = (_burstLeft > 0) ? 1 : 0;

                _csv.AppendLine($"{Time.frameCount},{Time.time * 1000f:F2},{autoFlag},{burstFlag},{p0:F4},{p1:F4},{_lastMotorSpikes},{_lastLatencyMs:F3},{m.State.threshold[motor]:F3},{m.State.leak[motor]:F3},{synWeight:F3},{m.refractoryMs:F2}");
                if (_csv.Length > 64 * 1024) File.WriteAllText(_csvPath, _csv.ToString());
            }
        }

        void OnDestroy()
        {
            if (logCSV && _csv != null)
            {
                File.WriteAllText(_csvPath, _csv.ToString());
                Debug.Log($"[LIF] CSV saved: {_csvPath}");
            }
        }

        void OnGUI()
        {
            if (!showHUD) return;
            var m = LIFManager.Instance; if (m == null) return;

            var style = new GUIStyle(GUI.skin.label) { fontSize = hudFontSize };
            float p0 = m.State.potential[sensory];
            float p1 = m.State.potential[motor];

            GUILayout.BeginArea(new Rect(16, 16, 560, 300), GUI.skin.box);
            GUILayout.Label("LIF Switch Demo (Pro)", style);
            GUILayout.Space(4);
            GUILayout.Label($"Pulse: Space({pulseAmplitude:F2} amp, {pulseWidthMs:F1} ms) | Auto[{(autoPulse ? "ON" : "OFF")} {autoPulsePeriodMs:F0}ms]  (T) | Burst[{burstCount}@{burstIntervalMs:F0}ms] (B)", style);
            GUILayout.Label($"W={synWeight:F2}  |  Thr={m.State.threshold[motor]:F2}  |  Leak={m.State.leak[motor]:F2}  |  Refractory={m.refractoryMs:F2}ms  |  Tick={m.SimTick}", style);
            GUILayout.Space(6);
            GUILayout.Label($"Potentials  p_sens={p0:F3}  p_motor={p1:F3}", style);
            GUILayout.Label($"Spikes  last={_lastMotorSpikes}  total={_totalSpikes}  |  Latency={_lastLatencyMs:F2} ms", style);
            GUILayout.Space(6);
            GUILayout.Label("Hotkeys: [+]/[-] weight,  [/ ] threshold,  [,]/[.] refractory,  T auto,  B burst", style);
            GUILayout.EndArea();
        }

        // ---- Helpers ----
        void RebuildEdge(LIFManager m)
        {
            m.BuildSynapsesFromEdges(new (int, int, float)[] { (sensory, motor, synWeight) });
        }

        void SchedulePulseSpan(LIFManager m, float amp, float widthMs)
        {
            // widthMs를 틱으로 변환하여 한 번에 스케줄(결정성↑)
            int durTicks = Mathf.Max(1, Mathf.CeilToInt(widthMs / Mathf.Max(0.0001f, m.stepDtMs)));
            int startTick = m.SimTick + 1;
            for (int t = 0; t < durTicks; t++)
                m.EnqueueAtTick(startTick + t, sensory, amp);

            // 지연 측정: 첫 주입이 적용되는 틱을 기준으로
            _awaitStartTick = startTick;
        }
    }


}