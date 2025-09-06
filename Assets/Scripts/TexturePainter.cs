using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Mathematics;
using System.Linq;
using System.Threading;

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
    [SerializeField] public MagnifierType magnifierType = MagnifierType.Hand;
    [SerializeField] public float baselineForce = 15.0f;
    [SerializeField] public FingerStressRecording fingerStressRecording;
    [SerializeField] public List<float> sensetiveStage = new List<float> { 8.1f, 5.5f }; //从小到大，越来越简单。对数均分



    public Texture2D runtimeTexture;
    public Texture2D resultTexture;
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
    public Texture2D originalTexture;
    private List<(Vector2Int, Color)> savedIndicatorColorList = new();
    private Magnifier magnifier;
    private int MagnifierLevel = 0;
    private float currentMagScale = 1.0f;
    private float targetMagScale = 1.0f;
    private float MagSpeed = 0.5f;
    private bool UpOrDown; //ture for up, false for down
    private bool magnifierUpTriggered = false;
    private bool magnifierDownTriggered = false;
    private Thread recodStressThread;
    private Vector3 accumulatedTranslation = Vector3.zero;
    private Vector3 accumulatedRotation = Vector3.zero;
    private Vector3 lastControllerPosition = Vector3.zero;
    private Vector3 lastControllerRotation = Vector3.zero;
    private bool isFirstFrame = true;

    //for calibration
    public bool calibration;
    public float calibrationValue;
    public int revertTime;
    public enum MagnifierType
    {
        Hand = 0,
        Auto = 1
    }
    // for auto method
    public enum CalibrationState
    {
        Init,
        Rising,
        Stable
    }

    public float calibrationMinValue, calibrationMaxValue;
    private float stabilityThreshold = 2.5f; // 稳定性阈值，可根据需要调整
    public float stabilizedValue = 0f;
    private CalibrationState calibrationState;
    private Queue<float> recentForceValues = new Queue<float>(5);
    private List<string> StressDataList = new List<string>();
    private int lastProcessedIndex = 0;
    private bool stabilized;
    public Renderer renderer2;


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
        calibration = true;
        calibrationValue = 0f;

        renderer2 = GetComponent<Renderer>();
        this.texelsToDraw = new();

        originalTexture = renderer2.material.mainTexture as Texture2D;

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
        renderer2.material.mainTexture = runtimeTexture;

        this.clearCanvasButton.onClick.AddListener(ClearCanvas);

        ClearCanvas();
        magnifier = GetComponent<Magnifier>();
        magnifier.CloseMagnifier();

        // SaveCanvasToFile(runtimeTexture, "original");
        ResetStabilityDetection();
        recodStressThread = new Thread(ReceiveStressData);
        recodStressThread.Start();
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
        {
            previousTexel = null;
            ResetAccumulatedCorrection();
            ResetMagLevel();
        }


        foreach (var device in InputSystem.devices)
        {
            if (device is UnityEngine.InputSystem.XR.XRController controller)
            {
                if (device == UnityEngine.InputSystem.XR.XRController.rightHand)
                {
                    // 这些通常是 ButtonControl
                    var primary = controller.TryGetChildControl<UnityEngine.InputSystem.Controls.ButtonControl>("primaryButton");
                    if (primary != null && primary.wasPressedThisFrame)//for revoke
                    {
                        Debug.Log($"{controller.name} Primary Button Pressed");
                        // 触发撤销事件
                        if (previousTextures.Count > 0)
                        {
                            var lastTexture = previousTextures.Pop();
                            revertTime++;
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
                }
                if (device == UnityEngine.InputSystem.XR.XRController.leftHand)
                {
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

                    else if (magnifierType == MagnifierType.Auto)
                    {
                        if (stabilized)
                        {
                            double currentW = setModel(stabilizedValue);
                            MagnifierLevel = checkLevel(currentW);
                            stabilized = false;
                            targetMagScale = MagScaleList[MagnifierLevel];
                            UpOrDown = DetermineUpOrDown(currentMagScale, targetMagScale);
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
    }

    private int checkLevel(double current_W)
    {
        if (current_W > sensetiveStage[0])
            return 0;
        else if (current_W > sensetiveStage[1])
            return 1;
        else
            return 2;
    }

    private void StartMagnifier()
    {
        magnifier.ShowMagnifier();
    }

    private void CloseMagnifier()
    {
        magnifier.CloseMagnifier();
    }
    private void ResetMagLevel()
    {
        MagnifierLevel = 0;
        targetMagScale = MagScaleList[MagnifierLevel];
        currentMagScale = targetMagScale;
    }

    private void ProcessBrush(Brush brushFlag, bool isPressed, Transform controllerTransform)
    {
        bool wasDrawing = this.inputState.HasFlag(brushFlag);
        // Ray ray = new Ray(controllerTransform.position, controllerTransform.forward);
        Ray ray = GetCorrectedRay(controllerTransform);
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
                if (magnifierType == MagnifierType.Auto && (!calibration))
                {
                    ResetStabilityDetection();
                }
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

    private Ray GetCorrectedRay(Transform controllerTransform)
    {
        if (isFirstFrame)
        {
            // 第一帧或未绘画时，使用原始射线
            isFirstFrame = false;
            return new Ray(controllerTransform.position, controllerTransform.forward);
        }

        // 计算当前帧的位移和旋转
        Vector3 currentPosition = controllerTransform.position;
        Vector3 currentRotation = controllerTransform.eulerAngles;

        Vector3 deltaTranslation = currentPosition - lastControllerPosition;
        Vector3 deltaRotation = currentRotation - lastControllerRotation;

        // 处理角度环绕问题
        for (int i = 0; i < 3; i++)
        {
            if (deltaRotation[i] > 180f) deltaRotation[i] -= 360f;
            if (deltaRotation[i] < -180f) deltaRotation[i] += 360f;
        }

        // 根据放大镜级别进行修正
        float scaleFactor = 1f / currentMagScale; // 放大镜倍数越高，敏感度越低

        // 修正平移和旋转
        Vector3 correctedTranslation = deltaTranslation * scaleFactor;
        Vector3 correctedRotation = deltaRotation * scaleFactor;

        // 累积修正向量
        accumulatedTranslation += (deltaTranslation - correctedTranslation);
        accumulatedRotation += (deltaRotation - correctedRotation);

        // 应用累积修正到控制器变换
        Vector3 correctedPosition = currentPosition - accumulatedTranslation;
        Vector3 correctedEulerAngles = currentRotation - accumulatedRotation;

        // 计算修正后的前向向量
        Quaternion correctedQuaternion = Quaternion.Euler(correctedEulerAngles);
        Vector3 correctedForward = correctedQuaternion * Vector3.forward;

        lastControllerPosition = controllerTransform.position;
        lastControllerRotation = controllerTransform.eulerAngles;

        return new Ray(correctedPosition, correctedForward);
    }

    private void ResetAccumulatedCorrection()
    {
        accumulatedTranslation = Vector3.zero;
        accumulatedRotation = Vector3.zero;
        isFirstFrame = true;
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

        revertTime = 0;
        previousTextures.Clear();
        previousresultTextures.Clear();
    }

    public void SaveCanvasToFile(Texture2D resultTexture = null, string name = null,string userName = "test")
    {
        string folderPath = Path.Combine(Application.dataPath, "Resources", userName);
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        string fileName;
        if (name == null)
            fileName = "texture" + System.DateTime.Now.ToString("HH-mm-ss") + ".png";
        else
        {
            fileName = name + ".png";
        }
        string filePath = Path.Combine(folderPath, fileName);
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
    private double setModel(double value)
    {
        double normalized_0_1 = (value - calibrationMinValue) / (calibrationMaxValue - calibrationMinValue);

        // Then map to 0.35-0.64 range
        double normalized_value = 0.35 + normalized_0_1 * (0.64 - 0.35);
        double W = -71532.3780 / (1 + math.exp(-13.1867 * (normalized_value + 0.2441))) + 71536.3552;
        return W;
    }

    private void CheckForceStability(string csvData)
    {
        try
        {
            // 假设力值在CSV的第一列，根据实际情况调整索引
            string[] values = csvData.Split(',');
            if (values.Length > 0 && float.TryParse(values[2], out float forceValue))
            {
                // 添加新值到队列
                recentForceValues.Enqueue(forceValue);

                // 保持队列大小为10
                if (recentForceValues.Count > 5)
                {
                    recentForceValues.Dequeue();
                }


                if (calibrationState == CalibrationState.Init)
                {
                    if (forceValue > baselineForce)
                        calibrationState = CalibrationState.Rising;
                    else
                    {
                        return;
                    }
                }


                // 当有足够数据时检测稳定性
                if (recentForceValues.Count >= 5 && (calibrationState == CalibrationState.Rising || calibrationState == CalibrationState.Stable))
                {
                    float[] values_array = recentForceValues.ToArray();
                    float mean = values_array.Average();
                    float variance = values_array.Select(v => (v - mean) * (v - mean)).Average();
                    float stdDev = Mathf.Sqrt(variance);
                    // 如果标准差小于阈值，认为已稳定
                    if (stdDev < stabilityThreshold)
                    {
                        calibrationState = CalibrationState.Stable;
                        // this should only be used when calibration
                        if ((calibration && mean > stabilizedValue) || !calibration)
                        {
                            stabilizedValue = mean;
                            stabilized = true;
                            Debug.Log($"Stabilized Force Value: {stabilizedValue}");
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Error parsing force data: {e.Message}");
        }
    }

    private void ReceiveStressData()
    {
        while (fingerStressRecording != null)
        {
            // Check if new data is available in StressDataList
            if (fingerStressRecording.StressDataList.Count > lastProcessedIndex)
            {
                // Process all new data
                for (int i = lastProcessedIndex; i < fingerStressRecording.StressDataList.Count; i++)
                {
                    string data = fingerStressRecording.StressDataList[i];
                    StressDataList.Add(data);

                    // Parse CSV data and detect stability
                    CheckForceStability(data);
                }

                // Update the last processed index
                lastProcessedIndex = fingerStressRecording.StressDataList.Count;
            }

            // Small delay to prevent excessive CPU usage
            Thread.Sleep(10);
        }
    }

    public void ResetStabilityDetection()
    {
        lastProcessedIndex = fingerStressRecording.StressDataList.Count; // Reset the last processed index
        recentForceValues.Clear();
        stabilizedValue = 0f;
        calibrationState = CalibrationState.Init;
        stabilized = false;
    }

}
