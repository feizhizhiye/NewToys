# NewToys 规则集预设 —— 页面图片（兜底）（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: images
# role: entry
# desc: 兜底预设：把一个网页上的所有图片抓成条目（替代文本 + 绝对网址）
# category: image
# works: 任意有图片的网页（图库页 / 图集页；别的预设都不匹配时用它）

# 懒加载图片（data-src）抓不到 ⇒ 命中少时改文件加一条 ("url", "css:img@data-src")。
RULES = [
    ("title", "css:img@alt"),
    ("url",   "css:img@src"),
]
