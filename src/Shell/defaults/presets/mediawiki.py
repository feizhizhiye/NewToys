# NewToys 规则集预设 —— MediaWiki API（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: mediawiki
# role: entry
# desc: 抓 MediaWiki 全站页面清单（标题 + 页面完整网址）
# category: doc
# artifact: text
# works: MediaWiki 站点（维基百科、各种 MediaWiki 站群）

# JSON 路径走到 query.pages.*；换接口（如搜索）时改 $.query.xxx.*.fullurl。
RULES = [
    ("title", "json:$.query.pages.*.title"),
    ("url",   "json:$.query.pages.*.fullurl"),
]
