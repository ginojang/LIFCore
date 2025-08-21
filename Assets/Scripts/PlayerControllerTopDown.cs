using UnityEngine;

namespace Legacy
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerControllerTopDown : MonoBehaviour
    {
        [Header("Movement")]
        public float moveSpeed = 6f;           // W/S ���� �̵� �ӵ� (m/s)
        public float rotationSpeedDeg = 180f;  // A/D �Ǵ� ��/�� ȸ�� �ӵ� (deg/s)

        [Header("Gravity")]
        public float gravity = -30f;
        public float groundSnap = -1f;         // ���� �� ��¦ ������

        private CharacterController cc;
        private float vy; // ���� �ӵ�

        void Awake()
        {
            cc = GetComponent<CharacterController>();
        }

        void Update()
        {
            // �Է�
            float turn = Input.GetAxisRaw("Horizontal"); // A/D, ��/��  -> ȸ��
            float thrust = Input.GetAxisRaw("Vertical");   // W/S, ��/��  -> ��/��

            // 1) ȸ�� (Y��)
            if (Mathf.Abs(turn) > 0f)
                transform.Rotate(0f, turn * rotationSpeedDeg * Time.deltaTime, 0f, Space.World);

            // 2) ��/�� �̵� (���� �ٶ󺸴� forward ����)
            Vector3 move = transform.forward * (thrust * moveSpeed);

            // 3) �߷�/����
            if (cc.isGrounded && vy < 0f) vy = groundSnap;
            vy += gravity * Time.deltaTime;

            // 4) ���� �̵�
            Vector3 vel = new Vector3(move.x, vy, move.z);
            cc.Move(vel * Time.deltaTime);
        }
    }

}