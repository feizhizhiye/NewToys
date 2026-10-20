# NewToys 规则集预设 —— 页面链接（兜底）（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: links
# role: entry
# desc: 兜底预设：把一个网页上的所有链接抓成条目（标题 + 绝对网址）
# category: generic
# artifact: text
# works: 任意网页（目录页 / 索引页；别的预设都不匹配时用它）

RULES = [
    ("title", "css:a@text"),
    ("url",   "css:a@href"),
]
