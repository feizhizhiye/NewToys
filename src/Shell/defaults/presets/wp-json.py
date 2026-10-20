# NewToys 规则集预设 —— WordPress REST API（由预设库播种；⛔ 直接改会被「从预设创建」覆盖思路，建议先复制一份再改）
# name: wp-json
# role: entry
# desc: 抓 WordPress /wp-json/wp/v2/posts 的文章（标题 + 链接 + 发布时间）
# category: doc
# artifact: text
# works: 开放 REST API 的 WordPress 站点（端点 /wp-json/wp/v2/posts）

# JSON 路径：$.*.title.rendered 之类；页面分类 / 标签等接口改 $.xxx 即可。
RULES = [
    ("title", "json:$.*.title.rendered"),
    ("url",   "json:$.*.link"),
    ("date",  "json:$.*.date"),
]
