using UnityEngine;


namespace Legacy
{
    /// <summary>
    /// 디버그용: LIF를 Start()에서 "딱 한 번"만 돌려서
    /// 플레이어를 기준으로 좌/우 판정하고(2S->2M),
    /// 로그 출력 + (옵션) 한 번만 회전 적용.
    /// </summary>
    public class ImpControllerLIF_OneShot : MonoBehaviour
    {
        [Header("Target")]
        public Transform player;                // 비워두면 "Player" 자동 검색

        [Header("Angle → Input (once)")]
        public float deadZoneDeg = 5f;        // |e|<deadzone 이면 입력 0
        public float angleNormDeg = 60f;       // |e|=angleNorm일 때 입력 1.0
        public float inputGain = 0.8f;      // 센서 입력 게인

        [Header("One-shot rotate (optional)")]
        public bool applyOneShotRotation = true;
        public float rotateClampDeg = 30f; // 한 번에 돌릴 최대 각도

        [Header("LIF step params")]
        public float dtMs = 10f;
        public float refractoryMs = 0f;

        // 내부 LIF
        LIFState s;
        LIFTickStats stats;

        void Start()
        {
            // 0) 타겟 찾기
            if (player == null)
            {
                var p = GameObject.Find("Player");
                if (p != null) player = p.transform;
            }
            if (player == null)
            {
                Debug.LogWarning("[OneShotLIF] Player not found.");
                enabled = false; return;
            }

            // 1) 각도 오차 계산 (XZ 평면)
            Vector3 a = transform.forward; a.y = 0f; a.Normalize();
            Vector3 b = (player.position - transform.position); b.y = 0f; b.Normalize();
            if (b.sqrMagnitude < 1e-6f) { Debug.Log("[OneShotLIF] Player too close."); enabled = false; return; }

            float e = Vector3.SignedAngle(a, b, Vector3.up); // +: 왼쪽(CCW), -: 오른쪽(CW)

            // 2) e → (Left, Right) 입력 선택
            float L = 0f, R = 0f;
            if (Mathf.Abs(e) >= deadZoneDeg)
            {
                if (e < 0)
                    L = 1.0f;
                else
                    R = 1.0f;
            }
            else
            {
                return;
            }

            // 3) 초소형 LIF 그래프(2S→2M) 구성
            //    index: 0=S_Left, 1=S_Right, 2=M_TurnL, 3=M_TurnR
            s = new LIFState
            {
                potential = new float[4],
                threshold = new float[4],
                leak = new float[4],
                refractory = new float[4],
                type = new NeuronType[4],
                synStartIndex = new int[4],
                synCount = new int[4],
                synPost = new int[6],   // S->M 2, M<->M 억제 2, M 자기억제 2
                synWeight = new float[6],
                externalInput = new float[4],
                motorFiring = new float[4],
            };

            s.type[0] = NeuronType.Sensory; s.type[1] = NeuronType.Sensory;
            s.type[2] = NeuronType.Motor; s.type[3] = NeuronType.Motor;

            // 덜 예민하게 + 빠른 식힘
            s.threshold[0] = 0.40f; s.threshold[1] = 0.40f; // S
            s.threshold[2] = 0.65f; s.threshold[3] = 0.65f; // M
            s.leak[0] = 2.0f; s.leak[1] = 2.0f;             // 센서 완만
            s.leak[2] = 6.0f; s.leak[3] = 6.0f;             // 모터 빨리 감쇠

            int c = 0;
            // S_Left -> M_TurnL
            s.synStartIndex[0] = c;
            s.synPost[c] = 2; s.synWeight[c] = +0.9f; c++; s.synCount[0] = 1;
            // S_Right -> M_TurnR
            s.synStartIndex[1] = c;
            s.synPost[c] = 3; s.synWeight[c] = +0.9f; c++; s.synCount[1] = 1;
            // M_TurnL -> [M_TurnR inhibit, self-inhibit]
            s.synStartIndex[2] = c;
            s.synPost[c] = 3; s.synWeight[c] = -0.8f; c++;
            s.synPost[c] = 2; s.synWeight[c] = -0.3f; c++;
            s.synCount[2] = 2;
            // M_TurnR -> [M_TurnL inhibit, self-inhibit]
            s.synStartIndex[3] = c;
            s.synPost[c] = 2; s.synWeight[c] = -0.8f; c++;
            s.synPost[c] = 3; s.synWeight[c] = -0.3f; c++;
            s.synCount[3] = 2;

            // 4) "한 번만" 주입 + 스텝 2회(전도→판정)
            s.externalInput[0] = L;
            s.externalInput[1] = R;

            // s.externalInput[0] = L;  s.externalInput[1] = R; // (주입)
            var stats = new LIFTickStats();

            LIFStepCpu.StepTwoPassOneShot(s, 4, dtMs, refractoryMs, ref stats,
                clearExternalAfterFirst: true,
                spikedFirst: null,
                spikedSecond: null);

            // 여기서 s.motorFiring[2], s.motorFiring[3] 읽으면 최종 좌/우 판정 완료


            // 결과 읽기
            float mL = s.motorFiring[2];
            float mR = s.motorFiring[3];
            float potL = s.potential[2];
            float potR = s.potential[3];

            Debug.Log($"[OneShotLIF] e={e:F1}°, L_in={L:F2}, R_in={R:F2} | " +
                      $"M_L fire={mL}, M_R fire={mR} | potL={potL:F2}, potR={potR:F2}");

            // 5) (옵션) 한 번만 회전 적용

            if (applyOneShotRotation)
            {
                float turnSign = Mathf.Sign((mL - mR) != 0 ? (mL - mR) : (potL - potR));
                float angle = Mathf.Clamp(e, -rotateClampDeg, rotateClampDeg); // 과하지만 않게
                if (turnSign != 0f)
                    transform.Rotate(0f, angle * turnSign, 0f, Space.World);
            }

            // e는 SignedAngle(forward, toPlayer, up)
            //transform.Rotate(0f, e, 0f, Space.World);   // => 곧바로 플레이어 정면

            // 루프 완전 차단
            enabled = false;
        }
    }

}