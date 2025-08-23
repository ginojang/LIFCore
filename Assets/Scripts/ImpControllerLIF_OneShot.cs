using Neuro.LIF;
using UnityEngine;
using UnityEngine.InputSystem;


public class ImpControllerLIF_OneShot : MonoBehaviour
{
    [Header("Target")]
    public Transform player;        // 비워두면 "Player" 자동 검색
                                    //
    public float deadZoneDeg = 5f;  // |e|<deadzone 이면 입력 0

    LIFNetwork      lifNetwork;
    LIFState        lifState;
    LIFTickStats    lifTickStats;


    float GetAngleWithPlayer()
    {
        // 1) 각도 오차 계산 (XZ 평면)
        Vector3 a = transform.forward; a.y = 0f; a.Normalize();
        Vector3 b = (player.position - transform.position); b.y = 0f; b.Normalize();
        if (b.sqrMagnitude < 1e-6f) 
        { 
            Debug.Log("[OneShotLIF] Player too close."); 
            enabled = false; 
            return 0.0f; 
        }

        float e = Vector3.SignedAngle(a, b, Vector3.up); // +: 왼쪽(CCW), -: 오른쪽(CW)

        return e;
    }

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
            enabled = false; 
            return;
        }

        //
        // 초기화
        // 0=S_L, 1=S_R, 2=M_L, 3=M_R
        lifNetwork = new LIFNetwork(
            type: new[] { NeuronType.Sensory, NeuronType.Sensory, NeuronType.Motor, NeuronType.Motor },
            threshold: new[] { 0.40f, 0.40f, 0.65f, 0.65f },
            leak: new[] { 2.0f, 2.0f, 6.0f, 6.0f },

            // S0: 2개(S0->M2, S0->M3[learn]), S1: 2개(S1->M3, S1->M2[learn])
            synapseStartIndex: new[] { 0, 2, 4, 4 },
            synapseCount: new[] { 2, 2, 0, 0 },

            synapsePost: new[] { 2, 3, 3, 2 },
            synapseWeight: new[] { +0.9f, 0.0f, +0.9f, 0.0f }
        // SynapseLearnable: [false,true,false,true] 로 교차선만 학습 등…
        );

        /*  종말 자기억제, 상호 억제 추가한 경우
        lifNetwork = new LIFNetwork(
            type: new[] { NeuronType.Sensory, NeuronType.Sensory, NeuronType.Motor, NeuronType.Motor },
            threshold: new[] { 0.40f, 0.40f, 0.65f, 0.65f },
            leak: new[] { 2.0f, 2.0f, 6.0f, 6.0f },
            synapseStartIndex: new[] { 0, 1, 2, 4 },
            synapseCount: new[] { 1, 1, 2, 2 },
            synapsePost: new[] { 2, 3, 3, 2, 2, 3 },
            synapseWeight: new[] { +0.9f, +0.9f, -0.8f, -0.3f, -0.8f, -0.3f }
        );*/

        lifState = new LIFState(lifNetwork.Count);
        lifTickStats = new LIFTickStats();

        //
        StepLif();
    }


    private void FixedUpdate()
    { 
    }

    void StepLif()
    {
        //
        float e = GetAngleWithPlayer();

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

        // 입력 주입
        lifState.ExternalInput[0] = L; // left
        lifState.ExternalInput[1] = R; // right

        // 원샷 2패스
        LIFStepper.StepTwoPassOneShot(lifNetwork, lifState, dtMs: 10f, refractoryMs: 0f, ref lifTickStats);

        // 결과 읽기
        //float mL = lifState.MotorFiring[2];
        //float mR = lifState.MotorFiring[3];

        

        // 2패스 후
        float mL = lifState.MotorFiring[2], mR = lifState.MotorFiring[3];
        float potL = lifState.Potential[2], potR = lifState.Potential[3];
        float turnSign = Mathf.Sign((mL - mR) != 0 ? (mL - mR) : (potL - potR));

        Debug.Log($"Result in FixedUpdate >>  mL:{mL} mR{mR}");
        Debug.Log($"Result in FixedUpdate >>  potL:{potL} potR{potR}");
        Debug.Log($"Result in FixedUpdate >>  turnSign:{turnSign} ");
    }
}
