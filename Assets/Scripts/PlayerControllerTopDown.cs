using UnityEngine;

namespace Legacy
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerControllerTopDown : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 6f;           // W/S 전후 이동 속도 (m/s)
        public float rotationSpeedDeg = 180f;  // A/D 또는 ←/→ 회전 속도 (deg/s)

        [Header("Gravity")]
        public float gravity = -30f;
        public float groundSnap = -1f;         // 접지 시 살짝 눌러줌

        private CharacterController cc;
        private float vy; // 수직 속도

        void Awake()
        {
            cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            // 입력
            float turn = Input.GetAxisRaw("Horizontal"); // A/D, ←/→  -> 회전
            float thrust = Input.GetAxisRaw("Vertical");   // W/S, ↑/↓  -> 전/후

            // 1) 회전 (Y축)
            if (Mathf.Abs(turn) > 0f)
                transform.Rotate(0f, turn * rotationSpeedDeg * Time.deltaTime, 0f, Space.World);

            // 2) 전/후 이동 (현재 바라보는 forward 기준)
            Vector3 move = transform.forward * (thrust * moveSpeed);

            // 3) 중력/접지
            if (cc.isGrounded && vy < 0f) vy = groundSnap;
            vy += gravity * Time.deltaTime;

            // 4) 최종 이동
            Vector3 vel = new Vector3(move.x, vy, move.z);
            cc.Move(vel * Time.deltaTime);
        }
    }

}