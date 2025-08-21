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
        public float pulseAmplitude = 1.2f;   // 1ƽ�� �Է� ũ��
        public float pulseWidthMs = 1.0f;     // �� ms ���� �Է� ��������
        public bool autoPulse = false;
        public float autoPulsePeriodMs = 80f; // �ڵ� �޽� �ֱ�(ms)

        [Header("Burst Mode")]
        public KeyCode burstKey = KeyCode.B;
        public int burstCount = 3;
        public float burstIntervalMs = 8f;

        [Header("HUD/Logging")]
        public bool showHUD = true;
        public int hudFontSize = 16;
        public bool logCSV = false;

        // --- internals ---
        float _periodAccumMs;        // �ڵ� �޽� Ÿ�̸�(ms)
        int _burstLeft;            // ���� ����Ʈ Ƚ��
        float _burstAccumMs;         // ����Ʈ ���� Ÿ�̸�(ms)

        float _lastMotorSpikes;      // �̹� ������ ���� ������ũ
        int _totalSpikes;          // ���� ������ũ ��

        int _awaitStartTick = -1;  // ���� ����: �Է� ���� ƽ
        float _lastLatencyMs;        // �ֱ� �Է¡潺����ũ ����(ms)

        StringBuilder _csv;
        string _csvPath;

        void Start()
        {
            var m = LIFManager.Instance;

            // ��Ʈ��ũ 2���� ����
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

            // === ��Ÿ�� �Ķ���� ���� (ƽ ��� �ݿ��� �ƴ�: ���⼱ ���� ����) ===
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.Plus))
            {
                synWeight += 0.1f;
                // ���� ���� �����̹Ƿ� 0->1 �ϳ���: pre=sensory, post=motor
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

            // === �Է� Ʈ���� (��� ť�� ������) ===
            // ���� �޽�(�� widthMs ��ŭ ƽ���� ��ȯ�Ͽ� �� ���� ������)
            if (Input.GetKeyDown(pulseKey))
                SchedulePulseSpan(m, pulseAmplitude, pulseWidthMs);

            // ����Ʈ ����
            if (Input.GetKeyDown(burstKey))
            {
                _burstLeft = Mathf.Max(1, burstCount);
                _burstAccumMs = 0f;
            }

            // �ڵ� �޽�
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

            // ����Ʈ ����(���ݸ��� 1�޽� ������)
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

            // === ��� ����(�б⸸, �ù��� Fixed������) ===
            _lastMotorSpikes = m.ReadMotorFiring(motor, clearAfterRead: true);
            if (_lastMotorSpikes >= 1f && _awaitStartTick >= 0)
            {
                int ticks = m.SimTick - _awaitStartTick + 1;
                if (ticks < 0) ticks = 0;
                _lastLatencyMs = ticks * Mathf.Max(m.stepDtMs, 0.0001f);
                _awaitStartTick = -1;
                _totalSpikes += (int)_lastMotorSpikes;
            }

            // === �α� ===
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
            // widthMs�� ƽ���� ��ȯ�Ͽ� �� ���� ������(��������)
            int durTicks = Mathf.Max(1, Mathf.CeilToInt(widthMs / Mathf.Max(0.0001f, m.stepDtMs)));
            int startTick = m.SimTick + 1;
            for (int t = 0; t < durTicks; t++)
                m.EnqueueAtTick(startTick + t, sensory, amp);

            // ���� ����: ù ������ ����Ǵ� ƽ�� ��������
            _awaitStartTick = startTick;
        }
    }


}