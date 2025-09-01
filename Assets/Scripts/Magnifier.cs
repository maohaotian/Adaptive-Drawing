using UnityEngine;
using UnityEngine.UI;

public class Magnifier : MonoBehaviour
{
    [SerializeField] private TexturePainter painter; // 你的主脚本
    [SerializeField] private RawImage magnifierImage; // 圆形RawImage
    [SerializeField] private int magnifierRadius = 300; // 放大镜半径（像素）
    [SerializeField] private float magnifierScale = 2f; // 放大倍数
    [SerializeField] private Material magnifierMaterial;

    private Texture2D magnifierTexture; // for CPU
    private RenderTexture magnifierRT; //for GPU
    private Vector3 meshSize;
    private Vector2 magnifierSize;
    void Start()
    {
        magnifierTexture = new Texture2D(magnifierRadius * 2, magnifierRadius * 2, TextureFormat.RGBA32, false);
        magnifierRT = new RenderTexture(magnifierRadius * 2, magnifierRadius * 2, 0, RenderTextureFormat.ARGB32);
        // magnifierImage.texture = magnifierTexture;
        magnifierImage.texture = magnifierRT;
        magnifierImage.material = null;
        meshSize = Vector3.Scale(painter.GetComponent<MeshFilter>().sharedMesh.bounds.size, painter.transform.lossyScale);
        magnifierSize = magnifierImage.rectTransform.rect.size * new Vector2(magnifierImage.rectTransform.lossyScale.x, magnifierImage.rectTransform.lossyScale.z);
        Debug.Log($"Mesh Size: {meshSize}, Magnifier Size: {magnifierSize}");
    }

    void setMagnifierCPU(Vector2Int centerPoint, Texture2D runtimeTexture)
    {
        if (!centerPoint.Equals(Vector2Int.zero))
        {
            int cx = centerPoint.x;
            int cy = centerPoint.y;
            int size = magnifierRadius * 2;
            Color[] pixels = new Color[size * size];

            for (int y = -magnifierRadius; y < magnifierRadius; y++)
            {
                for (int x = -magnifierRadius; x < magnifierRadius; x++)
                {
                    int px = x + magnifierRadius;
                    int py = y + magnifierRadius;
                    int idx = py * size + px;

                    float dist = Mathf.Sqrt(x * x + y * y);
                    if (dist > magnifierRadius)
                    {
                        pixels[idx] = Color.clear;
                        continue;
                    }

                    int sx = Mathf.Clamp(cx - Mathf.RoundToInt(x / magnifierScale), 0, runtimeTexture.width - 1);
                    int sy = Mathf.Clamp(cy - Mathf.RoundToInt(y / magnifierScale), 0, runtimeTexture.height - 1);
                    pixels[idx] = runtimeTexture.GetPixel(sx, sy);
                }
            }
            magnifierTexture.SetPixels(pixels);
            magnifierTexture.Apply();
        }
    }

    public void UpdateMagnifierCPU(Vector2Int centerPoint, Texture2D runtimeTexture, Vector3 worldPos)
    {
        setMagnifierCPU(centerPoint, runtimeTexture);

        // 转换到相机坐标系
        var cam = Camera.main;
        Vector3 camLocal = cam.transform.InverseTransformPoint(worldPos);

        // 微调 z 值（让放大镜更靠近相机，值可根据实际调整）
        camLocal.z -= 0.01f;

        // 转换回世界坐标
        Vector3 adjustedWorldPos = cam.transform.TransformPoint(camLocal);

        // 设置 RawImage 的世界坐标
        magnifierImage.rectTransform.position = adjustedWorldPos;
    }

    public void UpdateMagnifierGPU(Vector2Int centerPoint, Texture2D runtimeTexture, Vector3 worldPos,float scale)
    {
        magnifierScale = scale;
        // 设置 Shader 参数
        Vector2 uvCenter = new Vector2(
            (float)centerPoint.x / runtimeTexture.width,
            (float)centerPoint.y / runtimeTexture.height
        );

        Vector2 uvScale = new Vector2(
        magnifierSize.x/meshSize.x,
        magnifierSize.y/meshSize.z 
    );
        magnifierMaterial.SetVector("_Center", uvCenter);
        // magnifierMaterial.SetFloat("_Radius", (float)magnifierRadius / runtimeTexture.width); // 归一化
        magnifierMaterial.SetFloat("_Radius", (float)0.5);
        magnifierMaterial.SetFloat("_Scale", magnifierScale);
        magnifierMaterial.SetVector("_UVScale", uvScale); // 传入UV缩放

        // 把 runtimeTexture 拷贝到 magnifierRT
        Graphics.Blit(runtimeTexture, magnifierRT, magnifierMaterial);



        // 设置 RawImage 的世界坐标（如前面方案）
        // 转换到相机坐标系
        var cam = Camera.main;
        Vector3 camLocal = cam.transform.InverseTransformPoint(worldPos);

        // 微调 z 值（让放大镜更靠近相机，值可根据实际调整）
        camLocal.z -= 0.01f;

        // 转换回世界坐标
        Vector3 adjustedWorldPos = cam.transform.TransformPoint(camLocal);

        // 设置 RawImage 的世界坐标
        magnifierImage.rectTransform.position = adjustedWorldPos;
    }

    public void CloseMagnifier()
    {
        magnifierImage.enabled = false;
    }

    public void ShowMagnifier()
    {
        magnifierImage.enabled = true;
    }
}