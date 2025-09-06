using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;

public class ExpManager : MonoBehaviour
{
    TexturePainter texturePainter;
    private int currentIndex = 0; //0~4
    private float StartTime, EndTime;

    [Header("Configs")]
    [SerializeField]
    private List<Texture2D> BGList;

    [SerializeField]
    private int autoIndex = 3;
    [SerializeField]
    public string userName;
    // Start is called before the first frame update
    void Start()
    {
        if (texturePainter == null)
        {
            texturePainter = GetComponent<TexturePainter>();
        }
        StartTime = Time.time;
    }

    void CurrentEnd(int previousIndex)
    {
        if (previousIndex == 0)
        {
            texturePainter.calibrationMinValue = texturePainter.stabilizedValue;
            texturePainter.calibrationValue = 0;
            Debug.Log($"Calibration Min Value: {texturePainter.calibrationMinValue}");
        }
        else if (previousIndex == 1)
        {
            texturePainter.calibrationMaxValue = texturePainter.stabilizedValue;
            texturePainter.calibrationValue = 0;
            Debug.Log($"Calibration Max Value: {texturePainter.calibrationMaxValue}");
        }
        texturePainter.ResetStabilityDetection();
    }

    // Update is called once per frame
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            texturePainter.SaveCanvasToFile(texturePainter.resultTexture, $"result_{currentIndex}", userName);
            recordData(currentIndex, Time.time - StartTime, texturePainter.revertTime);
        }

        bool switchScene = false;
        int previousIndex = 0;
        if (Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            switchScene = true;
            previousIndex = currentIndex;
            currentIndex += 1;
            if (currentIndex >= BGList.Count)
            {
                currentIndex -= 1;
                switchScene = false;

            }
            if (currentIndex > 1)
            {
                texturePainter.calibration = false;
            }
            else
            {

                texturePainter.calibration = true;
            }
        }
        else if (Keyboard.current != null && Keyboard.current.leftArrowKey.wasPressedThisFrame)
        {
            switchScene = true;
            previousIndex = currentIndex;
            currentIndex -= 1;
            if (currentIndex < 0)
            {
                currentIndex = 0;
                switchScene = false;
            }
            if (currentIndex > 1)
            {
                texturePainter.calibration = false;
            }
            else
            {
                texturePainter.calibration = true;
            }
        }
        else if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame)
        {
            if (currentIndex == 2)
            {
                if (texturePainter.magnifierType == TexturePainter.MagnifierType.Hand)
                {
                    texturePainter.magnifierType = TexturePainter.MagnifierType.Auto;
                }
                else
                {
                    texturePainter.magnifierType = TexturePainter.MagnifierType.Hand;
                }
            }
        }

        if (switchScene)
        {
            CurrentEnd(previousIndex);
            texturePainter.originalTexture = BGList[currentIndex];
            //save data
            int revertTime = texturePainter.revertTime;
            texturePainter.ClearCanvas();
            texturePainter.renderer2.material.mainTexture = texturePainter.runtimeTexture;
            // save
            if (currentIndex == 3 || currentIndex == 4)
            {
                texturePainter.SaveCanvasToFile(texturePainter.runtimeTexture, $"original_{currentIndex}", userName);
            }
            if (previousIndex == 3 || previousIndex == 4)
            {
                texturePainter.SaveCanvasToFile(texturePainter.resultTexture, $"result_{previousIndex}", userName);
            }

            //set magnifier
            if (currentIndex == 2 || currentIndex == autoIndex)
            {
                texturePainter.magnifierType = TexturePainter.MagnifierType.Auto;
            }
            else
            {
                texturePainter.magnifierType = TexturePainter.MagnifierType.Hand;
            }

            float workTime = Time.time - StartTime;
            StartTime = Time.time;
            recordData(previousIndex, workTime, revertTime);
        }
    }

    void recordData(int index, float workTime, int revertTime)
    {
        string folderPath = Path.Combine(Application.dataPath, "Resources", userName);

        string csvPath = Path.Combine(folderPath, "info.csv");
        string csvLine = $"{index},{workTime},{revertTime}\n";
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, "previousIndex,workTime,revertTime\n");
        }
        File.AppendAllText(csvPath, csvLine);
    }

}
