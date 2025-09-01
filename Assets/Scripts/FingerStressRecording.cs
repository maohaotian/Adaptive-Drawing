using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Ports;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using System.Threading;

public class FingerStressRecording : MonoBehaviour
{
    public SerialPort serialPort;

    // ��������
    public string portName = "COM3"; // ���ں�
    public int baudRate = 9600;      // ������

    private static System.Timers.Timer aTimer;
    public string RecordingPath = "D:\\Projects\\test\\fingerSressRecording\\Data\\fingerStress";
    public int fileid = 1;

    private string filePath;

    private Thread recodStressThread;

    public List<string> StressDataList = new List<string>(); // �������ݵ��б�
    public float writeInterval = 0.1f; //д����

    void Start()
    {
        // ��ʼ������
        serialPort = new SerialPort(portName, baudRate);

        // �򿪴���
        try
        {
            serialPort.Open();
        }
        catch (System.Exception e)
        {
            Debug.LogError("�޷��򿪴���: " + e.Message);
        }

        filePath = RecordingPath + fileid + ".csv";

        //�����ļ�
        if (!File.Exists(filePath))
        {
            File.Create(filePath).Close();
            //���ڵ���д�뷽��
            // InvokeRepeating(nameof(WriteDataToCSV), writeInterval, writeInterval);
        }
        else
        {
            Debug.Log("�Ѵ����ļ����޷�������¼��");
        }

        recodStressThread = new Thread(ReceiveStressData);
        recodStressThread.Start();
    }

    void Update()
    {
    }

    private void ReceiveStressData()
    {
        while (serialPort != null && serialPort.IsOpen)
        {
            // ��ȡ����
            if (serialPort.ReadLine() != null)
            {
                string data = serialPort.ReadLine(); // ��ȡһ������
                string newData = GenerateTimestampedData(data);
                //Debug.Log("���յ�������: " + data);
                StressDataList.Add(newData);
                // ��������
                //ProcessData(data);
            }
            else
            {
                Debug.Log("δ���յ����ݡ�");
            }
        }
    }

    void OnDestroy()
    {
        // �˳�ʱ������ʣ������д���ļ�
        WriteDataToCSV();
        // �رմ���
        if (serialPort != null && serialPort.IsOpen)
        {
            serialPort.Close();
        }
    }

    // �������յ�������
    private void ProcessData(string data)
    {
        // �����ﴦ�����յ�������
        int[] numbers = ExtractIntegers(data);
        Debug.Log("��ȡ������: " + string.Join(", ", numbers));
    }

    // ��ȡ����
    private int[] ExtractIntegers(string input)
    {
        // �ö��ŷָ��ַ���
        string[] parts = input.Split(',');

        // ��ÿ������ת��Ϊ����
        int[] numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int number))
            {
                numbers[i] = number;
            }
            else
            {
                Debug.LogWarning("�޷�����������: " + parts[i]);
                numbers[i] = 0; // Ĭ��ֵ
            }
        }

        return numbers;
    }

    // ���ɴ�ʱ���������
    private string GenerateTimestampedData(string str)
    {
        // ��ȡ��ǰʱ���
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        // ���ش�ʱ���������
        return $"{timestamp},{str}";
    }

    // д�����ݵ� CSV �ļ�
    private void WriteDataToCSV()
    {
        if (StressDataList.Count == 0)
            return;

        using (StreamWriter writer = new StreamWriter(filePath, true)) // true ��ʾ׷��ģʽ
        {
            foreach (string data in StressDataList)
            {
                writer.WriteLine(data);
            }
        }

        // ��ջ���
        StressDataList.Clear();
    }
}
