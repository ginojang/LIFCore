// LIFMiniHud.cs
using UnityEngine;


namespace Legacy
{
    public class LIFMiniHud : MonoBehaviour
    {
        // ----------------------- Raster / HUD -----------------------
        [Header("Raster Settings")]
        public int history = 256;                       // 가로(시간)
        public int[] watchNeurons = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        public int pixelPerCell = 2;                    // 확대 배율
        public Color bgColor = new Color(0.08f, 0.08f, 0.08f, 1f);
        public Color offColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        public Color onColor = Color.white;

        [Header("Motor EMA")]
        public float motorEmaTau = 0.4f;                // seconds
        private float[] motorEma;                       // lazy alloc

        [Header("GUI Layout")]
        public Vector2 topLeft = new Vector2(10, 10);
        public int lineHeight = 18;

        // ----------------------- Debug Inputs -----------------------
        [Header("Debug Input (built-in)")]
        public bool enableDebugKeys = true;             // Space/R 등
        public int pulseSensoryIndex = 0;
        public float pulseAmplitude = 1.0f;
        public KeyCode pulseKey = KeyCode.Space;        // 감각 뉴런 펄스
        public KeyCode resetKey = KeyCode.R;            // 전위/불응기 초기화
        public KeyCode toggleHelpKey = KeyCode.H;       // 도움말 토글
        public bool showHelpOverlay = true;

        // ----------------------- Internals -----------------------
        private Texture2D rasterTex;
        private Color32[] rasterPixels;
        private int rows, texW, texH, writeX;

        void Start()
        {
            rows = Mathf.Max(1, watchNeurons != null ? watchNeurons.Length : 0);
            texW = Mathf.Max(8, history);
            texH = Mathf.Max(1, rows);

            rasterTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            rasterTex.wrapMode = TextureWrapMode.Clamp;
            rasterPixels = rasterTex.GetPixels32();

            for (int i = 0; i < rasterPixels.Length; i++)
                rasterPixels[i] = bgColor;

            rasterTex.SetPixels32(rasterPixels);
            rasterTex.Apply(false, false);
        }

        void Update()
        {
            var mgr = LIFStateManager.I;
            if (!enableDebugKeys || mgr == null) return;

            if (Input.GetKeyDown(pulseKey))
            {
                mgr.PulseExternal(pulseSensoryIndex, pulseAmplitude);
            }
            if (Input.GetKeyDown(resetKey))
            {
                mgr.ResetAllPotentials(0f);
            }
            if (Input.GetKeyDown(toggleHelpKey))
            {
                showHelpOverlay = !showHelpOverlay;
            }
        }

        void LateUpdate()
        {
            var mgr = LIFStateManager.I;
            var st = mgr != null ? mgr.State : null;
            if (mgr == null || st == null) return;

            // 1) 스파이크 래스터 1열 갱신
            var flags = mgr.SpikedThisTick;
            if (flags != null && watchNeurons != null && rasterPixels != null)
            {
                // 컬럼 초기화
                for (int y = 0; y < texH; y++)
                    rasterPixels[y * texW + writeX] = (Color32)offColor;

                for (int r = 0; r < rows; r++)
                {
                    int idx = watchNeurons[r];
                    bool fired = (idx >= 0 && idx < flags.Length) && flags[idx];
                    rasterPixels[r * texW + writeX] = fired ? (Color32)onColor : (Color32)offColor;
                }

                rasterTex.SetPixels32(rasterPixels);
                rasterTex.Apply(false, false);
                writeX = (writeX + 1) % texW;
            }

            // 2) motor EMA 갱신 (lazy alloc)
            if (st.motorFiring != null)
            {
                if (motorEma == null || motorEma.Length != st.motorFiring.Length)
                    motorEma = new float[st.motorFiring.Length];

                float dt = Mathf.Max(0.0001f, Time.deltaTime);
                float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, motorEmaTau));

                int len = Mathf.Min(motorEma.Length, st.motorFiring.Length);
                for (int i = 0; i < len; i++)
                {
                    float x = st.motorFiring[i]; // tick당 +1; 매니저에서 감쇠
                    motorEma[i] = Mathf.Lerp(motorEma[i], x, alpha);
                }
            }
        }

        void OnGUI()
        {
            var mgr = LIFStateManager.I;
            if (mgr == null) return;

            float x = topLeft.x;
            float y = topLeft.y;

            var stats = mgr.Stats;
            GUI.Label(new Rect(x, y, 900, lineHeight),
                $"Spikes:{stats.Spikes}  Syn:{stats.SynUpdates}  Pot[{stats.PotMin:F2},{stats.PotMax:F2}]  NaN:{(stats.HadNaNOrInf ? "Y" : "N")}");
            y += lineHeight + 4;

            // Raster
            if (rasterTex != null)
            {
                int w = texW * pixelPerCell;
                int h = texH * pixelPerCell;
                GUI.DrawTexture(new Rect(x, y, w, h), rasterTex, ScaleMode.StretchToFill, false);
                y += h + 6;
            }

            // Motor 히트바 (처음 16개만)
            if (motorEma != null)
            {
                int show = Mathf.Min(16, motorEma.Length);
                for (int i = 0; i < show; i++)
                {
                    float v = Mathf.Clamp01(motorEma[i] * 0.1f);
                    GUI.Box(new Rect(x, y + i * 12, 200 * v, 10), GUIContent.none);
                }
                y += show * 12 + 6;
            }

            // Help overlay
            if (showHelpOverlay)
            {
                string help = $"[LIFMiniHud]  SPACE: Pulse sensory[{pulseSensoryIndex}] (+{pulseAmplitude})   " +
                              $"R: Reset potentials   H: Toggle Help";
                GUI.Label(new Rect(x, y, 1000, lineHeight), help);
            }
        }
    }


}