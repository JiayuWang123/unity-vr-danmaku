using System.Collections.Generic;
using System.Xml;
using UnityEngine;
using System.IO;

public class DanmakuParser : MonoBehaviour
{
    public List<DanmakuItem> danmakuList = new List<DanmakuItem>();

    void Start()
    {
        LoadDanmaku("test_danmaku.xml");
    }

    void LoadDanmaku(string fileName)
    {
        string filePath = Path.Combine(Application.streamingAssetsPath, fileName);
        if (File.Exists(filePath))
        {
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(filePath);

            XmlNodeList nodes = xmlDoc.SelectNodes("i/d");
            foreach (XmlNode node in nodes)
            {
                // ��ȡ p ���Ե��ַ���
                string pAttr = node.Attributes["p"].Value;
                // �ö��ŷָ��ַ���
                string[] pValues = pAttr.Split(',');

                DanmakuItem item = new DanmakuItem();
                // pValues[0] ����ʱ�䣬ת��ΪС��
                item.time = float.Parse(pValues[0]);
                // InnerText ���ǵ�Ļ�ĺ�������
                item.text = node.InnerText;

                danmakuList.Add(item);
            }

            Debug.Log($"�ɹ������� {danmakuList.Count} ����Ļ��");
            if (danmakuList.Count > 0)
            {
                Debug.Log($"��һ����Ļ: ʱ�� {danmakuList[0].time}��, ����: {danmakuList[0].text}");
            }
        }
        else
        {
            Debug.LogError("�Ҳ�����Ļ�ļ������ StreamingAssets ƴд���ļ�����");
        }
    }
}