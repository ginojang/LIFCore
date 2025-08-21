
using System.Linq;
using UnityEngine;

using Legacy;

//using System.Drawing;


public class ArenaBuilder : MonoBehaviour
{
    [Header("Arena")]
    public float arenaSize = 30f;        // ����/���� (m)
    public float wallHeight = 3f;
    public float wallThickness = 0.5f;

    [Header("Camera (Top-Down)")]
    public float cameraHeight = 35f;     // ī�޶� Y ��ġ
    public float cameraMargin = 1.0f;    // ���һ����� ����

    [Header("Actors")]
    public Vector3 playerStart = new Vector3(-5f, 0.5f, 0f);
    public Vector3 impStart = new Vector3(5f, 0.5f, 0f);

    [Header("Colors")]
    public Color floorColor = new Color(0.12f, 0.12f, 0.12f);
    public Color wallColor = new Color(0.3f, 0.3f, 0.3f);
    public Color playerColor = new Color(0.2f, 0.6f, 1f);
    public Color impColor = new Color(0.9f, 0.35f, 0.25f);

    void Start()
    {
        BuildFloor();
        BuildWalls();
        var player = BuildCube("Player", playerStart, playerColor, true);
        BuildCube("Imp", impStart, impColor, false);
        SetupTopDownCamera(player.transform.position);
    }

    void BuildFloor()
    {
        // Unity �⺻ Plane�� 10��10 �� scale�� 30��30 ���߱�
        var floor = GameObject.Find("Floor") ?? GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        floor.transform.position = Vector3.zero;
        float scale = arenaSize / 10f;
        floor.transform.localScale = new Vector3(scale, 1f, scale);
        floor.transform.parent = gameObject.transform; 

        var rend = floor.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            rend.sharedMaterial.SetColor("_BaseColor", floorColor);
        }
        // �⺻ MeshCollider�� �پ� ����
        floor.isStatic = true;
    }

    void BuildWalls()
    {
        // 4�� ��: X���� 2��, Z���� 2��
        float half = arenaSize * 0.5f;
        float h = wallHeight;
        float t = wallThickness;

        BuildWall("Wall_North", new Vector3(0, h * 0.5f, half + t * 0.5f), new Vector3(arenaSize, h, t));
        BuildWall("Wall_South", new Vector3(0, h * 0.5f, -half - t * 0.5f), new Vector3(arenaSize, h, t));
        BuildWall("Wall_East", new Vector3(half + t * 0.5f, h * 0.5f, 0), new Vector3(t, h, arenaSize));
        BuildWall("Wall_West", new Vector3(-half - t * 0.5f, h * 0.5f, 0), new Vector3(t, h, arenaSize));
    }

    void BuildWall(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.Find(name) ?? GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        go.transform.parent = gameObject.transform;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Simple Lit"));
            rend.sharedMaterial.SetColor("_BaseColor", wallColor);
        }
        go.isStatic = true;
    }

    GameObject BuildCube(string name, Vector3 pos, Color color, bool isPlayer)
    {
        var go = GameObject.Find(name) ?? GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one; // 1m
        go.transform.parent = gameObject.transform;

        // ��Ƽ����
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            rend.sharedMaterial = mat;
        }

        // �浹/��Ʈ��
        if (isPlayer)
        {
            // ���� BoxCollider ������ ����(�ߺ� �浹 ����)
            var bc = go.GetComponent<BoxCollider>();
            if (bc) Destroy(bc);

            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.0f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0, 0.5f, 0);

            var ctrl = go.AddComponent<PlayerControllerTopDown>();
            ctrl.moveSpeed = 6f;

            // ����: �±� ����
            if (UnityEditorInternal.InternalEditorUtility.tags.Contains("Player")) go.tag = "Player";
        }
        else
        {
            // ������ �⺻ BoxCollider ����
            if (!go.TryGetComponent<Collider>(out _)) go.AddComponent<BoxCollider>();
            go.isStatic = false;

            go.AddComponent<ImpControllerLIF_OneShot>();            
        }

        CreateDirectionMarker(go, color);

        return go;
    }

    // ���� ť�� ����(+Z)���� 0.75m �տ�, 45�� ȸ���� �̴� ť�긦 �ڽ����� ����
    void CreateDirectionMarker(GameObject parent, Color baseColor)
    {
        var dir = GameObject.CreatePrimitive(PrimitiveType.Cube);
        dir.name = parent.name + "_Dir";
        dir.transform.SetParent(parent.transform, false);
        dir.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        dir.transform.localPosition = new Vector3(0f, 0f, 0.75f); // �������� ��¦ ����
        dir.transform.localRotation = Quaternion.Euler(0f, 45f, 0f); // Y�� 45��

        // �浹 ����(��Ĺ�)
        var col = dir.GetComponent<Collider>();
        if (col) Destroy(col);

        // ����: ��ü�� �� ȭ��Ʈ 30% ��� �� ���̰�
        var markerColor = Color.Lerp(baseColor, Color.white, 0.3f);

        var rend = dir.GetComponent<Renderer>();
        if (rend != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Simple Lit")
                     ?? Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard");
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
            else if (mat.HasProperty("_Color")) mat.SetColor("_Color", markerColor);
            rend.sharedMaterial = mat;
        }
    }

    void SetupTopDownCamera(Vector3 focus)
    {
        var cam = Camera.main;
        if (cam == null)
        {
            var camGO = new GameObject("Main Camera");
            cam = camGO.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.orthographic = true;

        float half = (arenaSize * 0.5f) + cameraMargin;
        float neededByWidth = half / Mathf.Max(0.0001f, cam.aspect); // ���ΰ� �߸��� �ʰ�
        cam.orthographicSize = Mathf.Max(half, neededByWidth);

        cam.transform.position = new Vector3(0, cameraHeight, 0);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.05f, 0.05f, 0.06f);
    }

}
