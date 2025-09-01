using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

[RequireComponent(typeof(Renderer))]
public class TexturePainter : MonoBehaviour
{
    [Flags]
    public enum Brush
    {
        None = 0,
        Left = 1 << 0,
        Right = 1 << 1,
    }

    [Header("Brush Settings")]
    [SerializeField] private Color brushColor = Color.red;
    [SerializeField] private Color canvasBaseColor = Color.white;
    [SerializeField] private int brushSize = 10;
    [SerializeField] private int brushSizeIncrement = 1;
    [SerializeField] private int brushSizeMin = 1;
    [SerializeField] private int brushSizeMax = 20;
    [SerializeField] private int canvasResolution = 1080;

    [Header("Controller References")]
    [SerializeField] private Transform leftControllerTransform;
    [SerializeField] private InputActionProperty leftControllerDrawAction;
    [SerializeField] private Transform rightControllerTransform;
    [SerializeField] private InputActionProperty rightControllerDrawAction;
    [SerializeField] private Button clearCanvasButton;

    [Header("Configs")]
    [SerializeField] private List<float> MagScaleList = new() { 1f, 1.5f, 2f };
    [SerializeField] private MagnifierType magnifierType = MagnifierType.Hand;


    private Texture2D runtimeTexture;
    private Texture2D resultTexture;
    private Vector2Int? previousTexel;
    private Dictionary<Vector2Int, Color> texelsToDraw;
    private bool isPainting = false;
    private Brush inputState = Brush.None;


    public static event Action<Brush> OnStartPainting;
    public static event Action<Brush> OnStopPainting;
    private Stack<Texture2D> previousTextures = new Stack<Texture2D>();
    private Stack<Texture2D> previousresultTextures = new Stack<Texture2D>();
    private const int maxUndoSteps = 5;
    private bool savedLastFrame = false;
    private Texture2D originalTexture;
    private List<(Vector2Int, Color)> savedIndicatorColorList = new();
    private Magnifier magnifier;
    private int MagnifierLevel = 0;
    private float currentMagScale;
    private float targetMagScale;
    private float MagSpeed = 0.5f;
    private bool UpOrDown; //ture for up, false for down
    private bool magnifierUpTriggered = false;
    private bool magnifierDownTriggered = false;

    public enum MagnifierType
    {
        Hand = 0,
        Auto = 1
    }


    void OnEnable()
    {
        leftControllerDrawAction.action.Enable();
        rightControllerDrawAction.action.Enable();
        ColorManager.OnColorChanged += this.SetBrushColor;
        BrushManager.OnSizeChanged += this.SetBrushSize;
        GameManager.OnRoundStart += ClearCanvas;
    }

    void OnDisable()
    {
        leftControllerDrawAction.action.Disable();
        rightControllerDrawAction.action.Disable();
        ColorManager.OnColorChanged -= this.SetBrushColor;
        BrushManager.OnSizeChanged -= this.SetBrushSize;
        GameManager.OnRoundStart -= ClearCanvas;
    }

    void Start()
    {
        var renderer = GetComponent<Renderer>();
        this.texelsToDraw = new();

        originalTexture = renderer.material.mainTexture as Texture2D;

        if (originalTexture != null)
        {
            // 基于原始纹理创建可读写的副本
            runtimeTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.RGBA32, false);
            resultTexture = new Texture2D(originalTexture.width, originalTexture.height, TextureFormat.RGBA32, false);

            // 复制原始纹理的像素到 runtimeTexture
            Color[] originalPixels = originalTexture.GetPixels();
            ClearCanvas();
            runtimeTexture.Apply();
        }
        else
        {
            // 如果没有原始纹理，创建空白纹理
            runtimeTexture = new Texture2D(this.canvasResolution, this.canvasResolution, TextureFormat.RGBA32, false);
            resultTexture = new Texture2D(this.canvasResolution, this.canvasResolution, TextureFormat.RGBA32, false);
            ClearCanvas(); // 填充基础颜色
        }
        renderer.material.mainTexture = runtimeTexture;

        this.clearCanvasButton.onClick.AddListener(ClearCanvas);

        ClearCanvas();
        magnifier = GetComponent<Magnifier>();
        magnifier.CloseMagnifier();
        currentMagScale = MagScaleList[MagnifierLevel];

        SaveCanvasToFile(runtimeTexture, "original");
    }

    bool DetermineUpOrDown(float current, float target)
    {
        if (current < target)
            return true;
        else
            return false;
    }

    void Update()
    {
        // Check input
        bool leftPressed = leftControllerDrawAction.action.IsPressed();
        bool rightPressed = rightControllerDrawAction.action.IsPressed();

        ProcessBrush(Brush.Left, leftPressed, leftControllerTransform);
        ProcessBrush(Brush.Right, rightPressed, rightControllerTransform);

        // Reset previous texel when no brush is drawing
        if (inputState == 0)
            previousTexel = null;


        foreach (var device in InputSystem.devices)
        {
            if (device is UnityEngine.InputSystem.XR.XRController controller)
            {
                if (!controller.path.ToLower().Contains("1"))
                    continue;
                // 这些通常是 ButtonControl
                var primary = controller.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("primaryButton");
                if (primary != null && primary.wasPressedThisFrame)//for revoke
                {
                    Debug.Log($"{controller.name} Primary Button Pressed");
                    // 触发撤销事件
                    if (previousTextures.Count > 0)
                    {
                        var lastTexture = previousTextures.Pop();
                        Destroy(runtimeTexture);
                        runtimeTexture = lastTexture;
                        GetComponent<Renderer>().material.mainTexture = runtimeTexture;
                        Debug.Log("Undo triggered by Primary Button");
                    }

                    if (previousresultTextures.Count > 0)
                    {
                        var lastTexture = previousresultTextures.Pop();
                        Destroy(resultTexture);
                        resultTexture = lastTexture;
                    }
                }

                if (magnifierType == MagnifierType.Hand)
                {
                    var stick = controller.TryGetChildControl<UnityEngine.InputSystem.Controls.StickControl>("thumbstick");
                    if (stick != null) // for magnifier
                    {
                        float vertical = stick.ReadValue().y;
                        // 你可以设置一个阈值，避免误触
                        if (vertical > 0.5f)
                        {
                            if (!magnifierUpTriggered)
                            {
                                if (MagnifierLevel < MagScaleList.Count - 1)
                                {
                                    Debug.Log($"Magnifier Level Up: {MagnifierLevel}");
                                    MagnifierLevel++; //change, more adjustment
                                    targetMagScale = MagScaleList[MagnifierLevel];
                                    UpOrDown = DetermineUpOrDown(currentMagScale, targetMagScale);
                                }

                                magnifierUpTriggered = true;
                            }
                        }
                        else
                        {
                            magnifierUpTriggered = false;
                        }

                        // 向下，只触发一次
                        if (vertical < -0.5f)
                        {
                            if (!magnifierDownTriggered)
                            {
                                if (MagnifierLevel > 0)
                                {
                                    MagnifierLevel--;
                                    Debug.Log($"Magnifier Level Down: {MagnifierLevel}");
                                    targetMagScale = MagScaleList[MagnifierLevel];
                                    UpOrDown = DetermineUpOrDown(currentMagScale, targetMagScale);
                                }
                                magnifierDownTriggered = true;
                            }
                        }
                        else
                        {
                            magnifierDownTriggered = false;
                        }
                    }
                }
            }
        }

        if (UpOrDown && currentMagScale < targetMagScale)
        {
            currentMagScale += MagSpeed * Time.deltaTime;
        }
        else if (!UpOrDown && currentMagScale > targetMagScale)
        {
            currentMagScale -= MagSpeed * Time.deltaTime;
        }

        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            SaveCanvasToFile();
        }

    }

    private void StartMagnifier()
    {
        magnifier.ShowMagnifier();
    }

    private void CloseMagnifier()
    {
        magnifier.CloseMagnifier();
    }

    private void ProcessBrush(Brush brushFlag, bool isPressed, Transform controllerTransform)
    {
        bool wasDrawing = this.inputState.HasFlag(brushFlag);
        Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        bool ifHit = Physics.Raycast(ray, out RaycastHit hit);

        // clear the indicator color 
        if (savedIndicatorColorList.Count > 0)
        {
            foreach (var (pos, color) in savedIndicatorColorList)
            {
                runtimeTexture.SetPixel(pos.x, pos.y, color);
            }
            savedIndicatorColorList.Clear();
            runtimeTexture.Apply();
        }

        if (isPressed)
        {
            if (savedLastFrame == false)
            {
                savedLastFrame = true;
                Debug.Log("invoke save a frame");
                MagnifierLevel = 0;
                targetMagScale = MagScaleList[MagnifierLevel];
                previousTextures.Push(Instantiate(this.runtimeTexture));
                previousresultTextures.Push(Instantiate(this.resultTexture));
                if (previousTextures.Count > maxUndoSteps)
                {
                    var tempStack = new Stack<Texture2D>();
                    for (int i = 0; i < maxUndoSteps; i++)
                    {
                        tempStack.Push(previousTextures.Pop());
                    }

                    // 销毁多余的纹理
                    while (previousTextures.Count > 0)
                    {
                        Destroy(previousTextures.Pop());
                    }

                    // 重新构建栈
                    while (tempStack.Count > 0)
                    {
                        previousTextures.Push(tempStack.Pop());
                    }

                    var tempStack2 = new Stack<Texture2D>();
                    for (int i = 0; i < maxUndoSteps; i++)
                    {
                        tempStack2.Push(previousresultTextures.Pop());
                    }

                    while (previousresultTextures.Count > 0)
                    {
                        Destroy(previousresultTextures.Pop());
                    }
                    while (tempStack2.Count > 0)
                    {
                        previousresultTextures.Push(tempStack2.Pop());
                    }
                }
            }
            if (ifHit)
            {
                Vector2 texelUV = hit.textureCoord;
                PaintAt(texelUV);

                if (!wasDrawing)
                {
                    this.inputState |= brushFlag;
                    OnStartPainting?.Invoke(inputState);
                }
                if (MagnifierLevel > 0)
                {
                    StartMagnifier();
                    magnifier.UpdateMagnifierGPU(new Vector2Int((int)(texelUV.x * runtimeTexture.width), (int)(texelUV.y * runtimeTexture.height)), runtimeTexture, hit.point, currentMagScale); // 默认隐藏放大镜
                }
                else
                {
                    if (currentMagScale < targetMagScale)
                        CloseMagnifier();
                    else
                    {
                        magnifier.UpdateMagnifierGPU(new Vector2Int((int)(texelUV.x * runtimeTexture.width), (int)(texelUV.y * runtimeTexture.height)), runtimeTexture, hit.point, currentMagScale); // 默认隐藏放大镜
                    }
                }

                return;
            }
        }
        else //only show indicator when not drawing
        {
            if (ifHit) //need an indicator
            {
                Vector2 texelUV = hit.textureCoord;
                int x = (int)(texelUV.x * runtimeTexture.width);
                int y = (int)(texelUV.y * runtimeTexture.height);

                int indicatorRadius = 6;
                List<(Vector2Int, Color)> indicatorBackup = new();
                for (int i = -indicatorRadius; i <= indicatorRadius; i++)
                {
                    for (int j = -indicatorRadius; j <= indicatorRadius; j++)
                    {
                        int x2 = x + i;
                        int y2 = y + j;
                        float rad_ij = Mathf.Sqrt(i * i + j * j);
                        if (x2 < 0 || y2 < 0 || x2 >= runtimeTexture.width || y2 >= runtimeTexture.height || rad_ij > indicatorRadius)
                            continue;

                        Vector2Int pos = new(x2, y2);
                        indicatorBackup.Add((pos, runtimeTexture.GetPixel(x2, y2)));
                        runtimeTexture.SetPixel(x2, y2, Color.green);
                    }
                }
                this.savedIndicatorColorList = indicatorBackup;
                runtimeTexture.Apply();
            }
            else
            {
                savedIndicatorColorList.Clear();
            }
        }

        // If here, brush not drawing this frame
        if (wasDrawing)
        {
            this.inputState &= ~brushFlag;
            OnStopPainting?.Invoke(inputState);
            savedLastFrame = false;
            CloseMagnifier();
        }
    }

    void LateUpdate()
    {
        if (this.texelsToDraw == null || this.texelsToDraw.Count == 0)
            return;

        // Draw all enqueued pixels.
        foreach ((var texel, var color) in this.texelsToDraw)
        {
            this.runtimeTexture.SetPixel(texel.x, texel.y, color);
            this.resultTexture.SetPixel(texel.x, texel.y, color);
        }

        this.runtimeTexture.Apply();
        this.texelsToDraw.Clear();
    }

    #region Private Methods

    private void PaintAt(Vector2 texel)
    {
        // Calculate the scaled texel coordinate.

        int x = (int)(texel.x * runtimeTexture.width);
        int y = (int)(texel.y * runtimeTexture.height);
        Vector2Int currentTexel = new(x, y);

        // Did the brush draw on the canvas on the last frame?
        if (previousTexel.HasValue)
        {
            Vector2 prev = previousTexel.Value;
            float distance = Vector2.Distance(prev, currentTexel);
            int steps = Mathf.CeilToInt(distance);

            // Interpolate and draw a line from the previous canvas position.
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 interp = Vector2.Lerp(prev, currentTexel, t);
                EnqueueTexel((int)interp.x, (int)interp.y, this.brushSize, this.brushColor);
            }
        }
        else
        {
            // Draw the single texel.
            EnqueueTexel(x, y, this.brushSize, this.brushColor);
        }

        previousTexel = currentTexel;
    }

    private void EnqueueTexel(int x, int y, int radius, Color color)
    {
        // Get locations of each surrounding pixel.
        for (int i = -radius; i <= radius; i++)
        {
            for (int j = -radius; j <= radius; j++)
            {
                int x2 = x + i;
                int y2 = y + j;

                // Validate bounds of surrounding pixel.
                // Pixel must be within the given circular radius.
                float rad_ij = Mathf.Sqrt(i * i + j * j);

                if (x2 < 0
                    || y2 < 0
                    || x2 >= this.runtimeTexture.width
                    || y2 >= this.runtimeTexture.height
                    || rad_ij >= radius)
                    continue;

                Vector2Int texel = new(x2, y2);
                this.texelsToDraw[texel] = color;
            }
        }
    }

    #endregion

    #region Public Methods

    public void SetBrushColor(Color newColor)
    {
        this.brushColor = newColor;
    }

    public void SetBrushSize(int size)
    {
        this.brushSize = Mathf.Clamp(size, this.brushSizeMin, this.brushSizeMax);
    }

    public void IncrementBrushSize()
    {
        this.SetBrushSize(this.brushSize + this.brushSizeIncrement);
    }

    public void DecrementBrushSize()
    {
        this.SetBrushSize(this.brushSize - this.brushSizeIncrement);
    }

    public void ClearCanvas()
    {
        if (originalTexture != null && originalTexture.isReadable)
        {
            // 恢复到原始背景图片
            Color[] originalPixels = originalTexture.GetPixels();
            runtimeTexture.SetPixels(originalPixels);
        }
        else
        {
            // 没有背景图片，用纯色填充
            for (int x = 0; x < runtimeTexture.width; x++)
            {
                for (int y = 0; y < runtimeTexture.height; y++)
                {
                    runtimeTexture.SetPixel(x, y, canvasBaseColor);
                }
            }
        }

        for (int x = 0; x < resultTexture.width; x++)
        {
            for (int y = 0; y < resultTexture.height; y++)
            {
                resultTexture.SetPixel(x, y, canvasBaseColor);
            }
        }

        runtimeTexture.Apply();
        if (texelsToDraw != null) texelsToDraw.Clear();
        previousTexel = null;
    }

    public void SaveCanvasToFile(Texture2D resultTexture = null, string name = null)
    {
        string fileName;
        if (name == null)
            fileName = "texture" + System.DateTime.Now.ToString("HH-mm-ss") + ".png";
        else
        {
            fileName = name + ".png";
        }
        string filePath = Application.persistentDataPath + fileName;
        if (File.Exists(filePath))
        {
            Debug.Log($"File already exists at {filePath}, skipping save.");
            return;
        }
        // var textureBytes = this.runtimeTexture.EncodeToPNG();
        if (resultTexture == null)
        {
            resultTexture = this.resultTexture;
        }
        var textureBytes = resultTexture.EncodeToPNG();
        File.WriteAllBytes(filePath, textureBytes);
        Debug.Log($"Canvas successfully written to {filePath}");
    }

    #endregion
}
