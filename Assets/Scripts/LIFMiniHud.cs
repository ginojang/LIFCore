// LIFMiniHud.cs
using UnityEngine;

public class LIFMiniHud : MonoBehaviour
{
	[Header("References")]
	public LIFState state;                 // �ùķ����Ϳ��� ����
	public LIFTickStats stats;             // �ùķ����Ϳ��� ref ���� �� ����
	public bool[] spikedThisTick;          // StepWithFlags ȣ�� �� ����

	[Header("Raster Settings")]
	public int history = 256;              // ����(�ð�)
	public int[] watchNeurons = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 }; // �Ҽ���
	public int pixelPerCell = 2;           // Ȯ�� ����
	public Color bgColor = new Color(0.08f, 0.08f, 0.08f, 1f);
	public Color offColor = new Color(0.15f, 0.15f, 0.15f, 1f);
	public Color onColor = Color.white;

	[Header("Motor EMA")]
	public float motorEmaTau = 0.4f;       // s
	private float[] motorEma;

	[Header("GUI Layout")]
	public Vector2 topLeft = new Vector2(10, 10);
	public int lineHeight = 18;

	// ���� ����
	private Texture2D rasterTex;
	private Color32[] rasterPixels;
	private int rows;
	private int texW, texH;
	private int writeX; // ��ȯ Ŀ��

	void Start()
	{
		rows = Mathf.Max(1, watchNeurons != null ? watchNeurons.Length : 0);
		texW = Mathf.Max(8, history);
		texH = Mathf.Max(1, rows);

		rasterTex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
		rasterTex.wrapMode = TextureWrapMode.Clamp;
		rasterPixels = rasterTex.GetPixels32();

		// �ʱ�ȭ
		for (int i = 0; i < rasterPixels.Length; i++)
			rasterPixels[i] = bgColor;
		rasterTex.SetPixels32(rasterPixels);
		rasterTex.Apply(false, false);

		if (state != null && state.motorFiring != null)
			motorEma = new float[state.motorFiring.Length];
	}

	void LateUpdate()
	{
		// 1) ������ũ ������ 1 �� ���� (spikedThisTick�� ������� �÷� ���)
		if (spikedThisTick != null && watchNeurons != null && rasterPixels != null)
		{
			// bg�� �÷� �ʱ�ȭ
			for (int y = 0; y < texH; y++)
				rasterPixels[y * texW + writeX] = (Color32)offColor;

			for (int r = 0; r < rows; r++)
			{
				int idx = watchNeurons[r];
				bool fired = (idx >= 0 && idx < spikedThisTick.Length) && spikedThisTick[idx];
				rasterPixels[r * texW + writeX] = fired ? (Color32)onColor : (Color32)offColor;
			}
			rasterTex.SetPixels32(rasterPixels);
			rasterTex.Apply(false, false);

			writeX = (writeX + 1) % texW;
		}

		// 2) motor EMA ���� (��ȭ�� ����ġ)
		if (state != null && state.motorFiring != null && motorEma != null)
		{
			// dt ~ Time.deltaTime; EMA ���
			float dt = Mathf.Max(0.0001f, Time.deltaTime);
			float alpha = 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, motorEmaTau));

			for (int i = 0; i < motorEma.Length; i++)
			{
				float x = state.motorFiring[i]; // tick�� +1 �ϰ�, �ܺο��� ���� or �����층 ����
				motorEma[i] = Mathf.Lerp(motorEma[i], x, alpha);
			}
		}
	}

	void OnGUI()
	{
		float x = topLeft.x;
		float y = topLeft.y;

		GUI.Label(new Rect(x, y, 800, lineHeight),
			$"Spikes:{stats.Spikes}  Syn:{stats.SynUpdates}  Pot[{stats.PotMin:F2},{stats.PotMax:F2}]  NaN:{(stats.HadNaNOrInf ? "Y" : "N")}");
		y += lineHeight + 4;

		// Raster (Ȯ�� ����)
		if (rasterTex != null)
		{
			int w = texW * pixelPerCell;
			int h = texH * pixelPerCell;
			GUI.DrawTexture(new Rect(x, y, w, h), rasterTex, ScaleMode.StretchToFill, false);
			y += h + 6;
		}

		// Motor ��Ʈ�� (ó�� 16����)
		if (motorEma != null)
		{
			int show = Mathf.Min(16, motorEma.Length);
			for (int i = 0; i < show; i++)
			{
				float v = Mathf.Clamp01(motorEma[i] * 0.1f); // �������� ��Ȳ�� �°�
				GUI.Box(new Rect(x, y + i * 12, 200 * v, 10), GUIContent.none);
			}
		}
	}
}
