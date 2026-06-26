"""Editable lexicons used by the layer-3 feature extractor."""

from __future__ import annotations

from pathlib import Path

from .schema import LEXICON_CATEGORIES


DEFAULT_LEXICONS: dict[str, list[str]] = {
    "sports": [
        "进了",
        "好球",
        "射门",
        "传球",
        "门将",
        "越位",
        "犯规",
        "红牌",
        "黄牌",
        "点球",
        "绝杀",
        "世界波",
        "防守",
        "篮板",
        "三分",
        "扣篮",
        "盖帽",
        "反杀",
        "团战",
        "开团",
        "击杀",
        "大龙",
        "小龙",
        "先锋",
        "基地",
        "冠军",
        "夺冠",
    ],
    "emotion": [
        "啊啊啊",
        "卧槽",
        "牛逼",
        "笑死",
        "哈哈",
        "哭了",
        "燃起来了",
        "离谱",
        "绷不住",
        "舒服了",
        "破防",
        "爽",
        "绝了",
        "吓死",
        "紧张",
    ],
    "meta_noise": [
        "空降",
        "跳伞",
        "打卡",
        "我来了",
        "前方高能",
        "去人声",
        "开启字幕",
        "几倍速",
        "缓存",
        "卡了",
        "听不见",
        "字幕呢",
        "刷屏",
        "举报",
    ],
    "toxic": [
        "傻逼",
        "sb",
        "滚",
        "恶心",
        "脑子",
        "喷",
        "黑哨",
        "裁判疯了",
        "去死",
        "垃圾",
    ],
    "meme": [
        "永远的神",
        "节目效果",
        "这集我看过",
        "开香槟",
        "圣经",
        "典",
        "泪目",
        "回旋镖",
        "名场面",
        "抽象",
        "享受",
    ],
}


def load_lexicons(resources_dir: Path | None = None) -> dict[str, list[str]]:
    """Load lexicons from text files when present, falling back to built-ins."""
    lexicons = {key: list(value) for key, value in DEFAULT_LEXICONS.items()}
    if resources_dir is None:
        resources_dir = Path(__file__).resolve().parents[2] / "resources" / "lexicons"

    if resources_dir.exists():
        for category in LEXICON_CATEGORIES:
            path = resources_dir / f"{category}.txt"
            if not path.exists():
                continue
            terms = [
                line.strip()
                for line in path.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")
            ]
            if terms:
                lexicons[category] = terms

    return lexicons


def match_lexicons(text: str, lexicons: dict[str, list[str]]) -> dict[str, list[str]]:
    lowered = text.lower()
    matched: dict[str, list[str]] = {}
    for category in LEXICON_CATEGORIES:
        hits = []
        for term in lexicons.get(category, []):
            if term.lower() in lowered:
                hits.append(term)
        matched[category] = hits
    return matched
