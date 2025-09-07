using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using System.IO;

public class ExpManager : MonoBehaviour
{
    TexturePainter texturePainter;
    private int currentIndex = 0; //0~4
    private float StartTime;

    [Header("Configs")]
    [SerializeField]
    private List<Texture2D> BGList;

    [SerializeField]
    private int autoType = 0;
    [SerializeField]
    public string userName;
    // Start is called before the first frame update
    private int totalCount;
    void Start()
    {
        if (texturePainter == null)
        {
            texturePainter = GetComponent<TexturePainter>();
        }
        StartTime = Time.time;
        totalCount = (BGList.Count - 3) * 2 + 3;
    }

    void CurrentEnd(int previousIndex) // only for calibration
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
            recordData(currentIndex, Time.time - StartTime, texturePainter.revertTime, texturePainter.magnifierType.ToString());
        }

        bool switchScene = false;
        int previousIndex = 0;
        if (Keyboard.current != null && Keyboard.current.rightArrowKey.wasPressedThisFrame)
        {
            switchScene = true;
            previousIndex = currentIndex;
            currentIndex += 1;
            if (currentIndex >= totalCount)
            {
                currentIndex -= 1;
                switchScene = false;
            }
            //only 2 for calibration
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
            int fixed_index = currentIndex;
            if (fixed_index > 2)
            {
                fixed_index = 3 + (currentIndex - 3) / 2;
            }
            texturePainter.originalTexture = BGList[fixed_index];
            //save data
            int revertTime = texturePainter.revertTime;
            // save
            if (previousIndex >= 3)
            {
                texturePainter.SaveCanvasToFile(texturePainter.resultTexture, $"result_{previousIndex}", userName); // save result
            }
            texturePainter.ClearCanvas();
            if (currentIndex >= 3)
            {
                texturePainter.SaveCanvasToFile(texturePainter.runtimeTexture, $"original_{currentIndex}", userName); // save original
            }

            texturePainter.renderer2.material.mainTexture = texturePainter.runtimeTexture;
            float workTime = Time.time - StartTime;
            StartTime = Time.time;
            recordData(previousIndex, workTime, revertTime, texturePainter.magnifierType.ToString());

            //set magnifier
            if (currentIndex == 2 || (currentIndex > 2 && ((currentIndex - 3) % 2 == autoType)))
            {
                texturePainter.magnifierType = TexturePainter.MagnifierType.Auto;
            }
            else
            {
                texturePainter.magnifierType = TexturePainter.MagnifierType.Hand;
            }


        }
    }

    void recordData(int index, float workTime, int revertTime, string type = null)
    {
        string folderPath = Path.Combine(Application.dataPath, "Resources", userName);

        string csvPath = Path.Combine(folderPath, "info.csv");
        string csvLine = $"{index},{workTime},{revertTime},{type}\n";
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        if (!File.Exists(csvPath))
        {
            File.WriteAllText(csvPath, "previousIndex,workTime,revertTime,type\n");
        }
        File.AppendAllText(csvPath, csvLine);
    }

}
