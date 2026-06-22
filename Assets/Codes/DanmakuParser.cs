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
                // 获取 p 属性的字符串
                string pAttr = node.Attributes["p"].Value;
                // 用逗号分割字符串
                string[] pValues = pAttr.Split(',');

                DanmakuItem item = new DanmakuItem();
                // pValues[0] 就是时间，转换为小数
                item.time = float.Parse(pValues[0]);
                // InnerText 就是弹幕的汉字内容
                item.text = node.InnerText;

                danmakuList.Add(item);
            }

            Debug.Log($"成功解析了 {danmakuList.Count} 条弹幕！");
            if (danmakuList.Count > 0)
            {
                Debug.Log($"第一条弹幕: 时间 {danmakuList[0].time}秒, 内容: {danmakuList[0].text}");
            }
        }
        else
        {
            Debug.LogError("找不到弹幕文件！检查 StreamingAssets 拼写和文件名。");
        }
    }
}