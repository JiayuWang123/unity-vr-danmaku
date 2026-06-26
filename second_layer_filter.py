analysis_words = {
    "梅西", "姆巴佩", "C罗", "哈兰德", "亚马尔",
    "进球", "破门", "助攻", "越位", "点球",
    "任意球", "角球", "传中", "反击", "控球",
    "世界波", "绝杀", "帽子戏法", "扑救", "VAR"
}

atmosphere_words = {
    "哈哈", "哈哈哈", "666", "牛逼", "封神",
    "卧槽", "太帅了", "绝了", "离谱", "燃",
    "泪目", "笑死", "逆天", "无敌", "精彩"
}

meta_words = {
    "打卡", "签到", "空降", "前排", "第一",
    "考古", "二刷", "三刷", "补课", "来了",
    "有人吗", "集合", "报到"
}

noise_words = {
    "111111", "222222", "333333",
    "......", "？？？？", "!!!!!!",
    "aaaa", "bbbb"
}

def classify(text):
    if text is None:
        return "REMOVE_NOISE"

    text = str(text).strip()

    if text == "":
        return "REMOVE_NOISE"

    for word in noise_words:
        if word in text:
            return "REMOVE_NOISE"

    for word in meta_words:
        if word in text:
            return "DOWNRANK_META"

    for word in atmosphere_words:
        if word in text:
            return "KEEP_ATMOSPHERE"

    for word in analysis_words:
        if word in text:
            return "KEEP_ANALYSIS"

    return "KEEP_ANALYSIS"


if __name__ == "__main__":
    tests = [
        "梅西封神",
        "哈哈哈哈",
        "打卡",
        "111111",
        "姆巴佩进球了"
    ]

    for t in tests:
        print(f"{t} -> {classify(t)}")
